# Resource Allocation & Help Desk Hub — secure codebase (PA3)

> **AI Usage Log:** written by Claude Code (Claude Opus 5.5) in the PA3 measured run (iteration 1) and updated in the post-measured hardening run (H1–H4), ~100% AI. Human changes: this note only. Verification: the agent rechecked every file path and line range below during the hardening run; I reviewed the authentication, authorization and encryption sections (`PA3/.ai_logs/conceptual_journal.md` §2.1) and checked the hardening on the running stack (`PA3/evidence/own/hardening-check.txt`).

Help desk tickets request hardware, software licenses and cloud credit vouchers for courses and
research labs; approved tickets are allocated from asset pools. This copy is the PA2 service
hardened to be secure by design. C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core.

- Service: `src/ResourceHub.Api/`
- Tests: `tests/ResourceHub.Tests/`
- Build and run: `Dockerfile`, `docker-compose.yml`, `db/init/01-app-role.sh`, `.env.example`
- Analyzer settings: `.editorconfig` (the rules SonarQube reported fail the build)

## Configure and run with Docker Compose

1. Get the token issuer's public keys as a JWKS file (`{"keys":[{"kty":"RSA","kid":"…","n":"…","e":"…"}]}`).
   The service never fetches keys over the network.
2. Create the field encryption key as a file (the `secrets/` folder is git-ignored and kept out of
   the image build):

   ```bash
   mkdir -p secrets && (umask 077 && openssl rand -base64 32 > secrets/field_encryption_key)
   ```

   Compose passes it to the containers as the Docker secret `field_encryption_key`, mounted at
   `/run/secrets/field_encryption_key`; the containers only get `FIELD_ENCRYPTION_KEY_FILE` (that
   path), so `docker inspect` never shows the key. On Docker Desktop a file readable only by you
   works. On Linux, Compose bind-mounts the file with its host owner and mode, so the API's
   non-root user (UID 1654 in the .NET images) must be able to read it, for example
   `sudo chown 1654 secrets/field_encryption_key && chmod 0400 secrets/field_encryption_key`.
3. Copy `.env.example` to `.env` (`.env` is git-ignored) and fill in every `<…>` value:

   | Variable | Meaning |
   |---|---|
   | `POSTGRES_USER`, `POSTGRES_PASSWORD` | Database superuser, used only by the database container at first start |
   | `OWNER_DB_USER`, `OWNER_DB_PASSWORD` | **Owner role**: owns the `resourcehub` database, its schema, tables and triggers. Only the one-shot `migrate` container gets it |
   | `APP_DB_USER`, `APP_DB_PASSWORD` | **Runtime role** the API connects as: owns nothing, reads and writes data only (must differ from the owner) |
   | `FIELD_ENCRYPTION_KEY_HOST_FILE` | Absolute host path of the key file from step 2 |
   | `JWKS_HOST_PATH` | Absolute host path of the JWKS file; mounted read-only into the API container |
   | `JWT_JWKS_PATH` | Where the API reads the JWKS inside the container (default `/etc/resourcehub/jwks.json`) |
   | `JWT_ISSUER` | Required `iss`: `http://localhost:9000` |
   | `JWT_AUDIENCE` | Required `aud`: `cs763-api` |
   | `API_PORT` | Host port, bound to `127.0.0.1` only (default 8080) |

   Compose stops with an error if a password, the key file or `JWKS_HOST_PATH` is missing; there
   are no fallback values.
4. Start it: `docker compose up -d --build`. Compose starts the database, then runs `migrate`
   (`dotnet ResourceHub.Api.dll --migrate` as the owner role: applies the migrations, encrypts any
   plaintext license keys, grants the runtime role its data access and exits), then starts the API
   once `migrate` has exited with 0. The API listens on `http://127.0.0.1:${API_PORT}`, and
   `docker compose ps` shows it as `healthy` once `/api/v1/health` answers 200. The health check
   runs `dotnet ResourceHub.Api.dll --healthcheck` inside the container, so the image needs no
   extra packages.
5. Call it with a bearer token from the issuer:

   ```bash
   curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8080/api/v1/assets
   ```

   `GET /api/v1/health` is the only endpoint that works without a token.

**Upgrading a database volume created before the owner/runtime split.** `db/init/01-app-role.sh`
runs only on an empty volume, so an older volume has no owner role and its tables still belong to
the runtime role. With throwaway data, recreate it (`docker compose down -v`, then step 4). To keep
the data, as the superuser create the owner role, then run `REASSIGN OWNED BY resourcehub_app TO
resourcehub_owner;`, `ALTER DATABASE resourcehub OWNER TO resourcehub_owner;` and `ALTER SCHEMA
public OWNER TO resourcehub_owner;` in `resourcehub`, and start the stack. `migrate` then grants
the runtime role its data access. (This in-place path is not covered by the tests; the fresh-volume
path is.)

### Configuration outside Compose

| Setting | Meaning |
|---|---|
| `ConnectionStrings:HubDb` | The runtime connection the API serves requests with (required) |
| `ConnectionStrings:HubDbOwner` | The owner connection. Only where the process may change the schema (tests, a local run): the API then migrates at startup as the owner and grants the runtime role. Not set in the API container; without it the API never runs DDL |
| `Database:RuntimeRole` | The role `--migrate` grants data access to; in-process startup takes the user name from `HubDb` when this is not set |
| `FIELD_ENCRYPTION_KEY_FILE` | Path of the key file (containers) |
| `FIELD_ENCRYPTION_KEY` | The base64 key itself, for tests and local runs. Setting both this and the file is refused |

### Tokens the service accepts

- Signed with **RS256** by a key in the JWKS file whose `kid` equals the token's `kid`. `none`,
  HS256 and every other algorithm are rejected; an unknown or missing `kid` is rejected; `jku`,
  `x5u` and `jwk` headers are ignored.
- `exp` is required; `exp`, `nbf` (when present), `iss` = `JWT_ISSUER` and `aud` = `JWT_AUDIENCE`
  (a string, or an array that contains it) are checked with at most 60 seconds of clock skew.
- `sub` must be a seeded user id (a GUID); `roles` must be a JSON array of strings. Roles:
  `admin` (IT director and administrator) and `faculty_instructor` (course instructor and lab
  advisor). Any other authenticated caller has no role-based rights.
- Identity, ownership and roles come only from the verified token, never from the body, query
  string or other headers (the old `X-User-Id` header is ignored). Course and lab association
  comes from the seeded `users` table, looked up by `sub`.
- Missing or invalid tokens get **401** (with no detail about which check failed).

### Running the tests

The database tests need PostgreSQL; set `RESOURCEHUB_TEST_DB` to a connection string for a user
that may create databases and roles (a superuser of a throwaway server). Each test class creates
its own database owned by its own owner role and runs the API as a separate runtime role, like
Compose; it drops the database and both roles afterwards. The tests create a throwaway RSA key pair
in memory, write a test JWKS file and sign their own tokens.

```bash
RESOURCEHUB_TEST_DB="Host=127.0.0.1;Port=55432;Database=rhtest;Username=rhtest;Password=<pw>" dotnet test tests/ResourceHub.Tests
```

`RESOURCEHUB_COMPOSE_TESTS=1` also builds and starts the Compose stack (needs Docker) under a
throwaway project name, with `JWKS_HOST_PATH` pointing at the test JWKS file, random passwords and
a random field key written to a secret file. It runs end-to-end checks against the stack (health
status, owner-only migration, runtime role restrictions, no owner credentials or plain key in the
API container) and tears it down.

## Where the security checks are

Paths are relative to `src/ResourceHub.Api/`.

### Token signature verification

| What | File and lines |
|---|---|
| JwtBearer setup: RS256 allowlist, key chosen by `kid` from the JWKS only, `iss`/`aud`/`exp`/`nbf`, 60 s skew, no error detail | `Security/JwtAuthentication.cs` lines 32–85 (validation parameters 47–65; `kid` resolver 54–55) |
| Strict `sub` (GUID) and `roles` (array of strings) after the signature check; all other claims dropped | `Security/JwtAuthentication.cs` lines 87–124 |
| Loading the JWKS file: RSA signing keys ≥ 2048 bits, public parts only, duplicate `kid` refused | `Security/JwtAuthentication.cs` lines 126–168 |
| Deny by default: every endpoint needs an authenticated user except health | `Program.cs` lines 61–63; `Endpoints/HealthEndpoints.cs` line 32 |
| Caller built from the token (sub, roles) plus seeded association | `Services/Actors.cs` lines 17–79 |

### Row-level authorization

| Resource | File and lines |
|---|---|
| Ticket read rule (`CanRead`) and owner rule (`IsOwnTicket`) | `Endpoints/TicketEndpoints.cs` lines 41–47 |
| Allocate: admin only | `Endpoints/TicketEndpoints.cs` lines 51–56 |
| Update: status change admin only (403); content edit faculty only (403), own ticket only (404), pending only (409) | `Endpoints/TicketEndpoints.cs` lines 62–126, record check 87–96 |
| Approve / reject / close: record check | `Endpoints/TicketEndpoints.cs` lines 144–149 |
| Delete: admin or faculty (403); admin any, faculty own (404); pending only (409) | `Endpoints/TicketEndpoints.cs` lines 205–235 |
| Listing: admin or faculty (403); faculty rows filtered to their own before counting and paging | `Endpoints/TicketEndpoints.cs` lines 239–305, filter 265–271 |
| Submit: faculty only; only for an associated course or lab | `Endpoints/TicketEndpoints.cs` lines 310–318; `Services/TicketContent.cs` (association check); `Services/Actors.cs` lines 35–37 |
| Get: admin or faculty (403), then record check (404) | `Endpoints/TicketEndpoints.cs` lines 351–364 |
| Allocation listing: admin or faculty (403); faculty see only their own course or lab (other groups: no rows); keys decrypted only for admin or the ticket's requester | `Endpoints/AllocationEndpoints.cs` lines 33–96 (group rule 55–69, key rule 75–77 and 94–96) |
| Assets: catalog open to any authenticated caller; create, update, delete and returns admin only | `Endpoints/AssetEndpoints.cs` lines 34–38 and each write handler's first lines |

### The 404 / 403 rule

- A role that is never allowed an action gets **403** before any record is looked at (for
  example faculty approving, allocating or creating assets; a caller with no role reading tickets).
- If the role may do the action, the record is checked against the caller. A record the caller
  may not access answers **404** with the same body as a record that does not exist (for example
  one faculty member reading, editing or deleting another's ticket).
- Listings never return 404 for this: they return only the rows the caller may read.

### Where encryption happens

License key values (the sensitive field) are encrypted with AES-256-GCM in the service before they
are stored; the database only holds `license_keys."KeyCiphertext"` (`v1.` + base64 of a random
12-byte nonce, the ciphertext and the 16-byte tag; the row id is bound in as associated data).

| Step | File and lines |
|---|---|
| Key loading and checks: from the secret file in `FIELD_ENCRYPTION_KEY_FILE`, or `FIELD_ENCRYPTION_KEY`; both set, a missing, unreadable or oversized file, or a key that is not 32 random-looking bytes is refused | `Security/FieldEncryptor.cs` lines 42–109 (choice of source 42–54, file read 90–109) |
| Encrypt / decrypt | `Security/FieldEncryptor.cs` lines 111–150 |
| The service and the migration job will not start without a valid key | `Program.cs` line 43; `Data/MigrationJob.cs` line 41 |
| Encrypt on asset create and update | `Endpoints/AssetEndpoints.cs` lines 223, 289–301, 364–368 |
| Decrypt for the admin allocate response | `Services/AllocationService.cs` line 141 |
| Decrypt in the allocation listing, only for admin or the requester | `Endpoints/AllocationEndpoints.cs` lines 75–77 |
| Seed keys and any old plaintext keys encrypted during migration (the `migrate` job in Compose), before the plaintext column is dropped | `Data/DatabaseStartup.cs` lines 41–50 and 147–188; `Data/Migrations/20261126100100_SecurityHardening.cs` |

Unallocated keys are never returned by any endpoint (only an admin could be allowed to see them,
and no endpoint lists them); asset search covers names only.

### Other protections

- **Kept history (CWE-221):** deleting a pending ticket sets `DeletedAt` and adds a `deleted`
  event; ticket events use `ON DELETE RESTRICT`, and database triggers refuse UPDATE, DELETE and
  TRUNCATE on `ticket_events` and `audit_events`.
- **Audit trail (T2):** every change to tickets, stock and devices writes an `audit_events` row with
  the caller's `sub`, roles, time, action and outcome, in the same transaction as the change.
  Refused and failed writes are recorded too (`Services/AuditTrail.cs`).
- **Device returns (T1):** a return needs the device token **and** the check-in code printed on
  the device's label at hand-out. The code is shown once, in the admin-only allocate response,
  stored only as a SHA-256 hash and compared in constant time; the borrower's allocation view
  never shows it (`Security/CheckInCodes.cs`, `Endpoints/AssetEndpoints.cs` lines 46–107).
- **Logs (CWE-532):** device tokens, check-in codes, license keys and bearer tokens are never
  logged; the return log names the device's internal id.
- **Rate limiting (T3):** 300 requests per minute per token `sub`; 200 per minute per client IP
  for callers without a valid token; 429 with `Retry-After` past that. Request bodies are capped
  at 2 MB (`Security/RateLimiting.cs`, configurable under `RateLimiting:*`).
- **Separate database roles (CWE-250, CWE-269):** the owner role owns the database, schema, tables,
  triggers and trigger function, and only the one-shot `migrate` container logs in as it. The API's
  runtime role owns nothing. It has `SELECT, INSERT, UPDATE, DELETE` on the data tables, only
  `SELECT, INSERT` on `ticket_events` and `audit_events`, `SELECT` on `__EFMigrationsHistory`, and
  no `CREATE`, `TEMPORARY`, `TRUNCATE`, `TRIGGER` or `REFERENCES`. It therefore cannot create,
  alter or drop tables, triggers or functions, cannot `DISABLE TRIGGER` (owner only), and cannot
  set `session_replication_role` (`db/init/01-app-role.sh`; grants in
  `Data/DatabaseStartup.cs` lines 90–142; migration only over the owner connection in `Program.cs`
  lines 83–99 and `Data/MigrationJob.cs`). The API container gets neither the owner's user name
  nor its password.
- **Database exposure (CWE-1392, CWE-1327, CWE-250, CWE-260):** no published database port (internal
  network only), no committed or fallback passwords, neither database role has superuser,
  create-role or create-database rights, the API container runs as non-root with a read-only
  filesystem, and the API port is bound to 127.0.0.1.
- **Key not in the environment (CWE-526):** the field key reaches the `migrate` and `api` containers
  only as a Docker secret file; their environment holds only its path.
- **Health check:** Compose marks the API healthy when `dotnet ResourceHub.Api.dll --healthcheck`
  gets 200 from `GET /api/v1/health` on loopback (`Endpoints/HealthProbe.cs`). The probe sends no
  token and the endpoint is still the only public one.
