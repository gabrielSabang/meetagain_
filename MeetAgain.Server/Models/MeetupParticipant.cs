using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    /// <summary>
    /// Stored in meetupParticipants collection. Id = $"{MeetupId}:{UserId}".
    /// </summary>
    public class MeetupParticipant
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("meetupId")] public string MeetupId { get; set; } = "";
        [BsonElement("userId")] public string UserId { get; set; } = "";
        [BsonElement("name")] public string Name { get; set; } = "";
        [BsonElement("email")] public string Email { get; set; } = "";
        [BsonElement("status")] public string Status { get; set; } = "invited"; // invited, accepted, declined, maybe
        [BsonElement("invitedAt")] public string InvitedAt { get; set; } = "";
        [BsonElement("respondedAt")] public string RespondedAt { get; set; } = "";
    }
}
