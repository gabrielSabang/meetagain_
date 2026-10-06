using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;
using MongoDB.Driver;

namespace MeetAgain.Server.Services
{
    public class NotificationService
    {
        private readonly MongoDbContext _ctx;
        private readonly CurrentUserAccessor _currentUserAccessor;

        public NotificationService(MongoDbContext ctx, CurrentUserAccessor currentUserAccessor)
        {
            _ctx = ctx;
            _currentUserAccessor = currentUserAccessor;
        }

        public async Task CreateNotificationAsync(string userId, string type, string message, Dictionary<string, object>? metadata = null)
        {
            var n = new Notification
            {
                Id = Guid.NewGuid().ToString(), UserId = userId, Type = type, Message = message,
                CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
            };
            if (metadata != null)
            {
                if (metadata.TryGetValue("MeetupId", out var mi)) n.MeetupId = mi?.ToString() ?? "";
                if (metadata.TryGetValue("FriendRequestId", out var fr)) n.FriendRequestId = fr?.ToString() ?? "";
                if (metadata.TryGetValue("GroupId", out var gr)) n.GroupId = gr?.ToString() ?? "";
            }
            await _ctx.Notifications.InsertOneAsync(n);
        }

        public async Task<List<NotificationDto>> GetMyNotificationsAsync(int page = 1, int pageSize = 50)
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return new();
            var list = await _ctx.Notifications.Find(n => n.UserId == userId)
                .SortByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task<int> GetUnreadCountAsync()
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return 0;
            return (int)await _ctx.Notifications.CountDocumentsAsync(n => n.UserId == userId && !n.IsRead);
        }

        public async Task MarkAsReadAsync(string notificationId)
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return;
            await _ctx.Notifications.UpdateOneAsync(n => n.Id == notificationId && n.UserId == userId,
                Builders<Notification>.Update.Set(n => n.IsRead, true));
        }

        public async Task<int> MarkAllAsReadAsync()
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return 0;
            var res = await _ctx.Notifications.UpdateManyAsync(n => n.UserId == userId && !n.IsRead,
                Builders<Notification>.Update.Set(n => n.IsRead, true));
            return (int)res.ModifiedCount;
        }

        public async Task DeleteNotificationAsync(string notificationId)
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return;
            await _ctx.Notifications.DeleteOneAsync(n => n.Id == notificationId && n.UserId == userId);
        }

        public Task NotifyMeetupInviteAsync(string userId, string meetupTitle, string creatorName, string meetupId)
            => CreateNotificationAsync(userId, "meetup_invite", $"{creatorName} invited you to '{meetupTitle}'",
                new Dictionary<string, object> { { "MeetupId", meetupId } });

        public Task NotifyMeetupUpdateAsync(string userId, string meetupTitle, string updateType, string meetupId)
            => CreateNotificationAsync(userId, "meetup_update", $"'{meetupTitle}' has been {updateType}",
                new Dictionary<string, object> { { "MeetupId", meetupId } });

        public Task NotifyFriendRequestAsync(string userId, string fromUserName, string requestId)
            => CreateNotificationAsync(userId, "friend_request", $"{fromUserName} sent you a friend request",
                new Dictionary<string, object> { { "FriendRequestId", requestId } });

        public Task NotifyFriendRequestAcceptedAsync(string userId, string acceptedByName)
            => CreateNotificationAsync(userId, "friend_request_accepted", $"{acceptedByName} accepted your friend request");

        public Task NotifyGroupInviteAsync(string userId, string groupName, string invitedByName, string groupId)
            => CreateNotificationAsync(userId, "group_invite", $"{invitedByName} added you to the group '{groupName}'",
                new Dictionary<string, object> { { "GroupId", groupId } });

        public Task NotifyRSVPChangeAsync(string creatorUserId, string userName, string meetupTitle, string status, string meetupId)
            => CreateNotificationAsync(creatorUserId, "rsvp_change", $"{userName} {status} your meetup '{meetupTitle}'",
                new Dictionary<string, object> { { "MeetupId", meetupId } });

        public static NotificationDto ToDto(Notification n) => new()
        {
            Id = n.Id, Type = n.Type, Message = n.Message, CreatedAt = n.CreatedAt, IsRead = n.IsRead,
            MeetupId = string.IsNullOrEmpty(n.MeetupId) ? null : n.MeetupId,
            FriendRequestId = string.IsNullOrEmpty(n.FriendRequestId) ? null : n.FriendRequestId,
            GroupId = string.IsNullOrEmpty(n.GroupId) ? null : n.GroupId,
        };
    }

    public class NotificationDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public bool IsRead { get; set; } = false;
        public string? MeetupId { get; set; }
        public string? FriendRequestId { get; set; }
        public string? GroupId { get; set; }
    }
}
