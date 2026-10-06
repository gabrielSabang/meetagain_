using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    public class Meetup
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("title")] public string Title { get; set; } = "";
        [BsonElement("description")] public string Description { get; set; } = "";
        [BsonElement("creatorUserId")] public string CreatorUserId { get; set; } = "";
        [BsonElement("creatorName")] public string CreatorName { get; set; } = "";
        [BsonElement("location")] public string Location { get; set; } = "";
        [BsonElement("eventDateTime")] public DateTime EventDateTime { get; set; }
        [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [BsonElement("status")] public string Status { get; set; } = "confirmed"; // planning, confirmed, completed, cancelled
        [BsonElement("participantCount")] public int ParticipantCount { get; set; } = 0;
    }
}
