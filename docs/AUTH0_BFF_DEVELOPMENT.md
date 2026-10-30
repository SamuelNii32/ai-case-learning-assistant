# Auth0 BFF development setup

CasePilot has an opt-in Auth0 login proof of concept for local development.
`Auth0:Enabled` and `AUTH0_ENABLED` still default to `false`. When explicitly
enabled in the Development environment, the backend registers separate cookie
and OpenID Connect schemes plus `/bff/dev/*` endpoints. The existing Bearer JWT
scheme remains the application default, and an Auth0 session does not authorize
any existing CasePilot resource endpoint.

## Auth0 dashboard

1. In **Applications > Applications**, select **Create Application**.
2. Name it `CasePilot Development` and choose **Regular Web Applications**.
   Do not choose Single Page Application: the planned BFF is a confidential
   server-side client that owns the client secret and authorization-code flow.
3. In the application settings, record these values without committing them:
   - **Domain** -> `AUTH0_DOMAIN` (hostname only, such as
     `tenant.us.auth0.com`)
   - **Client ID** -> `AUTH0_CLIENT_ID`
   - **Client Secret** -> `AUTH0_CLIENT_SECRET`
4. Keep **Token Endpoint Authentication Method** set to `POST` and use the
   Authorization Code flow. The later implementation will request the standard
   `openid profile email` scopes. No Auth0 API audience is needed for this BFF
   web login scaffolding.
5. For the backend-hosted local origin (`BFF_PUBLIC_ORIGIN=https://localhost:7002`),
   enter exactly:
   - **Allowed Callback URLs:** `https://localhost:7002/signin-auth0`
   - **Allowed Logout URLs:** `https://localhost:7002/signout-callback-auth0`
   - **Allowed Web Origins:** `https://localhost:7002`
6. If using the Vite development origin later, first add proxy routes for the
   Auth0 callback/logout paths. Then use this alternative set:
   - **Allowed Callback URLs:** `http://localhost:5174/signin-auth0`
   - **Allowed Logout URLs:** `http://localhost:5174/signout-callback-auth0`
   - **Allowed Web Origins:** `http://localhost:5174`
7. Save the application. Enable it only in the local Development environment.

Auth0 permits comma-separated entries, so both local URL sets can be registered
in the development application when both modes are needed. Production should
use a separate Auth0 application.

## Local secret configuration

The project now has a development `UserSecretsId`. Store the confidential value
outside the repository:

```powershell
dotnet user-secrets set "Auth0:Domain" "tenant.us.auth0.com"
dotnet user-secrets set "Auth0:ClientId" "<development-client-id>"
dotnet user-secrets set "Auth0:ClientSecret" "<development-client-secret>"
dotnet user-secrets set "Bff:PublicOrigin" "https://localhost:7002"
dotnet user-secrets set "Auth0:Enabled" "true"
```

Run these commands locally from the backend directory, substituting values
directly from the Auth0 dashboard. Do not paste secrets into chat, tickets, or
source files. `.env.auth0.example` lists equivalent variable names, but the
application does not automatically load dotenv files.

## Test with a disposable Auth0 account

1. In the Auth0 development tenant, open **User Management > Users** and create
   a disposable database user with a unique test-only email address. Do not use
   a real CasePilot account or production credentials.
2. Start the HTTPS profile:

   ```powershell
   dotnet run --launch-profile https
   ```

3. Open `https://localhost:7002/bff/dev/session`. It should report
   `{ "authenticated": false, "accountLinked": false, "status": "not_authenticated" }`.
4. Open `https://localhost:7002/bff/dev/login?returnUrl=/bff/dev/session`, sign
   in with the disposable user, and complete the Auth0 redirect. The session
   endpoint will report `account_not_linked` until that disposable subject has
   been deliberately mapped to an existing local test user. A valid mapping
   reports `linked` together with only the existing local user ID and role.
5. Confirm a protected CasePilot API such as `/me` still returns `401` unless a
   valid existing CasePilot Bearer JWT is supplied. The Auth0 cookie is not a
   local user identity and creates no `UserIdentities` row.
6. Logout requires a CSRF token. From the browser console on the CasePilot HTTPS
   origin, request `/bff/dev/csrf`, then POST `/bff/dev/logout` with the returned
   token in the `X-CSRF-TOKEN` header and same-origin credentials. Follow the
   returned Auth0 logout redirect in the top-level browser window.
7. After testing, delete the disposable Auth0 user and disable the proof of
   concept locally:

   ```powershell
   dotnet user-secrets set "Auth0:Enabled" "false"
   ```

The BFF does not return access, ID, or refresh tokens to JavaScript, and tokens
are not saved in the authentication ticket. Its status endpoint deliberately
returns no Auth0 subject, email, or other external claims. It never links by
email or creates a user/mapping; linked status comes only from an existing
`UserIdentities` record and its existing local `Users` row.

## Link a disposable development account

Identity linking is a local administrative CLI operation. There is no linking
HTTP endpoint, and both the Development environment and a separate opt-in flag
are required. The command also refuses any database provider other than the
local SQLite development database.

1. Stop the locally running backend so the command can use the database and
   rebuild the executable safely.
2. In **Auth0 > User Management > Users**, open the disposable user and copy its
   `user_id`/subject directly into .NET user-secrets. Do not use an email address
   and do not paste the subject into chat or source control:

   ```powershell
   dotnet user-secrets set "DevAuth0Link:Subject" "<disposable-auth0-user-id>"
   dotnet user-secrets set "DevAuth0Link:Enabled" "true"
   ```

3. Create the disposable local student and identity mapping:

   ```powershell
   dotnet run --launch-profile https -- --dev-link-auth0-test-user
   ```

   The command prints the generated disposable local user ID and a status such
   as `CreatedAndLinked`. Repeating it is safe and returns `AlreadyLinked`.
   A subject already mapped to another local user returns `Conflict` and is not
   reassigned.
4. Restart the HTTPS backend, sign in with the disposable Auth0 user, and open
   `/bff/dev/session`. It should report `linked`, the generated local user ID,
   and role `student`.
5. Stop the backend and clean up:

   ```powershell
   dotnet run --launch-profile https -- --dev-cleanup-auth0-test-user
   dotnet user-secrets remove "DevAuth0Link:Subject"
   dotnet user-secrets remove "DevAuth0Link:Enabled"
   ```

   Cleanup removes only the expected disposable mapping and marked local user.
   It refuses to proceed if the user owns uploads, sessions, notes, classes,
   class membership, tutor data, index jobs, or another identity mapping. It
   never deletes that related application data automatically. Delete the
   disposable Auth0 user separately in the Auth0 dashboard after verification.

## Opt in to the BFF API proof of concept

The limited proof of concept permits a linked BFF session to call these
existing read-only endpoints: `GET /me`, `GET /uploads/mine`, `GET
/sessions/mine`, and `GET /classes/enrolled`. It also permits the low-risk
`PATCH /uploads/{uploadId}/name` mutation with mandatory antiforgery and
same-origin validation. Every other CasePilot API keeps its existing
Bearer-only policy.
This integration is available only in Development and has a second feature flag
that defaults to false:

```powershell
dotnet user-secrets set "Auth0:BffApiEnabled" "true"
```

Restart the HTTPS backend, sign in with the linked disposable Auth0 user, and
open `https://localhost:7002/me`, `https://localhost:7002/uploads/mine`,
`https://localhost:7002/sessions/mine`, or
`https://localhost:7002/classes/enrolled`. Responses keep their existing JSON
shape and ownership filtering. On every request the server re-resolves the validated Auth0 `sub`
through `UserIdentities` and reloads the local user, so cookie-cached local IDs
and roles are not authorization inputs. An unlinked subject, deleted user, or
invalid mapping is denied. Client headers, query strings, and request bodies
cannot select a local user or role.

After verification, disable the proof of concept:

```powershell
dotnet user-secrets remove "Auth0:BffApiEnabled"
```

To exercise the rename proof of concept, first fetch `GET /bff/dev/csrf` with
the BFF cookie. It returns a request token and sets the Secure, HttpOnly
antiforgery cookie. Send both cookies, the request token in `X-CSRF-TOKEN`, and
JSON such as `{ "name": "Renamed case" }` to `PATCH
/uploads/{uploadId}/name`. Browsers provide the `Origin` header; the server
requires it, or a same-origin `Referer`, to match `BFF_PUBLIC_ORIGIN`.

Bearer-authenticated requests to the same endpoint remain compatible and do
not require an antiforgery token. Missing or invalid tokens are rejected before
rename logic executes, and the existing owner-only update still returns the
non-disclosing `404` for another user's upload.

Future cookie-authenticated POST, PUT, PATCH, or DELETE endpoints must opt in
through the same write-policy helper. This attaches both live local-identity
resolution and antiforgery/origin enforcement. SameSite cookies alone are not
the CSRF control. The React frontend has not yet been changed to fetch or send
these tokens.

## Same-origin production requirement

The browser must call the BFF on the same origin that serves CasePilot so its
Secure, HttpOnly session cookie remains first-party and CSRF protections can be
applied consistently. The current Vercel frontend plus Azure backend are
different origins, so they are not ready for the planned cookie BFF as-is.

Before production Auth0 is enabled, choose one of these arrangements:

- Serve the compiled frontend from the ASP.NET Core/Azure origin; or
- Keep Vercel as the public origin and configure it as a reverse proxy for all
  API and authentication paths (`/api/*`, `/uploads/*`, `/signin-auth0`,
  `/signout-callback-auth0`, and the future `/bff/*`) to Azure.

For a public origin such as `https://casepilot.example`, the production Auth0
application must contain exactly:

- **Allowed Callback URLs:** `https://casepilot.example/signin-auth0`
- **Allowed Logout URLs:** `https://casepilot.example/signout-callback-auth0`
- **Allowed Web Origins:** `https://casepilot.example`

Set `BFF_PUBLIC_ORIGIN=https://casepilot.example`. Do not use the direct Azure
API hostname there when users browse on the Vercel/custom-domain origin.
