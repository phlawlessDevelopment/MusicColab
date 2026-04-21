using System.Text;
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
builder.Services.Configure<SpotifyOptions>(builder.Configuration.GetSection(SpotifyOptions.SectionName));

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

    var artists = await spotifyService.BuildFeedForUserAsync(userId, limit ?? 12, cancellationToken);
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

    var artist = await dbContext.Artists.FindAsync([request.ArtistId], cancellationToken);
    if (artist is null)
    {
        return Results.BadRequest(new { message = "Unknown artist_id." });
    }

    if (!TryParsePreference(request.Preference, out var parsedPreference))
    {
        return Results.BadRequest(new { message = "Invalid preference. Use Like or Dislike." });
    }

    var existing = await dbContext.UserArtistPreferences
        .FirstOrDefaultAsync(x => x.UserId == userId && x.ArtistId == request.ArtistId, cancellationToken);

    if (existing is null)
    {
        dbContext.UserArtistPreferences.Add(new UserArtistPreference
        {
            UserId = userId,
            ArtistId = request.ArtistId,
            Preference = parsedPreference,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
    else
    {
        existing.Preference = parsedPreference;
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
        .Include(x => x.Artist)
        .Where(x => x.UserId == userA || x.UserId == userB)
        .ToListAsync(cancellationToken);

    var result = compatibilityService.BuildComparison(userA, userB, preferences);
    return Results.Ok(result);
});

app.Run();
