# CasePilot Auth0 BFF migration and production runbook

## Architecture decision

CasePilot uses the BFF pattern: the browser receives only a Secure, HttpOnly,
SameSite cookie. ASP.NET Core performs OpenID Connect Authorization Code + PKCE,
maps the validated Auth0 `sub` to the existing local `Users.Id`, reloads the
local account and role on each protected request, and executes the API in the
same process. There is no reason to forward an Auth0 access token between the
BFF and these same-process endpoints.

JWT Bearer remains the default scheme and rollback path. BFF use requires both
`AUTH0_ENABLED=true` and `AUTH0_BFF_API_ENABLED=true`. The bridge never accepts
user IDs, roles, email addresses, or provider subjects from request data.

All routes are authenticated by the fallback policy unless explicitly marked
anonymous. Anonymous routes are the health probes, `/ping`, legacy `/auth/login`
and `/auth/signup`, and the BFF protocol endpoints that perform their own
scheme-specific checks. Every cookie-authenticated mutation requires a trusted
Origin/Referer plus an antiforgery cookie and `X-CSRF-TOKEN`. The legacy GET
routes that start AI/tutor work receive the same protection pending an HTTP
method compatibility migration.

## Endpoint inventory and migration state

| Classification | Endpoints | BFF/JWT behavior |
| --- | --- | --- |
| Public | health probes, `GET /ping`, `POST /auth/login`, `POST /auth/signup`, OIDC callback | Explicit anonymous; login/signup rate-limited |
| Identity/session | `/bff/dev/login`, `/session`, `/csrf`, `/logout/prepare`, `/logout/complete`, `/register/student`, `/link-existing`, `GET /me` | Explicit BFF scheme; registration/linking separately disabled by default |
| Upload/document read | `/uploads/mine`, `/uploads/{id}/summary`, `/layout`, `/classification`, `/pages/preview`, `.pdf`, `/download`, `/cases`, `/search/{id}`, `/index/status/{id}`, document figures | BFF or Bearer; existing ownership/class assignment checks preserved; PDF range/cookie support preserved |
| Upload/document mutation | `POST /uploads`, `/index/{id}`, `/layout/analyze`; `PATCH /uploads/{id}/name`; `DELETE /uploads/{id}` | BFF requires Origin + CSRF; Bearer unchanged |
| Sessions/notes read | `/sessions/mine`, `/sessions/{id}`, `/sessions/{id}/notes`, `/sessions/{id}/messages` | BFF or Bearer; ownership/instructor checks preserved |
| Sessions/notes mutation | `POST /sessions`, `/sessions/start`, `/sessions/{id}/notes`; note PATCH/DELETE; session DELETE | BFF requires Origin + CSRF; Bearer unchanged |
| Student classes | `GET /classes/enrolled`; `POST /classes/join` | BFF or Bearer; joining requires CSRF |
| Instructor/admin | class create/delete/details/history/students/cases/join-code routes; `/admin/sessions*`; tutor progress/summary | BFF or Bearer; live instructor role and resource ownership enforced; mutations require CSRF |
| Tutor/reading coach | `/tutor/start`, `/step`, `/reading/start`, `/reading/answer`, `/reading/resume` | BFF or Bearer; mutations and legacy stateful resume GET require CSRF |
| AI/streaming | `/ask/{id}`, `/ask/stream/{id}`, `/api/chat/stream`, LLM/embedding diagnostics | BFF or Bearer; upload access retained; stateful/cost GETs require CSRF; SSE cancellation preserved |
| Administrative/debug | `/debug/*`, `/api/llm/*`, `/api/embeddings/*` | Authenticated by fallback; existing instructor checks remain where implemented |

## New users and existing accounts

New registration is off by default. When enabled, a validated Auth0 cookie with
a verified email may create only a local student. An email collision returns
`existing_account_requires_linking`; it never links by email. Instructor access
is never assigned by registration and requires an existing trusted approval
workflow.

Existing-account linking is off by default and requires both the Auth0 cookie
and a valid legacy CasePilot Bearer login in the same request, plus Origin and
CSRF validation. The endpoint derives the local ID from that validated Bearer
principal and uses the unique `UserIdentities` repository constraints. Trusted
operators may continue using the development-only disposable-user CLI.

Run a read-only aggregate assessment with:

```powershell
dotnet run -- --auth0-migration-dry-run
```

It prints counts only and never emails, IDs, subjects, or hashes. Before any
real import, confirm the current Auth0 import schema accepts the exact BCrypt
variants/work factors in the database. If not, require password resets or use
an approved lazy-migration design. Do not generate or upload an import file
until the dry run is reviewed and a production change is approved.

Idempotency comes from `(Provider, ProviderSubject)` uniqueness and repository
conflict handling. Rollback is: disable registration/linking and BFF flags,
restore frontend JWT mode, retain `UserIdentities` and local users (the additive
table/column need not be dropped), and invalidate BFF cookies by rotating the
cookie/Data Protection environment only as an explicit incident action.

## Same-origin production routing

Do not run production BFF mode with the browser calling the Azure hostname from
the Vercel hostname. Put one public HTTPS origin in front of both, preferably a
managed reverse proxy such as Azure Front Door:

- `https://casepilot.example/*` static navigation/assets -> Vercel;
- every backend route listed above, `/bff/*`, `/signin-auth0`, and
  `/signout-callback-auth0` -> Azure;
- preserve request method/body, `Range`, `If-Range`, `Origin`, `Referer`,
  `X-CSRF-TOKEN`, and streaming responses; disable proxy buffering for SSE;
- forward the original host and HTTPS scheme through a trusted proxy boundary;
- never cache authenticated, cookie-setting, PDF, download, or streaming routes.

Set Auth0 callback/logout/web-origin URLs to that public origin. Set
`BFF_PUBLIC_ORIGIN` to the exact origin and explicitly enable trusted forwarded
headers only after restricting Azure so requests can arrive only through the
approved proxy. The current Vercel SPA catch-all alone is not a secure BFF
production proxy and must not be enabled as-is.

## Data Protection and session revocation

`DATA_PROTECTION_KEYS_PATH` provides persistent filesystem keys for a single
instance. Before multiple instances or slot swaps, use a shared key ring (Azure
Blob is suitable) protected at rest (Azure Key Vault/managed identity), with a
stable application name and strict RBAC. Do not delete old keys during normal
rotation.

Deleted, disabled, or role-changed local accounts are reloaded on each request,
so authorization changes take effect immediately. Full remote session
revocation still requires an operator feature that records a per-user security
stamp/session version (or server-side ticket store) and invalidates all active
cookies. Until implemented, disabling the local account is the immediate deny
control; Auth0-only logout does not revoke every other CasePilot browser cookie.

## Staged deployment and rollback

1. Back up and verify PostgreSQL and Blob data; deploy additive schema with all
   Auth0 flags false and frontend `VITE_AUTH_MODE=jwt`.
2. Configure the shared public origin, proxy routes, Auth0 URLs, secrets, key
   persistence, HTTPS and forwarded headers. Smoke-test range/SSE/upload limits.
3. Enable Auth0 and BFF API only for disposable linked users. Keep registration
   and linking off. Verify student and instructor isolation in a staging clone.
4. Enable explicit linking, then student registration, only after policy and
   support readiness. Run the aggregate dry run before approved account import.
5. Switch frontend to `VITE_AUTH_MODE=bff` gradually. Monitor 401/403/400/429,
   callback failures, key-ring health, upload jobs and streaming disconnects.
6. Keep JWT issuance enabled through the observation window. Roll back by
   restoring frontend JWT mode and setting Auth0/BFF flags false. Do not remove
   additive identity data during rollback.

Production approval gates: proxy/DNS/TLS changes, Auth0 dashboard changes,
secrets, managed-identity/RBAC and shared key storage, production database
backup/migration, any user import or password-reset campaign, instructor
approvals, and the final frontend flag switch.
