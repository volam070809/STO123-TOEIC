# STO123 implementation progress

## STEP 0 — Safety / repository baseline

- Status: DONE
- Commands actually executed: `git branch --show-current`; `git status --short`; `git status`; `rg --files` (source inventory); reads of `web-learner/package.json`, `.gitignore`, and `STO123/STO123.csproj`.
- Files inspected: `.gitignore`, `web-learner/package.json`, `STO123/STO123.csproj`, the attached request, and the Figma design-to-code skill.
- Files changed: `docs/progress.md` created for this record. No application files changed in STEP 0.
- Branch: `feature/learner-auth` (expected).
- Pre-existing modified files: `web-learner/src/contexts/AuthContext.jsx`, `src/index.css`, `src/main.jsx`, `src/routes/AppRoutes.jsx`, `src/services/api.js`.
- Pre-existing untracked paths: `src/components/`, `src/contexts/AuthState.js`, `src/pages/`, `src/routes/ProtectedRoute.jsx`, `src/services/authErrors.js`, `src/services/passwordRules.js`, `src/styles/` (all under `web-learner/`).
- Decision: preserve all pre-existing work and make only targeted changes after inspection. Current architecture and folder structure remain the baseline.
- Blockers: none for repository safety.
- Unresolved: Figma frame audit and runtime capabilities.
- Next step: STEP 1 — Figma audit.


## STEP 1 — Figma audit

- Status: PARTIAL
- Commands/tools actually executed: Figma get_metadata for page 0:1 and eight relevant frame subtrees; Figma get_design_context with screenshots for shared header and eight product frames; git status --short.
- Files inspected: docs/progress.md; Figma page 0:1, shared header 1:50, home 282:1955, login 154:1613, account 63:596, guest vocabulary 282:1991 / 154:1505 / 282:2027 / 287:2087 / 282:2063.
- Files changed: docs/figma-audit.md created; docs/progress.md updated. No application files changed in STEP 1.
- Decisions: use actual inspected frames listed in the audit; Inter is confirmed from Figma context; preserve existing frontend structure and shared code.
- Blockers: requested named v2 frames are absent from the only connected Figma page; separate OTP, forgot/reset, update profile, and change password frames are unavailable. Exact visual matching for those screens cannot be claimed.
- Unresolved: app rendering comparison and exact design of missing frames.
- Changed/untracked status: 5 pre-existing modified files, 7 pre-existing untracked path groups, plus new docs/ path.
- Next step: STEP 2 — implement only traceable shared typography and design tokens.


## STEP 2 — Design system / typography

- Status: DONE
- Commands actually executed: reads of index.css, styles/auth.css, AuthUI.jsx, AppRoutes.jsx, LoginPage.jsx; targeted PowerShell CSS edits; rg font/token check; git status --short.
- Files inspected: web-learner/src/index.css, src/styles/auth.css, src/components/auth/AuthUI.jsx, src/routes/AppRoutes.jsx, src/pages/auth/LoginPage.jsx; docs/figma-audit.md.
- Files changed: web-learner/src/index.css and src/styles/auth.css; docs/progress.md.
- Decisions: replace prior DM Sans/Manrope loading with Figma-confirmed Inter 400/600/700; centralize observed palette, control dimensions, and radii in CSS variables. Keep page layout work in STEP 3.
- Blockers: no separate Figma frames for OTP/password screens; their exact geometry is unresolved.
- Unresolved: old layout declarations and hardcoded page colors in auth.css will be updated in STEP 3.
- Changed/untracked status: same 5 modified tracked frontend files as baseline; 7 pre-existing untracked groups; new docs/ path. The pre-existing styles/ group now contains targeted edits.
- Next step: STEP 3 — public home, shared navbar, auth and profile presentation.


## STEP 3 — Home / navbar / email auth / profile

- Status: PARTIAL
- Commands actually executed: reads of AuthContext.jsx, ProfilePage.jsx, RegisterPage.jsx, VerifyEmailPage.jsx, authApi.js, AuthResponses.cs and existing CSS/components; targeted writes; npm run build (twice, both passed); npm run lint (twice, both passed); rg checks; git status --short. An initial path-specific edit command failed because its workdir was already web-learner; the corrected command then succeeded.
- Files inspected: listed source files, docs/figma-audit.md, and Figma screenshots for home/login/account.
- Files changed: new components/layout/SiteHeader.jsx and SiteFooter.jsx, layouts/SiteLayout.jsx, pages/HomePage.jsx; updated components/auth/AuthUI.jsx, pages/auth/ProfilePage.jsx and LoginPage.jsx, routes/AppRoutes.jsx, styles/auth.css, index.css; docs/progress.md.
- Decisions: root is public; login and logout return to root; shared Figma-style navbar/footer; inactive future nav labels remain visible; profile omits unsupported score, exam date and profile editing; empty alerts are suppressed centrally.
- Blockers: separate Figma frames for register/OTP/forgot/reset/change password are absent. Live email-auth completion remains unverified.
- Unresolved: vocabulary route is linked but will be implemented in STEP 5; Google sign-in in STEP 4; visual rendering comparison in STEP 6.
- Changed/untracked status: 5 modified tracked frontend files; pre-existing untracked groups plus new layouts/ and docs/ groups. No backend files changed.
- Next step: STEP 4 — inspect and implement secure Google authentication.


## STEP 4 — Google authentication

- Status: PARTIAL
- Commands/tools actually executed: backend source/model reads; read-only search of XacThucDangNhap SQL constraints; git check-ignore -v web-learner/.env.local; official Google Identity Services and Google.Apis.Auth documentation lookup; targeted frontend/backend edits; dotnet build .\\STO123\\STO123.csproj --no-restore (failed only because the existing running STO123.exe locked the output); dotnet build .\\STO123\\STO123.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/GoogleAuthCheck/ (passed, 0 warnings/errors); npm run build (passed); npm run lint (passed after a React ref warning was fixed); git status --short; git diff --check.
- Files inspected: AuthController.cs, IAuthService.cs, JwtTokenService.cs, IJwtTokenService.cs, Program.cs, generated XacThucDangNhap.cs/NguoiDung.cs/ToeicDbContext.cs (read-only), relevant XacThucDangNhap SQL constraints (read-only), authApi.js, AuthContext.jsx, LoginPage.jsx, RegisterPage.jsx, ProfilePage.jsx, ChangePasswordPage.jsx, .gitignore.
- Files changed: new STO123/DTOs/Auth/GoogleLoginRequest.cs, Services/Auth/IGoogleAuthService.cs and GoogleAuthService.cs; modified AuthController.cs, Program.cs, AuthResponses.cs, AuthService.cs; new web-learner/src/services/googleIdentity.js, components/auth/GoogleSignIn.jsx, .env.example; modified authApi.js, AuthContext.jsx, LoginPage.jsx, RegisterPage.jsx, ProfilePage.jsx, ChangePasswordPage.jsx, styles/auth.css; docs/progress.md.
- Decisions: use official Google Identity Services JavaScript button, post only idToken, validate it server-side with GoogleJsonWebSignature and the configured audience, resolve GoogleId before email, create only HOC_VIEN/HOAT_DONG Google users, reuse existing JWT and GET /me session flow. GET /me now reports hasPassword so Google-only users do not see a broken change-password workflow.
- Blocker: current XacThucDangNhap table has unique MaNguoiDung and CK_XacThucDangNhap_PhuongThuc requiring EMAIL rows to have GoogleId null and GOOGLE rows to have MatKhauMaHoa null. Existing EMAIL accounts cannot safely acquire Google credentials without a schema change. Same-email accounts therefore receive 409; no generated model or database changes were made. Google:ClientId and VITE_GOOGLE_CLIENT_ID are not configured here; Google Cloud allowed origin is unverified.
- Unresolved: GOOGLE AUTH E2E: NOT tested end-to-end. New/returning Google account flows require manual configuration and live browser testing. Running backend must be restarted to serve the new endpoint.
- Configuration: web-learner/.env.local is ignored by web-learner/.gitignore rule *.local. No .env.local or User Secret was written. Google docs used: https://developers.google.com/identity/gsi/web/guides/display-button and https://developers.google.com/identity/gsi/web/guides/verify-google-id-token and https://docs.cloud.google.com/dotnet/docs/reference/Google.Apis/latest/Google.Apis.Auth.GoogleJsonWebSignature.
- Changed/untracked status: 10 modified tracked files; 14 untracked path/file entries (including pre-existing groups, docs, and new Google files).
- Next step: STEP 5 — guest vocabulary trial from inspected Figma states.


## STEP 5 — Guest vocabulary trial

- Status: DONE
- Commands actually executed: inspected stored Figma design context for topic, list, flashcard front/back and result frames; read route/layout/CSS and audit; added local vocabulary page/data/styles; npm run build (passed); npm run lint (passed); git status --short.
- Files inspected: docs/figma-audit.md; web-learner/src/routes/AppRoutes.jsx, pages/HomePage.jsx, layouts/SiteLayout.jsx, components/layout/SiteHeader.jsx, index.css, styles/auth.css; Figma nodes 282:1991, 154:1505, 282:2027, 287:2087 and 282:2063.
- Files changed: new web-learner/src/data/demoVocabulary.js, pages/vocabulary/VocabularyPage.jsx and styles/vocabulary.css; modified routes/AppRoutes.jsx; updated docs/progress.md.
- Decisions: public /vocabulary implements one guest Office Essentials list with topic choice, list detail, front/back flashcards and results. Both guest and signed-in users can access it. Demo word content is isolated locally, with the first six list terms and first flashcard details traced to Figma; the remaining illustrative terms support the Figma ten-word count. Next/remembered/forgotten/retry actions stay in local component state, without persistence. Other topic cards link to login as shown in Figma but have no destination content or fake APIs.
- Blockers: none for the local trial. VOCABULARY API: NOT IMPLEMENTED / NOT REQUIRED FOR THIS MILESTONE.
- Unresolved: browser interaction and rendered visual comparison are pending STEP 6. Production vocabulary data and persistence are intentionally deferred.
- Changed/untracked status: 10 modified tracked files and 15 untracked path/file entries, including the pre-existing frontend groups.
- Next step: STEP 6 — final build, QA, visual capability check and report.

## STEP 6 — QA / build / final review

- Status: PARTIAL
- Commands actually executed: npm run build and npm run lint after final frontend edits (both passed); dotnet build .\STO123\STO123.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/GoogleAuthCheck/ (passed, 0 warnings/errors); git diff --check (passed after trailing blank-line cleanup, with line-ending conversion notices); git branch --show-current; git status --short; read-only searches for debug logs, embedded tokens, hardcoded localhost and raw page fetches. The normal dotnet build was also attempted in STEP 4 but its output executable was locked by an existing STO123 process.
- Browser QA: launched local Vite and isolated newly built backend; rendered public home/login/vocabulary pages in headless Chrome at 1440, 1280, 768, 390 and 375 CSS px. Viewed Figma screenshots and rendered screenshots. Device emulation reported scrollWidth equal to viewport width for every tested screen. Public home exposed login/register/vocabulary links; guest refresh stayed on /; guest /profile redirected to /login; invalid stored token against configured localhost origin was cleared and redirected to /login. Registration fields were present, HTML validity rejected empty form, and weak password showed the expected client message without a registration request.
- Vocabulary QA: navigated topic → list → first flashcard → revealed back → all 10 remembered/forgotten decisions → 6/10 result → four-word retry. All states rendered without horizontal overflow at 390 CSS px. Rechecked after Figma styling refinements and list-row start action.
- Backend smoke tests: unauthenticated /api/auth/me returned 401; invalid bearer token /me returned 401. The isolated new backend started and POST /api/auth/google returned 400 for empty input and 503 for a nonempty placeholder when Google:ClientId is absent. A nonexistent-account email login probe against the pre-existing server closed the connection without an HTTP response, so it did not establish wrong-password behavior or end-to-end email auth.
- Files inspected: touched frontend auth/vocabulary components and services, Program.cs, GoogleAuthService.cs, figma-audit.md, build/lint output, browser screenshots, Git status and diff.
- Files changed: final visual alignment in pages/vocabulary/VocabularyPage.jsx and styles/vocabulary.css; retry handling in services/googleIdentity.js; trailing blank-line cleanup in touched tracked files; docs/figma-audit.md and docs/progress.md.
- Decisions: keep local browser QA screenshots and scripts in the system temporary directory, outside the repository; stop the temporary Vite, Chrome and isolated backend processes. No account was created or changed by QA.
- Blockers/unresolved: no live Google Cloud client ID/origin/account selection; no authenticated learner test account; the existing email login probe did not return an HTTP response; Figma lacks the requested exact named frames; production vocabulary API/data and profile editing are outside this milestone. GOOGLE AUTH E2E: NOT tested end-to-end. No pixel-perfect comparison is claimed.
- Changed/untracked status: 10 modified tracked files and 15 untracked path/file entries, including baseline work. No commit, push, branch switch, database migration or generated model edit.
- Next step: report A–N with manual Google configuration and outstanding E2E checks.

# Stabilization pass — 2026-09-30

## STEP 0 — Repository baseline

- Status: DONE.
- Inspected: branch, working tree, tracked diff summary and names, whitespace diff, prior progress record. No repository AGENTS.md was found by `rg --files -g AGENTS.md`.
- Commands: `git branch --show-current`, `git status --short`, `git diff --stat`, `git diff --name-only`, `git diff --check`, `rg --files`.
- Tests/measurements: `git diff --check` exit 0; no performance measurement in this step.
- Findings: branch is `feature/learner-auth`. Working baseline has 10 modified tracked files and 15 untracked entries/groups; this includes the prior implementation work and documentation. Git reported only LF→CRLF notices.
- Root cause: not applicable; this is the safety baseline.
- Files changed: this progress record only.
- Decision: preserve the entire dirty baseline, inspect diffs before targeted edits, avoid generated EF models and database schema.
- Deferred: functional and performance findings until source inspection.
- Next: STEP 1 architecture and current code inspection.

## STEP 1 — Architecture and current-code inspection

- Status: DONE.
- Inspected: tracked frontend/backend diffs; routes, auth pages, AuthContext, API service, Google UI, shared shell; AuthController, auth DTOs/services, OTP, SMTP, JWT, Google and Program.cs; package manifests; handwritten source inventory.
- Commands: `git diff -- ...`, `rg --files`, `rg` review-pattern search, targeted `Get-Content` reads.
- Tests/measurements: source inspection only; no runtime measurement in this step.
- Findings: controller→service backend design and centralized frontend networking exist. LoginResponse has token/expiry only, so immediate `/me` supplies current user data. `/me` uses a read-only projection. Registration commits user/credential/OTP before SMTP and distinguishes delivery failure. OTP purposes are separate. Home/vocabulary hard-code guest content; navbar already branches by auth. API requests have no timeout.
- Root cause hypotheses: guest-state defect comes from pages ignoring AuthContext; loading hang may be a stalled request or `/me`, not a missing `finally`. Registration latency requires DB/SMTP measurements.
- Files changed: this progress record only.
- Decision: keep folders/contracts; measure before optimizing; complete all-handwritten-source review in STEP 10.
- Deferred: live E2E requires test accounts and external services.
- Next: STEP 2 behavior and performance baseline.

## STEP 2 — Behavior and performance baseline

- Status: PARTIAL.
- Inspected: isolated backend startup and safe nonexistent-account login path. No real account or OTP was used.
- Commands: launched existing isolated build on `http://127.0.0.1:5182`; ran three timed POST `/api/auth/login` lookup probes with a 20-second client deadline. The first PowerShell timing attempt failed before sending requests because System.Net.Http was not loaded; the first Node one-liner failed quoting before sending requests; the corrected temporary Node script ran.
- Tests/measurements: three DB-backed lookup probes each timed out without HTTP status: 20.0s, 20.0s, 20.0s (rounded). These are timeout bounds, not successful login timings. Backend startup itself completed; this does not establish Azure SQL, SMTP, or registration latency.
- Findings/root cause: the DB-backed login path did not return within 20s even for an unknown email. This points to the DB/network path or server request execution as a current environment blocker; it does not prove which stage is slow. Registration DB/SMTP and Google token/DB timing remain NOT MEASURED.
- Files changed: this progress record only; temporary measurement script under system temp.
- Decision: do not claim SMTP or password hashing causes first-registration slowness; avoid speculative performance changes. Continue source-level business audit and seek any safe diagnostics.
- Deferred: successful cold/warm login and registration measurements until backend DB connectivity and a disposable test inbox are available.
- Next: STEP 3 authentication business-logic audit.

## STEP 3 — Authentication business-logic audit

- Status: PARTIAL.
- Inspected: registration, login, `/me`, verification, resend, forgot/reset/change password, Google credential handling, OTP service, SMTP service, DTO validation, frontend auth actions, shared API errors.
- Commands: targeted source reads and `rg` across handwritten auth files; isolated backend remained available for safe probes.
- Tests/measurements: unauthenticated `/me` returned 401 in 60ms, 5ms, 3ms (warm local HTTP samples; these do not access DB). Three nonexistent-email login DB probes timed out at 20.0s each. No account creation or email send was attempted.
- Findings: registration transaction commits before SMTP; delivery failure uses a distinct 500 response and frontend preserves verification email. OTP purposes `XAC_THUC_EMAIL` and `QUEN_MAT_KHAU` are distinct; valid OTP lookup checks purpose, status and expiry. Frontend resend has 60s cooldown, but the API currently has no server-side cooldown/rate limiting. Forgot response is generic. Google provider identity uses Subject and verified email; incompatible same-email accounts conflict. `/me` is AsNoTracking projection. Wrong current password uses a 401 error string recognized by the frontend API layer.
- Root cause: current environment's DB-backed path is stalled, so registration and SMTP latency cannot be isolated; the login lookup never reaches a response within 20s.
- Files changed: progress log only.
- Decision: retain existing API contracts; prioritize guest/auth correctness, bounded requests, async/session races, and safe abuse controls. Do not weaken hashing or move SMTP to fire-and-forget.
- Deferred: live OTP, account creation, SMTP and Google validation timing; full abuse/rate-limit policy needs deployment context.
- Next: STEP 4 guest/authenticated UI correction.

## STEP 4 — Guest/authenticated UI correction

- Status: DONE for frontend state behavior; live authenticated API remains untested.
- Inspected: HomePage.jsx, VocabularyPage.jsx, SiteHeader.jsx, SiteLayout.jsx, AuthContext and current CSS.
- Commands: targeted PowerShell writes/reads; `npm run lint`, `npm run build`; temporary Chrome DevTools script at 390 CSS px with a clearly mocked delayed `/me` response.
- Tests/measurements: during a 700ms mocked restore, home and vocabulary showed loading, no guest copy and no login link. After restore, home showed learner name and no guest/login copy. Authenticated vocabulary topic, list, card front/back and 10-word result rendered; no guest login/register/unlock CTA was in the result. Frontend lint/build passed. This is a mock UI test, not auth E2E.
- Findings/root cause: page bodies previously rendered guest-specific copy unconditionally while only the navbar used AuthContext.
- Files changed: web-learner/src/components/auth/SessionBoundary.jsx (new), pages/HomePage.jsx, pages/vocabulary/VocabularyPage.jsx, components/layout/SiteHeader.jsx, styles/vocabulary.css; docs/progress.md.
- Decision: keep one shared shell and AuthContext. Authenticated home shows real learner name plus demo vocabulary/profile links, without fabricated scores. Authenticated vocabulary identifies demo data and omits login/unlock prompts.
- Deferred: real account validation and complete responsive matrix in later steps.
- Next: STEP 5 registration/login/OTP stabilization.

### STEP 2 measurement addendum

- Three unauthenticated GET `/api/auth/me` requests on the already-running isolated backend returned 401 in 60ms, 5ms, and 3ms. This exercises HTTP/auth middleware, not EF or Azure SQL. It reinforces that the 20s login timeouts are specific to the DB-backed path in this environment; the exact network/SQL cause is still unproven.

## STEP 5 — Email registration/login/OTP stabilization

- Status: PARTIAL.
- Inspected: Register/Login/Verify/Resend/Forgot/Reset/Change frontend actions; AuthService, OtpService, EmailService, DTOs, read-only OTP SQL schema.
- Commands: source reads, `rg` schema search; targeted edits; `dotnet build .\STO123\STO123.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/StabilizationCheck/` (passed twice, 0 warnings/errors); `npm run lint` and `npm run build` (passed).
- Tests/measurements: compile/lint/build only for new cooldown and OTP syntax behavior. Live SQL/SMTP, account creation and email delivery are NOT TESTED because DB-backed login probes timed out and no disposable inbox was supplied. Registration/OTP timings remain NOT MEASURED.
- Findings/root cause: frontend already prevents repeated submit while loading and preserves verification email after the backend's committed-account/SMTP-failure response. Backend had no reissue cooldown despite the frontend's 60s cooldown; refresh or direct API calls could bypass it. OTP generator emits six numeric digits, but UI and lookup accepted arbitrary strings.
- Files changed: STO123/Services/Auth/AuthService.cs (server-side 60s cooldown under serializable transaction), OtpService.cs (reject malformed code before DB), web-learner/src/components/auth/AuthUI.jsx (inputMode/pattern passthrough), pages/auth/VerifyEmailPage.jsx and ResetPasswordPage.jsx (six-digit input guidance), docs/progress.md.
- Decision: preserve all endpoint names, DTO shapes and response codes. Use existing OTP timestamp/table for cooldown. Keep password hashing and synchronous SMTP awaited; no unsafe background send.
- Deferred: live validation of cooldown, OTP, registration and SMTP behavior; deployment-level rate limiting remains a security recommendation. Registration DB/SMTP stage attribution remains blocked by the DB path.
- Next: STEP 6 Google authentication stabilization.

## STEP 6 — Google authentication stabilization

- Status: PARTIAL; provider E2E is unavailable without a configured client ID and test account.
- Inspected: GoogleSignIn, googleIdentity loader, GoogleAuthService and API timeout behavior; official Google validation documentation.
- Commands/tests: isolated backend with a nonsecret fake Client ID on port 5183; three invalid-token POST /api/auth/google probes returned 401 in 549ms, 21ms and 13ms. These are invalid-token checks, not real sign-in measurements. Frontend lint/build passed after changes.
- Findings: backend validates Google ID token audience and verified email; the Google library's public ValidateAsync overload has no cancellation token. Frontend already resets busy state in finally, but requests and script load had no upper time bound.
- Files changed: web-learner/src/services/api.js (bounded fetch and safe network/timeout errors), authApi.js (operation-specific deadlines), googleIdentity.js (15-second script load deadline and retry cleanup), RegisterPage.jsx (ambiguous timeout recovery), docs/progress.md.
- Decision: preserve Google API contract and existing provider behavior. A timeout now gives a visible error; registration timeout preserves the entered email and offers verification/resend because the server may already have committed the account.
- Deferred: real Google browser E2E and provider/DB timing; configuration is unavailable. Next: STEP 7 session/routing.

## STEP 7 — Session and route stabilization

- Status: PARTIAL; frontend compile/lint passed, browser regression pending.
- Inspected: AuthContext, API 401 event, shared SessionBoundary, ProtectedRoute and AppRoutes.
- Findings/root cause: startup /me was duplicated in AuthContext and StrictMode could trigger two requests. An old token's 401 could clear a newer token because the event carried no token identity; late /me completion could overwrite a later logout/login.
- Files changed: AuthContext.jsx (one startup restore, generation guard for stale /me and login completion, recoverable /me timeout state), api.js (401 event carries the request token), docs/progress.md.
- Tests: npm run lint and npm run build passed; delayed-request race regression still pending.
- Decision: retain the token on transient /me failure and show retry/continue-as-guest rather than render guest content under a stored token. Only a 401 for the currently active token clears that session.
- Deferred: live authenticated session flow until test credentials/database access. Next: STEP 8 performance evidence and STEP 9 visual QA.
## STEP 8 — Performance evidence and safe optimization

- Status: PARTIAL because the Azure SQL-backed path did not return in this environment.
- Measurements: three nonexistent-account login requests each ended at the 20.0s client timeout without an HTTP response; three unauthenticated /me checks returned 401 in 60ms/5ms/3ms; three invalid Google-token checks with a fake client ID returned 401 in 549ms/21ms/13ms. Registration total/DB/SMTP, successful login cold/warm, authenticated /me, and real Google sign-in are NOT MEASURED.
- Analysis: the basic HTTP/auth middleware answered quickly while a DB-backed login lookup did not. This is evidence of a DB-backed request stall in this setup, but it does not identify SQL connectivity, query planning, server thread work, or SMTP as the cause. Google's first invalid-token check includes library initialization/certificate work, but it does not establish the real login latency.
- Files changed: no speculative backend optimization. Frontend request deadlines and session recovery from STEP 6-7 bound UI loading.
- Decision: do not claim a first-registration speed improvement. Measure from a reachable database and disposable inbox before changing query or SMTP architecture.
- Next: STEP 9 Figma/responsive QA.

## STEP 9 — Visual and responsive review

- Status: DONE for browser layout checks, not pixel-perfect Figma certification.
- Inspected: existing docs/figma-audit.md from the prior pass and rendered guest/authenticated pages.
- Commands/tests: headless Chrome on Vite localhost:5174 at 1440, 1280, 768, 390 and 375 CSS px; home, login, register and vocabulary each had document scrollWidth equal to viewport width. /verify-email without pending email correctly redirected to /register. Mock authenticated home/vocabulary had no guest login or unlock prompts at 390 px, including vocabulary result.
- Findings: responsive layout remains usable in tested sizes; no horizontal overflow found. Browser state checks are not a pixel-perfect comparison or real-account E2E.
- Files changed: no further layout edits.
- Next: STEP 10 handwritten codebase review.

## STEP 10 — Handwritten codebase review

- Status: DONE for static review of frontend src and manually maintained backend controllers, DTOs, services and Program; generated EF Models, bin/obj, node_modules and dist excluded.
- Inspected: auth flows and shared UI, routes, demo vocabulary, CSS, API client, controllers/services, DTOs and configuration wiring. Searched for TODO/debugger/console, raw page fetch, synchronous task waits, secret-like literals and unused starter references.
- Findings: P0 active issue found: none. P1 fixed: guest content under a stored learner token, stale 401/session race, unbounded network loading. P1 remaining: DB-backed auth path times out in this environment; root cause unproven. P2 fixed: server-side OTP reissue cooldown and malformed OTP rejection. P3 deferred: unreferenced Vite starter App.css/react.svg/vite.svg and unrelated sample backend endpoints; deleting those offers little stabilization value. Existing one-credential-per-user schema prevents safe Google/email linking.
- Files changed: fixes recorded in STEP 4-7; no broad cleanup or generated EF edit.
- Decision: retain healthy code and unrelated endpoints; document future work.
- Next: STEP 11 regression.

## STEP 11 — Regression checks

- Status: PARTIAL because live accounts/external providers are unavailable.
- Browser checks: mock delayed /me restored learner home/vocabulary without guest flash; full 10-word demo reached results without guest CTAs. Separate browser script observed exactly one startup /me under StrictMode, old-token 401 left newer token intact, current-token 401 cleared it, and a 100ms simulated hanging request returned timeout/network flags and visible Vietnamese error. Responsive matrix passed with no horizontal overflow.
- Backend checks: invalid Google tokens returned 401; unauthenticated /me returned 401. Live email registration, SMTP, OTP, successful login, password mutation and real Google token were NOT TESTED.
- Files changed: none beyond earlier steps.
- Next: STEP 12 final build/status.

## STEP 12 — Final build and status gate

- Status: DONE for compilation and lint; live auth remains unverified.
- Commands: npm run lint and npm run build passed; normal dotnet build .\STO123\STO123.csproj first failed because repository backend executable was locked by process 9352. Verified that PID's executable path was this repository's bin/Debug/net10.0/STO123.exe, stopped only that process, and reran the normal build. It passed with 0 warnings and 0 errors. git diff --check passed (only Git LF→CRLF notices).
- Decision: no commit, push, merge, branch switch, schema or generated EF changes. Temporary QA services and browser will be stopped after documentation.
- Next: STEP 13 team handoff.
## STEP 13 — Team handoff documentation

- Status: DONE.
- Inspected: actual project directories, current API contracts, environment key names, working-tree baseline and verification record.
- Commands/tests: wrote docs/team-handoff.md; no runtime test applies to documentation.
- Findings: the compile/UI mock foundation is usable for separate feature work, but DB-backed and provider E2E auth remain unverified; shared files need coordination.
- Files changed: docs/team-handoff.md and this progress entry.
- Decision: distinguish verified behavior from assumptions, name generated EF files as do-not-edit, preserve current API shapes and list safe feature ownership.
- Deferred: real external-service tests and future features listed in handoff.
- Next: STEP 14 final report and final Git/security check.

## STEP 14 — Final report preparation

- Status: DONE for the available environment; external-service checks are explicitly NOT TESTED.
- Inspected: final changes, handoff, status and diff whitespace.
- Commands/tests: final git status --short, git diff --check, npm run lint/build, normal dotnet build; results are in final report. No secret values recorded.
- Findings: core UI/session regressions passed mock browser tests; first registration duration and actual DB/SMTP cause remain unknown.
- Files changed: progress record only in this step.
- Decision: report exact measured limits and working-baseline versus new changes; no commit/push/merge.
- Deferred: live DB/email/Google validation.