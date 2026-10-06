using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/groups")]
    [Route("api/v1/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly MongoDbContext _ctx;

        public GroupsController(MongoDbContext ctx) => _ctx = ctx;

        private Task<string?> CallerUidAsync(string? q = null, string? b = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, q, b);

        private static IActionResult NeedAuth() => new UnauthorizedObjectResult(ApiErrors.Unauthorized());

        private static GroupDto ToDto(Models.Group g, bool isOwner) => new()
        {
            Id = g.Id, OwnerId = g.OwnerId, OwnerName = g.OwnerName, Name = g.Name,
            Description = g.Description, MemberCount = g.MemberCount, CreatedAt = g.CreatedAt, IsOwner = isOwner,
        };

        // GET /api/groups?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var owned = await _ctx.Groups.Find(g => g.OwnerId == uid).ToListAsync();
            var memberships = await _ctx.GroupMembers.Find(m => m.UserId == uid).ToListAsync();
            var memberIds = memberships.Select(m => m.GroupId).Distinct().ToHashSet();
            var memberGroups = memberIds.Count > 0
                ? await _ctx.Groups.Find(g => memberIds.Contains(g.Id)).ToListAsync()
                : new List<Models.Group>();

            var result = owned.Select(g => ToDto(g, true))
                .Concat(memberGroups.Where(g => g.OwnerId != uid).Select(g => ToDto(g, false)))
                .OrderBy(g => g.Name).ToList();
            return Ok(result);
        }

        // GET /api/groups/{id}?userId={uid}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var group = await _ctx.Groups.Find(g => g.Id == id).FirstOrDefaultAsync();
            if (group == null) return NotFound(ApiErrors.NotFound("Group not found."));
            var members = await _ctx.GroupMembers.Find(m => m.GroupId == id).SortBy(m => m.Name).ToListAsync();
            var dto = ToDto(group, group.OwnerId == uid);
            dto.Members = members;
            return Ok(dto);
        }

        // POST /api/groups  { userId, name, description, initialMemberIds[] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateGroupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiErrors.BadRequest("name is required."));

            var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
            if (me == null) return NotFound(ApiErrors.NotFound("Caller user document not found."));

            var groupId = Guid.NewGuid().ToString("N");
            var group = new Models.Group
            {
                Id = groupId, OwnerId = uid, OwnerName = me.DisplayName,
                Name = req.Name.Trim(), Description = req.Description ?? "",
                MemberCount = req.InitialMemberIds?.Count ?? 0,
                CreatedAt = DateTime.UtcNow.ToString("o"),
            };
            await _ctx.Groups.InsertOneAsync(group);

            if (req.InitialMemberIds is { Count: > 0 })
                await AddMembersInternalAsync(groupId, uid, req.InitialMemberIds);

            return CreatedAtAction(nameof(GetDetail), new { id = groupId }, new { id = groupId, group });
        }

        // POST /api/groups/{id}/members  { userId(owner), memberIds[] }
        [HttpPost("{id}/members")]
        public async Task<IActionResult> AddMembers(string id, [FromBody] AddGroupMembersRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            var ok = await AddMembersInternalAsync(id, uid, req.MemberIds ?? new());
            if (!ok) return Forbid();
            return Ok(new { message = "Members added." });
        }

        // DELETE /api/groups/{id}/members/{memberId}?userId={ownerUid}
        [HttpDelete("{id}/members/{memberId}")]
        public async Task<IActionResult> RemoveMember(string id, string memberId, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var group = await _ctx.Groups.Find(g => g.Id == id).FirstOrDefaultAsync();
            if (group == null) return NotFound(ApiErrors.NotFound("Group not found."));
            if (group.OwnerId != uid) return Forbid();

            await _ctx.GroupMembers.DeleteOneAsync(m => m.Id == $"{id}:{memberId}");
            await _ctx.Groups.UpdateOneAsync(g => g.Id == id, Builders<Models.Group>.Update.Inc(g => g.MemberCount, -1));
            return Ok(new { message = "Member removed." });
        }

        // DELETE /api/groups/{id}?userId={ownerUid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var group = await _ctx.Groups.Find(g => g.Id == id).FirstOrDefaultAsync();
            if (group == null) return NotFound(ApiErrors.NotFound("Group not found."));
            if (group.OwnerId != uid) return Forbid();

            await _ctx.GroupMembers.DeleteManyAsync(m => m.GroupId == id);
            await _ctx.Groups.DeleteOneAsync(g => g.Id == id);
            return Ok(new { message = "Group deleted." });
        }

        private async Task<bool> AddMembersInternalAsync(string groupId, string ownerUid, List<string> friendIds)
        {
            try
            {
                var group = await _ctx.Groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
                if (group == null || group.OwnerId != ownerUid) return false;

                var added = 0;
                foreach (var fid in friendIds.Distinct())
                {
                    if (await _ctx.GroupMembers.Find(m => m.Id == $"{groupId}:{fid}").AnyAsync()) continue;
                    var friend = await _ctx.Users.Find(u => u.Uid == fid).FirstOrDefaultAsync();
                    if (friend == null) continue;
                    await _ctx.GroupMembers.InsertOneAsync(new GroupMember
                    {
                        Id = $"{groupId}:{fid}", GroupId = groupId, UserId = fid,
                        Name = friend.DisplayName, Email = friend.Email,
                        AddedAt = DateTime.UtcNow.ToString("o"), AddedBy = ownerUid,
                    });
                    added++;
                }
                if (added > 0)
                    await _ctx.Groups.UpdateOneAsync(g => g.Id == groupId,
                        Builders<Models.Group>.Update.Inc(g => g.MemberCount, added));
                return true;
            }
            catch { return false; }
        }
    }
}
