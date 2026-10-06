using System.ComponentModel.DataAnnotations;

namespace MeetAgain.Server.Models
{
    public class SignupRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, MinLength(8)] public string Password { get; set; } = "";
        [Required, MinLength(2), MaxLength(50)] public string DisplayName { get; set; } = "";
    }

    public class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Password { get; set; } = "";
    }

    public class UpdateUserRequest
    {
        [EmailAddress] public string Email { get; set; } = "";
        [MinLength(2), MaxLength(50)] public string DisplayName { get; set; } = "";
    }

    public class SendFriendRequestBody
    {
        public string UserId { get; set; } = "";
        [Required, EmailAddress] public string RecipientEmail { get; set; } = "";
    }

    public class FriendActionBody
    {
        public string UserId { get; set; } = "";
    }

    public class CreateGroupRequest
    {
        public string UserId { get; set; } = "";
        [Required, MinLength(2), MaxLength(100)] public string Name { get; set; } = "";
        [MaxLength(500)] public string Description { get; set; } = "";
        public List<string> InitialMemberIds { get; set; } = new();
    }

    public class AddGroupMembersRequest
    {
        public string UserId { get; set; } = "";
        [MinLength(1)] public List<string> MemberIds { get; set; } = new();
    }

    public class CreateMeetupRequest
    {
        public string UserId { get; set; } = "";
        [Required, MinLength(2), MaxLength(150)] public string Title { get; set; } = "";
        [MaxLength(2000)] public string Description { get; set; } = "";
        [Required] public DateTime EventDateTime { get; set; }
        [MaxLength(300)] public string Location { get; set; } = "";
        public List<string> InvitedFriendIds { get; set; } = new();
    }

    public class UpdateMeetupRequest
    {
        public string UserId { get; set; } = "";
        [MinLength(2), MaxLength(150)] public string Title { get; set; } = "";
        [MaxLength(2000)] public string Description { get; set; } = "";
        public DateTime EventDateTime { get; set; }
        [MaxLength(300)] public string Location { get; set; } = "";
        public string Status { get; set; } = "confirmed";
    }

    public class RsvpRequest
    {
        public string UserId { get; set; } = "";
        [Required] public string Status { get; set; } = "accepted";
    }

    public class TimeSlotDto
    {
        [Required] public string Start { get; set; } = "09:00";
        [Required] public string End { get; set; } = "22:00";
    }

    public class SaveAvailabilityRequest
    {
        public List<string> PreferredDays { get; set; } = new();
        public List<TimeSlotDto> AvailableTimeSlots { get; set; } = new();
        public List<DateTime> BlockedDates { get; set; } = new();
    }

    public class SuggestTimesRequest
    {
        [MinLength(1)] public List<string> ParticipantUserIds { get; set; } = new();
        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }
        [Range(15, 1440)] public int DurationMinutes { get; set; } = 120;
        [Range(1, 50)] public int MaxSuggestions { get; set; } = 5;
    }

    /// <summary>Unified error envelope (Backend API Master skill).</summary>
    public class ApiError
    {
        public bool Success { get; set; } = false;
        public ApiErrorBody Error { get; set; } = new();
    }

    public class ApiErrorBody
    {
        public string Message { get; set; } = "";
        public int StatusCode { get; set; }
        public object? Details { get; set; }
    }

    /// <summary>Paginated list envelope (opt-in via ?page=&amp;pageSize=).</summary>
    public class PagedResult<T>
    {
        public List<T> Data { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }
    }

    public static class ApiErrors
    {
        public static object NotFound(string message) => new { success = false, error = new { message, statusCode = 404 } };
        public static object BadRequest(string message, object? details = null) => new { success = false, error = new { message, statusCode = 400, details } };
        public static object Unauthorized(string message = "Missing user identity. Send Authorization: Bearer <JWT>, X-User-Id header, or userId in query/body.") => new { success = false, error = new { message, statusCode = 401 } };
        public static object Conflict(string message) => new { success = false, error = new { message, statusCode = 409 } };
    }
}
