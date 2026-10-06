using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    /// <summary>Stored in friends collection. Id = $"{UserId}:{FriendId}".</summary>
    public class Friend
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("userId")] public string UserId { get; set; } = "";
        [BsonElement("friendId")] public string FriendId { get; set; } = "";
        [BsonElement("name")] public string Name { get; set; } = "";
        [BsonElement("email")] public string Email { get; set; } = "";
        [BsonElement("addedAt")] public string AddedAt { get; set; } = "";
    }
}
