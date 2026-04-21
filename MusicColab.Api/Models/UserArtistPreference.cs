namespace MusicColab.Api.Models;

public sealed class UserArtistPreference
{
    public Guid UserId { get; set; }
    public string ArtistId { get; set; } = string.Empty;
    public PreferenceValue Preference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
    public Artist Artist { get; set; } = null!;
}
