using MusicColab.Api.Models;

namespace MusicColab.Api.Contracts;

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string Token, Guid UserId, string Email);

public sealed record PreferenceRequest(string ArtistId, string Preference);

public sealed record ArtistCard(string Id, string Name, string? ImageUrl, IReadOnlyList<string> Genres);
public sealed record ArtistFeedResponse(IReadOnlyList<ArtistCard> Artists);

public sealed record UserSummary(Guid Id, string Email, DateTimeOffset CreatedAt);
public sealed record ProfileResponse(UserSummary User, int Likes, int Dislikes, int TotalRatings);

public sealed record CompareResponse(
    int CompatibilityScore,
    IReadOnlyList<ArtistCard> SharedLikes,
    IReadOnlyList<ArtistCard> Conflicts,
    IReadOnlyList<ArtistCard> DiscoveryFromA,
    IReadOnlyList<ArtistCard> DiscoveryFromB
);
