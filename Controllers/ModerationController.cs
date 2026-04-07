using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.AdminOrModerator)]
public class ModerationController : AppControllerBase
{
    private static readonly string[] ReviewActions = ["Flag", "Block"];
    private static readonly string[] AppealStatuses = ["Open", "Accepted", "Rejected"];

    private readonly ApplicationDbContext _dbContext;
    private readonly LocalAiService _localAiService;
    private readonly ModerationService _moderationService;
    private readonly AuditService _auditService;

    public ModerationController(
        ApplicationDbContext dbContext,
        LocalAiService localAiService,
        ModerationService moderationService,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _localAiService = localAiService;
        _moderationService = moderationService;
        _auditService = auditService;
    }

    [HttpPost("score")]
    public async Task<ActionResult<ModerationResponse>> Score(ModerationRequest request, CancellationToken cancellationToken)
    {
        var messageId = request.MessageId ?? Guid.NewGuid();
        var result = await _moderationService.EvaluateAsync(messageId, request.Content, request.ContentType, cancellationToken);

        return Ok(new ModerationResponse
        {
            MessageId = messageId,
            Score = result.Score,
            Flags = [.. result.Flags],
            Action = result.Action,
            Reason = result.Reason
        });
    }

    [HttpPost]
    public async Task<ActionResult<LocalAiModerationResult>> Moderate(ModerationRequest request, CancellationToken cancellationToken)
    {
        var localResult = await _localAiService.ModerateAsync(
            request.Content,
            request.ContentType,
            cancellationToken);

        return Ok(localResult);
    }

    [HttpGet("flagged-content")]
    public async Task<ActionResult<IEnumerable<FlaggedContentReviewResponse>>> GetFlaggedContent(
        [FromQuery] int take = 50,
        [FromQuery] string? action = null,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var normalizedAction = NormalizeReviewAction(action);
        if (action is not null && normalizedAction is null)
        {
            return BadRequest("Action must be Flag or Block.");
        }

        var query = _dbContext.ModerationResults
            .AsNoTracking()
            .Include(result => result.User)
            .Where(result => result.Action != "Allow");

        if (normalizedAction is not null)
        {
            query = query.Where(result => result.Action == normalizedAction);
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(result => result.UserId == filteredUserId);
        }

        var records = await query
            .OrderByDescending(result => result.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = records
            .Select(result => new FlaggedContentReviewResponse
            {
                ModerationResultId = result.Id,
                ContentId = result.ContentId,
                MessageId = result.ContentId,
                ContentType = result.ContentType,
                UserId = result.UserId,
                UserDisplayName = result.User != null ? result.User.DisplayName : "Unknown",
                UserEmail = result.User != null ? result.User.Email : string.Empty,
                Content = result.ContentSnapshot,
                Score = result.Score,
                Action = result.Action,
                Reason = result.Reason,
                Flags = [.. result.Flags],
                CreatedAtUtc = result.CreatedAtUtc
            })
            .ToList();

        return Ok(items);
    }

    [HttpGet("warnings")]
    public async Task<ActionResult<IEnumerable<WarningReviewResponse>>> GetWarnings(
        [FromQuery] int take = 50,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Warnings
            .AsNoTracking()
            .Include(warning => warning.User)
            .AsQueryable();

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(warning => warning.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(warning => warning.IssuedAtUtc)
            .Take(take)
            .Select(warning => new WarningReviewResponse
            {
                Id = warning.Id,
                UserId = warning.UserId,
                UserDisplayName = warning.User != null ? warning.User.DisplayName : "Unknown",
                UserEmail = warning.User != null ? warning.User.Email : string.Empty,
                Reason = warning.Reason,
                IssuedAtUtc = warning.IssuedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("freezes")]
    public async Task<ActionResult<IEnumerable<FreezeReviewResponse>>> GetFreezes(
        [FromQuery] int take = 50,
        [FromQuery] bool activeOnly = false,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Freezes
            .AsNoTracking()
            .Include(freeze => freeze.User)
            .AsQueryable();

        if (activeOnly)
        {
            query = query.Where(freeze => freeze.IsActive);
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(freeze => freeze.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(freeze => freeze.StartsAtUtc)
            .Take(take)
            .Select(freeze => new FreezeReviewResponse
            {
                Id = freeze.Id,
                UserId = freeze.UserId,
                UserDisplayName = freeze.User != null ? freeze.User.DisplayName : "Unknown",
                UserEmail = freeze.User != null ? freeze.User.Email : string.Empty,
                Reason = freeze.Reason,
                StartsAtUtc = freeze.StartsAtUtc,
                EndsAtUtc = freeze.EndsAtUtc,
                IsActive = freeze.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("appeals")]
    public async Task<ActionResult<IEnumerable<AppealReviewResponse>>> GetAppeals(
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var normalizedStatus = NormalizeAppealStatus(status);
        if (status is not null && normalizedStatus is null)
        {
            return BadRequest("Status must be Open, Accepted, or Rejected.");
        }

        var query = _dbContext.Appeals
            .AsNoTracking()
            .Include(appeal => appeal.User)
            .AsQueryable();

        if (normalizedStatus is not null)
        {
            query = query.Where(appeal => appeal.Status == normalizedStatus);
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(appeal => appeal.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(appeal => appeal.CreatedAtUtc)
            .Take(take)
            .Select(appeal => new AppealReviewResponse
            {
                Id = appeal.Id,
                UserId = appeal.UserId,
                UserDisplayName = appeal.User != null ? appeal.User.DisplayName : "Unknown",
                UserEmail = appeal.User != null ? appeal.User.Email : string.Empty,
                TargetType = appeal.TargetType,
                TargetId = appeal.TargetId,
                Status = appeal.Status,
                Reason = appeal.Reason,
                CreatedAtUtc = appeal.CreatedAtUtc,
                ResolvedAtUtc = appeal.ResolvedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPut("appeals/{id:guid}/status")]
    public async Task<ActionResult<AppealReviewResponse>> UpdateAppealStatus(
        Guid id,
        UpdateAppealStatusRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeAppealStatus(request.Status);
        if (normalizedStatus is null)
        {
            return BadRequest("Status must be Open, Accepted, or Rejected.");
        }

        var appeal = await _dbContext.Appeals
            .Include(current => current.User)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (appeal == null)
        {
            return NotFound("Appeal not found.");
        }

        appeal.Status = normalizedStatus;
        appeal.ResolvedAtUtc = normalizedStatus == "Open"
            ? null
            : DateTime.UtcNow;

        if (normalizedStatus == "Accepted")
        {
            await AcceptAppealAsync(appeal, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "appeal.review",
            nameof(Models.Appeal),
            appeal.Id.ToString(),
            $"status={appeal.Status}",
            CurrentUserId,
            cancellationToken);

        return Ok(new AppealReviewResponse
        {
            Id = appeal.Id,
            UserId = appeal.UserId,
            UserDisplayName = appeal.User?.DisplayName ?? "Unknown",
            UserEmail = appeal.User?.Email ?? string.Empty,
            TargetType = appeal.TargetType,
            TargetId = appeal.TargetId,
            Status = appeal.Status,
            Reason = appeal.Reason,
            CreatedAtUtc = appeal.CreatedAtUtc,
            ResolvedAtUtc = appeal.ResolvedAtUtc
        });
    }

    private static string? NormalizeReviewAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant() switch
        {
            "flag" => "Flag",
            "block" => "Block",
            _ => null
        };

        return normalized is not null && ReviewActions.Contains(normalized)
            ? normalized
            : null;
    }

    private static string? NormalizeAppealStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "accepted" => "Accepted",
            "rejected" => "Rejected",
            _ => null
        };

        return normalized is not null && AppealStatuses.Contains(normalized)
            ? normalized
            : null;
    }

    private async Task AcceptAppealAsync(Models.Appeal appeal, CancellationToken cancellationToken)
    {
        if (!appeal.TargetType.Equals(nameof(Models.Freeze), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var freeze = await _dbContext.Freezes
            .FirstOrDefaultAsync(current => current.Id == appeal.TargetId && current.UserId == appeal.UserId, cancellationToken);

        if (freeze == null)
        {
            return;
        }

        freeze.IsActive = false;
        freeze.EndsAtUtc = DateTime.UtcNow;

        var hasActiveFreeze = await _dbContext.Freezes
            .AnyAsync(current => current.UserId == appeal.UserId && current.IsActive && current.Id != freeze.Id, cancellationToken);

        if (hasActiveFreeze)
        {
            return;
        }

        var user = appeal.User ?? await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == appeal.UserId, cancellationToken);
        if (user != null)
        {
            user.IsFrozen = false;
        }
    }
}
