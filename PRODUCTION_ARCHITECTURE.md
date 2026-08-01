# Production Architecture Plan

This app is moving from a single-instance SQLite/local-disk pilot toward a production deployment that can support classroom-scale usage.

## Current Production Seams

- `IDocumentStorage` abstracts document artifacts: PDFs, summaries, layout manifests, and indexes.
- `LocalDocumentStorage` preserves the current local-disk behavior for development and current deployments.
- `AzureBlobDocumentStorage` stores document artifacts in Azure Blob Storage and caches PDFs locally when PDF libraries require file paths.
- `DatabaseOptions` centralizes database configuration and intentionally fails fast for unsupported providers until repository migration is complete.
- `IUploadRepository` owns upload metadata, ownership, class-based access checks, and upload display-name lookup.
- `IUserRepository` owns user creation, credential lookup, and profile lookup for auth and `/me`.
- `ISessionRepository` owns user-facing session history, message history, notes, user-owned session deletion, and admin session reporting.
- `IMessageRepository` owns AI chat message persistence, answer-cache lookup, and recent conversation context.
- `IClassRepository` owns class flows: create/list/join/enrolled/delete/join-code, roster management, case assignment, class details/history, and instructor session logs.
- `ITutorRepository` owns tutor help events, reading assignment lookup, reading performance snapshots, and class progress reporting.
- `IndexJobs`, `IndexingService`, and `IndexJobWorkerHostedService` provide a durable job boundary for document indexing and classification.
- `/health/live` is a process liveness probe. `/health/ready` verifies PostgreSQL, the index queue, and Redis when configured. `/healthz` remains a liveness-compatible alias.

## Target Architecture

- Frontend: static Vite/React hosting.
- API: horizontally scalable app service or container app.
- Database: managed PostgreSQL or SQL Server.
- Document artifacts: Azure Blob Storage.
- Background processing: dedicated worker role for PDF analysis, classification, embeddings, and index generation.
- Job storage: the `IndexJobs` table is the durable handoff point between web and worker processes.
- Cache/rate limits: Redis-backed global and endpoint limits when `REDIS_CONNECTION_STRING` is configured, with the existing per-instance limiter retained as a fail-open safety net.
- Observability: structured logs, tracing, queue depth, model usage, latency, and cost dashboards.

## Migration Order

1. Replace direct SQLite usage with repository interfaces.
   - Done: upload endpoints use `IUploadRepository`.
   - Done: auth and `/me` use `IUserRepository`.
   - Done: user-facing sessions/messages/notes use `ISessionRepository`.
   - Done: ask/stream AI message persistence and cache lookup use `IMessageRepository`.
   - Done: class management and instructor class reporting use `IClassRepository`.
   - Next: remaining legacy chat helpers, detailed tutor inspection endpoints, and debug-only diagnostics.
2. Add managed database implementation and migrations.
   - Done: repositories support SQLite and PostgreSQL through `DatabaseOptions`.
   - Done: fresh and repeat startup schemas are verified against PostgreSQL in CI.
   - Done: PostgreSQL workers claim jobs atomically with `FOR UPDATE SKIP LOCKED`.
   - Next: replace startup DDL with versioned, reviewed migration files before the first production schema change.
3. Add Azure Blob implementation of `IDocumentStorage`.
   - Done: select with `DOCUMENT_STORAGE_PROVIDER=azureblob`.
   - Done: API and worker share PDFs, summaries, indexes, layouts, and document classifications through the same storage provider.
   - Done: Azure Blob behavior is integration-tested in CI with Azurite, including cache eviction and re-download.
4. Done: upload post-processing now enqueues durable index jobs.
5. Done: the app can run as a worker with `--worker`, and `RUN_BACKGROUND_WORKER=false` allows web-only instances.
6. Performance and observability hardening.
   - Done: OpenTelemetry traces and metrics cover HTTP, outbound calls, runtime behavior, AI latency/token use, index jobs, and distributed rate limiting.
   - Done: separate liveness and dependency-aware readiness probes cover PostgreSQL, Redis, and durable index queue health.
   - Done: repeatable k6 profiles cover public health, authenticated reads, and a deliberately opt-in paid AI path.
   - Next: establish staging baselines for upload/indexing, tutor, and instructor dashboards using seeded test data.

## Process-local caches

- Vector indexes are cached only as an acceleration layer; Blob Storage remains authoritative.
- The vector cache defaults to a 256 MiB exact memory budget and 30-minute sliding expiration. Override with `VECTOR_INDEX_CACHE_MAX_BYTES` and `VECTOR_INDEX_CACHE_SLIDING_MINUTES`.
- Tutor sessions are loaded from PostgreSQL at each mutating request, scoped to their owning user, and cached only during processing. The local cache defaults to 2,048 entries and a 120-minute sliding expiration. Override with `TUTOR_SESSION_CACHE_MAX_ENTRIES` and `TUTOR_SESSION_CACHE_SLIDING_MINUTES`.
- Set `REDIS_CONNECTION_STRING` before scaling the API horizontally. Redis uses atomic fixed-window counters for global, authentication, upload, and AI limits; the local limiter remains active if Redis is temporarily unavailable.
- Render enables forwarded-header processing automatically through its built-in `RENDER=true` variable because the service port is reachable only through Render's proxy. On other platforms, set `TRUST_FORWARDED_HEADERS=true` only when the service port is likewise isolated behind a trusted proxy. The app then uses the proxy-normalized client IP instead of trusting a raw, spoofable `X-Forwarded-For` value.

## Runtime Flags

- `DATABASE_PROVIDER=sqlite|postgres` selects the relational database provider.
- `SQLITE_CONNECTION_STRING` optionally overrides the default local SQLite path.
- `POSTGRES_CONNECTION_STRING` is required when `DATABASE_PROVIDER=postgres`.
- `DOCUMENT_STORAGE_PROVIDER=local|azureblob` selects artifact storage.
- `AZURE_STORAGE_CONNECTION_STRING` is required for Azure Blob storage.
- `AZURE_STORAGE_CONTAINER` defaults to `documents`.
- `RUN_BACKGROUND_WORKER=false` disables the hosted worker in web instances.
- `REDIS_CONNECTION_STRING` enables cross-instance rate-limit coordination.
- `TRUST_FORWARDED_HEADERS=true` processes one proxy hop for client IP and HTTPS scheme on non-Render platforms; use only behind a trusted managed proxy.
- `--worker` starts the process in worker-only mode.
- `--verify-database` runs destructive database and document-storage smoke checks and must only target an empty disposable database and test storage container.
- `MAX_UPLOAD_BYTES` limits uploaded file size.
- `MAX_UPLOAD_PAGES` limits uploaded PDF page count.
- `ENABLE_DEBUG_ENDPOINTS=true` exposes protected debug endpoints in production.

## Observability and performance

- Set `OTEL_EXPORTER_OTLP_ENDPOINT` to send traces and metrics to any OTLP-compatible observability provider. Standard `OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_EXPORTER_OTLP_HEADERS` settings are honored by the exporter.
- Leave the endpoint unset to keep instrumentation active without attempting network export. This is safe for local development and CI.
- `SLOW_REQUEST_THRESHOLD_MS` defaults to `1500`; slower requests and server errors produce structured warning logs containing route templates and request IDs, never query contents.
- `INDEX_QUEUE_DEGRADED_DEPTH` defaults to `20`; readiness becomes degraded at that queue depth.
- `INDEX_JOB_STALE_MINUTES` defaults to `35`; readiness becomes degraded when a running job has not renewed its lease within that window.
- Dashboard the HTTP p50/p95/p99 latency and error rate, AI latency and tokens by operation/model, index completion/failure duration, Redis fallback/rejection counts, runtime GC/memory, and readiness state.
- Run the release profiles documented in `performance/README.md` against staging before raising production capacity or changing model configuration.
