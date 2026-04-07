using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Common;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DirectMessagesController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;

    public DirectMessagesController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IEnumerable<ConversationResponse>>> GetConversations(
        [FromQuery] Guid userId,
        [FromQuery] int messageLimit = 60,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        messageLimit = Math.Clamp(messageLimit, 10, 200);

        var messages = await _dbContext.DirectMessages
            .AsNoTracking()
            .Include(message => message.SenderUser)
            .Include(message => message.RecipientUser)
            .Where(message => message.SenderUserId == currentUserId || message.RecipientUserId == currentUserId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .ToListAsync(cancellationToken);

        var partnerIds = messages
            .Select(message => message.SenderUserId == currentUserId ? message.RecipientUserId : message.SenderUserId)
            .Distinct()
            .ToList();

        var partnerCursorLookup = await _dbContext.DirectMessageReadCursors
            .AsNoTracking()
            .Where(cursor => cursor.PartnerUserId == currentUserId && partnerIds.Contains(cursor.UserId))
            .ToDictionaryAsync(cursor => cursor.UserId, cancellationToken);

        var grouped = messages
            .GroupBy(message => message.SenderUserId == currentUserId ? message.RecipientUserId : message.SenderUserId)
            .Select(group =>
            {
                var orderedMessages = group
                    .OrderBy(message => message.CreatedAtUtc)
                    .ThenBy(message => message.Id)
                    .TakeLast(messageLimit)
                    .ToList();
                var firstMessage = orderedMessages.First();
                var partner = firstMessage.SenderUserId == currentUserId
                    ? firstMessage.RecipientUser
                    : firstMessage.SenderUser;

                var unreadCount = group.Count(message => message.RecipientUserId == currentUserId && !message.IsRead);

                return new ConversationResponse
                {
                    PartnerUserId = partner?.Id ?? Guid.Empty,
                    PartnerName = partner?.DisplayName ?? partner?.UserName ?? "Member",
                    PartnerRole = partner?.Role ?? "User",
                    PartnerLastVisibleMessageId = partner != null && partnerCursorLookup.TryGetValue(partner.Id, out var cursor)
                        ? cursor.LastVisibleMessageId
                        : null,
                    LastMessageAtUtc = orderedMessages.Last().CreatedAtUtc,
                    IsPartnerOnline = false,
                    UnreadCount = unreadCount,
                    Messages = orderedMessages.Select(MapMessage).ToList()
                };
            })
            .OrderByDescending(conversation => conversation.LastMessageAtUtc)
            .ToList();

        return Ok(grouped);
    }

    [HttpPost]
    public async Task<ActionResult<SubmissionResult<DirectMessageResponse>>> Send(
        SendDirectMessageRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.SenderUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.RecipientUserId == Guid.Empty)
        {
            return BadRequest("RecipientUserId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Message content is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        if (currentUserId == request.RecipientUserId)
        {
            return BadRequest("You cannot send a direct message to yourself.");
        }

        var sender = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);
        var recipient = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == request.RecipientUserId, cancellationToken);
        if (sender == null || recipient == null)
        {
            return NotFound("Sender or recipient user not found.");
        }

        var frozenError = EnsureActiveUser(sender);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var entityId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var moderation = await _moderationService.EvaluateAsync(entityId, rawContent, "MESSAGE", cancellationToken);
        moderation.ContentType = nameof(DirectMessage);
        moderation.UserId = sender.Id;
        moderation.ContentSnapshot = rawContent;
        _dbContext.ModerationResults.Add(moderation);

        var outcome = await _sanctionService.EvaluateAsync(sender, moderation, cancellationToken);
        if (outcome.Warning != null)
        {
            _dbContext.Warnings.Add(outcome.Warning);
        }

        if (outcome.Freeze != null)
        {
            _dbContext.Freezes.Add(outcome.Freeze);
        }

        DirectMessage? entity = null;
        if (moderation.Action == "Allow")
        {
            entity = new DirectMessage
            {
                Id = entityId,
                SenderUserId = currentUserId,
                RecipientUserId = request.RecipientUserId,
                Content = rawContent,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            };

            _dbContext.DirectMessages.Add(entity);
            _dbContext.Notifications.Add(new CommunityNotification
            {
                Id = Guid.NewGuid(),
                UserId = request.RecipientUserId,
                Type = "Message",
                Title = "New direct message",
                Detail = $"{sender.DisplayName} sent you a message.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.send",
            nameof(DirectMessage),
            entity?.Id.ToString() ?? entityId.ToString(),
            $"action={moderation.Action};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
            currentUserId,
            cancellationToken);

        if (entity != null)
        {
            entity.SenderUser = sender;
            entity.RecipientUser = recipient;
        }

        return Ok(new SubmissionResult<DirectMessageResponse>
        {
            Data = entity == null ? null : MapMessage(entity),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpPost("conversations/{partnerUserId:guid}/cursor")]
    public async Task<ActionResult> UpdateConversationReadCursor(
        Guid partnerUserId,
        UpdateConversationReadCursorRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.LastVisibleMessageId == Guid.Empty)
        {
            return BadRequest("LastVisibleMessageId is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var candidate = await _dbContext.DirectMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(message =>
                message.Id == request.LastVisibleMessageId &&
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId,
                cancellationToken);

        if (candidate is null)
        {
            return NotFound("Message not found in this conversation.");
        }

        var cursor = await _dbContext.DirectMessageReadCursors
            .FirstOrDefaultAsync(
                item => item.UserId == currentUserId && item.PartnerUserId == partnerUserId,
                cancellationToken);

        if (cursor != null && !IsLaterVisibleMessage(candidate, cursor.LastVisibleMessageAtUtc, cursor.LastVisibleMessageId))
        {
            return Ok(new { updated = 0, lastVisibleMessageId = cursor.LastVisibleMessageId });
        }

        if (cursor == null)
        {
            cursor = new DirectMessageReadCursor
            {
                UserId = currentUserId,
                PartnerUserId = partnerUserId,
                LastVisibleMessageId = candidate.Id,
                LastVisibleMessageAtUtc = candidate.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.DirectMessageReadCursors.Add(cursor);
        }
        else
        {
            cursor.LastVisibleMessageId = candidate.Id;
            cursor.LastVisibleMessageAtUtc = candidate.CreatedAtUtc;
            cursor.UpdatedAtUtc = DateTime.UtcNow;
        }

        var targets = await _dbContext.DirectMessages
            .Where(message =>
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId &&
                !message.IsRead &&
                message.CreatedAtUtc <= candidate.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var message in targets)
        {
            message.IsRead = true;
            message.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.cursor",
            nameof(DirectMessage),
            currentUserId.ToString(),
            $"partner={partnerUserId};message={candidate.Id};count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count, lastVisibleMessageId = candidate.Id });
    }

    [HttpPost("conversations/{partnerUserId:guid}/read")]
    public async Task<ActionResult> MarkConversationRead(
        Guid partnerUserId,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var targets = await _dbContext.DirectMessages
            .Where(message =>
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId &&
                !message.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var message in targets)
        {
            message.IsRead = true;
            message.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.read",
            nameof(DirectMessage),
            currentUserId.ToString(),
            $"partner={partnerUserId};count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count });
    }

    private static bool IsLaterVisibleMessage(
        DirectMessage candidate,
        DateTime? currentVisibleAtUtc,
        Guid? currentVisibleMessageId)
    {
        if (currentVisibleAtUtc == null || currentVisibleMessageId == null)
        {
            return true;
        }

        if (candidate.CreatedAtUtc > currentVisibleAtUtc.Value)
        {
            return true;
        }

        if (candidate.CreatedAtUtc < currentVisibleAtUtc.Value)
        {
            return false;
        }

        return string.CompareOrdinal(candidate.Id.ToString(), currentVisibleMessageId.Value.ToString()) > 0;
    }

    private static DirectMessageResponse MapMessage(DirectMessage message)
    {
        return new DirectMessageResponse
        {
            Id = message.Id,
            SenderUserId = message.SenderUserId,
            SenderName = message.SenderUser?.DisplayName ?? message.SenderUser?.UserName ?? "Member",
            RecipientUserId = message.RecipientUserId,
            RecipientName = message.RecipientUser?.DisplayName ?? message.RecipientUser?.UserName ?? "Member",
            Content = message.Content,
            CreatedAtUtc = message.CreatedAtUtc,
            IsRead = message.IsRead
        };
    }
}
