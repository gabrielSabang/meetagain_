using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Route("api/v1/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly MongoDbContext _ctx;

        public NotificationsController(MongoDbContext ctx) => _ctx = ctx;

        private Task<string?> CallerUidAsync(string? q = null) => ApiUserContext.RequireUserIdAsync(HttpContext, q);
        private static IActionResult NeedAuth() => new UnauthorizedObjectResult(ApiErrors.Unauthorized());

        // GET /api/notifications?userId={uid}&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId, [FromQuery] int? page, [FromQuery] int? pageSize)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var p = page.GetValueOrDefault(1);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 100);
            var list = await _ctx.Notifications.Find(n => n.UserId == uid)
                .SortByDescending(n => n.CreatedAt).Skip((p - 1) * ps).Limit(ps).ToListAsync();
            var dtos = list.Select(NotificationService.ToDto).ToList();

            if (page.HasValue || pageSize.HasValue)
            {
                var total = await _ctx.Notifications.CountDocumentsAsync(n => n.UserId == uid);
                return Ok(new PagedResult<NotificationDto> { Data = dtos, Page = p, PageSize = ps, Total = total });
            }
            return Ok(dtos);
        }

        // GET /api/notifications/unread-count?userId={uid}
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            var count = await _ctx.Notifications.CountDocumentsAsync(n => n.UserId == uid && !n.IsRead);
            return Ok(new { count });
        }

        // POST /api/notifications/{id}/read?userId={uid}
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            await _ctx.Notifications.UpdateOneAsync(n => n.Id == id && n.UserId == uid,
                Builders<Notification>.Update.Set(n => n.IsRead, true));
            return Ok(new { message = "Marked as read." });
        }

        // POST /api/notifications/read-all?userId={uid}
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            var res = await _ctx.Notifications.UpdateManyAsync(n => n.UserId == uid && !n.IsRead,
                Builders<Notification>.Update.Set(n => n.IsRead, true));
            return Ok(new { message = $"Marked {res.ModifiedCount} notification(s) as read." });
        }

        // DELETE /api/notifications/{id}?userId={uid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            await _ctx.Notifications.DeleteOneAsync(n => n.Id == id && n.UserId == uid);
            return Ok(new { message = "Notification deleted." });
        }
    }
}
