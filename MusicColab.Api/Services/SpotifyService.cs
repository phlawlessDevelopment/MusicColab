using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicColab.Api.Configuration;
using MusicColab.Api.Contracts;
using MusicColab.Api.Data;
using MusicColab.Api.Models;

namespace MusicColab.Api.Services;

public interface ISpotifyService
{
    Task<IReadOnlyList<ArtistCard>> BuildFeedForUserAsync(Guid userId, int limit, CancellationToken cancellationToken);
}

public sealed class SpotifyService(
    IHttpClientFactory httpClientFactory,
    IOptions<SpotifyOptions> options,
    AppDbContext dbContext,
    ILogger<SpotifyService> logger) : ISpotifyService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly SpotifyOptions _options = options.Value;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILogger<SpotifyService> _logger = logger;

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ArtistCard>> BuildFeedForUserAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 5, 30);

        var ratedArtistIds = await _dbContext.UserArtistPreferences
            .Where(x => x.UserId == userId)
            .Select(x => x.ArtistId)
            .ToHashSetAsync(cancellationToken);

        var seeds = await _dbContext.UserArtistPreferences
            .Include(x => x.Artist)
            .Where(x => x.UserId == userId && x.Preference == PreferenceValue.Like)
            .ToListAsync(cancellationToken);

        seeds = seeds
            .OrderByDescending(x => x.CreatedAt)
            .Take(3)
            .ToList();

        var seedIds = seeds.Select(x => x.ArtistId).ToList();

        if (seeds.Count == 0)
        {
            seedIds = _options.SeedArtistIds.Take(3).ToList();
        }

        var candidates = new List<ArtistCard>();

        if (seedIds.Count > 0 && !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            foreach (var seed in seedIds)
            {
                var related = await GetRelatedArtistsAsync(seed, cancellationToken);
                candidates.AddRange(related);
            }
        }

        // Fallback for API quota issues or missing keys uses local cached artists.
        if (candidates.Count == 0)
        {
            var fallback = await _dbContext.Artists
                .OrderBy(x => x.Name)
                .Take(limit * 2)
                .ToListAsync(cancellationToken);

            candidates.AddRange(fallback.Select(ToCard));
        }

        var deduped = candidates
            .Where(x => !ratedArtistIds.Contains(x.Id))
            .DistinctBy(x => x.Id)
            .Take(limit)
            .ToList();

        if (deduped.Count < limit)
        {
            var additional = await _dbContext.Artists
                .Where(x => !ratedArtistIds.Contains(x.Id))
                .OrderBy(x => x.Name)
                .Take(limit)
                .ToListAsync(cancellationToken);

            deduped = deduped
                .Concat(additional.Select(ToCard))
                .DistinctBy(x => x.Id)
                .Take(limit)
                .ToList();
        }

        return deduped;
    }

    private async Task<IReadOnlyList<ArtistCard>> GetRelatedArtistsAsync(string artistId, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/artists/{artistId}/related-artists");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 429)
        {
            _logger.LogWarning("Spotify API rate limit reached while loading related artists.");
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify related artists call failed with {StatusCode}.", response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<RelatedArtistsResponse>(stream, cancellationToken: cancellationToken);
        if (payload?.Artists is null)
        {
            return [];
        }

        var cards = payload.Artists.Select(ToCard).ToList();
        foreach (var artist in payload.Artists)
        {
            await UpsertArtistAsync(artist, cancellationToken);
        }

        return cards;
    }

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _token;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify token request failed with {StatusCode}.", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken);
        if (payload?.AccessToken is null)
        {
            return null;
        }

        _token = payload.AccessToken;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn);
        return _token;
    }

    private async Task UpsertArtistAsync(SpotifyArtistDto artist, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Artists.FindAsync([artist.Id], cancellationToken);
        var metadata = new SpotifyArtistMetadata
        {
            ImageUrl = artist.Images?.FirstOrDefault()?.Url,
            Genres = artist.Genres ?? []
        };

        if (existing is null)
        {
            _dbContext.Artists.Add(new Artist
            {
                Id = artist.Id,
                Name = artist.Name,
                MetadataJson = JsonSerializer.Serialize(metadata),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Name = artist.Name;
            existing.MetadataJson = JsonSerializer.Serialize(metadata);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ArtistCard ToCard(SpotifyArtistDto artist)
    {
        return new ArtistCard(
            artist.Id,
            artist.Name,
            artist.Images?.FirstOrDefault()?.Url,
            artist.Genres ?? []);
    }

    private static ArtistCard ToCard(Artist artist)
    {
        var metadata = JsonSerializer.Deserialize<SpotifyArtistMetadata>(artist.MetadataJson) ?? new SpotifyArtistMetadata();
        return new ArtistCard(artist.Id, artist.Name, metadata.ImageUrl, metadata.Genres);
    }
}

public sealed class SpotifyArtistMetadata
{
    public string? ImageUrl { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
}

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}

internal sealed class RelatedArtistsResponse
{
    public List<SpotifyArtistDto>? Artists { get; init; }
}

internal sealed class SpotifyArtistDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string>? Genres { get; init; }
    public List<SpotifyImage>? Images { get; init; }
}

internal sealed class SpotifyImage
{
    public string Url { get; init; } = string.Empty;
}
