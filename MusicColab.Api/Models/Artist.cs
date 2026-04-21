namespace MusicColab.Api.Models;

public sealed class Artist
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserArtistPreference> Preferences { get; set; } = new List<UserArtistPreference>();
}
