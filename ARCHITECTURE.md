# Architecture

A 5-minute orientation for a developer joining this codebase.

## What this is

A single ASP.NET Core 10 web app that:

1. Serves four SMS landing pages (`/c?t=<token>`).
2. Receives behaviour events from a JavaScript SDK embedded in those pages.
3. Stores everything in SQL Server.
4. Hosts a real-time admin dashboard under `/dashboard`.

There is no separate API process. The same Kestrel server hosts the public
landing pages, the tracking endpoints, and the dashboard. Everything is
same-origin — no CORS, no second deploy.

## Solution layout

```
src/
  SharedLibrary/                        Cross-cutting types.
    Entities/                           EF Core entities + AnalyticsDbContext.
    Dtos/                               Request/response shapes for both
                                         /api/v1 (tracking) and /api/dashboard.
    Configuration/                      Strongly-typed options (Security, GeoIp,
                                         Tracking, Dashboard).
    Security/                           TrackingTokenService (opaque token + NTN
                                         hash), PiiCipher (AES-GCM column crypto),
                                         BotDetector, UserAgentParser.
    Constants/                          EngagementRules + TrackingLimits — every
                                         magic number lives here.
    Data/                               SqlSchemaInitializer (applies db/schema.sql
                                         at startup) + DbInitializerExtensions.
    Enums/                              EventType, DeviceType, CampaignStatus.

  LandingPortal/                        The single ASP.NET Core app.
    Program.cs                          DI + pipeline. Read this first.
    Controllers/
      CampaignController.cs             GET  /c?t=...           — renders a Razor view
                                         GET  /c/redirect        — tracked outbound
      HomeController.cs                 GET  /  /dev/test        — dummy-token launcher
      SessionController.cs              POST /api/v1/session/start
      EventsController.cs               POST /api/v1/events/batch + /heartbeat
      Dashboard/
        AuthController.cs               GET/POST /dashboard/login, POST /logout
        DashboardController.cs          HTML pages: overview, campaign, taxpayers,
                                         taxpayer, feed.
        DashboardApiController.cs       JSON for charts + live refresh.
        ExportController.cs             Excel + PDF.
    Hubs/
      DashboardHub.cs                   SignalR. Three groups: all / per-campaign /
                                         per-recipient.
    Services/
      DummyDataSeeder.cs                Creates 4 dummy recipients for the launcher.
      GeoIpService.cs                   Optional MaxMind GeoIP2 City lookup.
      EventIngestionQueue.cs            Bounded Channel<EventLog> between controllers
                                         and the background flush.
      Dashboard/
        DashboardQueryService.cs        All SELECT queries for the dashboard.
        RealtimeNotifier.cs             Thin wrapper around IHubContext.
        ExcelExporter.cs                ClosedXML workbooks.
        PdfExporter.cs                  QuestPDF reports.
    BackgroundServices/
      EventFlushService.cs              Drains the queue into SQL in batches of 500
                                         / every 2 seconds.
      ActiveUsersBroadcaster.cs         Pushes per-campaign active-user counts every
                                         5 seconds via SignalR.
    Repositories/
      Repositories.cs                   ISessionRepository + IEventRepository — the
                                         only direct DB readers/writers outside the
                                         query service.
    Middleware/
      SecurityHeadersMiddleware.cs      X-Content-Type-Options, X-Frame-Options, etc.
      GlobalExceptionMiddleware.cs      Catches unhandled errors; JSON for /api,
                                         HTML for everything else.
    Models/
      LandingPageViewModel.cs           Per-request view model.
      PageTemplateMeta.cs               Single source for "what icon + accent does
                                         this campaign template get on the dashboard".
      Dashboard/                        Login VM + DashboardOptions (admin creds).
    Views/Campaign/                     4 SMS landing pages + Invalid.cshtml.
    Views/Dashboard/                    Login + overview + campaign + taxpayers +
                                         taxpayer + feed + _Layout.
    Views/Home/TestLauncher.cshtml      Dev-only landing-page launcher.
    wwwroot/
      css/styles.css                    Public landing-page styles (your upload).
      css/dashboard.css                 Dashboard theme — glassmorphism + animations.
      js/tracking.js                    The analytics SDK. Loaded by every Razor
                                         landing page. Same-origin to /api/v1/...
      js/dashboard.js                   DashRealtime (SignalR), DashLive (refresh +
                                         flash), DashFeed, DashCharts, DashAnim.

db/
  schema.sql                            Idempotent DDL — runs at every app start.
  partitioning.sql                      Optional monthly partitioning for EventLog.
  reset-test-data.sql                   Three levels of test-data wipe.
```

## The two request flows

### A) Taxpayer hits an SMS link

```
SMS pipeline pre-populates TaxpayerRecipient rows (NTN + Mobile encrypted at rest,
TrackingToken = 22-char opaque random).

  Browser ─GET /c?t=<token>──────────────► CampaignController.Index
                                              │
                                              ▼
                                       lookup by TrackingToken,
                                       pick Razor view based on
                                       CampaignMaster.PageTemplate
                                              │
                                              ▼
                                       Render Index | Enforcement |
                                       Combined | CivicDuty .cshtml
                                       (every page embeds tracking.js)

  Browser ─POST /api/v1/session/start───► SessionController
                                              │
                                              ▼
                                       Session continuation:
                                       same recipient + heartbeat
                                       within 5 min → reuse session.
                                       Else create UserSession.
                                              │
                                              ▼
                                       Queue PageOpen EventLog
                                       Push LiveEvent over SignalR

  Browser ─POST /api/v1/events/batch ───► EventsController.TrackBatch
                                              │   (every 2s or 20 events)
                                              ▼
                                       Queue each event, update
                                       session aggregates in place,
                                       broadcast meaningful events.

  Browser ─POST /api/v1/events/heartbeat► EventsController.Heartbeat
                                              │   (every 10s while visible)
                                              ▼
                                       Bump LastHeartbeatAt + metrics.

  EventFlushService (background)
       reads queue → SaveChanges every 500 events / 2s
```

### B) Admin opens the dashboard

```
Browser ─GET /dashboard────────────────► DashboardController.Index
                                            │
                                            ▼  (cookie auth — admin from appsettings)
                                       DashboardQueryService.GetAllCampaignsOverviewAsync

Browser ──WS /hubs/dashboard──────────► DashboardHub
                                            │  Joins group 'dashboard:all',
                                            │  or 'dashboard:campaign:{id}',
                                            │  or 'dashboard:recipient:{rid}'.
                                            ▼
                                       Receives liveEvent + activeUsers pushes.

DashLive.attach() on every dashboard page:
  • On `liveEvent` (SignalR) → debounced 600ms refetch of the page's JSON endpoint.
  • Every 15s → safety-net refetch.
  • On `activeUsers` → direct in-place badge patch.
  • Each value patched via DashLive.setField → flashes if changed.
```

## Where to add things

| You want to... | Edit |
| --- | --- |
| A new event type | `SharedLibrary/Enums/EventType.cs` then update `tracking.js` `EVENT`, then if it should be broadcast add it to `EventsController.BroadcastEventsAsync` |
| A new KPI on the overview | Add field to `CampaignOverviewDto`, compute in `DashboardQueryService.BuildOverviewAsync`, render in `Views/Dashboard/Index.cshtml`, patch in the JS via `DashLive.setField` |
| Tighten / relax engagement | `SharedLibrary/Constants/EngagementRules.cs` |
| Change SMS continuation window | `Security:SessionContinuationMinutes` in `appsettings.json` |
| Force a new dashboard refresh | `RealtimeNotifier.PushEventAsync(...)` from anywhere |
| A new landing-page template | Create `Views/Campaign/<Name>.cshtml`, add entry to `PageTemplateMeta.Map`, update the seed in `db/schema.sql` |

## Configuration (`appsettings.json`)

| Section | Key | Default | What it does |
| --- | --- | --- | --- |
| ConnectionStrings | AnalyticsDb | `(localdb)\MSSQLLocalDB` | Target SQL Server |
| Security | NtnHashPepper | placeholder | HMAC pepper for `NtnHash` |
| Security | PiiEncryptionKey | placeholder | AES key for NTN/Mobile at rest |
| Security | RateLimitPerMinute | 240 | Per-IP throttle on `/api/v1/*` |
| Security | SessionContinuationMinutes | 5 | Refresh within this many minutes = same session |
| GeoIp | Enabled / DbPath | false | MaxMind GeoLite2 City DB |
| Database | AutoApplySchema | true | Run `db/schema.sql` at startup |
| Dashboard | Admin.Username/Password | admin/admin | Cookie login |
| TestMode | Enabled | (Dev only) | Force-enable `/` and `/dev/test` launcher in non-Dev |

## Conventions

- **Cancellation tokens** flow from `HttpContext` to repos to EF.
- **PII** is encrypted at rest (`PiiCipher`); NTN never leaves the database in plain text.
- **Magic numbers** live in `SharedLibrary/Constants/`.
- **Bot traffic** is detected in `SessionController.Start` and short-circuited in
  every other endpoint (`if (session.IsBot) return`).
- **The flush service is the only writer of `EventLog`.** Controllers enqueue, the
  background service writes — so request latency isn't bound to SQL throughput.
- **`EventLog.EventTypeName`** is auto-populated by `EventLogNameInterceptor` so
  the int enum and the readable string stay in sync.
