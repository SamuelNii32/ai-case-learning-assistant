# Security audit exceptions

## GHSA-qwww-vcr4-c8h2 — React Router unstable RSC APIs

- Added: 2026-08-01
- Review by: 2026-09-01
- Current package: `react-router-dom@7.18.2`
- Patched package: `react-router@8.3.0`
- Scope: the advisory only affects React Router's unstable React Server Components APIs. This application is a client-rendered Vite SPA using `BrowserRouter` and does not use the RSC APIs.
- Removal condition: upgrade to a compatible patched React Router release and remove this allowlist entry.

This exception is intentionally limited to one advisory. New high or critical findings continue to fail CI.
