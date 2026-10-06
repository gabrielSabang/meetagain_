using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/friends")]
    [Route("api/v1/friends")]
    public class FriendsController : ControllerBase
    {
        private readonly MongoDbContext _ctx;

        public FriendsController(MongoDbContext ctx) => _ctx = ctx;

        private Task<string?> CallerUidAsync(string? userIdQuery = null, string? userIdBody = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, userIdQuery, userIdBody);

        private static IActionResult NeedAuth() => new UnauthorizedObjectResult(ApiErrors.Unauthorized());

        // GET /api/friends?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetFriends([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            var friends = await _ctx.Friends.Find(f => f.UserId == uid).SortBy(f => f.Name).ToListAsync();
            return Ok(friends);
        }

        // GET /api/friends/requests?userId={uid}
        [HttpGet("requests")]
        public async Task<IActionResult> GetRequests([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            var list = await _ctx.FriendRequests.Find(r => r.UserId == uid && r.Status == "pending").ToListAsync();
            return Ok(list);
        }

        // POST /api/friends/requests  { userId, recipientEmail }
        [HttpPost("requests")]
        public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestBody body)
        {
            var uid = await CallerUidAsync(null, body.UserId);
            if (uid == null) return NeedAuth();
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (string.IsNullOrWhiteSpace(body.RecipientEmail))
                return BadRequest(ApiErrors.BadRequest("recipientEmail is required."));

            var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
            if (me == null) return NotFound(ApiErrors.NotFound("Caller user document not found."));

            var recipient = await _ctx.Users.Find(u => u.Email == body.RecipientEmail.Trim().ToLowerInvariant()).FirstOrDefaultAsync();
            if (recipient == null) return NotFound(ApiErrors.NotFound("Recipient not found."));
            if (recipient.Uid == uid) return BadRequest(ApiErrors.BadRequest("Cannot send friend request to yourself."));

            if (await _ctx.Friends.Find(f => f.Id == $"{uid}:{recipient.Uid}").AnyAsync())
                return Conflict(ApiErrors.Conflict("Already friends."));
            if (await _ctx.FriendRequests.Find(r => r.Id == $"{recipient.Uid}:{uid}").AnyAsync())
                return Conflict(ApiErrors.Conflict("Friend request already sent."));

            await _ctx.FriendRequests.InsertOneAsync(new FriendRequest
            {
                Id = $"{recipient.Uid}:{uid}", UserId = recipient.Uid, FromUserId = uid,
                FromUserEmail = me.Email, FromUserName = me.DisplayName,
                Status = "pending", SentAt = DateTime.UtcNow.ToString("o"),
            });
            await CreateNotificationAsync(recipient.Uid, "friend_request", $"{me.DisplayName} sent you a friend request");
            return Ok(new { message = "Friend request sent.", to = recipient.Uid });
        }

        // POST /api/friends/requests/{fromUserId}/accept  { userId }
        [HttpPost("requests/{fromUserId}/accept")]
        public async Task<IActionResult> Accept(string fromUserId, [FromBody] FriendActionBody? body, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId, body?.UserId);
            if (uid == null) return NeedAuth();

            var req = await _ctx.FriendRequests.Find(r => r.Id == $"{uid}:{fromUserId}").FirstOrDefaultAsync();
            if (req == null) return NotFound(ApiErrors.NotFound("Friend request not found."));
            var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
            if (me == null) return NotFound(ApiErrors.NotFound("Caller user document not found."));

            var now = DateTime.UtcNow.ToString("o");
            await _ctx.Friends.ReplaceOneAsync(f => f.Id == $"{uid}:{fromUserId}",
                new Friend { Id = $"{uid}:{fromUserId}", UserId = uid, FriendId = fromUserId, Name = req.FromUserName, Email = req.FromUserEmail, AddedAt = now },
                new ReplaceOptions { IsUpsert = true });
            await _ctx.Friends.ReplaceOneAsync(f => f.Id == $"{fromUserId}:{uid}",
                new Friend { Id = $"{fromUserId}:{uid}", UserId = fromUserId, FriendId = uid, Name = me.DisplayName, Email = me.Email, AddedAt = now },
                new ReplaceOptions { IsUpsert = true });
            await _ctx.FriendRequests.DeleteOneAsync(r => r.Id == $"{uid}:{fromUserId}");

            await CreateNotificationAsync(fromUserId, "friend_accepted", $"{me.DisplayName} accepted your friend request");
            return Ok(new { message = "Friend request accepted." });
        }

        // POST /api/friends/requests/{fromUserId}/reject  { userId }
        [HttpPost("requests/{fromUserId}/reject")]
        public async Task<IActionResult> Reject(string fromUserId, [FromBody] FriendActionBody? body, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId, body?.UserId);
            if (uid == null) return NeedAuth();
            await _ctx.FriendRequests.DeleteOneAsync(r => r.Id == $"{uid}:{fromUserId}");
            return Ok(new { message = "Friend request rejected." });
        }

        // DELETE /api/friends/{friendId}?userId={uid}
        [HttpDelete("{friendId}")]
        public async Task<IActionResult> Remove(string friendId, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            await _ctx.Friends.DeleteOneAsync(f => f.Id == $"{uid}:{friendId}");
            await _ctx.Friends.DeleteOneAsync(f => f.Id == $"{friendId}:{uid}");
            return Ok(new { message = "Friend removed." });
        }

        private async Task CreateNotificationAsync(string userId, string type, string message)
        {
            try
            {
                await _ctx.Notifications.InsertOneAsync(new Notification
                {
                    Id = Guid.NewGuid().ToString(), UserId = userId, Type = type, Message = message,
                    CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                });
            }
            catch { }
        }
    }
}
