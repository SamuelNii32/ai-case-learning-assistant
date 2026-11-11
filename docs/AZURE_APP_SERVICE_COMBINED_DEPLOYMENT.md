# Combined CasePilot Azure App Service deployment

This runbook prepares a single-origin deployment in which one Azure App Service
serves the compiled React application, Auth0 BFF, ASP.NET Core APIs, PDFs, and
AI streams. It does not authorize a deployment, Azure changes, Auth0 changes,
database migrations, or production account changes.

> **Azure owner start here:** If you are helping by gathering information from
> the Azure portal, use the short, screenshot-by-screenshot checklist in
> [`AZURE_OWNER_STAGING_CHECKLIST.md`](AZURE_OWNER_STAGING_CHECKLIST.md). It does
> not ask you to change anything or reveal secret values.

The Phase 2 branch is based on the unmerged instructor-role fix. Merge order
must preserve that dependency: merge the instructor fix first, then update this
branch from `main` and re-run all checks before considering this pull request.

Before either merge, follow the repository release controls in
[`GITHUB_MAIN_PROTECTION.md`](GITHUB_MAIN_PROTECTION.md). The Vercel production
branch must remain on the verified hold branch until an explicit cutover.

## Build one immutable artifact

The web project runs the following automatically during `dotnet publish`:

1. `npm ci --no-audit --no-fund`
2. `npm run build:appservice`
3. validates that `dist/index.html` exists
4. includes `dist/**` as `wwwroot/**` in the ASP.NET Core publish output

The `appservice` Vite mode is defined by `.env.appservice`. It requires
`VITE_AUTH_MODE=bff` and rejects any non-empty `VITE_API_BASE`, keeping browser
requests on the public App Service origin. These are build settings, not Azure
runtime secrets.

From a clean checkout with .NET 9 and Node.js 20 installed:

```powershell
dotnet publish IngestionApi.csproj `
  --configuration Release `
  --output artifacts/appservice-publish `
  /p:UseAppHost=false
npm run verify:appservice -- artifacts/appservice-publish
npm run smoke:appservice -- artifacts/appservice-publish
```

For a backend-only diagnostic publish, `/p:BuildReactOnPublish=false` is
available. Do not use that opt-out for the combined production site.

Zip the *contents* of `artifacts/appservice-publish`, not the parent directory.
Keep the ZIP and commit SHA as the immutable rollback artifact. Do not include
local `.env` files, user-secrets, databases, uploads, or publish profiles.

## Information the Azure owner must confirm

No credentials need to be shared with the developer. The subscription owner
should report only these non-secret facts:

- App Service name, region, operating system, runtime stack, and plan SKU.
- Whether **HTTPS Only**, minimum TLS 1.2, HTTP/2, and **Always On** are enabled.
- Current CPU, memory working set, HTTP queue, response-time, and disk metrics
  during a representative upload/index/chat workload.
- Current deployment mechanism and whether a deployment slot exists.
- The approved public HTTPS hostname or owned custom domain.
- Whether the API and worker are separate processes, and confirmation that
  both use the same PostgreSQL database and document Blob container.
- App Service managed-identity object ID and whether its existing Blob and Key
  Vault role assignments cover the Data Protection key ring.
- The production Auth0 tenant/application owner who can enter callback URLs.

Repository evidence points to a Windows App Service, but the portal remains
the authority. Basic B1 or higher supports Always On and custom domains. B1 has
no deployment slots. Serving the small hashed React bundle adds little steady
CPU or memory compared with PDF processing and AI calls, but the existing SKU
cannot be approved without the metrics and SKU facts above. Do not add a new
plan merely to host the frontend.

## Required App Service configuration

Use App Service settings or Key Vault references. Do not put values in source,
the frontend build, chat, or a deployment ZIP.

```text
ASPNETCORE_ENVIRONMENT=Production
AUTH0_ENABLED=true
AUTH0_BFF_API_ENABLED=true
AUTH0_DOMAIN=<hostname only>
AUTH0_CLIENT_ID=<confidential web application client ID>
AUTH0_CLIENT_SECRET=<secret or Key Vault reference>
AUTH0_CALLBACK_PATH=/signin-auth0
AUTH0_SIGNED_OUT_CALLBACK_PATH=/signout-callback-auth0
BFF_PUBLIC_ORIGIN=https://<public-hostname>
AUTH0_STUDENT_REGISTRATION_ENABLED=false
AUTH0_EXISTING_ACCOUNT_LINKING_ENABLED=false
JWT_SECRET=<retain for rollback>
JWT_ISSUER=IngestionApi
JWT_AUDIENCE=IngestionClient
DATABASE_PROVIDER=postgres
POSTGRES_CONNECTION_STRING=<secret or Key Vault reference>
DOCUMENT_STORAGE_PROVIDER=azureblob
AZURE_STORAGE_CONNECTION_STRING=<secret/Key Vault reference if managed identity is not yet used>
AZURE_STORAGE_CONTAINER=<existing document container>
DATA_PROTECTION_BLOB_URI=https://<account>.blob.core.windows.net/<container>/<key-blob>
DATA_PROTECTION_KEY_VAULT_KEY_ID=https://<vault>.vault.azure.net/keys/<key-name>
RUN_BACKGROUND_WORKER=false
TRUST_FORWARDED_HEADERS=false
ENABLE_DEBUG_ENDPOINTS=false
```

`AZURE_FRONT_DOOR_ID` must remain unset. `VITE_AUTH_MODE` and `VITE_API_BASE`
are not runtime settings for this artifact: the safe BFF values are embedded by
the reviewed build.

For the existing Windows App Service, IIS/ASP.NET Core Module supplies the
trusted forwarded scheme before application middleware. Confirm in staging
that HTTPS requests do not redirect repeatedly and that generated Auth0 URLs
are HTTPS. If the owner reports a Linux App Service, stop and perform a
platform-specific forwarded-header review; do not enable the current Front
Door trust switch or trust arbitrary forwarded headers.

The web managed identity needs, at minimum:

- read/write access to the Data Protection key Blob (normally **Storage Blob
  Data Contributor** scoped to its container), and
- wrap/unwrap/get access to the Key Vault key (normally **Key Vault Crypto
  User** scoped as narrowly as practical).

Deployment access should be granted to the Azure owner or an approved CI
identity, not shared as a personal publish-profile password. An operator needs
permission to deploy the Web App and manage its settings; identity/RBAC changes
require separate owner approval.

## Auth0 application settings

Use a separate Regular Web Application for staging. For an approved origin
`https://PUBLIC_HOST`, enter exactly:

```text
Allowed Callback URLs: https://PUBLIC_HOST/signin-auth0
Allowed Logout URLs: https://PUBLIC_HOST/signout-callback-auth0
Allowed Web Origins: https://PUBLIC_HOST
```

Keep Authorization Code flow, confidential client authentication, PKCE,
state, nonce, and correlation validation enabled. Do not put the client secret
or Auth0 tokens in React settings. Do not enable automatic email linking.

## Route and security behavior

Known React document navigations requesting `text/html` receive `index.html`,
including `/cases`, `/sessions`, `/admin/*`, and `/workspace/*`. JSON/API calls
to overlapping paths continue to their API endpoints. Unknown `/api`, `/bff`,
health, upload/PDF, and Auth0 callback paths never fall through to the SPA.

The combined origin does not change resource authorization. BFF requests still
resolve the Auth0 subject through `UserIdentities`, reload the local user and
role, reject disabled/unlinked accounts, and require antiforgery plus a matching
Origin/Referer on cookie-authenticated mutations. JWT Bearer remains available
for rollback.

## Safe staging sequence

1. Merge the instructor-role fix only after its review; do not merge this
   dependent branch first.
2. Rebase or merge current `main` into this branch and run all CI checks again.
3. Create/download the combined artifact from the reviewed commit.
4. Back up the current deployable artifact and record current App settings.
5. Prefer a staging slot if the current SKU already supports one. Do not upgrade
   or buy a resource without approval. If there is no slot, arrange an explicit
   maintenance window and use a separately approved staging Web App if one
   already exists.
6. Configure the staging public origin in App Service and the staging Auth0
   application. Do not point production DNS or use production accounts.
7. Deploy the ZIP using the owner's existing approved deployment mechanism.
8. Verify health, static assets, SPA refreshes, login/logout, CSRF rejection,
   student/instructor class workflows, upload/index completion, PDF range
   responses, AI streaming/cancellation, and JWT rollback.
9. Review App Service CPU/memory/response metrics before any production change.

## Rollback

1. Stop further identity-linking or schema work during the rollback window.
2. If using a slot, swap the prior slot back. Otherwise redeploy the recorded
   previous ZIP artifact.
3. Restore the previous frontend authentication mode only with its matching
   backend artifact; avoid frontend/backend version skew.
4. Keep additive `UserIdentities` and account-status data. Do not delete users,
   mappings, uploads, sessions, classes, or Data Protection keys.
5. Keep Auth0 and legacy JWT secrets unchanged during an application rollback.
6. Confirm health, legacy JWT login, worker queue progress, PDF access, and
   instructor/student isolation after rollback.

## Staging acceptance checklist

- [ ] `/`, `/login`, `/dashboard`, `/admin/classes`, and a workspace URL refresh correctly.
- [ ] Unknown `/api/*`, `/bff/*`, and callback paths return API/auth errors, not React HTML.
- [ ] Auth0 callback and logout return to the same HTTPS origin.
- [ ] Cookies are Secure, HttpOnly, host-only, and absent from JavaScript storage.
- [ ] Missing/invalid CSRF and foreign Origin requests are rejected.
- [ ] Linked instructor can list/create classes and assign an owned upload.
- [ ] Students cannot use instructor operations or access unrelated records.
- [ ] Disabled, deleted, invalidly mapped, and unlinked identities are denied.
- [ ] A PDF byte range returns `206 Partial Content` with valid range headers.
- [ ] Maximum supported PDF upload succeeds and its worker job completes.
- [ ] AI streams incrementally and cancellation closes the request.
- [ ] A valid legacy Bearer JWT still works during the rollback window.
- [ ] Restarting the App Service preserves valid sessions through shared Data Protection keys.
