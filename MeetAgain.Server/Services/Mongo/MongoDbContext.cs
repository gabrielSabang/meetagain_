using MeetAgain.Server.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MeetAgain.Server.Services.Mongo
{
    /// <summary>
    /// Singleton MongoDB access. Primary MONGODB_URI is tried first; on authentication
    /// failure (or connection failure) MONGODB_URI_FALLBACK is used. Construction never
    /// performs I/O so the app and /health can start even when Atlas is unreachable;
    /// use EnsureConnectedAsync()/PingAsync for connection checks.
    /// </summary>
    public class MongoDbContext
    {
        private readonly IMongoDatabase _primary;
        private readonly IMongoDatabase? _fallback;
        private IMongoDatabase _active;

        public IMongoDatabase Database => _active;
        public string DatabaseName { get; }
        public bool UsedFallback { get; private set; }
        public string? LastError { get; private set; }

        public MongoDbContext(IConfiguration config)
        {
            var primary = Environment.GetEnvironmentVariable("MONGODB_URI")
                ?? config["MONGODB_URI"];
            var fallback = Environment.GetEnvironmentVariable("MONGODB_URI_FALLBACK")
                ?? config["MONGODB_URI_FALLBACK"];
            DatabaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE")
                ?? config["MongoDb:Database"]
                ?? "meetagain";

            if (string.IsNullOrWhiteSpace(primary))
                throw new Exception("Missing MONGODB_URI. Set it in .env or environment.");

            _primary = CreateDatabase(primary!, DatabaseName);
            _fallback = string.IsNullOrWhiteSpace(fallback) ? null : CreateDatabase(fallback!, DatabaseName);
            _active = _primary;
        }

        private static IMongoDatabase CreateDatabase(string connectionString, string dbName)
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            // Fail fast so /health and startup don't hang when Atlas is unreachable (IP allowlist).
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(10);
            settings.SocketTimeout = TimeSpan.FromSeconds(10);
            return new MongoClient(settings).GetDatabase(dbName);
        }

        private static bool IsAuthFailure(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is MongoAuthenticationException) return true;
            return ex is MongoAuthenticationException;
        }

        /// <summary>Tries primary ping, then fallback ping. Returns true when connected.</summary>
        public async Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
        {
            try
            {
                await _primary.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
                _active = _primary;
                UsedFallback = false;
                LastError = null;
                return true;
            }
            catch (Exception primaryEx)
            {
                LastError = primaryEx.Message;
                // Spec: fallback on auth failure; be lenient and also try fallback on
                // connection/timeout failures (a distinct fallback host could still work).
                if (_fallback == null) return false;
                try
                {
                    await _fallback.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
                    _active = _fallback;
                    UsedFallback = true;
                    LastError = null;
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    LastError = $"primary: {primaryEx.GetType().Name}: {Trim(primaryEx.Message)} | fallback: {Trim(fallbackEx.Message)}";
                    return false;
                }
            }
        }

        private static string Trim(string s) => s.Length > 200 ? s[..200] + "…" : s;

        public IMongoCollection<AppUser> Users => Database.GetCollection<AppUser>("users");
        public IMongoCollection<Meetup> Meetups => Database.GetCollection<Meetup>("meetups");
        public IMongoCollection<MeetupParticipant> MeetupParticipants => Database.GetCollection<MeetupParticipant>("meetupParticipants");
        public IMongoCollection<Models.Group> Groups => Database.GetCollection<Models.Group>("groups");
        public IMongoCollection<GroupMember> GroupMembers => Database.GetCollection<GroupMember>("groupMembers");
        public IMongoCollection<Friend> Friends => Database.GetCollection<Friend>("friends");
        public IMongoCollection<FriendRequest> FriendRequests => Database.GetCollection<FriendRequest>("friendRequests");
        public IMongoCollection<Notification> Notifications => Database.GetCollection<Notification>("notifications");
        public IMongoCollection<UserAvailabilityDoc> Availability => Database.GetCollection<UserAvailabilityDoc>("availability");
        public IMongoCollection<GroupInvite> GroupInvites => Database.GetCollection<GroupInvite>("groupInvites");

        public async Task EnsureIndexesAsync()
        {
            // Connect first (primary -> fallback); throw only if neither works.
            if (!await EnsureConnectedAsync())
                throw new TimeoutException("MongoDB unreachable: " + LastError);

            await Users.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<AppUser>(Builders<AppUser>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Unique = true, Name = "ux_users_email" }),
            });
            await Meetups.Indexes.CreateOneAsync(
                new CreateIndexModel<Meetup>(Builders<Meetup>.IndexKeys.Ascending(m => m.CreatorUserId),
                    new CreateIndexOptions { Name = "ix_meetups_creator" }));
            await MeetupParticipants.Indexes.CreateOneAsync(
                new CreateIndexModel<MeetupParticipant>(
                    Builders<MeetupParticipant>.IndexKeys.Ascending(p => p.MeetupId).Ascending(p => p.UserId),
                    new CreateIndexOptions { Unique = true, Name = "ux_participants_meetup_user" }));
            await Groups.Indexes.CreateOneAsync(
                new CreateIndexModel<Models.Group>(Builders<Models.Group>.IndexKeys.Ascending(g => g.OwnerId),
                    new CreateIndexOptions { Name = "ix_groups_owner" }));
            await GroupMembers.Indexes.CreateOneAsync(
                new CreateIndexModel<GroupMember>(
                    Builders<GroupMember>.IndexKeys.Ascending(m => m.GroupId).Ascending(m => m.UserId),
                    new CreateIndexOptions { Unique = true, Name = "ux_groupMembers_group_user" }));
            await Friends.Indexes.CreateOneAsync(
                new CreateIndexModel<Friend>(
                    Builders<Friend>.IndexKeys.Ascending(f => f.UserId).Ascending(f => f.FriendId),
                    new CreateIndexOptions { Unique = true, Name = "ux_friends_user_friend" }));
            await FriendRequests.Indexes.CreateOneAsync(
                new CreateIndexModel<FriendRequest>(
                    Builders<FriendRequest>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.Status),
                    new CreateIndexOptions { Name = "ix_friendRequests_user_status" }));
            await Notifications.Indexes.CreateOneAsync(
                new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.UserId).Descending(n => n.CreatedAt),
                    new CreateIndexOptions { Name = "ix_notifications_user_created" }));
        }

        public static string NewId() => Guid.NewGuid().ToString("N");
    }
}
