# Performance verification

These k6 profiles turn CasePilot performance expectations into repeatable pass/fail checks.

## Safe public smoke test

```powershell
docker run --rm -i -e BASE_URL=https://your-api.example.com grafana/k6 run - < performance/k6/smoke.js
```

The smoke profile ramps to 10 virtual users and verifies liveness and full dependency readiness. Override `VUS`, `RAMP_UP`, or `DURATION` as needed.

## Authenticated read test

Create dedicated non-production test users. One user supports the default single VU without exceeding the user-scoped rate limit. Supply one credential per concurrent VU for higher concurrency:

```powershell
$env:BASE_URL = "https://your-staging-api.example.com"
$env:LOAD_TEST_USERS_JSON = '[{"email":"load1@example.com","password":"..."}]'
k6 run performance/k6/authenticated.js
```

Do not load-test production with real user accounts. Login is intentionally protected by a strict IP rate limit, so keep the credential set to ten or fewer per test runner.

## Paid AI test

The AI profile is disabled unless `ALLOW_PAID_AI_TEST=true`. It uses one VU and three iterations by default because each request consumes model tokens:

```powershell
$env:ALLOW_PAID_AI_TEST = "true"
$env:LOAD_TEST_UPLOAD_ID = "a-dedicated-test-document-id"
k6 run performance/k6/ai.js
```

Run this only against a dedicated indexed document and account. Increase `ITERATIONS` deliberately after checking OpenAI budget limits.

## Release criteria

- Public and authenticated request failure rate stays below 1%.
- Public liveness p95 stays below 250 ms and readiness p95 below 750 ms.
- Authenticated read endpoints stay below 750 ms p95.
- AI answers stay below 20 seconds p95 in the small paid sample.
- Readiness remains healthy and queue depth does not trend upward during the test.

Treat these as initial service-level objectives. Save each release's k6 summary, then tighten thresholds from observed staging baselines rather than loosening them to make a failing release pass.

## Staged capacity certification

The capacity profiles require a unique short-lived identity for every virtual user so Redis and API limits behave like 1,000 distinct users rather than one abusive account.

Generate a local token file with the same JWT settings as the approved target:

```powershell
$env:ALLOW_LOAD_TEST_TOKEN_GENERATION = "true"
$env:LOAD_TEST_TOKEN_COUNT = "1000"
$env:LOAD_TEST_TOKEN_OUTPUT = "performance/secrets/capacity-tokens.json"
$env:JWT_SECRET = "the-staging-secret"
dotnet run -- --generate-load-test-tokens
```

Run the gradual profile:

```powershell
docker run --rm `
  -v "${PWD}:/work" -w /work `
  -e ALLOW_CAPACITY_TEST=true `
  -e TARGET_ENVIRONMENT=staging `
  -e BASE_URL=https://staging-api.example.com `
  -e MAX_VUS=1000 `
  -e LOAD_TEST_TOKENS_FILE=/work/performance/secrets/capacity-tokens.json `
  grafana/k6 run /work/performance/k6/capacity.js
```

`capacity.js` ramps through 5%, 10%, 25%, 50%, and 100% of `MAX_VUS`, holds the peak, and ramps down. `spike.js` sends one simultaneous authenticated request per virtual user. Both refuse to start without explicit acknowledgement, require unique identities, and require an additional override for production targets.

The manual `Staging Capacity Test` GitHub workflow automates token generation, preflight readiness, execution, credential cleanup, and summary upload. Complete [CAPACITY_CHECKLIST.md](./CAPACITY_CHECKLIST.md) before making a concurrency claim.
