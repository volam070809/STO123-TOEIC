# STO123 team handoff

Updated 2026-09-30 on branch feature/learner-auth. This repository already had uncommitted learner-auth work when the stabilization pass began. See progress.md for the measured checks and the exact step record.

## CURRENT STABLE FOUNDATION

- ASP.NET Core controller → service → EF Database First structure compiles with dotnet build (0 warnings/errors after stopping an executable that locked normal output).
- React/Vite frontend passes npm run lint and npm run build. Home, auth pages and sample vocabulary rendered without horizontal overflow at 1440, 1280, 768, 390 and 375 CSS px.
- Mocked browser checks verify stored-session loading does not expose guest home/vocabulary copy, StrictMode restores with one /me call, stale-token 401 cannot clear the current token, active-token 401 clears it, and request timeout surfaces an error. These are mock UI checks, not live account E2E.
- Invalid Google credentials returned 401 with a fake client ID; unauthenticated /me returned 401. No successful database-backed auth flow has been verified in this environment.

## IMPORTANT DIRECTORY MAP

- web-learner/src/pages: route pages, including auth and vocabulary.
- web-learner/src/components: shared auth and layout presentation.
- web-learner/src/contexts: AuthProvider and useAuth context.
- web-learner/src/services: central apiRequest, authApi, Google Identity loader, validation/error helpers.
- web-learner/src/data: local vocabulary demonstration content; web-learner/src/styles and index.css: visual styles.
- STO123/Controllers: HTTP endpoints; STO123/DTOs/Auth: request/response contracts; STO123/Services/Auth: business rules, OTP, email, JWT and Google validation; STO123/Program.cs: dependency injection, CORS and authentication.
- STO123/Models: scaffolded database entities/context. docs/progress.md records checks; docs/figma-audit.md records design comparison.

## RESPONSIBILITY MAP

Put a new route page under pages and register its path in routes/AppRoutes.jsx. Use ProtectedRoute for private pages and SessionBoundary when a public page has different guest and learner content. Put shared presentation under components and styles; keep API calls in services rather than direct page fetches. Put backend request validation/response mapping in DTOs/controllers and business rules in Services/Auth or an equivalent feature service. Add service registration in Program.cs only when necessary. Keep actual vocabulary data separate from the local demo data.

## GENERATED / DO NOT EDIT

STO123/Models/**, including ToeicDbContext, is generated from Azure SQL by the Database First workflow. Do not hand-edit those classes or change DB_TOEIC123_ver2.sql during ordinary feature work. Coordinate a schema change and regenerate models through the established scaffold process if a future feature truly needs one.

## AUTH API CONTRACTS

All auth routes are under /api/auth. POST /register creates a pending EMAIL account and OTP, then awaits SMTP; a delivery failure can occur after commit and returns the existing distinct 500 message. POST /verify-email consumes a six-digit OTP. POST /resend-otp and /forgot-password use generic success messages and now enforce a 60-second per-purpose issuance cooldown server-side. POST /login returns JWT token and expiry. POST /google accepts {idToken}, validates audience and verified email, and returns the same token/expiry shape; existing same-email EMAIL accounts return 409 because credentials cannot be linked safely under the current schema. GET /me is bearer protected and returns current user/profile fields including hasPassword. POST /reset-password consumes a reset OTP; POST /change-password is bearer protected. Frontend completes login through /me before showing learner content. No endpoint/DTO shape was changed in this stabilization pass.

The frontend stores the token in sessionStorage for the tab. A 401 response only expires the session if it belongs to the current token; incorrect current password is handled as a form error. A transient /me failure keeps the token and offers retry or explicit guest continuation.

## SHARED CONFLICT HOTSPOTS

Coordinate edits to STO123/Program.cs, Controllers/AuthController.cs, Services/Auth/AuthService.cs, DTOs/Auth/**, web-learner/src/routes/AppRoutes.jsx, contexts/AuthContext.jsx, services/api.js and authApi.js, index.css, shared auth/layout components, and docs/progress.md. These files connect multiple features or contracts and currently carry uncommitted baseline changes.

## SAFE PARALLEL WORK AREAS

Independent feature pages, feature-local components/styles and feature-specific backend services/controllers can be developed separately when routes/contracts are agreed first. A vocabulary API implementation can be isolated from the local demo component while its contract is designed. Documentation or focused tests may proceed separately from feature code. Avoid concurrent edits to the hotspots above.

## KNOWN LIMITATIONS

- Three safe, nonexistent-account login probes timed out at 20 seconds with no HTTP response. Azure SQL path/connectivity and first-registration DB/SMTP time are not diagnosed. Registration, OTP, successful login, password mutation and session completion have not been tested with a real account.
- No configured Google browser client ID/test account was available. GOOGLE AUTH E2E: NOT tested end-to-end. The backend invalid-token checks did not reach the account/database branch.
- EMAIL and GOOGLE identities with the same email cannot be linked under the current one-credential-per-user schema; the API returns 409.
- Registration and resend email are awaited; a durable background mail queue is deferred. If registration timed out after server commit, the UI preserves the email and advises verification/resend before retrying registration. Server cooldown can delay immediate resend for up to 60 seconds even after SMTP delivery failure; a future design should resolve that with delivery state.
- Vocabulary is a 10-word local demonstration, with no production API, persistence or learner progress. Profile editing has no endpoint. Exact named Figma frames were not available; prior visual audit is in docs/figma-audit.md.
- Unreferenced Vite starter files and unrelated sample backend endpoints remain for a separate cleanup.

## ENVIRONMENT REQUIREMENTS

Use .NET 10 SDK and Node/npm as declared by the project. Normal backend launch settings use localhost:5170 (HTTP) or localhost:7232 (HTTPS); Vite normally uses localhost:5173. Match frontend VITE_API_BASE_URL to the backend and register the exact frontend origin in Cors:AllowedOrigins. The existing development CORS origin is localhost:5173; another port/origin needs explicit configuration.

Backend needs ConnectionStrings:ToeicDb, Jwt:Key (at least 32 UTF-8 bytes), Jwt:Issuer, Jwt:Audience and Jwt:ExpiresMinutes=60. Email delivery needs Smtp:Host, Port, FromEmail, FromName, Username/Password when applicable, and UseSsl. Google needs Google:ClientId on the backend and the matching VITE_GOOGLE_CLIENT_ID in the frontend, with the exact browser origin authorized in Google Cloud. Keep sensitive backend values in User Secrets or environment variables; use the gitignored web-learner/.env.local for local Vite values. web-learner/.env.example shows frontend key names. No Google Client Secret belongs in the frontend.

## DEFINITION OF DONE

For each future feature: run npm run lint and npm run build; run dotnet build if backend code changed; test the real API and state honestly when external services prevent it; check mobile and desktop layout; verify no secrets entered source/docs; regress guest, learner and expired-session behavior; protect private routes and use the shared API service; record any contract change and remaining limitation in docs/progress.md. Run git diff --check before integration.

## FUTURE WORK

Restore reliable Azure SQL connectivity, then measure registration total, duplicate lookup/hash/transaction/SMTP phases, login cold/warm and authenticated /me cold/warm with a disposable inbox. Test real Google sign-in and all OTP/password paths using dedicated test accounts. Decide a durable SMTP/retry/rate-limit policy, including resend after delivery failure. Design production vocabulary contracts and persistence separately from the sample. Add profile update only with an agreed API. Revisit unrelated starter/sample cleanup during a dedicated maintenance change.