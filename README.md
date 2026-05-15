# Taxpayer Behavioral Analytics

ASP.NET Core 10 system that tracks taxpayer behaviour on 4 SMS-driven landing
pages and stores everything in SQL Server. No dashboard yet — that's a
follow-up.

## Solution layout

```
src/
  SharedLibrary/        EF Core entities, DTOs, security primitives (token, cipher, bot detector, UA parser)
  LandingPortal/        Public site — serves the 4 Razor views ported from the uploaded HTMLs
  TrackingApi/          Analytics ingestion API + background flush service
db/
  schema.sql            SQL Server DDL for all 4 tables, indexes, and seed campaigns
  partitioning.sql      Optional monthly partitioning for EventLog
```

## How a taxpayer interaction flows

1. The existing SMS pipeline writes a row into `TaxpayerRecipient` with the
   encrypted NTN/mobile and a `TrackingToken` (AES-GCM payload from
   `TrackingTokenService.Issue`).
2. SMS is sent with `https://<host>/c?t=<TrackingToken>`.
3. `LandingPortal CampaignController` validates the token, looks up the
   recipient, picks the Razor view named after `CampaignMaster.PageTemplate`
   (`Index | Enforcement | Combined | CivicDuty`), and renders it.
4. Embedded `tracking.js` calls `POST /api/v1/session/start` to open a session,
   then batches behavioural events to `POST /api/v1/events/batch` and pings
   `POST /api/v1/events/heartbeat` every 10s.
5. `EventFlushService` drains the in-memory ingestion queue into SQL Server in
   batches of up to 500 events, with a 2-second ceiling so partial batches land
   under light load.
6. The redirect button paths go through `/c/redirect?t=...&target=register|file`
   so the click is recorded server-side before bouncing to IRIS.

## Database setup

**Automatic (default).** Both apps run `db/schema.sql` at startup:

  1. Connect to `master` and `CREATE DATABASE` if missing.
  2. Apply `db/schema.sql` against the target DB (idempotent: every CREATE is
     guarded by `IF OBJECT_ID(...) IS NULL` or `IF NOT EXISTS`).
  3. Seed the 4 campaigns (`GENERAL`, `ENFORCEMENT`, `COMBINED`, `CIVIC`) via
     a MERGE so re-runs don't duplicate.

So a clean clone just needs a connection string and `dotnet run`. The csproj
copies `db/schema.sql` to the output dir as `DbScripts/schema.sql`; the
`SqlSchemaInitializer` reads it from `AppContext.BaseDirectory` at startup.

**Disable auto-apply** for production where a DBA manages migrations:

```jsonc
// appsettings.Production.json
{ "Database": { "AutoApplySchema": false } }
```

**Manual.** Same script, run yourself:

```bash
sqlcmd -S . -d master -Q "CREATE DATABASE TaxpayerAnalytics;"
sqlcmd -S . -d TaxpayerAnalytics -i db/schema.sql
# Optional, for high-volume EventLog:
sqlcmd -S . -d TaxpayerAnalytics -i db/partitioning.sql
```

**EF Core migrations.** If you prefer EF-tracked migrations over the SQL
script, generate them once and disable the auto-apply:

```bash
cd src
dotnet ef migrations add InitialCreate \
    --project SharedLibrary \
    --startup-project TrackingApi \
    --output-dir Migrations
dotnet ef database update --startup-project TrackingApi
```

## Configuration

All secrets in `appsettings.json` are placeholders. In production load them
from Key Vault / environment variables:

| Key | Purpose |
| --- | --- |
| `Security:TokenEncryptionKey` | AES key for the SMS tracking token (32+ bytes) |
| `Security:PiiEncryptionKey`   | AES key for column-level NTN/Mobile encryption |
| `Security:NtnHashPepper`      | HMAC pepper for `NtnHash` lookups |
| `ConnectionStrings:AnalyticsDb` | SQL Server connection string |
| `Tracking:ApiBaseUrl`         | Origin of the TrackingApi (CORS + CSP `connect-src`) |
| `Cors:AllowedOrigins`         | Comma-separated origins the API accepts |

Rotate keys carefully — the token key is what makes existing SMS links
verifiable. If you rotate, support both old + new keys during a transition
window or invalidate outstanding links.

## Required runtime assets

The Razor views reference image and video files that aren't committed
(binaries shouldn't sit in git). Drop them in before running:

```
src/LandingPortal/wwwroot/images/   # iris-logo.png, fbr-logo.png, etc. (see README.txt)
src/LandingPortal/wwwroot/videos/   # video1.mp4 (Index/Combined/CivicDuty), video2.mp4 (Enforcement)
```

Per-campaign overrides are available — set `CampaignMaster.VideoUrl` to point
at a CDN URL and the view will use it instead of the local file.

## Tracked events

| EventType (int)  | Source | Notes |
| --- | --- | --- |
| `PageOpen` (1)        | `/api/v1/session/start` | Once per session |
| `PageClose` (2)       | `pagehide` / `beforeunload` | Beacon flush |
| `Heartbeat` (3)       | Every 10s while visible | Updates session aggregates |
| `ScrollDepth` (4)     | Quartile thresholds (25/50/75/100) | Bounded volume |
| `VideoPlay/Pause/Complete/Progress` (10–13) | `<video data-tptrack="video">` | `Progress` fires every 5s |
| `RegisterClick` (20)  | `data-tptrack-cta="register"` | Force-flushed before redirect |
| `FileClick` (21)      | `data-tptrack-cta="file"` | Force-flushed before redirect |
| `CtaClick` (22)       | Any other `data-tptrack-cta` | |
| `OutboundRedirect` (23) | Server-side at `/c/redirect` | Tagged with `ref=` + `rid=` |
| `Bounce` / `Engagement` (30, 50) | Derived in session metrics | `IsEngaged` if duration ≥ 15s or scroll ≥ 50 |
| `BotDetected` (99)    | Server-side in `SessionController` | Session marked `IsBot`; future batches dropped |

Aggregates also live on `UserSession` (`MaxScrollDepth`, `VideoWatchSeconds`,
`RegisterClicked`, `FileClicked`, etc.) so a future dashboard can answer
common questions without re-aggregating `EventLog`.

## Security posture

- **No NTN in URLs.** SMS link only contains an AES-GCM authenticated token.
- **PII encrypted at rest.** `NtnEncrypted`/`MobileEncrypted` use AES-256-GCM
  with keys held outside the database.
- **NTN lookup without decryption** via deterministic `HMAC(NTN, pepper)`.
- **CSP, HSTS, X-Frame-Options, X-Content-Type-Options** on both portal + API.
- **Bot filter** drops fake traffic before it pollutes analytics.
- **Rate limiting** per-IP on the API (default 120 req/min, configurable).
- **HTTPS-only** redirection + `Strict-Transport-Security`.

## Local run

```bash
cd src
dotnet restore
dotnet run --project TrackingApi      # https://localhost:5443 (or whatever launchSettings says)
dotnet run --project LandingPortal    # https://localhost:5001
```

Then in SQL Server, generate a token for an existing recipient:

```csharp
// in any C# script with a configured TrackingTokenService instance:
var token = tokens.Issue(recipientId, campaignId);
// SMS body: https://landing.example.gov/c?t={token}
```
