using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    public class FriendRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = ""; // recipientUid:senderUid
        [BsonElement("userId")] public string UserId { get; set; } = ""; // recipient
        [BsonElement("fromUserId")] public string FromUserId { get; set; } = "";
        [BsonElement("fromUserEmail")] public string FromUserEmail { get; set; } = "";
        [BsonElement("fromUserName")] public string FromUserName { get; set; } = "";
        [BsonElement("status")] public string Status { get; set; } = "pending";
        [BsonElement("sentAt")] public string SentAt { get; set; } = "";
    }
}
