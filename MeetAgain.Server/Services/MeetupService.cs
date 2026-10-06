using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;
using MongoDB.Driver;

namespace MeetAgain.Server.Services
{
    public class MeetupService
    {
        private readonly MongoDbContext _ctx;
        private readonly MongoService _mongo;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public MeetupService(MongoDbContext ctx, MongoService mongo, AuthService auth, CurrentUserAccessor currentUser)
        {
            _ctx = ctx;
            _mongo = mongo;
            _auth = auth;
            _currentUser = currentUser;
        }

        private async Task<string?> CurrentUidAsync()
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            return string.IsNullOrWhiteSpace(uid) ? _auth.UserId : uid;
        }

        public async Task<bool> CreateMeetupAsync(CreateMeetupModel model, List<string>? invitedFriendIds = null)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;
            try
            {
                var me = await _ctx.Users.Find(u => u.Uid == uid).FirstOrDefaultAsync();
                if (me == null) return false;

                var eventDateTime = model.Date.Date.Add(model.Time.ToTimeSpan()).ToUniversalTime();
                var meetupId = Guid.NewGuid().ToString("N");
                await _ctx.Meetups.InsertOneAsync(new Meetup
                {
                    Id = meetupId, Title = model.Title, Description = model.Description,
                    CreatorUserId = uid, CreatorName = me.DisplayName, Location = "",
                    EventDateTime = eventDateTime, CreatedAt = DateTime.UtcNow,
                    Status = "confirmed", ParticipantCount = (invitedFriendIds?.Count ?? 0) + 1,
                });
                await _ctx.MeetupParticipants.InsertOneAsync(new MeetupParticipant
                {
                    Id = $"{meetupId}:{uid}", MeetupId = meetupId, UserId = uid,
                    Name = me.DisplayName, Email = me.Email, Status = "accepted",
                    InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = DateTime.UtcNow.ToString("o"),
                });

                if (invitedFriendIds is { Count: > 0 })
                    await InviteFriendsAsync(meetupId, invitedFriendIds, model.Title, me.DisplayName);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating meetup: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InviteFriendsAsync(string meetupId, List<string> friendIds, string meetupTitle, string creatorName)
        {
            foreach (var fid in friendIds.Distinct())
            {
                if (await _ctx.MeetupParticipants.Find(p => p.Id == $"{meetupId}:{fid}").AnyAsync()) continue;
                var friend = await _ctx.Users.Find(u => u.Uid == fid).FirstOrDefaultAsync();
                if (friend == null) continue;
                await _ctx.MeetupParticipants.InsertOneAsync(new MeetupParticipant
                {
                    Id = $"{meetupId}:{fid}", MeetupId = meetupId, UserId = fid,
                    Name = friend.DisplayName, Email = friend.Email, Status = "invited",
                    InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = "",
                });
                await NotifyAsync(fid, "meetup_invite", $"{creatorName} invited you to '{meetupTitle}'", meetupId);
            }
            return true;
        }

        public async Task<bool> RespondToMeetupAsync(string meetupId, string status)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;
            var part = await _ctx.MeetupParticipants.Find(p => p.Id == $"{meetupId}:{uid}").FirstOrDefaultAsync();
            if (part == null) return false;

            await _ctx.MeetupParticipants.UpdateOneAsync(p => p.Id == part.Id,
                Builders<MeetupParticipant>.Update.Set(p => p.Status, status).Set(p => p.RespondedAt, DateTime.UtcNow.ToString("o")));

            var meetup = await _ctx.Meetups.Find(m => m.Id == meetupId).FirstOrDefaultAsync();
            if (meetup != null && meetup.CreatorUserId != uid)
            {
                var statusText = status switch { "accepted" => "accepted", "declined" => "declined", "maybe" => "responded 'maybe' to", _ => "responded to" };
                await NotifyAsync(meetup.CreatorUserId, "rsvp_change", $"{part.Name} {statusText} your meetup '{meetup.Title}'", meetupId);
            }
            return true;
        }

        public async Task<List<MeetupDto>> GetMyMeetupsAsync()
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid)) return new();
            var parts = await _ctx.MeetupParticipants.Find(p => p.UserId == uid).ToListAsync();
            if (parts.Count == 0) return new();
            var ids = parts.Select(p => p.MeetupId).ToHashSet();
            var meetups = await _ctx.Meetups.Find(m => ids.Contains(m.Id)).SortBy(m => m.EventDateTime).ToListAsync();
            var byId = parts.ToDictionary(p => p.MeetupId);
            return meetups.Select(m => new MeetupDto
            {
                Id = m.Id, Title = m.Title, Description = m.Description,
                CreatorUserId = m.CreatorUserId, CreatorName = m.CreatorName, Location = m.Location,
                EventDateTime = m.EventDateTime, CreatedAt = m.CreatedAt, Status = m.Status,
                ParticipantCount = m.ParticipantCount, IsCreator = m.CreatorUserId == uid,
                MyRSVPStatus = byId.TryGetValue(m.Id, out var p) ? p.Status : "",
            }).ToList();
        }

        public async Task<MeetupDetailDto?> GetMeetupDetailAsync(string meetupId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid)) return null;
            var meetup = await _ctx.Meetups.Find(m => m.Id == meetupId).FirstOrDefaultAsync();
            if (meetup == null) return null;
            var parts = await _ctx.MeetupParticipants.Find(p => p.MeetupId == meetupId).SortBy(p => p.Name).ToListAsync();
            return new MeetupDetailDto
            {
                Meetup = meetup, Participants = parts,
                IsCreator = meetup.CreatorUserId == uid,
                MyRSVPStatus = parts.FirstOrDefault(p => p.UserId == uid)?.Status ?? "",
            };
        }

        public async Task<bool> DeleteMeetupAsync(string meetupId)
        {
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;
            var meetup = await _ctx.Meetups.Find(m => m.Id == meetupId).FirstOrDefaultAsync();
            if (meetup == null || meetup.CreatorUserId != uid) return false;
            var parts = await _ctx.MeetupParticipants.Find(p => p.MeetupId == meetupId).ToListAsync();
            foreach (var p in parts.Where(p => p.UserId != uid))
                await NotifyAsync(p.UserId, "meetup_update", $"'{meetup.Title}' has been cancelled", meetupId);
            await _ctx.MeetupParticipants.DeleteManyAsync(p => p.MeetupId == meetupId);
            await _ctx.Meetups.DeleteOneAsync(m => m.Id == meetupId);
            return true;
        }

        public async Task<bool> UpdateMeetupAsync(Meetup meetup)
        {
            if (meetup == null || string.IsNullOrWhiteSpace(meetup.Id)) return false;
            var uid = await CurrentUidAsync();
            if (string.IsNullOrWhiteSpace(uid) || meetup.CreatorUserId != uid) return false;
            await _ctx.Meetups.ReplaceOneAsync(m => m.Id == meetup.Id, meetup, new ReplaceOptions { IsUpsert = true });
            var parts = await _ctx.MeetupParticipants
                .Find(p => p.MeetupId == meetup.Id && p.UserId != uid && (p.Status == "accepted" || p.Status == "invited"))
                .ToListAsync();
            foreach (var p in parts)
                await NotifyAsync(p.UserId, "meetup_update", $"'{meetup.Title}' has been updated", meetup.Id);
            return true;
        }

        private async Task NotifyAsync(string userId, string type, string message, string meetupId)
        {
            await _ctx.Notifications.InsertOneAsync(new Notification
            {
                Id = Guid.NewGuid().ToString(), UserId = userId, Type = type, Message = message,
                MeetupId = meetupId, CreatedAt = DateTime.UtcNow.ToString("o"), IsRead = false,
            });
        }
    }
}
