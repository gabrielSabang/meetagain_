using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Route("api/v1/auth")]
    public class AuthController : ControllerBase
    {
        private readonly MongoService _db;
        private readonly AuthService _auth;

        public AuthController(MongoService db, AuthService auth)
        {
            _db = db;
            _auth = auth;
        }

        // POST /api/auth/signup (also /api/v1/auth/signup)
        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] SignupRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.DisplayName))
                return BadRequest(ApiErrors.BadRequest("email, password and displayName are required."));

            var existing = await _db.GetUserByEmailAsync(req.Email.Trim().ToLowerInvariant());
            if (existing != null)
                return Conflict(ApiErrors.Conflict("Email already exists."));

            var user = new AppUser
            {
                Uid = Guid.NewGuid().ToString("N"),
                Email = req.Email.Trim().ToLowerInvariant(),
                DisplayName = req.DisplayName.Trim(),
                CreatedAt = DateTime.UtcNow.ToString("o"),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            };
            await _db.CreateOrUpdateUserAsync(user);
            return CreatedAtAction(nameof(Me), new { userId = user.Uid }, new { user = SafeUser(user) });
        }

        // POST /api/auth/login — verifies BCrypt hash, returns JWT.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiErrors.BadRequest("Validation failed.", ModelState));
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(ApiErrors.BadRequest("email and password are required."));

            var (ok, token, user, error) = await _auth.LoginWithTokenAsync(req.Email, req.Password);
            if (!ok || user == null || token == null)
                return Unauthorized(ApiErrors.Unauthorized(error ?? "Invalid email or password."));

            // Client sends this back as: Authorization: Bearer <token> or X-User-Id: <uid>.
            return Ok(new { token, user = SafeUser(user) });
        }

        // POST /api/auth/logout — stateless (client discards token).
        [HttpPost("logout")]
        public IActionResult Logout() => Ok(new { message = "Logged out. Discard token client-side." });

        // GET /api/auth/me?userId=xxx (or Authorization: Bearer <token>, or X-User-Id header)
        [HttpGet("me")]
        public async Task<IActionResult> Me([FromQuery] string? userId)
        {
            var (uid, _) = await ApiUserContext.ResolveAsync(HttpContext, userId);
            if (string.IsNullOrWhiteSpace(uid))
                return Unauthorized(ApiErrors.Unauthorized());

            var user = await _db.GetUserAsync(uid);
            if (user == null) return NotFound(ApiErrors.NotFound("User not found."));
            return Ok(SafeUser(user));
        }

        private static object SafeUser(AppUser u) => new { uid = u.Uid, email = u.Email, displayName = u.DisplayName, createdAt = u.CreatedAt };
    }
}
