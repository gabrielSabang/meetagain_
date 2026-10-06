using MeetAgain.Server.Models;
using MongoDB.Driver;

namespace MeetAgain.Server.Services.Mongo
{
    /// <summary>
    /// Mongo replacement for FirestoreService. Same behavioral surface (users/meetups/friends/groups)
    /// so Blazor pages and controllers keep working.
    /// </summary>
    public class MongoService
    {
        private readonly MongoDbContext _ctx;
        public CustomAuthStateProvider? AuthStateProvider { get; set; }

        public MongoService(MongoDbContext ctx) => _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));

        // ---------------- USERS ----------------
        public async Task<AppUser?> GetUserAsync(string userId)
            => await _ctx.Users.Find(u => u.Uid == userId).FirstOrDefaultAsync();

        public async Task<AppUser?> GetUserByEmailAsync(string email)
            => await _ctx.Users.Find(u => u.Email == email).FirstOrDefaultAsync();

        public async Task<List<AppUser>> GetAllUsersAsync(int page = 1, int pageSize = 100)
            => await _ctx.Users.Find(FilterDefinition<AppUser>.Empty)
                .SortBy(u => u.DisplayName).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();

        public async Task<long> CountUsersAsync()
            => await _ctx.Users.CountDocumentsAsync(FilterDefinition<AppUser>.Empty);

        public async Task CreateOrUpdateUserAsync(AppUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Uid))
                throw new ArgumentException("User must have Uid.");
            await _ctx.Users.ReplaceOneAsync(u => u.Uid == user.Uid, user, new ReplaceOptions { IsUpsert = true });
        }

        // ---------------- MEETUPS ----------------
        public async Task<string> CreateMeetupAsync(Meetup meetup)
        {
            if (string.IsNullOrWhiteSpace(meetup.Id))
                meetup.Id = NewId();
            if (meetup.CreatedAt == default)
                meetup.CreatedAt = DateTime.UtcNow;
            await _ctx.Meetups.ReplaceOneAsync(m => m.Id == meetup.Id, meetup, new ReplaceOptions { IsUpsert = true });
            return meetup.Id;
        }

        public async Task<Meetup?> GetMeetupByIdAsync(string meetupId)
            => await _ctx.Meetups.Find(m => m.Id == meetupId).FirstOrDefaultAsync();

        public async Task<List<MeetupDto>> GetUserMeetupsAsync(string userId)
        {
            var list = await _ctx.Meetups.Find(m => m.CreatorUserId == userId)
                .SortBy(m => m.EventDateTime).ToListAsync();
            return list.Select(m => new MeetupDto
            {
                Id = m.Id, Title = m.Title, Description = m.Description,
                CreatorUserId = m.CreatorUserId, CreatorName = m.CreatorName,
                Location = m.Location, EventDateTime = m.EventDateTime,
                CreatedAt = m.CreatedAt, Status = m.Status, ParticipantCount = m.ParticipantCount,
            }).ToList();
        }

        public async Task DeleteMeetupAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId)) return;
            await _ctx.Meetups.DeleteOneAsync(m => m.Id == meetupId);
            await _ctx.MeetupParticipants.DeleteManyAsync(p => p.MeetupId == meetupId);
        }

        public Task DeleteMeetupAsync(string userId, string meetupId) => DeleteMeetupAsync(meetupId);

        public async Task UpdateMeetupAsync(Meetup meetup)
        {
            if (string.IsNullOrWhiteSpace(meetup.Id))
                throw new ArgumentException("Meetup ID is required.");
            await _ctx.Meetups.ReplaceOneAsync(m => m.Id == meetup.Id, meetup, new ReplaceOptions { IsUpsert = true });
        }

        // ---------------- FRIENDS (canonical: friends collection) ----------------
        public async Task AddFriendAsync(Friend friend)
        {
            if (string.IsNullOrWhiteSpace(friend.Id))
                friend.Id = $"{friend.UserId}:{friend.FriendId}";
            if (string.IsNullOrWhiteSpace(friend.AddedAt))
                friend.AddedAt = DateTime.UtcNow.ToString("o");
            await _ctx.Friends.ReplaceOneAsync(f => f.Id == friend.Id, friend, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<Friend>> GetFriendsAsync(string userId)
            => await _ctx.Friends.Find(f => f.UserId == userId).SortBy(f => f.Name).ToListAsync();

        public async Task RemoveFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId)) return;
            await _ctx.Friends.DeleteOneAsync(f => f.Id == friendId);
        }

        // ---------------- GROUPS ----------------
        public async Task CreateGroupAsync(Models.Group group)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                group.Id = NewId();
            if (string.IsNullOrWhiteSpace(group.CreatedAt))
                group.CreatedAt = DateTime.UtcNow.ToString("o");
            await _ctx.Groups.ReplaceOneAsync(g => g.Id == group.Id, group, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<Models.Group>> GetGroupsByOwnerAsync(string ownerId)
            => await _ctx.Groups.Find(g => g.OwnerId == ownerId).ToListAsync();

        public async Task AddMemberAsync(string groupId, GroupMember member)
        {
            member.GroupId = groupId;
            member.Id = $"{groupId}:{member.UserId}";
            if (string.IsNullOrWhiteSpace(member.AddedAt))
                member.AddedAt = DateTime.UtcNow.ToString("o");
            await _ctx.GroupMembers.ReplaceOneAsync(m => m.Id == member.Id, member, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId)
            => await _ctx.GroupMembers.Find(m => m.GroupId == groupId).SortBy(m => m.Name).ToListAsync();

        public string NewId() => MongoDbContext.NewId();
    }
}
