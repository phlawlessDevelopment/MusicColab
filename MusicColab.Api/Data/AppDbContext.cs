using Microsoft.EntityFrameworkCore;
using MusicColab.Api.Models;

namespace MusicColab.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<UserArtistPreference> UserArtistPreferences => Set<UserArtistPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Artist>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(64);
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.MetadataJson).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<UserArtistPreference>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.ArtistId });
            entity.Property(x => x.Preference).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.Preferences)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Artist)
                .WithMany(x => x.Preferences)
                .HasForeignKey(x => x.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
