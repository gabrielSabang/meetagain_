using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/availability")]
    [Route("api/v1/availability")]
    public class AvailabilityController : ControllerBase
    {
        private readonly AvailabilityService _svc;

        public AvailabilityController(AvailabilityService svc) => _svc = svc;

        private static UserAvailability ToDomain(string userId, SaveAvailabilityRequest req)
        {
            return new UserAvailability
            {
                UserId = userId,
                PreferredDays = req.PreferredDays ?? new(),
                AvailableTimeSlots = (req.AvailableTimeSlots ?? new()).Select(t => new TimeSlot
                {
                    Start = TimeOnly.Parse(t.Start),
                    End = TimeOnly.Parse(t.End),
                }).ToList(),
                BlockedDates = req.BlockedDates ?? new(),
            };
        }

        private static object ToDto(UserAvailability a) => new
        {
            userId = a.UserId,
            preferredDays = a.PreferredDays,
            availableTimeSlots = a.AvailableTimeSlots.Select(t => new { start = t.Start.ToString("HH:mm"), end = t.End.ToString("HH:mm") }),
            blockedDates = a.BlockedDates,
        };

        // GET /api/availability/{userId}
        [HttpGet("{userId}")]
        public async Task<IActionResult> Get(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest(ApiErrors.BadRequest("userId is required."));
            var avail = await _svc.GetUserAvailabilityAsync(userId);
            return Ok(ToDto(avail));
        }

        // PUT /api/availability/{userId}
        [HttpPut("{userId}")]
        public async Task<IActionResult> Save(string userId, [FromBody] SaveAvailabilityRequest req)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest(ApiErrors.BadRequest("userId is required."));
            try
            {
                await _svc.SaveUserAvailabilityAsync(ToDomain(userId, req));
                return Ok(new { message = "Availability saved." });
            }
            catch (FormatException ex)
            {
                return BadRequest(ApiErrors.BadRequest("Invalid time format. Use HH:mm. " + ex.Message));
            }
        }

        // POST /api/availability/suggest
        [HttpPost("suggest")]
        public async Task<IActionResult> Suggest([FromBody] SuggestTimesRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (req.ParticipantUserIds is not { Count: > 0 })
                return BadRequest(ApiErrors.BadRequest("participantUserIds[] is required."));
            if (req.StartDate == default || req.EndDate == default)
                return BadRequest(ApiErrors.BadRequest("startDate and endDate are required (ISO 8601)."));

            var suggestions = await _svc.FindBestMeetupTimesAsync(
                req.ParticipantUserIds, req.StartDate, req.EndDate,
                req.DurationMinutes <= 0 ? 120 : req.DurationMinutes,
                req.MaxSuggestions <= 0 ? 5 : req.MaxSuggestions);
            return Ok(suggestions);
        }
    }
}
