# MeetAgain — MongoDB Edition

Blazor Server + ASP.NET Core REST API on .NET 9, backed by **MongoDB Atlas** (migrated from Firebase/Firestore).
Auth is **BCrypt password hashes + self-issued JWTs** (no Firebase Auth).

## Prerequisites

- .NET 9 SDK (`dotnet --version` → 9.x)
- MongoDB Atlas cluster (or local `mongod`)
- (Migration only) Firestore access: `GOOGLE_APPLICATION_CREDENTIALS` pointing at `firebase-adminsdk.json` + `FIRESTORE_PROJECT_ID`

## Setup

1. Copy env template and fill secrets (never commit `.env`):
   ```bash
   cp MeetAgain.Server/.env.example MeetAgain.Server/.env
   ```
   Required keys in `MeetAgain.Server/.env`:
   | Key | Purpose |
   |---|---|
   | `MONGODB_URI` | Primary Atlas connection string (tried first) |
   | `MONGODB_URI_FALLBACK` | Used when primary fails (auth or connection failure) |
   | `MONGODB_DATABASE` | Database name (default `meetagain`) |
   | `JWT_SECRET` | ≥32-char signing key for app tokens |
   | `JWT_EXPIRES_MINUTES` | Token lifetime (default 10080 = 7 days) |
   | `ALLOWED_ORIGINS` | Comma-separated CORS origins |

2. Restore + build:
   ```bash
   dotnet build MeetAgain.Server/MeetAgain.Server.csproj
   ```

## Run

```bash
dotnet run --project MeetAgain.Server
# Dev URLs: http://localhost:5082  https://localhost:7155
```

- Health: `GET /health` (also `/api/health`, `/api/v1/health`) — returns `{status, db, collections{...}}`, or `503` with `{error, hint}` when Atlas is unreachable.
- Swagger (Development): `/swagger`
- API base: `/api/...` (legacy) and canonical `/api/v1/...` (both served). See `MeetAgain.Server/api.http` (VS Code REST Client) and `MeetAgain.Server/meetagain.postman_collection.json`.

### Auth flow
1. `POST /api/v1/auth/signup {email, password>=8, displayName}` → `201`
2. `POST /api/v1/auth/login {email, password}` → `{token, user}`
3. Subsequent calls: `Authorization: Bearer <token>` (or dev fallback `X-User-Id` / `?userId=`)

## Migration (Firestore → MongoDB, one-time, idempotent)

All writes are **upserts** (safe to re-run). Preserves document IDs, timestamps, and relationships
(`users` + `friends`/`friendRequests`/`notifications`/`settings.availability`,
`meetups` + `participants`, `groups` + `members`, legacy root `friends`).

```bash
export GOOGLE_APPLICATION_CREDENTIALS=/path/to/firebase-adminsdk.json
export FIRESTORE_PROJECT_ID=meetagain-50f2b
dotnet run --project tools/FirestoreToMongo
# → console per-collection counts + migration-report.json
```

Then verify: `GET /health` collection counts should match the migration report and Firestore console counts.

> **Note (2026-09-29):** from this network both Atlas URIs time out at TCP level
> (`Timed out connecting to ...:27017`), so live migration/CRUD verification could not run here.
> If you see `503 {"db":"disconnected"}` with a timeout hint, add your IP in
> Atlas → Network Access → IP Access List and retry. App code, validation, auth
> envelopes, Swagger, and `/health` structure were verified locally (build 0 warnings/0 errors).

## What changed (Firebase removal)

- Removed packages: `FirebaseAdmin`, `Google.Cloud.Firestore`, `Google.Apis.Auth`
- Removed: `Services/FirestoreService.cs`, `wwwroot/firebase.js`, `wwwroot/index.html` (stale WASM Firebase interop), `wwwroot/appsettings.json` (Firebase web config), `Firebase:{...}` from `appsettings.json`
- Added packages: `MongoDB.Driver`, `BCrypt.Net-Next`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Asp.Versioning.Mvc`, `Swashbuckle.AspNetCore`, `DotNetEnv`
- New: `Services/Mongo/{MongoDbContext,MongoService,JwtTokenService,UserAvailabilityDoc}.cs`, `Controllers/HealthController.cs`
- API upgrades (Backend API Master): versioned `/api/v1` routes (legacy `/api` kept), unified `{success,error{message,statusCode}}` errors, DataAnnotations validation, opt-in pagination (`?page=&pageSize=` → `{data,page,pageSize,total}`), CORS policy, fixed-window rate limiting (100/min global, 10/min auth), request-duration logging with >1s slow warnings, Swagger+JWT, Mongo indexes

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Missing MONGODB_URI` at startup | Create `MeetAgain.Server/.env` (see `.env.example`) |
| `Missing JWT_SECRET` / length error | Set ≥32-char `JWT_SECRET` in `.env` |
| `/health` 503 timeout | Atlas IP allowlist / credentials; check `MONGODB_URI` |
| `Email already exists` on signup | Expected 409 — log in instead |
| 401 `Missing user identity` | Send `Authorization: Bearer <JWT>` (login returns it) |
