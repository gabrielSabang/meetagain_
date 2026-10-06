using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("health")]
    [Route("api/health")]
    [Route("api/v1/health")]
    public class HealthController : ControllerBase
    {
        private static readonly DateTime StartedAt = DateTime.UtcNow;
        private readonly MongoDbContext _ctx;

        public HealthController(MongoDbContext ctx) => _ctx = ctx;

        // GET /health — confirms the DB connection.
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            if (!await _ctx.EnsureConnectedAsync(ct))
            {
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    db = "disconnected",
                    error = _ctx.LastError,
                    hint = "If Atlas times out, add your IP in Atlas > Network Access > IP Access List, or check MONGODB_URI credentials.",
                    timestamp = DateTime.UtcNow.ToString("o"),
                });
            }

            try
            {
                var counts = new Dictionary<string, long>
                {
                    ["users"] = await _ctx.Users.CountDocumentsAsync(FilterDefinition<Models.AppUser>.Empty, cancellationToken: ct),
                    ["meetups"] = await _ctx.Meetups.CountDocumentsAsync(FilterDefinition<Models.Meetup>.Empty, cancellationToken: ct),
                    ["meetupParticipants"] = await _ctx.MeetupParticipants.CountDocumentsAsync(FilterDefinition<Models.MeetupParticipant>.Empty, cancellationToken: ct),
                    ["groups"] = await _ctx.Groups.CountDocumentsAsync(FilterDefinition<Models.Group>.Empty, cancellationToken: ct),
                    ["groupMembers"] = await _ctx.GroupMembers.CountDocumentsAsync(FilterDefinition<Models.GroupMember>.Empty, cancellationToken: ct),
                    ["friends"] = await _ctx.Friends.CountDocumentsAsync(FilterDefinition<Models.Friend>.Empty, cancellationToken: ct),
                    ["friendRequests"] = await _ctx.FriendRequests.CountDocumentsAsync(FilterDefinition<Models.FriendRequest>.Empty, cancellationToken: ct),
                    ["notifications"] = await _ctx.Notifications.CountDocumentsAsync(FilterDefinition<Models.Notification>.Empty, cancellationToken: ct),
                };
                return Ok(new
                {
                    status = "healthy",
                    db = "connected",
                    database = _ctx.DatabaseName,
                    usedFallback = _ctx.UsedFallback,
                    uptimeSeconds = (DateTime.UtcNow - StartedAt).TotalSeconds,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    collections = counts,
                });
            }
            catch (Exception ex)
            {
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    db = "disconnected",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("o"),
                });
            }
        }
    }
}
