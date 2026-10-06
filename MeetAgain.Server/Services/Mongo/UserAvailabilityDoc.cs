using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Services.Mongo
{
    /// <summary>Persisted availability doc (_id = userId).</summary>
    public class UserAvailabilityDoc
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string UserId { get; set; } = "";
        [BsonElement("preferredDays")] public List<string> PreferredDays { get; set; } = new();
        [BsonElement("availableTimeSlots")] public List<TimeSlotDoc> AvailableTimeSlots { get; set; } = new();
        [BsonElement("blockedDates")] public List<string> BlockedDates { get; set; } = new(); // yyyy-MM-dd
    }

    public class TimeSlotDoc
    {
        [BsonElement("start")] public string Start { get; set; } = "09:00";
        [BsonElement("end")] public string End { get; set; } = "22:00";
    }
}
