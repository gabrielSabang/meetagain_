using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    public class AppUser
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Uid { get; set; } = "";

        [BsonElement("email")]
        public string Email { get; set; } = "";

        [BsonElement("displayName")]
        public string DisplayName { get; set; } = "";

        [BsonElement("createdAt")]
        public string CreatedAt { get; set; } = "";

        /// <summary>BCrypt password hash. Never serialized to API responses.</summary>
        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = "";

        public AppUser() { }

        public AppUser(string uid, string email, string displayName, string createdAt)
        {
            Uid = uid;
            Email = email;
            DisplayName = displayName;
            CreatedAt = createdAt;
        }
    }
}
