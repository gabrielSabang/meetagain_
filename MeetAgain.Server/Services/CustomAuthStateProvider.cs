using System.Security.Claims;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MeetAgain.Server.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private const string TokenKey = "authToken";
        private readonly ProtectedSessionStorage _storage;
        private readonly JwtTokenService _jwt;
        private string? _token;

        public CustomAuthStateProvider(ProtectedSessionStorage storage, JwtTokenService jwt)
        {
            _storage = storage;
            _jwt = jwt;
        }

        public async Task SetTokenAsync(string? token)
        {
            _token = token;

            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    await _storage.DeleteAsync(TokenKey);
                else
                    await _storage.SetAsync(TokenKey, token);
            }
            catch
            {
                // JS interop unavailable (e.g. prerendering / API path) — keep in-memory token only.
            }

            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_token))
                {
                    try
                    {
                        var stored = await _storage.GetAsync<string>(TokenKey);
                        if (stored.Success && !string.IsNullOrWhiteSpace(stored.Value))
                            _token = stored.Value;
                    }
                    catch
                    {
                        // ignore storage failures
                    }
                }

                if (string.IsNullOrWhiteSpace(_token))
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

                var principal = _jwt.ValidateToken(_token);
                if (principal == null)
                {
                    try { await _storage.DeleteAsync(TokenKey); } catch { }
                    _token = null;
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                var identity = new ClaimsIdentity(principal.Claims, "jwt");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                _token = null;
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }
    }
}
