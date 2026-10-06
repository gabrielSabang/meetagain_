using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;
using MongoDB.Driver;

namespace MeetAgain.Server.Services
{
    public class FriendService
    {
        private readonly MongoDbContext _ctx;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public FriendService(MongoDbContext ctx, AuthService auth, CurrentUserAccessor currentUser)
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

        public async Task<bool> SendFriendRequestAsync(string recipientEmail)
        {
            var currentUserId = await CurrentUidAsync();
            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var me = await _ctx.Users.Find(u => u.Uid == currentUserId).FirstOrDefaultAsync();
                if (me == null) return false;

                var recipient = await _ctx.Users.Find(u => u.Email == recipientEmail.Trim().ToLowerInvariant()).FirstOrDefaultAsync();
                if (recipient == null || recipient.Uid == currentUserId) return false;

                if (await _ctx.Friends.Find(f => f.UserId == currentUserId && f.FriendId == recipient.Uid).AnyAsync())
                    return false;
                if (await _ctx.FriendRequests.Find(r => r.Id == $"{recipient.Uid}:{currentUserId}").AnyAsync())
                    return false;

                await _ctx.FriendRequests.InsertOneAsync(new FriendRequest
                {
                    Id = $"{recipient.Uid}:{currentUserId}",
                    UserId = recipient.Uid,
                    FromUserId = currentUserId,
                    FromUserEmail = me.Email,
                    FromUserName = me.DisplayName,
                    Status = "pending",
                    SentAt = DateTime.UtcNow.ToString("o"),
                });

                await CreateNotificationAsync(recipient.Uid, "friend_request", $"{me.DisplayName} sent you a friend request");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending friend request: {ex.Message}");
                return false;
            }
        }

        public async Task<List<FriendRequest>> GetFriendRequestsAsync()
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return new();
            return await _ctx.FriendRequests.Find(r => r.UserId == uid && r.Status == "pending").ToListAsync();
        }

        public async Task<bool> AcceptFriendRequestAsync(string fromUserId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            try
            {
                var req = await _ctx.FriendRequests.Find(r => r.Id == $"{uid}:{fromUserId}").FirstOrDefaultAsync();
                if (req == null) return false;

                var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
                var friendUser = await _ctx.Users.Find(u => u.Uid == fromUserId).FirstOrDefaultAsync();
                if (me == null || friendUser == null) return false;

                var now = DateTime.UtcNow.ToString("o");
                await _ctx.Friends.ReplaceOneAsync(f => f.Id == $"{uid}:{fromUserId}",
                    new Friend { Id = $"{uid}:{fromUserId}", UserId = uid, FriendId = fromUserId, Name = req.FromUserName, Email = req.FromUserEmail, AddedAt = now },
                    new ReplaceOptions { IsUpsert = true });
                await _ctx.Friends.ReplaceOneAsync(f => f.Id == $"{fromUserId}:{uid}",
                    new Friend { Id = $"{fromUserId}:{uid}", UserId = fromUserId, FriendId = uid, Name = me.DisplayName, Email = me.Email, AddedAt = now },
                    new ReplaceOptions { IsUpsert = true });
                await _ctx.FriendRequests.DeleteOneAsync(r => r.Id == $"{uid}:{fromUserId}");

                await CreateNotificationAsync(fromUserId, "friend_accepted", $"{me.DisplayName} accepted your friend request");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting friend request: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RejectFriendRequestAsync(string fromUserId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            await _ctx.FriendRequests.DeleteOneAsync(r => r.Id == $"{uid}:{fromUserId}");
            return true;
        }

        public async Task<List<Friend>> GetFriendsAsync()
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return new();
            return await _ctx.Friends.Find(f => f.UserId == uid).SortBy(f => f.Name).ToListAsync();
        }

        public async Task<bool> RemoveFriendAsync(string friendId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return false;
            await _ctx.Friends.DeleteOneAsync(f => f.Id == $"{uid}:{friendId}");
            await _ctx.Friends.DeleteOneAsync(f => f.Id == $"{friendId}:{uid}");
            return true;
        }

        public async Task<AppUser?> SearchUserByEmailAsync(string email)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrEmpty(uid)) return null;
            var user = await _ctx.Users.Find(u => u.Email == email.Trim().ToLowerInvariant()).FirstOrDefaultAsync();
            return user?.Uid == uid ? null : user;
        }

        private async Task CreateNotificationAsync(string userId, string type, string message)
        {
            try
            {
                var id = Guid.NewGuid().ToString();
                await _ctx.Notifications.InsertOneAsync(new Notification
                {
                    Id = id, UserId = userId, Type = type, Message = message,
                    CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating notification: {ex.Message}");
            }
        }
    }
}
