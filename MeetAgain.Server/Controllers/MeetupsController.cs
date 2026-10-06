using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/meetups")]
    [Route("api/v1/meetups")]
    public class MeetupsController : ControllerBase
    {
        private readonly MongoDbContext _ctx;

        public MeetupsController(MongoDbContext ctx) => _ctx = ctx;

        private Task<string?> CallerUidAsync(string? q = null, string? b = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, q, b);

        private static IActionResult NeedAuth() => new UnauthorizedObjectResult(ApiErrors.Unauthorized());

        private static MeetupDto ToDto(Meetup m, string uid, string rsvp) => new()
        {
            Id = m.Id, Title = m.Title, Description = m.Description,
            CreatorUserId = m.CreatorUserId, CreatorName = m.CreatorName,
            Location = m.Location, EventDateTime = m.EventDateTime,
            CreatedAt = m.CreatedAt, Status = m.Status,
            ParticipantCount = m.ParticipantCount,
            IsCreator = m.CreatorUserId == uid, MyRSVPStatus = rsvp,
        };

        // GET /api/meetups?userId={uid}&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId, [FromQuery] int? page, [FromQuery] int? pageSize)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var parts = await _ctx.MeetupParticipants.Find(p => p.UserId == uid).ToListAsync();
            var byMeetup = parts.ToDictionary(p => p.MeetupId);
            var ids = byMeetup.Keys.ToHashSet();
            var meetups = await _ctx.Meetups.Find(m => ids.Contains(m.Id)).SortBy(m => m.EventDateTime).ToListAsync();
            var result = meetups.Select(m => ToDto(m, uid, byMeetup[m.Id].Status)).ToList();

            if (page.HasValue || pageSize.HasValue)
            {
                var p = page.GetValueOrDefault(1);
                var ps = Math.Clamp(pageSize.GetValueOrDefault(20), 1, 100);
                return Ok(new PagedResult<MeetupDto>
                {
                    Data = result.Skip((p - 1) * ps).Take(ps).ToList(),
                    Page = p, PageSize = ps, Total = result.Count,
                });
            }
            return Ok(result);
        }

        // GET /api/meetups/{id}?userId={uid}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var meetup = await _ctx.Meetups.Find(m => m.Id == id).FirstOrDefaultAsync();
            if (meetup == null) return NotFound(ApiErrors.NotFound("Meetup not found."));
            var parts = await _ctx.MeetupParticipants.Find(p => p.MeetupId == id).SortBy(p => p.Name).ToListAsync();
            return Ok(new MeetupDetailDto
            {
                Meetup = meetup, Participants = parts,
                IsCreator = meetup.CreatorUserId == uid,
                MyRSVPStatus = parts.FirstOrDefault(p => p.UserId == uid)?.Status ?? "",
            });
        }

        // POST /api/meetups  { userId, title, description, eventDateTime, location, invitedFriendIds[] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateMeetupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(ApiErrors.BadRequest("title is required."));
            if (req.EventDateTime == default) return BadRequest(ApiErrors.BadRequest("eventDateTime is required (ISO 8601)."));

            var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
            if (me == null) return NotFound(ApiErrors.NotFound("Caller user document not found."));

            var meetupId = Guid.NewGuid().ToString("N");
            var meetup = new Meetup
            {
                Id = meetupId, Title = req.Title.Trim(), Description = req.Description ?? "",
                CreatorUserId = uid, CreatorName = me.DisplayName, Location = req.Location ?? "",
                EventDateTime = DateTime.SpecifyKind(req.EventDateTime, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow, Status = "confirmed",
                ParticipantCount = (req.InvitedFriendIds?.Count ?? 0) + 1,
            };
            await _ctx.Meetups.InsertOneAsync(meetup);
            await _ctx.MeetupParticipants.InsertOneAsync(new MeetupParticipant
            {
                Id = $"{meetupId}:{uid}", MeetupId = meetupId, UserId = uid,
                Name = me.DisplayName, Email = me.Email, Status = "accepted",
                InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = DateTime.UtcNow.ToString("o"),
            });

            if (req.InvitedFriendIds is { Count: > 0 })
            {
                foreach (var fid in req.InvitedFriendIds.Distinct())
                {
                    if (await _ctx.MeetupParticipants.Find(p => p.Id == $"{meetupId}:{fid}").AnyAsync()) continue;
                    var f = await _ctx.Users.Find(u => u.Uid == fid).FirstOrDefaultAsync();
                    if (f == null) continue;
                    await _ctx.MeetupParticipants.InsertOneAsync(new MeetupParticipant
                    {
                        Id = $"{meetupId}:{fid}", MeetupId = meetupId, UserId = fid,
                        Name = f.DisplayName, Email = f.Email, Status = "invited",
                        InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = "",
                    });
                    await _ctx.Notifications.InsertOneAsync(new Notification
                    {
                        Id = Guid.NewGuid().ToString(), UserId = fid, Type = "meetup_invite",
                        Message = $"{me.DisplayName} invited you to '{req.Title}'",
                        MeetupId = meetupId, CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                    });
                }
            }

            return CreatedAtAction(nameof(GetDetail), new { id = meetupId }, meetup);
        }

        // PUT /api/meetups/{id}  { userId(creator), title, description, eventDateTime, location, status }
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateMeetupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();

            var meetup = await _ctx.Meetups.Find(m => m.Id == id).FirstOrDefaultAsync();
            if (meetup == null) return NotFound(ApiErrors.NotFound("Meetup not found."));
            if (meetup.CreatorUserId != uid) return Forbid();

            if (!string.IsNullOrWhiteSpace(req.Title)) meetup.Title = req.Title.Trim();
            if (req.Description != null) meetup.Description = req.Description;
            if (req.EventDateTime != default) meetup.EventDateTime = DateTime.SpecifyKind(req.EventDateTime, DateTimeKind.Utc);
            if (req.Location != null) meetup.Location = req.Location;
            if (!string.IsNullOrWhiteSpace(req.Status)) meetup.Status = req.Status;

            await _ctx.Meetups.ReplaceOneAsync(m => m.Id == id, meetup);
            var parts = await _ctx.MeetupParticipants
                .Find(p => p.MeetupId == id && p.UserId != uid && (p.Status == "accepted" || p.Status == "invited")).ToListAsync();
            foreach (var p in parts)
                await _ctx.Notifications.InsertOneAsync(new Notification
                {
                    Id = Guid.NewGuid().ToString(), UserId = p.UserId, Type = "meetup_update",
                    Message = $"'{meetup.Title}' has been updated", MeetupId = id,
                    CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                });
            return Ok(meetup);
        }

        // DELETE /api/meetups/{id}?userId={creatorUid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var meetup = await _ctx.Meetups.Find(m => m.Id == id).FirstOrDefaultAsync();
            if (meetup == null) return NotFound(ApiErrors.NotFound("Meetup not found."));
            if (meetup.CreatorUserId != uid) return Forbid();

            var parts = await _ctx.MeetupParticipants.Find(p => p.MeetupId == id).ToListAsync();
            foreach (var p in parts.Where(p => p.UserId != uid))
                await _ctx.Notifications.InsertOneAsync(new Notification
                {
                    Id = Guid.NewGuid().ToString(), UserId = p.UserId, Type = "meetup_update",
                    Message = $"'{meetup.Title}' has been cancelled", MeetupId = id,
                    CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                });
            await _ctx.MeetupParticipants.DeleteManyAsync(p => p.MeetupId == id);
            await _ctx.Meetups.DeleteOneAsync(m => m.Id == id);
            return Ok(new { message = "Meetup deleted." });
        }

        // POST /api/meetups/{id}/rsvp  { userId, status: accepted|declined|maybe }
        [HttpPost("{id}/rsvp")]
        public async Task<IActionResult> Rsvp(string id, [FromBody] RsvpRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            var allowed = new[] { "accepted", "declined", "maybe", "invited" };
            if (!allowed.Contains(req.Status)) return BadRequest(ApiErrors.BadRequest("status must be one of: accepted, declined, maybe."));

            var part = await _ctx.MeetupParticipants.Find(p => p.Id == $"{id}:{uid}").FirstOrDefaultAsync();
            if (part == null) return NotFound(ApiErrors.NotFound("You are not a participant of this meetup."));

            await _ctx.MeetupParticipants.UpdateOneAsync(p => p.Id == part.Id,
                Builders<MeetupParticipant>.Update.Set(p => p.Status, req.Status).Set(p => p.RespondedAt, DateTime.UtcNow.ToString("o")));

            var meetup = await _ctx.Meetups.Find(m => m.Id == id).FirstOrDefaultAsync();
            if (meetup != null && meetup.CreatorUserId != uid)
            {
                var statusText = req.Status switch { "accepted" => "accepted", "declined" => "declined", "maybe" => "responded 'maybe' to", _ => "responded to" };
                await _ctx.Notifications.InsertOneAsync(new Notification
                {
                    Id = Guid.NewGuid().ToString(), UserId = meetup.CreatorUserId, Type = "rsvp_change",
                    Message = $"{part.Name} {statusText} your meetup '{meetup.Title}'", MeetupId = id,
                    CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                });
            }
            return Ok(new { message = $"RSVP set to {req.Status}." });
        }
    }
}
