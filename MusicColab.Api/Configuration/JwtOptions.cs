namespace MusicColab.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "MusicColab";
    public string Audience { get; init; } = "MusicColab.Web";
    public string SigningKey { get; init; } = "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_KEY";
    public int ExpirationMinutes { get; init; } = 60 * 24;
}
