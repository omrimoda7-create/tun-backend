using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.UserName).IsUnique();
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.UserName).HasMaxLength(320);
        builder.Property(user => user.DisplayName).HasMaxLength(128);
        builder.Property(user => user.Gender).HasMaxLength(16).HasDefaultValue("Male");
        builder.Property(user => user.Age);
        builder.Property(user => user.AvatarUrl).HasColumnType("longtext");
        builder.Property(user => user.PasswordHash).HasMaxLength(255);
        builder.Property(user => user.Role).HasMaxLength(32);
        builder.Property(user => user.FailedLoginAttempts).HasDefaultValue(0);
    }
}
