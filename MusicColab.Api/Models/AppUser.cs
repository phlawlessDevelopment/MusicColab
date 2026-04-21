namespace MusicColab.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserArtistPreference> Preferences { get; set; } = new List<UserArtistPreference>();
}
