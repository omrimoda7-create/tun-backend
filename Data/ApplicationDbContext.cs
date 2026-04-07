using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ModerationResult> ModerationResults => Set<ModerationResult>();
    public DbSet<Warning> Warnings => Set<Warning>();
    public DbSet<Freeze> Freezes => Set<Freeze>();
    public DbSet<Appeal> Appeals => Set<Appeal>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<PostReaction> PostReactions => Set<PostReaction>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<CommunityNotification> Notifications => Set<CommunityNotification>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<DirectMessageReadCursor> DirectMessageReadCursors => Set<DirectMessageReadCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
