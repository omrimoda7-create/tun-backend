using TunSociety.Api.Data;
using TunSociety.Api.Models;

namespace TunSociety.Api.Services;

public class AuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(ApplicationDbContext dbContext, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(string action, string entityType, string entityId, string? data, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            _dbContext.AuditLogs.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist audit log for action {Action} on {EntityType}/{EntityId}",
                action,
                entityType,
                entityId);
        }
    }
}
