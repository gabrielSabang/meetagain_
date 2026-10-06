using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using MeetAgain.Server.Services;
using MeetAgain.Server.Services.Mongo;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.IdentityModel.Tokens;

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// MongoDB (primary MONGODB_URI, fallback MONGODB_URI_FALLBACK on auth failure)
// ------------------------------------------------------
var mongoSection = builder.Configuration.GetSection("MongoDb");
var dbName = Environment.GetEnvironmentVariable("MONGODB_DATABASE")
    ?? mongoSection["Database"] ?? "meetagain";

builder.Services.AddSingleton<MongoDbContext>();

// ------------------------------------------------------
// JWT
// ------------------------------------------------------
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "";
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new Exception("Missing JWT_SECRET. Set it in .env or environment (min 32 chars).");
if (jwtSecret.Length < 32)
    throw new Exception("JWT_SECRET must be at least 32 characters.");

var jwtExpires = int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRES_MINUTES"), out var m)
    ? m : (mongoSection.GetSection("Jwt").GetValue<int?>("ExpiresMinutes") ?? builder.Configuration.GetValue<int?>("Jwt:ExpiresMinutes") ?? 10080);

builder.Services.AddSingleton(new JwtOptions { Secret = jwtSecret, ExpiresMinutes = jwtExpires });
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(2),
        };
    });
builder.Services.AddAuthorization();

// ------------------------------------------------------
// CORS (env-driven, Backend API Master skill)
// ------------------------------------------------------
var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("app", policy =>
    {
        if (allowedOrigins.Length == 0)
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(allowedOrigins);
        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

// ------------------------------------------------------
// Rate limiting (fixed window: global 100/min/IP, auth 10/min)
// ------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("global", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 100, QueueLimit = 0,
        }));
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "anon-auth",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 10, QueueLimit = 0,
        }));
});

// ------------------------------------------------------
// Blazor + REST API
// ------------------------------------------------------
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MeetAgain API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization", Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer", BearerFormat = "JWT", In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token.",
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});
builder.Services.AddHttpContextAccessor();

// browser storage
builder.Services.AddScoped<ProtectedLocalStorage>();
builder.Services.AddScoped<ProtectedSessionStorage>();

// auth state provider wiring
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CustomAuthStateProvider>());

builder.Services.AddAuthorizationCore();

// Blazor server + circuit options
builder.Services.AddServerSideBlazor().AddCircuitOptions(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
});

// ------------------------------------------------------
// App Services
// ------------------------------------------------------
builder.Services.AddSingleton<MongoService>();
builder.Services.AddScoped(sp =>
{
    var mongo = sp.GetRequiredService<MongoService>();
    var jwt = sp.GetRequiredService<JwtTokenService>();
    var authStateProvider = sp.GetRequiredService<CustomAuthStateProvider>();
    var svc = new AuthService(mongo, jwt);
    svc.AuthStateProvider = authStateProvider;
    mongo.AuthStateProvider = authStateProvider;
    return svc;
});

builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<MeetupService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AvailabilityService>();

// ------------------------------------------------------
// Build app
// ------------------------------------------------------
var app = builder.Build();

// Ensure Mongo indexes at startup (non-fatal if DB unreachable in offline dev)
try
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MongoDbContext>().EnsureIndexesAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "MongoDB index creation skipped at startup.");
}

// JSON error envelope for API/health paths (both envs): DB outages etc.
// return {success:false,error{...}} instead of HTML/stack traces.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var status = ex is TimeoutException or MongoDB.Driver.MongoException ? 503 : 500;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = new
            {
                message = status == 503 ? "Database unavailable." : "Internal server error.",
                statusCode = status,
                details = app.Environment.IsDevelopment() ? ex?.GetType().Name + ": " + ex?.Message : null,
            },
        });
    })));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MeetAgain API v1"));
}

// Request logging with duration + slow-op warning (skill: requestLogger)
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("{Method} {Path} -> {Status} in {ElapsedMs}ms",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
    if (sw.ElapsedMilliseconds > 1000)
        logger.LogWarning("Slow operation: {Method} {Path} took {ElapsedMs}ms",
            context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseCors("app");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
