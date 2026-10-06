using MeetAgain.Server.Models;
using MeetAgain.Server.Services.Mongo;

namespace MeetAgain.Server.Services
{
    public class AuthService
    {
        private readonly MongoService _db;
        private readonly JwtTokenService _jwt;

        public AppUser? CurrentUser { get; private set; }
        public string? CurrentToken { get; private set; }
        public string? UserId => CurrentUser?.Uid;

        public CustomAuthStateProvider? AuthStateProvider { get; set; }

        /// <summary>Exposes last auth error (renamed from LastFirebaseError; kept as alias for pages).</summary>
        public string? LastAuthError { get; private set; }
        public string? LastFirebaseError => LastAuthError;

        public AuthService(MongoService db, JwtTokenService jwt)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _jwt = jwt ?? throw new ArgumentNullException(nameof(jwt));
        }

        // ---------------- REGISTER ----------------
        public async Task<bool> SignUpAsync(string email, string password, string displayName)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
            {
                LastAuthError = "Email, password and display name are required.";
                return false;
            }

            try
            {
                var existing = await _db.GetUserByEmailAsync(email.Trim().ToLowerInvariant());
                if (existing != null)
                {
                    LastAuthError = "Email already exists.";
                    return false;
                }

                var user = new AppUser
                {
                    Uid = Guid.NewGuid().ToString("N"),
                    Email = email.Trim().ToLowerInvariant(),
                    DisplayName = displayName.Trim(),
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                };
                await _db.CreateOrUpdateUserAsync(user);

                CurrentUser = user;
                CurrentToken = _jwt.IssueToken(user.Uid, user.Email);
                if (AuthStateProvider != null)
                    await AuthStateProvider.SetTokenAsync(CurrentToken);

                LastAuthError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastAuthError = "Registration failed: " + ex.Message;
                return false;
            }
        }

        // ---------------- LOGIN (Mongo + BCrypt, issues JWT) ----------------
        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                var user = await _db.GetUserByEmailAsync(email.Trim().ToLowerInvariant());
                if (user == null || string.IsNullOrEmpty(user.PasswordHash) ||
                    !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                {
                    LastAuthError = "Invalid email or password.";
                    return false;
                }

                CurrentUser = user;
                CurrentToken = _jwt.IssueToken(user.Uid, user.Email);
                if (AuthStateProvider != null)
                    await AuthStateProvider.SetTokenAsync(CurrentToken);

                LastAuthError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastAuthError = ex.Message;
                return false;
            }
        }

        /// <summary>Used by POST /api/auth/login to also return the token.</summary>
        public async Task<(bool Ok, string? Token, AppUser? User, string? Error)> LoginWithTokenAsync(string email, string password)
        {
            var ok = await LoginAsync(email, password);
            return ok ? (true, CurrentToken, CurrentUser, null) : (false, null, null, LastAuthError);
        }

        // ---------------- LOGOUT ----------------
        public async Task LogoutAsync()
        {
            CurrentUser = null;
            CurrentToken = null;
            if (AuthStateProvider != null)
                await AuthStateProvider.SetTokenAsync(null);
            LastAuthError = null;
        }

        public Task<AppUser?> GetCurrentUserAsync() => Task.FromResult(CurrentUser);
    }
}
