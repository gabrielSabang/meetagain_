using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MeetAgain.Server.Models
{
    /// <summary>
    /// Stored in groups collection (_id = groupId).
    /// </summary>
    public class Group
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = "";
        [BsonElement("ownerId")] public string OwnerId { get; set; } = "";
        [BsonElement("ownerName")] public string OwnerName { get; set; } = "";
        [BsonElement("name")] public string Name { get; set; } = "";
        [BsonElement("description")] public string Description { get; set; } = "";
        [BsonElement("memberCount")] public int MemberCount { get; set; } = 0;
        [BsonElement("createdAt")] public string CreatedAt { get; set; } = "";
    }

    /// <summary>
    /// Stored in groupMembers collection (groupId + userId compound key).
    /// </summary>
    public class GroupMember
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = ""; // $"{GroupId}:{UserId}"
        [BsonElement("groupId")] public string GroupId { get; set; } = "";
        [BsonElement("userId")] public string UserId { get; set; } = "";
        [BsonElement("name")] public string Name { get; set; } = "";
        [BsonElement("email")] public string Email { get; set; } = "";
        [BsonElement("addedAt")] public string AddedAt { get; set; } = "";
        [BsonElement("addedBy")] public string AddedBy { get; set; } = ""; // UserId who added this member
    }

    /// <summary>
    /// DTO for displaying groups with member info
    /// </summary>
    public class GroupDto
    {
        public string Id { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int MemberCount { get; set; } = 0;
        public string CreatedAt { get; set; } = "";
        public bool IsOwner { get; set; } = false;
        public List<GroupMember> Members { get; set; } = new();
    }

    /// <summary>
    /// Model for creating a new group
    /// </summary>
    public class CreateGroupModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> InitialMemberIds { get; set; } = new();
    }
}
