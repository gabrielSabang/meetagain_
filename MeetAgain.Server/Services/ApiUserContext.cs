using System.Security.Claims;
using MeetAgain.Server.Services.Mongo;
using Microsoft.Extensions.DependencyInjection;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Stateless helper to resolve the calling user inside API controllers.
    /// Priority: HttpContext claims -&gt; Bearer JWT -&gt; X-User-Id header -&gt; ?userId query.
    /// </summary>
    public static class ApiUserContext
    {
        public static string? GetUserIdFromClaims(HttpContext http)
        {
            var uid = http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? http.User?.FindFirst("uid")?.Value
                ?? http.User?.FindFirst("user_id")?.Value
                ?? http.User?.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(uid) ? null : uid;
        }

        public static async Task<(string? Uid, string? Email)> ResolveAsync(
            HttpContext http,
            string? userIdQuery = null,
            string? userIdBody = null)
        {
            // 1. Claims already populated by JWT middleware
            var claimed = GetUserIdFromClaims(http);
            if (!string.IsNullOrWhiteSpace(claimed))
            {
                var email = http.User?.FindFirst(ClaimTypes.Email)?.Value;
                return (claimed, email);
            }

            // 2. Authorization: Bearer <JWT>
            if (http.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var bearer = authHeader.ToString();
                if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = bearer["Bearer ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        var jwt = http.RequestServices.GetService<JwtTokenService>();
                        var principal = jwt?.ValidateToken(token);
                        if (principal != null)
                        {
                            var uid = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                ?? principal.FindFirst("uid")?.Value;
                            var em = principal.FindFirst(ClaimTypes.Email)?.Value;
                            if (!string.IsNullOrWhiteSpace(uid))
                                return (uid, em);
                        }
                    }
                }
            }

            await Task.CompletedTask;

            // 3. X-User-Id header (dev convenience; logged by controllers when used)
            if (http.Request.Headers.TryGetValue("X-User-Id", out var headerUid))
            {
                var h = headerUid.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(h)) return (h, null);
            }

            // 4. Explicit userId from body or query
            if (!string.IsNullOrWhiteSpace(userIdBody)) return (userIdBody.Trim(), null);
            if (!string.IsNullOrWhiteSpace(userIdQuery)) return (userIdQuery.Trim(), null);
            if (http.Request.Query.TryGetValue("userId", out var q))
            {
                var qv = q.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(qv)) return (qv, null);
            }

            return (null, null);
        }

        public static async Task<string?> RequireUserIdAsync(
            HttpContext http,
            string? userIdQuery = null,
            string? userIdBody = null)
        {
            var (uid, _) = await ResolveAsync(http, userIdQuery, userIdBody);
            return string.IsNullOrWhiteSpace(uid) ? null : uid;
        }
    }
}
