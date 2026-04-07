using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AuditService _auditService;

    public NotificationsController(ApplicationDbContext dbContext, AuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationResponse>>> GetByUser(
        [FromQuery] Guid userId,
        [FromQuery] bool includeRead = true,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;

        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == currentUserId);

        if (!includeRead)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        var items = await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(take)
            .Select(notification => new NotificationResponse
            {
                Id = notification.Id,
                UserId = notification.UserId,
                Type = notification.Type,
                Title = notification.Title,
                Detail = notification.Detail,
                IsRead = notification.IsRead,
                CreatedAtUtc = notification.CreatedAtUtc,
                ReadAtUtc = notification.ReadAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize(Roles = RoleNames.AdminOrModerator)]
    [HttpPost]
    public async Task<ActionResult<NotificationResponse>> Create(
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (request.UserId == Guid.Empty)
        {
            return BadRequest("userId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Detail))
        {
            return BadRequest("Title and detail are required.");
        }

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var entity = new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? "System" : request.Type.Trim(),
            Title = request.Title.Trim(),
            Detail = request.Detail.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.create",
            nameof(CommunityNotification),
            entity.Id.ToString(),
            $"type={entity.Type}",
            currentUserId,
            cancellationToken);

        return Ok(new NotificationResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Type = entity.Type,
            Title = entity.Title,
            Detail = entity.Detail,
            IsRead = entity.IsRead,
            CreatedAtUtc = entity.CreatedAtUtc,
            ReadAtUtc = entity.ReadAtUtc
        });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(
        Guid id,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var entity = await _dbContext.Notifications.FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound("Notification not found.");
        }

        if (entity.UserId != currentUserId)
        {
            return Forbid();
        }

        entity.IsRead = true;
        entity.ReadAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.read",
            nameof(CommunityNotification),
            entity.Id.ToString(),
            null,
            currentUserId,
            cancellationToken);

        return Ok(new NotificationResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Type = entity.Type,
            Title = entity.Title,
            Detail = entity.Detail,
            IsRead = entity.IsRead,
            CreatedAtUtc = entity.CreatedAtUtc,
            ReadAtUtc = entity.ReadAtUtc
        });
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(
        MarkAllNotificationsReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var targets = await _dbContext.Notifications
            .Where(notification => notification.UserId == currentUserId && !notification.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var item in targets)
        {
            item.IsRead = true;
            item.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.readall",
            nameof(CommunityNotification),
            currentUserId.ToString(),
            $"count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count });
    }
}
