using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;
using MongoDB.Driver;

namespace MeetAgain.Server.Services
{
    public class AvailabilityService
    {
        private readonly MongoDbContext _ctx;

        public AvailabilityService(MongoDbContext ctx) => _ctx = ctx;

        public async Task<UserAvailability> GetUserAvailabilityAsync(string userId)
        {
            var doc = await _ctx.Availability.Find(a => a.UserId == userId).FirstOrDefaultAsync();
            if (doc != null)
            {
                return new UserAvailability
                {
                    UserId = userId,
                    PreferredDays = doc.PreferredDays,
                    AvailableTimeSlots = doc.AvailableTimeSlots.Select(t => new TimeSlot
                    {
                        Start = TimeOnly.Parse(t.Start), End = TimeOnly.Parse(t.End),
                    }).ToList(),
                    BlockedDates = doc.BlockedDates.Select(d => DateTime.Parse(d)).ToList(),
                };
            }

            return new UserAvailability
            {
                UserId = userId,
                PreferredDays = new List<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
                AvailableTimeSlots = new List<TimeSlot> { new() { Start = new TimeOnly(9, 0), End = new TimeOnly(22, 0) } },
                BlockedDates = new List<DateTime>(),
            };
        }

        public async Task SaveUserAvailabilityAsync(UserAvailability availability)
        {
            var doc = new UserAvailabilityDoc
            {
                UserId = availability.UserId,
                PreferredDays = availability.PreferredDays,
                AvailableTimeSlots = availability.AvailableTimeSlots.Select(t => new TimeSlotDoc
                {
                    Start = t.Start.ToString("HH:mm"), End = t.End.ToString("HH:mm"),
                }).ToList(),
                BlockedDates = availability.BlockedDates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
            };
            await _ctx.Availability.ReplaceOneAsync(a => a.UserId == doc.UserId, doc, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<SuggestedTimeSlot>> FindBestMeetupTimesAsync(
            List<string> participantUserIds, DateTime startDate, DateTime endDate,
            int durationMinutes = 120, int maxSuggestions = 5)
        {
            var availabilities = new List<UserAvailability>();
            foreach (var userId in participantUserIds)
                availabilities.Add(await GetUserAvailabilityAsync(userId));

            var existingMeetups = await GetExistingMeetupsAsync(participantUserIds, startDate, endDate);
            var suggestions = new List<SuggestedTimeSlot>();

            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                var dayOfWeek = date.DayOfWeek.ToString();
                if (availabilities.Any(a => a.BlockedDates.Any(bd => bd.Date == date.Date)))
                    continue;
                suggestions.AddRange(FindCommonTimeSlots(availabilities, dayOfWeek, date, existingMeetups, durationMinutes));
            }

            return suggestions.OrderByDescending(s => s.AvailabilityScore).ThenBy(s => s.StartTime)
                .Take(maxSuggestions).ToList();
        }

        private List<SuggestedTimeSlot> FindCommonTimeSlots(
            List<UserAvailability> availabilities, string dayOfWeek, DateTime date,
            List<Meetup> existingMeetups, int durationMinutes)
        {
            var suggestions = new List<SuggestedTimeSlot>();
            var availableUsers = availabilities.Where(a => a.PreferredDays.Contains(dayOfWeek)).ToList();
            if (availableUsers.Count == 0) availableUsers = availabilities;

            var allTimeSlots = availableUsers.SelectMany(a => a.AvailableTimeSlots).ToList();
            if (allTimeSlots.Count == 0)
                allTimeSlots = new List<TimeSlot> { new() { Start = new TimeOnly(9, 0), End = new TimeOnly(22, 0) } };

            var earliestStart = allTimeSlots.Min(s => s.Start);
            var latestEnd = allTimeSlots.Max(s => s.End);

            var currentTime = earliestStart;
            while (currentTime.AddMinutes(durationMinutes) <= latestEnd)
            {
                var endTime = currentTime.AddMinutes(durationMinutes);
                int availableCount = availabilities.Count(a => IsUserAvailable(a, currentTime, endTime, date, existingMeetups));
                double score = (double)availableCount / availabilities.Count * 100;
                var minAvailable = availabilities.Count > 2 ? availabilities.Count * 0.5 : 1;
                if (availableCount >= minAvailable)
                {
                    suggestions.Add(new SuggestedTimeSlot
                    {
                        StartTime = date.Add(currentTime.ToTimeSpan()),
                        EndTime = date.Add(endTime.ToTimeSpan()),
                        AvailableParticipants = availableCount,
                        TotalParticipants = availabilities.Count,
                        AvailabilityScore = score,
                        ConflictCount = availabilities.Count - availableCount,
                    });
                }
                currentTime = currentTime.AddMinutes(30);
            }
            return suggestions;
        }

        private bool IsUserAvailable(UserAvailability userAvail, TimeOnly startTime, TimeOnly endTime, DateTime date, List<Meetup> existingMeetups)
        {
            if (!userAvail.AvailableTimeSlots.Any(slot => startTime >= slot.Start && endTime <= slot.End))
                return false;
            var startDateTime = date.Add(startTime.ToTimeSpan());
            var endDateTime = date.Add(endTime.ToTimeSpan());
            return !existingMeetups.Where(m => m.Status != "cancelled")
                .Any(m => m.EventDateTime < endDateTime && m.EventDateTime.AddHours(2) > startDateTime);
        }

        private async Task<List<Meetup>> GetExistingMeetupsAsync(List<string> participantUserIds, DateTime startDate, DateTime endDate)
        {
            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(endDate.AddDays(1), DateTimeKind.Utc);
            var inRange = await _ctx.Meetups
                .Find(m => m.EventDateTime >= startUtc && m.EventDateTime <= endUtc).ToListAsync();

            var result = new List<Meetup>();
            foreach (var m in inRange)
            {
                if (participantUserIds.Contains(m.CreatorUserId))
                {
                    result.Add(m);
                    continue;
                }
                var count = await _ctx.MeetupParticipants.CountDocumentsAsync(
                    p => p.MeetupId == m.Id && participantUserIds.Contains(p.UserId));
                if (count > 0) result.Add(m);
            }
            return result;
        }
    }

    public class UserAvailability
    {
        public string UserId { get; set; } = "";
        public List<string> PreferredDays { get; set; } = new();
        public List<TimeSlot> AvailableTimeSlots { get; set; } = new();
        public List<DateTime> BlockedDates { get; set; } = new();
    }

    public class TimeSlot
    {
        public TimeOnly Start { get; set; }
        public TimeOnly End { get; set; }
    }

    public class SuggestedTimeSlot
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int AvailableParticipants { get; set; }
        public int TotalParticipants { get; set; }
        public double AvailabilityScore { get; set; }
        public int ConflictCount { get; set; }
        public string DisplayText =>
            $"{StartTime:ddd, MMM dd} at {StartTime:h:mm tt} - {EndTime:h:mm tt} " +
            $"({AvailableParticipants}/{TotalParticipants} available - {AvailabilityScore:F0}%)";
    }
}
