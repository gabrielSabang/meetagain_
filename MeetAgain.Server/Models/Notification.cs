using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    public class Notification
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("userId")] public string UserId { get; set; } = "";
        [BsonElement("type")] public string Type { get; set; } = "";
        [BsonElement("message")] public string Message { get; set; } = "";
        [BsonElement("createdAt")] public string CreatedAt { get; set; } = "";
        [BsonElement("isRead")] public bool IsRead { get; set; } = false;

        // Optional metadata fields
        [BsonElement("meetupId")] public string MeetupId { get; set; } = "";
        [BsonElement("friendRequestId")] public string FriendRequestId { get; set; } = "";
        [BsonElement("groupId")] public string GroupId { get; set; } = "";
    }
}
