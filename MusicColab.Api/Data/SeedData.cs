using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicColab.Api.Models;
using MusicColab.Api.Services;

namespace MusicColab.Api.Data;

public static class SeedData
{
    private static readonly Guid AliceUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BobUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task EnsureSeedDataAsync(AppDbContext dbContext)
    {
        // Keep local data stable across restarts; deleting the SQLite file on startup
        // can cause WAL/file-handle contention and intermittent disk I/O failures.
        await dbContext.Database.EnsureCreatedAsync();

        if (!await dbContext.Users.AnyAsync())
        {
            var users = new[]
            {
                new AppUser
                {
                    Id = AliceUserId,
                    Email = "alice@example.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new AppUser
                {
                    Id = BobUserId,
                    Email = "bob@example.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };

            dbContext.Users.AddRange(users);
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.Artists.AnyAsync())
        {
            var artists = new[]
            {
                CreateArtist("06HL4z0CvFAxyc27GXpf02", "Taylor Swift", "https://i.scdn.co/image/ab6761610000e5eb5b2da061aa7b4f11d6b2f2cd", ["pop"]),
                CreateArtist("4gzpq5DPGxSnKTe4SA8HAU", "Coldplay", "https://i.scdn.co/image/ab6761610000e5ebf1819b7397f48a1875d5f2c2", ["permanent wave", "pop rock"]),
                CreateArtist("3TVXtAsR1Inumwj472S9r4", "Drake", "https://i.scdn.co/image/ab6761610000e5eb4293385d324db8558179afd9", ["canadian hip hop"]),
                CreateArtist("1Xyo4u8uXC1ZmMpatF05PJ", "The Weeknd", "https://i.scdn.co/image/ab6761610000e5ebfc0cfd43de13e9f8c3a0bd0f", ["canadian contemporary r&b"]),
                CreateArtist("6eUKZXaKkcviH0Ku9w2n3V", "Ed Sheeran", "https://i.scdn.co/image/ab6761610000e5eb3bcef85e105dfc42399ef0f5", ["pop"]),

                // Additional seeded artists (10)
                CreateArtist("6vWDO969PvNqNYHIOW5v0m", "Beyoncé", "https://i.scdn.co/image/ab6761610000e5eb1a2b3c4d5e6f7a8b9c0d1e2f3", ["pop", "r&b"]),
                CreateArtist("4Z8W4fKeB5YxbusRsdQVPb", "Radiohead", "https://i.scdn.co/image/ab6761610000e5ebc9a0b1c2d3e4f567890abcdef", ["alternative rock", "art rock"]),
                CreateArtist("7Ln80lUS6He07XvHI8qqHH", "Arctic Monkeys", "https://i.scdn.co/image/ab6761610000e5eb1234567890abcdef12345678", ["indie rock", "garage rock"]),
                CreateArtist("2YZyLoL8N0Wb9xBt1NhZWg", "Kendrick Lamar", "https://i.scdn.co/image/ab6761610000e5ebabcdef1234567890abcdef12", ["hip hop", "rap"]),
                CreateArtist("5c5ybZQpX2fP4L8s3NX1Yt", "Lorde", "https://i.scdn.co/image/ab6761610000e5eb0a1b2c3d4e5f67890abcdef01", ["alternative pop"]),
                CreateArtist("0C8ZW7ezQVs4URX5aX7Kqx", "The Killers", "https://i.scdn.co/image/ab6761610000e5eb222222222222222222222222", ["alternative rock", "indie rock"]),
                CreateArtist("2fenSS68JI1h4Fo296JfGr", "Florence + The Machine", "https://i.scdn.co/image/ab6761610000e5eb333333333333333333333333", ["indie pop", "baroque pop"]),
                CreateArtist("7jy3rLJdDQY21OgRLCZ9sD", "Foo Fighters", "https://i.scdn.co/image/ab6761610000e5eb444444444444444444444444", ["rock"]),
                CreateArtist("5WUlDfRSoLAfcVSX1WnrxN", "Sia", "https://i.scdn.co/image/ab6761610000e5eb555555555555555555555555", ["pop"]),
                CreateArtist("53XhwfbYqKCa1cC15pYq2q", "Imagine Dragons", "https://i.scdn.co/image/ab6761610000e5eb666666666666666666666666", ["alternative rock", "pop rock"])
            };

            dbContext.Artists.AddRange(artists);
            await dbContext.SaveChangesAsync();
        }
    }

    private static Artist CreateArtist(string id, string name, string imageUrl, IReadOnlyList<string> genres)
    {
        return new Artist
        {
            Id = id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new SpotifyArtistMetadata
            {
                ImageUrl = imageUrl,
                Genres = genres
            })
        };
    }
}
