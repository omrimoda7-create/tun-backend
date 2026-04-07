using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.User;
using TunSociety.Api.Infrastructure;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult> GetAuditLogs([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var logs = await _dbContext.AuditLogs
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(UserResponse.FromEntity).ToList());
    }
}
