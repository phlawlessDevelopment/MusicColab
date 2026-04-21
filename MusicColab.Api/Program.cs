using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MusicColab.Api.Configuration;
using MusicColab.Api.Contracts;
using MusicColab.Api.Data;
using MusicColab.Api.Infrastructure;
using MusicColab.Api.Models;
using MusicColab.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services
    .AddOptions<SpotifyOptions>()
    .Bind(builder.Configuration.GetSection(SpotifyOptions.SectionName))
    .Configure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            options.ClientId = builder.Configuration["SPOTIFY_ID"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            options.ClientSecret = builder.Configuration["SPOTIFY_SECRET"] ?? string.Empty;
        }
    });

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ISpotifyService, SpotifyService>();
builder.Services.AddScoped<ICompatibilityService, CompatibilityService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        await dbContext.Database.MigrateAsync();
    }
    catch (SqliteException ex) when (app.Environment.IsDevelopment())
    {
        logger.LogWarning(ex, "Legacy SQLite schema detected. Recreating local development database.");
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }

    await SeedData.EnsureSeedDataAsync(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/auth/register", async (
    RegisterRequest request,
    AppDbContext dbContext,
    IJwtTokenService jwtTokenService,
    CancellationToken cancellationToken) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
    {
        return Results.BadRequest(new { message = "Email and password (min 8 chars) are required." });
    }

    if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
    {
        return Results.Conflict(new { message = "Email already registered." });
    }

    var user = new AppUser
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        CreatedAt = DateTimeOffset.UtcNow
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync(cancellationToken);

    var token = jwtTokenService.CreateToken(user);
    return Results.Ok(new AuthResponse(token, user.Id, user.Email));
});

app.MapPost("/auth/login", async (
    LoginRequest request,
    AppDbContext dbContext,
    IJwtTokenService jwtTokenService,
    CancellationToken cancellationToken) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = jwtTokenService.CreateToken(user);
    return Results.Ok(new AuthResponse(token, user.Id, user.Email));
});

app.MapGet("/artists/feed", async (
    HttpContext httpContext,
    AppDbContext dbContext,
    ISpotifyService spotifyService,
    int? limit,
    CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.RequireUserId();

    var userExists = await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
    if (!userExists)
    {
        return Results.Json(new { message = "Your session no longer matches the current database. Sign in again." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var artists = await spotifyService.BuildFeedForUserAsync(userId, limit ?? 5, cancellationToken);
    return Results.Ok(new ArtistFeedResponse(artists));
}).RequireAuthorization();

app.MapPost("/preferences", async (
    HttpContext httpContext,
    PreferenceRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.RequireUserId();

    var userExists = await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
    if (!userExists)
    {
        return Results.Json(new { message = "Your session no longer matches the current database. Sign in again." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (string.IsNullOrWhiteSpace(request.ArtistId) || request.ArtistId.Length > 64)
    {
        return Results.BadRequest(new { message = "artistId is required and must be 64 characters or less." });
    }

    if (string.IsNullOrWhiteSpace(request.ArtistName) || request.ArtistName.Length > 300)
    {
        return Results.BadRequest(new { message = "artistName is required and must be 300 characters or less." });
    }

    if (!string.IsNullOrWhiteSpace(request.ImageUrl) && request.ImageUrl.Length > 2048)
    {
        return Results.BadRequest(new { message = "imageUrl must be 2048 characters or less." });
    }

    if (!string.IsNullOrWhiteSpace(request.PreviewUrl) && request.PreviewUrl.Length > 2048)
    {
        return Results.BadRequest(new { message = "previewUrl must be 2048 characters or less." });
    }

    if (!TryParsePreference(request.Preference, out var parsedPreference))
    {
        return Results.BadRequest(new { message = "Invalid preference. Use Like or Dislike." });
    }

    var genres = NormalizeGenres(request.Genres);
    var tagsJson = JsonSerializer.Serialize(genres);

    var existing = await dbContext.UserArtistPreferences
        .FirstOrDefaultAsync(x => x.UserId == userId && x.ArtistId == request.ArtistId, cancellationToken);

    if (existing is null)
    {
        dbContext.UserArtistPreferences.Add(new UserArtistPreference
        {
            UserId = userId,
            ArtistId = request.ArtistId.Trim(),
            ArtistName = request.ArtistName.Trim(),
            ImageUrl = NormalizeOptionalValue(request.ImageUrl),
            PreviewUrl = NormalizeOptionalValue(request.PreviewUrl),
            TagsJson = tagsJson,
            Preference = parsedPreference,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
    else
    {
        existing.Preference = parsedPreference;
        existing.ArtistName = request.ArtistName.Trim();
        existing.ImageUrl = NormalizeOptionalValue(request.ImageUrl);
        existing.PreviewUrl = NormalizeOptionalValue(request.PreviewUrl);
        existing.TagsJson = tagsJson;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok();
}).RequireAuthorization();

static bool TryParsePreference(string value, out PreferenceValue preference)
{
    var normalized = value?.Trim().ToLowerInvariant();
    switch (normalized)
    {
        case "like":
        case "liked":
        case "1":
            preference = PreferenceValue.Like;
            return true;
        case "dislike":
        case "disliked":
        case "2":
            preference = PreferenceValue.Dislike;
            return true;
        default:
            preference = default;
            return false;
    }
}

app.MapGet("/users", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var users = await dbContext.Users
        .ToListAsync(cancellationToken);

    users = users
        .OrderBy(x => x.CreatedAt)
        .ToList();

    var result = users.Select(x => new UserSummary(x.Id, x.Email, x.CreatedAt)).ToList();

    return Results.Ok(result);
});

app.MapGet("/users/{id:guid}/profile", async (Guid id, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (user is null)
    {
        return Results.NotFound();
    }

    var query = dbContext.UserArtistPreferences.Where(x => x.UserId == id);
    var likes = await query.CountAsync(x => x.Preference == PreferenceValue.Like, cancellationToken);
    var dislikes = await query.CountAsync(x => x.Preference == PreferenceValue.Dislike, cancellationToken);

    return Results.Ok(new ProfileResponse(
        new UserSummary(user.Id, user.Email, user.CreatedAt),
        likes,
        dislikes,
        likes + dislikes));
});

app.MapGet("/compare/{userA:guid}/{userB:guid}", async (
    Guid userA,
    Guid userB,
    AppDbContext dbContext,
    ICompatibilityService compatibilityService,
    CancellationToken cancellationToken) =>
{
    var users = await dbContext.Users
        .Where(x => x.Id == userA || x.Id == userB)
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);

    if (users.Count != 2)
    {
        return Results.NotFound(new { message = "One or both users were not found." });
    }

    var preferences = await dbContext.UserArtistPreferences
        .Where(x => x.UserId == userA || x.UserId == userB)
        .ToListAsync(cancellationToken);

    var result = compatibilityService.BuildComparison(userA, userB, preferences);
    return Results.Ok(result);
});

app.Run();

static string? NormalizeOptionalValue(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value.Trim();
}

static IReadOnlyList<string> NormalizeGenres(IReadOnlyList<string>? genres)
{
    if (genres is null || genres.Count == 0)
    {
        return [];
    }

    return genres
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(25)
        .ToList();
}
