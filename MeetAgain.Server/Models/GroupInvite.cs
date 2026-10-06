using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    public class GroupInvite
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("groupId")] public string GroupId { get; set; } = "";
        [BsonElement("userId")] public string UserId { get; set; } = "";
        [BsonElement("sentBy")] public string SentBy { get; set; } = "";
        [BsonElement("sentAt")] public string SentAt { get; set; } = "";
        [BsonElement("status")] public string Status { get; set; } = "pending";
    }
}
