using System.Text.Json;
using MusicColab.Api.Contracts;
using MusicColab.Api.Models;

namespace MusicColab.Api.Services;

public interface ICompatibilityService
{
    CompareResponse BuildComparison(Guid userA, Guid userB, IReadOnlyList<UserArtistPreference> preferences);
}

public sealed class CompatibilityService : ICompatibilityService
{
    public CompareResponse BuildComparison(Guid userA, Guid userB, IReadOnlyList<UserArtistPreference> preferences)
    {
        var byArtist = preferences
            .GroupBy(x => x.ArtistId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sharedLikes = new List<ArtistCard>();
        var conflicts = new List<ArtistCard>();
        var discoveryFromA = new List<ArtistCard>();
        var discoveryFromB = new List<ArtistCard>();

        foreach (var group in byArtist.Values)
        {
            var prefA = group.FirstOrDefault(x => x.UserId == userA);
            var prefB = group.FirstOrDefault(x => x.UserId == userB);

            if (prefA is null && prefB is null)
            {
                continue;
            }

            var card = ToArtistCard(group[0].Artist);

            if (prefA?.Preference == PreferenceValue.Like && prefB?.Preference == PreferenceValue.Like)
            {
                sharedLikes.Add(card);
            }
            else if (prefA is not null && prefB is not null && prefA.Preference != prefB.Preference)
            {
                conflicts.Add(card);
            }
            else if (prefA?.Preference == PreferenceValue.Like && prefB is null)
            {
                discoveryFromA.Add(card);
            }
            else if (prefB?.Preference == PreferenceValue.Like && prefA is null)
            {
                discoveryFromB.Add(card);
            }
        }

        var rawScore = (sharedLikes.Count * 2.0) - (conflicts.Count * 1.5);
        var denominator = Math.Max(1.0, (sharedLikes.Count + conflicts.Count) * 2.0);
        var normalized = ((rawScore / denominator) + 1.0) / 2.0;
        var compatibilityScore = (int)Math.Clamp(Math.Round(normalized * 100.0), 0, 100);

        return new CompareResponse(
            compatibilityScore,
            sharedLikes.OrderBy(x => x.Name).ToList(),
            conflicts.OrderBy(x => x.Name).ToList(),
            discoveryFromA.OrderBy(x => x.Name).ToList(),
            discoveryFromB.OrderBy(x => x.Name).ToList());
    }

    private static ArtistCard ToArtistCard(Artist artist)
    {
        var metadata = JsonSerializer.Deserialize<SpotifyArtistMetadata>(artist.MetadataJson) ?? new SpotifyArtistMetadata();
        return new ArtistCard(artist.Id, artist.Name, metadata.ImageUrl, metadata.Genres);
    }
}
