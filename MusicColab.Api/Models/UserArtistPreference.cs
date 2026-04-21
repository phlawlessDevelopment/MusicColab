namespace MusicColab.Api.Models;

public sealed class UserArtistPreference
{
    public Guid UserId { get; set; }
    public string ArtistId { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? PreviewUrl { get; set; }
    public string TagsJson { get; set; } = "[]";
    public PreferenceValue Preference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
}
