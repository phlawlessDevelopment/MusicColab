using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
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
    private const int SpotifySearchPageSize = 10;
    private static readonly JsonSerializerOptions SpotifyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly string[] DefaultSearchTerms =
    [
        "indie",
        "pop",
        "hip hop",
        "rock",
        "electronic",
        "alternative",
        "jazz",
        "soul",
        "funk",
        "metal",
        "house",
        "ambient",
        "latin",
        "afrobeats",
        "k-pop",
        "folk",
        "r&b",
        "dance",
        "reggaeton",
        "soundtrack"
    ];

    private static readonly string[] BroadFallbackTerms =
    [
        "a",
        "e",
        "i",
        "o",
        "u",
        "the",
        "dj",
        "band",
        "live",
        "feat"
    ];

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly SpotifyOptions _options = options.Value;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILogger<SpotifyService> _logger = logger;

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private DateTimeOffset _searchRateLimitUntil = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ArtistCard>> BuildFeedForUserAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 10);

        var ratedArtistIds = await _dbContext.UserArtistPreferences
            .Where(x => x.UserId == userId)
            .Select(x => x.ArtistId)
            .ToHashSetAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogWarning("Spotify credentials are missing. Returning an empty feed.");
            return [];
        }

        var token = await GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Spotify token could not be acquired. Returning an empty feed.");
            return [];
        }

        var seedTerms = await BuildSearchTermsAsync(userId, cancellationToken);
        var candidates = new List<SpotifyArtistDto>();
        var maxAttempts = Math.Max(8, limit * 3);

        for (var attempt = 0; attempt < maxAttempts && candidates.Count < (limit * 3); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSearchRateLimited())
            {
                break;
            }

            var term = seedTerms[Random.Shared.Next(seedTerms.Count)];
            var offset = Random.Shared.Next(0, 990);
            var batch = await SearchArtistsAsync(token, term, offset, cancellationToken);
            if (batch.Count == 0)
            {
                // A narrow term plus a high offset can easily produce empty pages.
                // Retry same term from the first page to keep discovery moving.
                batch = await SearchArtistsAsync(token, term, 0, cancellationToken);
            }

            if (batch.Count == 0)
            {
                if (IsSearchRateLimited())
                {
                    break;
                }

                continue;
            }

            candidates.AddRange(batch);
        }

        var deduped = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => !ratedArtistIds.Contains(x.Id))
            .DistinctBy(x => x.Id)
            .OrderBy(_ => Random.Shared.Next())
            .Take(limit * 20)
            .ToList();

        var prioritized = deduped
            .Where(HasUsableImage)
            .Concat(deduped.Where(x => !HasUsableImage(x)))
            .DistinctBy(x => x.Id)
            .Take(limit)
            .ToList();

        if (prioritized.Count == 0)
        {
            var fallbackCandidates = await SearchBroadFallbackAsync(token, ratedArtistIds, limit, cancellationToken);
            prioritized = fallbackCandidates
                .Where(HasUsableImage)
                .Concat(fallbackCandidates.Where(x => !HasUsableImage(x)))
                .DistinctBy(x => x.Id)
                .Take(limit)
                .ToList();
        }

        if (prioritized.Count == 0)
        {
            return [];
        }

        var cards = new List<ArtistCard>(prioritized.Count);

        foreach (var artist in prioritized)
        {
            cards.Add(new ArtistCard(
                artist.Id,
                artist.Name,
                artist.Images?.Select(x => x.Url).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
                null,
                artist.Genres ?? []));
        }

        return cards.Take(limit).ToList();
    }

    private async Task<List<string>> BuildSearchTermsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var likedSnapshots = await _dbContext.UserArtistPreferences
            .Where(x => x.UserId == userId && x.Preference == PreferenceValue.Like)
            .Select(x => new { x.CreatedAt, x.ArtistName, x.TagsJson })
            .ToListAsync(cancellationToken);

        likedSnapshots = likedSnapshots
            .OrderByDescending(x => x.CreatedAt)
            .Take(30)
            .ToList();

        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in likedSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ArtistName))
            {
                terms.Add(snapshot.ArtistName.Trim());
            }

            if (string.IsNullOrWhiteSpace(snapshot.TagsJson))
            {
                continue;
            }

            List<string>? tags;
            try
            {
                tags = JsonSerializer.Deserialize<List<string>>(snapshot.TagsJson);
            }
            catch (JsonException)
            {
                tags = null;
            }

            if (tags is null)
            {
                continue;
            }

            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    terms.Add(tag.Trim());
                }
            }
        }

        foreach (var defaultTerm in DefaultSearchTerms)
        {
            terms.Add(defaultTerm);
        }

        return terms.ToList();
    }

    private async Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(
        string token,
        string term,
        int offset,
        CancellationToken cancellationToken)
    {
        var encodedTerm = Uri.EscapeDataString(term);

        if (IsSearchRateLimited())
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.spotify.com/v1/search?q={encodedTerm}&type=artist&limit={SpotifySearchPageSize}&offset={offset}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode == 429)
            {
                MarkSearchRateLimited(response);
                _logger.LogWarning("Spotify API rate limit reached while searching artists.");
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Spotify artist search failed with {StatusCode} for term '{Term}'.", response.StatusCode, term);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<SearchArtistsResponse>(stream, SpotifyJsonOptions, cancellationToken);
            return payload?.Artists?.Items ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Spotify artist search request failed for term '{Term}'.", term);
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Spotify artist search payload could not be parsed for term '{Term}'.", term);
            return [];
        }
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
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
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
        var payload = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, SpotifyJsonOptions, cancellationToken);
        if (payload?.AccessToken is null)
        {
            return null;
        }

        _token = payload.AccessToken;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn);
        return _token;
    }

    private async Task<string?> GetPreviewUrlAsync(
        string token,
        string artistId,
        Dictionary<string, string?> previewCache,
        CancellationToken cancellationToken)
    {
        if (previewCache.TryGetValue(artistId, out var cachedPreviewUrl))
        {
            return cachedPreviewUrl;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.spotify.com/v1/artists/{artistId}/top-tracks?market=US");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                previewCache[artistId] = null;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TopTracksResponse>(stream, SpotifyJsonOptions, cancellationToken);
            var previewUrl = payload?.Tracks?.FirstOrDefault(track => !string.IsNullOrWhiteSpace(track.PreviewUrl))?.PreviewUrl;

            previewCache[artistId] = previewUrl;
            return previewUrl;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Spotify top tracks request failed for artist {ArtistId}.", artistId);
            previewCache[artistId] = null;
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Spotify top tracks payload could not be parsed for artist {ArtistId}.", artistId);
            previewCache[artistId] = null;
            return null;
        }
    }

    private bool IsSearchRateLimited()
    {
        return _searchRateLimitUntil > DateTimeOffset.UtcNow;
    }

    private void MarkSearchRateLimited(HttpResponseMessage response)
    {
        var retryAfter = ParseRetryAfterSeconds(response);
        var cooldown = retryAfter > 0 ? retryAfter : 10;
        _searchRateLimitUntil = DateTimeOffset.UtcNow.AddSeconds(cooldown);
    }

    private static int ParseRetryAfterSeconds(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return Math.Max(0, seconds);
            }
        }

        return 0;
    }

    private static bool HasUsableImage(SpotifyArtistDto artist)
    {
        return artist.Images?.Any(x => !string.IsNullOrWhiteSpace(x.Url)) == true;
    }

    private async Task<IReadOnlyList<SpotifyArtistDto>> SearchBroadFallbackAsync(
        string token,
        HashSet<string> ratedArtistIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = new List<SpotifyArtistDto>();
        var attempts = Math.Max(20, limit * 10);

        for (var i = 0; i < attempts && candidates.Count < (limit * 8); i++)
        {
            if (IsSearchRateLimited())
            {
                break;
            }

            var term = BroadFallbackTerms[Random.Shared.Next(BroadFallbackTerms.Length)];
            var offset = Random.Shared.Next(0, 950);
            var batch = await SearchArtistsAsync(token, term, offset, cancellationToken);
            if (batch.Count == 0)
            {
                continue;
            }

            candidates.AddRange(batch);
        }

        return candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => !ratedArtistIds.Contains(x.Id))
            .DistinctBy(x => x.Id)
            .ToList();
    }
}

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}

internal sealed class SearchArtistsResponse
{
    public SearchArtistsContainer? Artists { get; init; }
}

internal sealed class SearchArtistsContainer
{
    public List<SpotifyArtistDto>? Items { get; init; }
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

internal sealed class TopTracksResponse
{
    public List<SpotifyTrackDto>? Tracks { get; init; }
}

internal sealed class SpotifyTrackDto
{
    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }
}
