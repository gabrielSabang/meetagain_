using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;
using MongoDB.Driver;

namespace MeetAgain.Server.Services
{
    public class GroupService
    {
        private readonly MongoDbContext _ctx;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public GroupService(MongoDbContext ctx, AuthService auth, CurrentUserAccessor currentUser)
        {
            _ctx = ctx;
            _auth = auth;
            _currentUser = currentUser;
        }

        private async Task<string?> CurrentUidAsync()
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            return string.IsNullOrEmpty(uid) ? _auth.UserId : uid;
        }

        public async Task<string?> CreateGroupAsync(CreateGroupModel model)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return null;
            try
            {
                var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
                if (me == null) return null;

                var groupId = Guid.NewGuid().ToString("N");
                await _ctx.Groups.InsertOneAsync(new Models.Group
                {
                    Id = groupId, OwnerId = uid, OwnerName = me.DisplayName,
                    Name = model.Name, Description = model.Description,
                    MemberCount = model.InitialMemberIds?.Count ?? 0,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                });

                if (model.InitialMemberIds is { Count: > 0 })
                    await AddMembersToGroupAsync(groupId, model.InitialMemberIds);

                return groupId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating group: {ex.Message}");
                return null;
            }
        }

        public async Task<List<GroupDto>> GetMyGroupsAsync()
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return new();

            var owned = await _ctx.Groups.Find(g => g.OwnerId == uid).ToListAsync();
            var memberships = await _ctx.GroupMembers.Find(m => m.UserId == uid).ToListAsync();
            var memberGroupIds = memberships.Select(m => m.GroupId).Distinct().ToHashSet();
            var memberGroups = memberGroupIds.Count > 0
                ? await _ctx.Groups.Find(g => memberGroupIds.Contains(g.Id)).ToListAsync()
                : new List<Models.Group>();

            var result = owned.Select(g => ToDto(g, true))
                .Concat(memberGroups.Where(g => g.OwnerId != uid).Select(g => ToDto(g, false)))
                .OrderBy(g => g.Name).ToList();
            return result;
        }

        public async Task<GroupDto?> GetGroupDetailAsync(string groupId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return null;
            var group = await _ctx.Groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null) return null;
            var members = await _ctx.GroupMembers.Find(m => m.GroupId == groupId).SortBy(m => m.Name).ToListAsync();
            var dto = ToDto(group, group.OwnerId == uid);
            dto.Members = members;
            return dto;
        }

        public Task<GroupDto?> GetGroupByIdAsync(string groupId) => GetGroupDetailAsync(groupId);

        public async Task<bool> AddMembersToGroupAsync(string groupId, List<string> friendIds)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            var group = await _ctx.Groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null || group.OwnerId != uid) return false;

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
                    AddedAt = DateTime.UtcNow.ToString("o"), AddedBy = uid,
                });
                added++;
            }
            if (added > 0)
                await _ctx.Groups.UpdateOneAsync(g => g.Id == groupId,
                    Builders<Models.Group>.Update.Inc(g => g.MemberCount, added));
            return true;
        }

        public async Task<bool> RemoveMemberAsync(string groupId, string memberId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            var group = await _ctx.Groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null || group.OwnerId != uid) return false;
            await _ctx.GroupMembers.DeleteOneAsync(m => m.Id == $"{groupId}:{memberId}");
            await _ctx.Groups.UpdateOneAsync(g => g.Id == groupId,
                Builders<Models.Group>.Update.Inc(g => g.MemberCount, -1));
            return true;
        }

        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            var group = await _ctx.Groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null || group.OwnerId != uid) return false;
            await _ctx.GroupMembers.DeleteManyAsync(m => m.GroupId == groupId);
            await _ctx.Groups.DeleteOneAsync(g => g.Id == groupId);
            return true;
        }

        public async Task<List<string>> GetGroupMemberIdsAsync(string groupId)
            => (await _ctx.GroupMembers.Find(m => m.GroupId == groupId).ToListAsync()).Select(m => m.UserId).ToList();

        private static GroupDto ToDto(Models.Group g, bool isOwner) => new()
        {
            Id = g.Id, OwnerId = g.OwnerId, OwnerName = g.OwnerName, Name = g.Name,
            Description = g.Description, MemberCount = g.MemberCount, CreatedAt = g.CreatedAt, IsOwner = isOwner,
        };
    }
}
