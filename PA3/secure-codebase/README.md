# Resource Allocation & Help Desk Hub — secure codebase (PA3)

Help desk tickets request hardware, software licenses and cloud credit vouchers for courses and
research labs; approved tickets are allocated from asset pools. This copy is the PA2 service
hardened to be secure by design. C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core.

- Service: `src/ResourceHub.Api/`
- Tests: `tests/ResourceHub.Tests/`
- Build and run: `Dockerfile`, `docker-compose.yml`, `db/init/01-app-role.sh`, `.env.example`

## Configure and run with Docker Compose

1. Get the token issuer's public keys as a JWKS file (`{"keys":[{"kty":"RSA","kid":"…","n":"…","e":"…"}]}`).
   The service never fetches keys over the network.
2. Copy `.env.example` to `.env` (`.env` is git-ignored) and fill in every `<…>` value:

   | Variable | Meaning |
   |---|---|
   | `POSTGRES_USER`, `POSTGRES_PASSWORD` | Database superuser, used only by the database container at first start |
   | `APP_DB_USER`, `APP_DB_PASSWORD` | Non-superuser role the API connects as (created by `db/init/01-app-role.sh`) |
   | `FIELD_ENCRYPTION_KEY` | Base64 of 32 random bytes (`openssl rand -base64 32`): the AES-256 key for license key values |
   | `JWKS_HOST_PATH` | Absolute host path of the JWKS file; mounted read-only into the API container |
   | `JWT_JWKS_PATH` | Where the API reads the JWKS inside the container (default `/etc/resourcehub/jwks.json`) |
   | `JWT_ISSUER` | Required `iss`: `http://localhost:9000` |
   | `JWT_AUDIENCE` | Required `aud`: `cs763-api` |
   | `API_PORT` | Host port, bound to `127.0.0.1` only (default 8080) |

   Compose stops with an error if a password, the field key or `JWKS_HOST_PATH` is missing; there
   are no fallback values.
3. Start it: `docker compose up -d --build`. The API listens on `http://127.0.0.1:${API_PORT}`.
4. Call it with a bearer token from the issuer:

   ```bash
   curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8080/api/v1/assets
   ```

   `GET /api/v1/health` is the only endpoint that works without a token.

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
that may create databases (each test class creates and drops its own database). The tests create a
throwaway RSA key pair in memory, write a test JWKS file and sign their own tokens.

```bash
RESOURCEHUB_TEST_DB="Host=127.0.0.1;Port=55432;Database=rhtest;Username=rhtest;Password=<pw>" dotnet test tests/ResourceHub.Tests
```

`RESOURCEHUB_COMPOSE_TESTS=1` also builds and starts the Compose stack (needs Docker), with
`JWKS_HOST_PATH` pointing at the test JWKS file and random passwords and field key, runs
end-to-end checks against it and tears it down.

## Where the security checks are

Paths are relative to `src/ResourceHub.Api/`.

### Token signature verification

| What | File and lines |
|---|---|
| JwtBearer setup: RS256 allowlist, key chosen by `kid` from the JWKS only, `iss`/`aud`/`exp`/`nbf`, 60 s skew, no error detail | `Security/JwtAuthentication.cs` lines 32–85 (validation parameters 47–64; `kid` resolver 54–55) |
| Strict `sub` (GUID) and `roles` (array of strings) after the signature check; all other claims dropped | `Security/JwtAuthentication.cs` lines 87–124 |
| Loading the JWKS file: RSA signing keys ≥ 2048 bits, public parts only, duplicate `kid` refused | `Security/JwtAuthentication.cs` lines 126–168 |
| Deny by default: every endpoint needs an authenticated user except health | `Program.cs` lines 42–46; `Endpoints/HealthEndpoints.cs` line 31 |
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
| Encrypt / decrypt, key loading and checks | `Security/FieldEncryptor.cs` lines 31–101 |
| Key from `FIELD_ENCRYPTION_KEY`; the service will not start without a valid one | `Program.cs` line 27 |
| Encrypt on asset create and update | `Endpoints/AssetEndpoints.cs` lines 223, 289–301, 364–368 |
| Decrypt for the admin allocate response | `Services/AllocationService.cs` line 141 |
| Decrypt in the allocation listing, only for admin or the requester | `Endpoints/AllocationEndpoints.cs` lines 75–77 |
| Seed keys and any old plaintext keys encrypted at startup, before the plaintext column is dropped | `Data/DatabaseStartup.cs` lines 31–83; `Data/Migrations/20261126100100_SecurityHardening.cs` |

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
- **Database exposure (CWE-1392, CWE-1327, CWE-250, CWE-260):** no published database port (internal
  network only), no committed or fallback passwords, the API connects as a role without
  superuser, create-role or create-database rights, the API container runs as non-root with a
  read-only filesystem, and the API port is bound to 127.0.0.1.
