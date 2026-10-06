using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Route("api/v1/users")]
    public class UsersController : ControllerBase
    {
        private readonly MongoDbContext _ctx;
        private readonly MongoService _db;

        public UsersController(MongoDbContext ctx, MongoService db)
        {
            _ctx = ctx;
            _db = db;
        }

        // GET /api/users?page=&pageSize= (paginated when params present, else legacy array)
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? page, [FromQuery] int? pageSize)
        {
            if (page.HasValue || pageSize.HasValue)
            {
                var p = page.GetValueOrDefault(1);
                var ps = Math.Clamp(pageSize.GetValueOrDefault(20), 1, 100);
                var users = await _db.GetAllUsersAsync(p, ps);
                var total = await _db.CountUsersAsync();
                return Ok(new PagedResult<object>
                {
                    Data = users.Select(Safe).Cast<object>().ToList(),
                    Page = p, PageSize = ps, Total = total,
                });
            }

            var all = await _db.GetAllUsersAsync(1, 100);
            return Ok(all.Select(Safe).ToList());
        }

        // GET /api/users/search?email=a@b.com&userId=<callerUid>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string email, [FromQuery] string? userId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(ApiErrors.BadRequest("email query param is required."));

            var (callerId, _) = await ApiUserContext.ResolveAsync(HttpContext, userId);
            if (string.IsNullOrWhiteSpace(callerId))
                return Unauthorized(ApiErrors.Unauthorized());

            var user = await _db.GetUserByEmailAsync(email.Trim().ToLowerInvariant());
            if (user == null || user.Uid == callerId)
                return NotFound(ApiErrors.NotFound("User not found."));
            return Ok(Safe(user));
        }

        // GET /api/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var user = await _db.GetUserAsync(id);
            if (user == null) return NotFound(ApiErrors.NotFound("User not found."));
            return Ok(Safe(user));
        }

        // PUT /api/users/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Upsert(string id, [FromBody] UpdateUserRequest req)
        {
            var existing = await _db.GetUserAsync(id);
            if (existing == null) return NotFound(ApiErrors.NotFound("User not found."));

            if (!string.IsNullOrWhiteSpace(req.Email)) existing.Email = req.Email.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(req.DisplayName)) existing.DisplayName = req.DisplayName.Trim();
            await _db.CreateOrUpdateUserAsync(existing);
            return Ok(Safe(existing));
        }

        private static object Safe(AppUser u) => new { uid = u.Uid, email = u.Email, displayName = u.DisplayName, createdAt = u.CreatedAt };
    }
}
