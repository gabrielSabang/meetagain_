// One-time Firestore -> MongoDB migration (safe to re-run: all writes are upserts).
// Usage:
//   GOOGLE_APPLICATION_CREDENTIALS=/path/to/firebase-adminsdk.json \
//   FIRESTORE_PROJECT_ID=meetagain-50f2b \
//   dotnet run --project tools/FirestoreToMongo
// Mongo connection comes from MeetAgain.Server/.env (MONGODB_URI + MONGODB_URI_FALLBACK) or env vars.

using System.Text.Json;
using DotNetEnv;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using MongoDB.Bson;
using MongoDB.Driver;

Env.TraversePath().Load();

var projectId = Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID") ?? "meetagain-50f2b";
var credentialsFile = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
var serverEnvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MeetAgain.Server", ".env");
if (File.Exists(serverEnvPath))
    Env.Load(serverEnvPath);

var mongoPrimary = Environment.GetEnvironmentVariable("MONGODB_URI");
var mongoFallback = Environment.GetEnvironmentVariable("MONGODB_URI_FALLBACK");
var dbName = Environment.GetEnvironmentVariable("MONGODB_DATABASE") ?? "meetagain";
if (string.IsNullOrWhiteSpace(mongoPrimary))
    throw new Exception("Missing MONGODB_URI.");

GoogleCredential credential;
if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
{
    using var stream = new FileStream(credentialsFile, FileMode.Open, FileAccess.Read);
    credential = GoogleCredential.FromStream(stream).CreateScoped("https://www.googleapis.com/auth/cloud-platform");
}
else
{
    credential = GoogleCredential.GetApplicationDefault().CreateScoped("https://www.googleapis.com/auth/cloud-platform");
}

if (FirebaseApp.DefaultInstance == null)
    FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = projectId });

var firestore = new FirestoreDbBuilder { ProjectId = projectId, Credential = credential }.Build();

IMongoDatabase Connect(string uri)
{
    var settings = MongoClientSettings.FromConnectionString(uri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings).GetDatabase(dbName);
}

IMongoDatabase mongo;
try
{
    mongo = Connect(mongoPrimary!);
    mongo.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
    Console.WriteLine("MongoDB: connected via primary.");
}
catch (MongoAuthenticationException)
{
    if (string.IsNullOrWhiteSpace(mongoFallback)) throw;
    mongo = Connect(mongoFallback!);
    mongo.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
    Console.WriteLine("MongoDB: primary auth failed, connected via fallback.");
}

var counts = new Dictionary<string, (int scanned, long upserted)>();
async Task UpsertAsync(string collection, string id, Dictionary<string, object?> doc)
{
    doc["_id"] = id;
    var col = mongo.GetCollection<BsonDocument>(collection);
    var bson = new BsonDocument(doc.ToDictionary(
        kv => kv.Key,
        kv => kv.Value is null ? BsonNull.Value : BsonValue.Create(kv.Value)));
    var res = await col.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), bson, new ReplaceOptions { IsUpsert = true });
    if (!counts.ContainsKey(collection)) counts[collection] = (0, 0);
    var (s, u) = counts[collection];
    counts[collection] = (s + 1, u + (res.UpsertedId != null ? 1 : 0));
}

static Dictionary<string, object?> ToDict(DocumentSnapshot snap)
{
    var d = new Dictionary<string, object?>();
    foreach (var kv in snap.ToDictionary())
        d[kv.Key] = kv.Value is Timestamp ts ? ts.ToDateTime().ToString("o") : kv.Value;
    return d;
}

// ---- users + subcollections ----
var users = await firestore.Collection("users").GetSnapshotAsync();
foreach (var u in users.Documents)
{
    var doc = ToDict(u);
    doc["Uid"] = u.Id;
    await UpsertAsync("users", u.Id, doc);

    var subcollections = new (string name, Func<string, string, string> key)[]
    {
        ("friends", (uid, did) => $"{uid}:{did}"),
        ("friendRequests", (uid, did) => $"{uid}:{did}"),
        ("notifications", (uid, did) => did),
    };
    foreach (var (sub, keyFn) in subcollections)
    {
        var subs = await u.Reference.Collection(sub).GetSnapshotAsync();
        foreach (var s in subs.Documents)
        {
            var sd = ToDict(s);
            sd["UserId"] = u.Id;
            var key = keyFn(u.Id, string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString("N") : s.Id);
            var target = sub == "friends" ? "friends" : sub == "friendRequests" ? "friendRequests" : "notifications";
            await UpsertAsync(target, key, sd);
        }
    }

    var avail = await u.Reference.Collection("settings").Document("availability").GetSnapshotAsync();
    if (avail.Exists)
    {
        var ad = ToDict(avail);
        await UpsertAsync("availability", u.Id, ad);
    }
}

// ---- legacy root friends collection (FirestoreService.AddFriendAsync path) ----
try
{
    var legacyFriends = await firestore.Collection("friends").GetSnapshotAsync();
    foreach (var f in legacyFriends.Documents)
    {
        var fd = ToDict(f);
        await UpsertAsync("friends", f.Id, fd);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Skipping legacy root friends collection: {ex.Message}");
}

// ---- meetups + participants ----
var meetups = await firestore.Collection("meetups").GetSnapshotAsync();
foreach (var m in meetups.Documents)
{
    var doc = ToDict(m);
    doc["Id"] = m.Id;
    await UpsertAsync("meetups", m.Id, doc);

    var parts = await m.Reference.Collection("participants").GetSnapshotAsync();
    foreach (var p in parts.Documents)
    {
        var pd = ToDict(p);
        pd["MeetupId"] = m.Id;
        await UpsertAsync("meetupParticipants", $"{m.Id}:{p.Id}", pd);
    }
}

// ---- groups + members ----
var groups = await firestore.Collection("groups").GetSnapshotAsync();
foreach (var g in groups.Documents)
{
    var doc = ToDict(g);
    doc["Id"] = g.Id;
    await UpsertAsync("groups", g.Id, doc);

    var members = await g.Reference.Collection("members").GetSnapshotAsync();
    foreach (var mem in members.Documents)
    {
        var md = ToDict(mem);
        md["GroupId"] = g.Id;
        await UpsertAsync("groupMembers", $"{g.Id}:{mem.Id}", md);
    }
}

Console.WriteLine("\nMigration complete (all writes were upserts, safe to re-run):");
foreach (var (col, (scanned, upserted)) in counts)
    Console.WriteLine($"  {col}: scanned={scanned} upserted-new={upserted}");

await File.WriteAllTextAsync("migration-report.json",
    JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow, counts }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Wrote migration-report.json");
