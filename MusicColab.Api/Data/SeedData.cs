using Microsoft.EntityFrameworkCore;
using MusicColab.Api.Models;

namespace MusicColab.Api.Data;

public static class SeedData
{
    private static readonly Guid AliceUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BobUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task EnsureSeedDataAsync(AppDbContext dbContext)
    {
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
    }
}
