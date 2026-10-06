using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MeetAgain.Server.Services.Mongo
{
    public class JwtOptions
    {
        public string Secret { get; set; } = "";
        public int ExpiresMinutes { get; set; } = 10080;
    }

    /// <summary>Issues and validates self-contained JWTs (BCrypt passwords live in users.passwordHash).</summary>
    public class JwtTokenService
    {
        private readonly JwtOptions _options;
        public JwtTokenService(JwtOptions options) => _options = options;

        public string IssueToken(string userId, string email)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("uid", userId),
                new Claim(ClaimTypes.Email, email ?? ""),
            };
            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
                return handler.ValidateToken(token, parameters, out _);
            }
            catch
            {
                return null;
            }
        }
    }
}
