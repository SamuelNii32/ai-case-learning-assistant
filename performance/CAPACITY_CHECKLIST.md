# Capacity certification checklist

Passing a k6 threshold proves that a specific deployment handled a specific workload. It does not permanently certify every future deployment or every kind of user activity.

## Prepare staging

- Use a staging API, PostgreSQL database, Redis instance, Blob container, and worker that match production topology and instance sizes.
- Configure the `capacity-staging` GitHub environment with required reviewers.
- Add `CAPACITY_TEST_JWT_SECRET` as an environment secret. It must match staging `JWT_SECRET`.
- If staging uses non-default JWT values, add `CAPACITY_TEST_JWT_ISSUER` and `CAPACITY_TEST_JWT_AUDIENCE` as environment variables.
- Configure `OTEL_EXPORTER_OTLP_ENDPOINT` and provider headers on the staging API and worker.
- Confirm no production hostname, database, Redis instance, Blob container, or OpenAI key is present in the staging environment.

## Establish capacity

Run the `Staging Capacity Test` workflow in this order, stopping when a level fails:

1. Capacity profile at 50 users.
2. Capacity profile at 100 users.
3. Capacity profile at 250 users.
4. Capacity profile at 500 users.
5. Capacity profile at 1,000 users.
6. Spike profile at the highest capacity level that passed.

Use at least a one-minute stage while finding the first bottleneck. Repeat the final candidate with two-minute stages to expose connection-pool exhaustion, memory growth, and queue buildup.

## Pass criteria

- Fewer than 1% failed requests and checks.
- Fewer than 0.1% HTTP 429 responses and server errors.
- Capacity workload p95 below 750 ms and p99 below 1.5 seconds.
- Spike workload p95 below 1.5 seconds and p99 below 3 seconds.
- Readiness remains healthy throughout the run.
- API CPU remains below 80% sustained and memory returns toward baseline after ramp-down.
- PostgreSQL connections stay below the configured pool/database ceiling, with no connection timeouts or lock buildup.
- Redis latency and fallback counters remain stable.
- Index queue depth does not grow during this read-only profile.

## Interpretation

The profiles use one unique JWT identity per virtual user and exercise authenticated upload history, session history, and enrolled-class reads. The generated identities do not correspond to real users, so result sets are intentionally empty. This isolates authentication, API, Redis, and database concurrency capacity.

It does not certify paid AI inference, large result payloads, uploads, or indexing. Those workloads need separately seeded staging data and explicit cost budgets after the authenticated read baseline passes.

Record the commit SHA, infrastructure sizes, database connection limits, k6 summary artifact, dashboard screenshots, and first failing level. Capacity claims should always include those conditions.
