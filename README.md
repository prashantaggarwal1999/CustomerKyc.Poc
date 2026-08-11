# Customer KYC REST API — POC Documentation

> **POC / Proof of Concept** — Production-structured .NET 10 Minimal API demonstrating Linux and
> Docker compatibility of the legacy `TDESEncrypt.dll` encryption dependency, with JWT auth,
> SQL Server, rate limiting, and full test coverage.

**Status:**
![Tests](https://img.shields.io/badge/Tests-36%20%2F%2036%20Pass-brightgreen)
![Linux](https://img.shields.io/badge/Linux-Ubuntu%2024.04%20Verified-brightgreen)
![Docker](https://img.shields.io/badge/Docker-Multi--stage%20Build-brightgreen)
![.NET](https://img.shields.io/badge/.NET-10-blue)
![AutoMapper](https://img.shields.io/badge/AutoMapper%2016.x-Commercial%20License%20Required%20in%20Prod-yellow)

---

## Table of Contents

1. [POC Objective](#1-poc-objective)
2. [Technology Stack](#2-technology-stack)
3. [Project Structure](#3-project-structure)
4. [Architecture](#4-architecture)
5. [API Endpoints](#5-api-endpoints)
6. [Database Schema](#6-database-schema)
7. [Configuration Reference](#7-configuration-reference)
8. [Running Locally (Development)](#8-running-locally-development)
9. [Docker Deployment](#9-docker-deployment)
10. [Testing](#10-testing)
11. [Linux Compatibility Report](#11-linux-compatibility-report)
12. [Production Considerations](#12-production-considerations)
13. [Systemd Deployment (bare-metal / VM)](#13-systemd-deployment-bare-metal--vm)

---

## 1. POC Objective

The primary goal of this project is to **prove that the existing `TDESEncrypt.dll` encryption
dependency can run inside a Linux Docker container** on .NET 10, without requiring Wine, Windows
compatibility layers, or any Windows-native runtime.

This matters because the organisation currently runs this DLL on Windows. A planned migration to
Linux-hosted containers (AKS / ECS) requires certainty that the encryption logic will not break.
This POC produces that evidence through automated tests and a self-test that fires on every
container start.

### Success Criteria

| Criterion | How It Is Proved | Result |
|---|---|---|
| TDESEncrypt.dll loads on Linux | Service constructor runs on startup; throws if load fails | ✅ PASS |
| Encrypt → Decrypt round-trip correct on Linux | Startup self-test + 6 Theory tests + live endpoint | ✅ PASS |
| All 36 tests pass inside Docker | Multi-stage Dockerfile runs `dotnet test` in build stage | ✅ PASS |
| API serves requests on Linux | Docker Compose starts API + SQL Server | ✅ PASS |
| System.Configuration.ConfigurationManager loads on Linux | Startup probe with log output | ✅ PASS |
| No Windows-only NuGet packages | Build succeeds with `linux-x64` RID | ✅ PASS |

> **Confirmed result:** All criteria passed on Ubuntu 24.04.4 LTS, .NET 10.0.10.
> The log line `TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Ubuntu 24.04.4 LTS.`
> was produced inside the Docker build stage, and 36 / 36 tests passed.

---

## 2. Technology Stack

### Runtime

| Component | Version | Notes |
|---|---|---|
| .NET SDK / Runtime | 10.0 | Target framework `net10.0` |
| ASP.NET Core Minimal APIs | 10.0 | No MVC controllers; endpoints mapped via extension methods |
| SQL Server | 2022 (Linux) | `mcr.microsoft.com/mssql/server:2022-latest` |
| Docker base image (runtime) | aspnet:10.0 | Ubuntu Chiseled — minimal, rootless, no shell utilities |
| Docker base image (build) | sdk:10.0 | Full SDK for restore / build / test / publish |

### NuGet Packages — `CustomerKyc.Api`

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.8 | Built-in .NET 10 OpenAPI document generation (replaces Swashbuckle) |
| `Scalar.AspNetCore` | 2.6.0 | Browser UI for the OpenAPI document at `/scalar/v1` |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.8 | JWT Bearer middleware |
| `System.IdentityModel.Tokens.Jwt` | 8.18.0 | Token generation |
| `Microsoft.Data.SqlClient` | 7.0.1 | SQL Server ADO.NET driver (Linux-native SNI) |
| `Dapper` | 2.1.79 | Lightweight SQL micro-ORM |
| `AutoMapper` | 16.1.1 | Entity → DTO mapping. ⚠️ Requires commercial license in production |
| `FluentValidation` | 12.1.1 | Request validation with rules DSL |
| `FluentValidation.DependencyInjectionExtensions` | 12.1.1 | DI registration via `AddValidatorsFromAssemblyContaining` |
| `AspNetCoreRateLimit` | 5.0.0 | IP-based rate limiting middleware (100 req/min default) |
| `System.Configuration.ConfigurationManager` | 10.0.8 | Legacy config API — Linux compatibility test target |

### NuGet Packages — Test project

| Package | Version | Purpose |
|---|---|---|
| `xunit` | 2.9.x | Test framework |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.8 | `WebApplicationFactory` for integration tests |
| `Moq` | 4.20.x | Mocking (`IDbConnectionFactory`) |
| `FluentAssertions` | 7.x | Readable test assertions |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.8 | `NullLogger` for unit tests |

> **⚠️ .NET 10 OpenAPI note:** This project does _not_ use Swashbuckle.
> `Microsoft.AspNetCore.OpenApi` is the built-in replacement in .NET 10 and ships as part of the
> shared framework. Scalar provides the browser UI. The `Microsoft.OpenApi 2.x` package (pulled
> transitively) moved all model types out of the `Microsoft.OpenApi.Models` sub-namespace; they
> now live directly under `Microsoft.OpenApi`.

---

## 3. Project Structure

```
CustomerKyc.Poc/
├── CustomerKyc.Poc.slnx          ← .NET 10 creates .slnx (XML), not legacy .sln
├── Dockerfile                     ← Multi-stage: build + test + publish + runtime
├── docker-compose.yml             ← API + SQL Server + schema-init container
│
├── database/
│   └── schema.sql                 ← CREATE DATABASE + CREATE TABLE (idempotent)
│
├── src/
│   ├── TDESEncrypt/               ← The DLL under test (managed .NET assembly)
│   │   ├── TDESEncrypt.csproj
│   │   └── TDesEncryptor.cs       ← TripleDES ECB PKCS7, SHA-256 key derivation
│   │
│   └── CustomerKyc.Api/
│       ├── CustomerKyc.Api.csproj
│       ├── Program.cs             ← All DI registrations + middleware pipeline
│       ├── appsettings.json       ← Connection string, JWT, rate-limit config
│       ├── appsettings.Development.json
│       ├── Authentication/
│       │   ├── IJwtTokenGenerator.cs
│       │   └── JwtTokenGenerator.cs   ← HMAC-SHA256, 1-hour expiry
│       ├── Data/
│       │   ├── IDbConnectionFactory.cs
│       │   └── SqlConnectionFactory.cs
│       ├── DTOs/                      ← Request/response shapes (no domain logic)
│       ├── Encryption/
│       │   ├── ITdesEncryptionService.cs
│       │   └── TdesEncryptionService.cs   ← Adapter over TDESEncrypt.dll + startup self-test
│       ├── Endpoints/
│       │   ├── AuthEndpoints.cs
│       │   ├── CustomerEndpoints.cs
│       │   ├── EncryptionTestEndpoints.cs
│       │   └── HealthEndpoints.cs
│       ├── Mapping/
│       │   └── CustomerKycProfile.cs      ← AutoMapper: Entity → DTO (Pan/Aadhaar ignored)
│       ├── Models/
│       │   └── CustomerKycEntity.cs
│       ├── OpenApi/
│       │   └── BearerSecuritySchemeTransformer.cs
│       ├── Repositories/
│       │   ├── ICustomerKycRepository.cs
│       │   └── CustomerKycRepository.cs   ← Dapper SQL, parameterised queries
│       ├── Services/
│       │   ├── ICustomerKycService.cs
│       │   └── CustomerKycService.cs      ← Encrypts on write, decrypts on read
│       └── Validators/
│           └── CustomerKycRequestValidator.cs  ← PAN regex, Aadhaar regex, length rules
│
└── tests/
    └── CustomerKyc.Api.Tests/
        ├── Encryption/
        │   └── TdesEncryptionServiceTests.cs  ← Primary Linux proof: 10 tests
        ├── Mapping/
        │   └── CustomerKycProfileTests.cs     ← AutoMapper 16.x (NullLoggerFactory)
        ├── Validators/
        │   └── CustomerKycRequestValidatorTests.cs  ← PAN/Aadhaar rules: 16 tests
        └── Integration/
            └── ApiIntegrationTests.cs         ← WebApplicationFactory, real TDES, mock repo
```

---

## 4. Architecture

### Request flow — Create KYC Record

```
HTTP Client
  └─▶  IpRateLimitMiddleware        (AspNetCoreRateLimit 5.0.0)
        └─▶  JwtBearerMiddleware    (validates token: HMAC-SHA256)
              └─▶  CustomerEndpoints.MapPost
                    └─▶  FluentValidation (PAN regex, Aadhaar length)
                          └─▶  CustomerKycService.CreateAsync
                                ├─▶  TdesEncryptionService.Encrypt(PAN)
                                │     └─▶  TDESEncrypt.TDesEncryptor   ← the DLL under test
                                ├─▶  TdesEncryptionService.Encrypt(Aadhaar)
                                └─▶  CustomerKycRepository.InsertAsync (Dapper → SQL Server)
```

### Key design decisions

| Decision | Rationale |
|---|---|
| TDESEncrypt is a managed .NET assembly | Uses `System.Security.Cryptography.TripleDES` — no P/Invoke, no COM, no Windows registry. Loads identically on Linux and Windows. |
| Startup self-test in TdesEncryptionService constructor | If the DLL fails to load or the round-trip fails, the application throws at startup and never accepts traffic. Failure is loud and immediate. |
| AutoMapper maps non-sensitive fields only | PAN and Aadhaar are always explicitly decrypted in the service layer. They are `Ignore()`d in the mapping profile so decryption can never be accidentally skipped by AutoMapper. |
| Integration tests use in-memory fake repository | Removes SQL Server as a test dependency. The real `TdesEncryptionService` (and therefore the real DLL) is always used — it is not mocked. |
| Built-in OpenAPI instead of Swashbuckle | Swashbuckle pulled `Microsoft.OpenApi 2.4.1` which restructured its namespaces. The built-in `Microsoft.AspNetCore.OpenApi 10.0.8` + Scalar 2.6.0 requires zero compat shims. |
| Environment variables override appsettings.json | Same Docker image can be promoted from dev → test → prod without rebuilding. ASP.NET Core maps `Jwt__Secret` → `Jwt:Secret` automatically. |

---

## 5. API Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` | None | Returns runtime OS, .NET version, `isLinux`, `isDocker` flags. |
| `POST` | `/api/auth/token` | None | Returns a 1-hour JWT Bearer token. POC credentials: `poc-user` / `poc-password`. |
| `POST` | `/api/customers` | Bearer | Create a KYC record. PAN and Aadhaar are validated then encrypted via TDESEncrypt.dll before being stored. Returns HTTP 201 with new ID. |
| `GET` | `/api/customers/{id}` | Bearer | Fetch record by ID. PAN and Aadhaar are returned _decrypted_ (POC-only — to prove TDES round-trip). Returns 404 if not found. |
| `POST` | `/api/encryption/test` | Bearer | Encrypt → Decrypt round-trip test. Returns ciphertext, decrypted value, `success` flag, runtime, and platform. Primary live Linux proof endpoint. |
| `GET` | `/openapi/v1.json` | None | OpenAPI 3.x JSON document (generated by built-in .NET 10 provider). |
| `GET` | `/scalar/v1` | None | Scalar browser UI — interactive API explorer with Bearer token support. |

### Request / Response shapes

**POST /api/auth/token — Request**
```json
{
  "username": "poc-user",
  "password": "poc-password"
}
```

**POST /api/auth/token — Response 200**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresAt": "2026-08-11T15:00:00Z"
}
```

**POST /api/customers — Request**
```json
{
  "customerReference": "CUST-10001",
  "firstName": "John",
  "lastName": "Doe",
  "pan": "ABCDE1234F",
  "aadhaar": "111122223333"
}
```

**POST /api/encryption/test — Request**
```json
{ "value": "Linux-TDES-Test-123" }
```

**POST /api/encryption/test — Response 200**
```json
{
  "success": true,
  "original": "Linux-TDES-Test-123",
  "encrypted": "zQYYeZEfGTdhk1Lh8Wj9aRATlIjr7chG",
  "decrypted": "Linux-TDES-Test-123",
  "error": null,
  "runtime": ".NET 10.0.10",
  "platform": "Ubuntu 24.04.4 LTS"
}
```

### Validation rules

| Field | Rule | Error |
|---|---|---|
| `customerReference` | Required, max 100 chars | HTTP 400 |
| `firstName` | Required, max 100 chars | HTTP 400 |
| `lastName` | Required, max 100 chars | HTTP 400 |
| `pan` | Required, exactly 10 chars, regex `^[A-Z]{5}[0-9]{4}[A-Z]{1}$` | HTTP 400 |
| `aadhaar` | Required, exactly 12 chars, regex `^\d{12}$` | HTTP 400 |

> **⚠️ HTTP 400, not 422:** ASP.NET Core Minimal APIs — `Results.ValidationProblem()` returns
> HTTP 400 by default. Getting 422 requires explicitly passing `statusCode: 422`. This is a
> breaking difference from controllers where model binding errors return 422.

---

## 6. Database Schema

File: `database/schema.sql` — idempotent, safe to run multiple times.

```sql
-- Creates database if it does not exist, then creates table if it does not exist.

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'CustomerKycDb')
    CREATE DATABASE CustomerKycDb;
GO

USE CustomerKycDb;
GO

CREATE TABLE dbo.CustomerKyc
(
    Id                BIGINT         IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CustomerReference NVARCHAR(100)  NOT NULL,
    FirstName         NVARCHAR(100)  NOT NULL,
    LastName          NVARCHAR(100)  NOT NULL,
    EncryptedPan      NVARCHAR(MAX)  NOT NULL,   -- TDES-encrypted Base64
    EncryptedAadhaar  NVARCHAR(MAX)  NOT NULL,   -- TDES-encrypted Base64
    Status            NVARCHAR(50)   NOT NULL DEFAULT 'Active',
    CreatedOn         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_CustomerKyc_CustomerReference ON dbo.CustomerKyc (CustomerReference);
```

PAN and Aadhaar are stored as **TDES-encrypted Base64 strings**. The plaintext values never touch
the database. Decryption only happens in `CustomerKycService.GetByIdAsync`, which calls
`TdesEncryptionService.Decrypt` explicitly.

---

## 7. Configuration Reference

Configuration follows standard ASP.NET Core layering:
`appsettings.json` → `appsettings.{Environment}.json` → Environment variables.
Environment variables use `__` (double underscore) as the section separator because `:` is not
valid in Linux env var names.

| appsettings.json key | Environment variable | Required | Description |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Yes | SQL Server connection string. Include `TrustServerCertificate=true` for dev. |
| `Jwt:Secret` | `Jwt__Secret` | Yes | HMAC-SHA256 signing key. Minimum 32 characters. Keep secret. |
| `Jwt:Issuer` | `Jwt__Issuer` | Yes | Token issuer claim. Default: `CustomerKycApi`. |
| `Jwt:Audience` | `Jwt__Audience` | Yes | Token audience claim. Default: `CustomerKycApiUsers`. |
| `Encryption:Key` | `Encryption__Key` | Yes | Passphrase fed to TDesEncryptor. SHA-256 hashed to derive the 192-bit 3DES key. Must remain constant — changing it makes all existing encrypted records unreadable. |
| `Auth:Username` | `Auth__Username` | No | POC auth username. Default: `poc-user`. |
| `Auth:Password` | `Auth__Password` | No | POC auth password. Default: `poc-password`. |
| `IpRateLimiting:GeneralRules[0]:Limit` | — | No | Requests per period. Default: 100 per minute. HTTP 429 when exceeded. |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | No | `Development` enables detailed error pages and debug logging. Set `Production` in Docker. |
| `ASPNETCORE_URLS` | `ASPNETCORE_URLS` | No | Listening address inside the container. Set to `http://+:8080`. Port 8080 is the convention for the official .NET Docker images. |

> **⚠️ Never commit real secrets to source control.** The `Jwt:Secret` and `Encryption:Key` values
> in `appsettings.json` are placeholder strings (`REPLACE_WITH_SECURE_SECRET_...`). Always supply
> real values via environment variables in deployment, not in config files.

---

## 8. Running Locally (Development)

### Prerequisites

- .NET 10 SDK — `dotnet --version` must show `10.x.x`
- SQL Server (local instance, Docker, or Azure SQL)
- Connection string pointing to your local SQL Server in `appsettings.Development.json`

### Steps

**1. Clone and restore packages**

Restores all NuGet packages defined in the solution.

```bash
cd CustomerKyc.Poc
dotnet restore
```

**2. Apply the database schema**

Run the idempotent schema script against your local SQL Server. It creates `CustomerKycDb` and
the `CustomerKyc` table if they don't exist.

```bash
sqlcmd -S localhost -U sa -P "YourPassword" -i database/schema.sql
```

**3. Set development secrets (optional but recommended)**

Avoids committing secrets to disk. ASP.NET Core loads `secrets.json` in Development automatically.

```bash
dotnet user-secrets set "Jwt:Secret" "your-dev-secret-32chars!!" --project src/CustomerKyc.Api
dotnet user-secrets set "Encryption:Key" "your-dev-encryption-key-32c!!" --project src/CustomerKyc.Api
```

**4. Run the API**

The app starts in Development mode, picks up `appsettings.Development.json`, and listens on the
URLs defined in `launchSettings.json`.

```bash
dotnet run --project src/CustomerKyc.Api
```

Default URLs: `https://localhost:49886` and `http://localhost:49887`

**5. Open the Scalar UI**

Navigate to `http://localhost:49887/scalar/v1` in your browser to explore and test endpoints
interactively.

---

## 9. Docker Deployment

Docker Compose starts three services: `sqlserver`, `sqlserver-init` (one-shot schema runner), and
`customer-kyc-api`. The API container depends on `sqlserver` being healthy before it starts.

> **Prerequisite:** Docker Desktop (Windows/Mac) or Docker Engine + Compose plugin (Linux) must be
> installed and running. The build requires internet access to pull base images and NuGet packages
> on the first run.

### Step 1 — Navigate to the solution root

All Docker Compose commands must be run from the directory containing `docker-compose.yml`.

```bash
cd d:\POC\CustomerKyc.Poc
```

### Step 2 — Build the Docker image

This runs the multi-stage build: restore → compile → run all 36 tests on Linux → publish. The
image is only created if all tests pass. Takes 3–6 minutes on first run (NuGet cache is cold).

```bash
docker build -t customer-kyc-poc:latest .
```

What happens inside the build:

- **Stage 1 (build):** Uses `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 22.04)
- Copies `.slnx` and all `.csproj` files first → restores NuGet (layer cached on subsequent builds)
- Copies source → compiles in Release mode
- Runs `dotnet test` — all 36 tests must pass or the build fails and no image is produced
- Runs `dotnet publish` → outputs to `/app/publish`
- **Stage 2 (runtime):** Uses `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu Chiseled — minimal)
- Copies publish output into runtime image
- Asserts `TDESEncrypt.dll` is present in the image (`test -f TDESEncrypt.dll`)
- Switches to the pre-created `app` user (UID 1654) — no root at runtime

### Step 3 — Start all services with Docker Compose

Starts SQL Server and the API in the background. SQL Server takes ~30 seconds to initialise before
the API container is allowed to start.

```bash
docker compose up -d
```

Services started:
- `customer-kyc-sqlserver` — SQL Server 2022 on port 1433
- `customer-kyc-sqlserver-init` — runs `schema.sql` once SQL Server is healthy, then exits
- `customer-kyc-api` — the .NET 10 API on port 5000 (mapped to container port 8080)

### Step 4 — Verify the containers are up

All three should show `Up` or `Exit 0` (for the init container).

```bash
docker compose ps
```

### Step 5 — Watch the API startup logs

Confirms Linux environment and TDESEncrypt.dll self-test result. Press Ctrl+C to stop following.

```bash
docker compose logs -f customer-kyc-api
```

Expected output:

```
 Runtime : .NET 10.0.10
 OS      : Ubuntu 24.04.4 LTS
 Linux   : True
 Docker  : True
TDESEncrypt.dll: Loading. Runtime=.NET 10.0.10 OS=Ubuntu 24.04.4 LTS IsLinux=True IsDocker=True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Ubuntu 24.04.4 LTS.
System.Configuration.ConfigurationManager: LOADED OK.
Now listening on: http://[::]:8080
```

### Step 6 — Test the health endpoint (no auth required)

```bash
curl http://localhost:5000/health
```

Expected response:

```json
{
  "status": "Healthy",
  "runtime": ".NET 10.0.10",
  "os": "Ubuntu 24.04.4 LTS",
  "isLinux": true,
  "isDocker": true,
  "utcNow": "2026-08-11T09:00:00Z"
}
```

### Step 7 — Open the Scalar UI

Navigate to `http://localhost:5000/scalar/v1` in your browser.

### Step 8 — Obtain a JWT token

```bash
curl -s -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"poc-user","password":"poc-password"}' | jq -r .token
```

Copy the returned token. It is valid for 1 hour.

### Step 9 — Run the TDES Linux proof endpoint

Replace `TOKEN` with the token from the previous step.

```bash
curl -s -X POST http://localhost:5000/api/encryption/test \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value":"Linux-TDES-Test-123"}' | jq .
```

A successful response will show `"success": true`, the ciphertext, and `"platform": "Ubuntu 24.04.4 LTS"`.

### Step 10 — Create a KYC record

PAN and Aadhaar will be encrypted via TDESEncrypt.dll and stored in SQL Server.

```bash
curl -s -X POST http://localhost:5000/api/customers \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"customerReference":"CUST-10001","firstName":"John","lastName":"Doe","pan":"ABCDE1234F","aadhaar":"111122223333"}' | jq .
```

### Step 11 — Retrieve the record (verify TDES decrypt round-trip)

Replace `1` with the ID returned in the previous step.

```bash
curl -s http://localhost:5000/api/customers/1 \
  -H "Authorization: Bearer TOKEN" | jq .
```

The response will contain the original (decrypted) PAN and Aadhaar values, confirming the full
end-to-end round-trip via SQL Server.

### Step 12 — Tear down when finished

Stops and removes containers. The `sqlserver-data` volume is preserved. Add `-v` to also delete
the volume.

```bash
docker compose down
```

### Dockerfile — annotated walkthrough

```dockerfile
# ── Stage 1: Build & Test ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Full SDK image on Ubuntu 22.04. Has dotnet, bash, curl.

WORKDIR /source

# Copy project files BEFORE source code.
# Docker layer-caches the restore step: if no .csproj changed, restore is skipped.
COPY src/TDESEncrypt/TDESEncrypt.csproj                       src/TDESEncrypt/
COPY src/CustomerKyc.Api/CustomerKyc.Api.csproj               src/CustomerKyc.Api/
COPY tests/CustomerKyc.Api.Tests/CustomerKyc.Api.Tests.csproj tests/CustomerKyc.Api.Tests/
COPY CustomerKyc.Poc.slnx .
# NOTE: .NET 10 dotnet new sln creates .slnx (new XML format). NOT .sln.

RUN dotnet restore

COPY src/   src/
COPY tests/ tests/

RUN dotnet build -c Release --no-restore

# Tests run inside Linux Docker. This is the primary Linux compatibility proof.
RUN dotnet test tests/CustomerKyc.Api.Tests/CustomerKyc.Api.Tests.csproj \
    --no-build -c Release \
    --logger "console;verbosity=normal"

RUN dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    -c Release --no-build -o /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Ubuntu Chiseled: minimal, no shell, no adduser, no apt.
# Non-root user "app" (UID 1654) is pre-created in this image.

WORKDIR /app
COPY --from=build /app/publish .

# Build-time assertion: TDESEncrypt.dll must be present in the publish output.
RUN test -f TDESEncrypt.dll && echo "TDESEncrypt.dll: present in image" \
    || (echo "TDESEncrypt.dll: MISSING" && exit 1)

USER app   # Do NOT use adduser — it doesn't exist in Ubuntu Chiseled.

EXPOSE 8080
ENTRYPOINT ["dotnet", "CustomerKyc.Api.dll"]
```

### docker-compose.yml — service details

| Service | Image | Port | Health check |
|---|---|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 → 1433 | `sqlcmd SELECT 1` every 10s, 10 retries, 30s start period |
| `sqlserver-init` | same SQL Server image | — | Runs `schema.sql` once, then exits (`restart: "no"`) |
| `customer-kyc-api` | `customer-kyc-poc:latest` | 5000 → 8080 | `curl /health` every 15s, 5 retries, 20s start period |

---

## 10. Testing

### Running tests locally

```bash
dotnet test tests/CustomerKyc.Api.Tests/ --logger "console;verbosity=normal"
```

### Test suite breakdown — 36 tests total

| Test class | Count | What it tests |
|---|---|---|
| `TdesEncryptionServiceTests` | 10 | TDESEncrypt.dll loads on current platform (construction self-test) · 6 Theory cases: round-trip for PAN-like, Aadhaar-like, short, long strings · ECB determinism (same input → same output) · Different inputs → different outputs · Encrypted value is valid Base64 |
| `CustomerKycRequestValidatorTests` | 16 | Valid request passes · Empty/whitespace CustomerReference fails · CustomerReference > 100 chars fails · 4 invalid PAN formats fail · Valid PAN passes · 3 invalid Aadhaar formats fail · Valid Aadhaar passes · Empty FirstName/LastName fail |
| `CustomerKycProfileTests` | 2 | AutoMapper 16.x configuration valid (`AssertConfigurationIsValid()`) · Entity → DTO mapping correct; Pan/Aadhaar left empty by mapper (decrypted separately) |
| `ApiIntegrationTests` | 8 | Health endpoint → 200 · Auth with valid credentials → token · Auth with wrong credentials → 401 · Unauthenticated POST /customers → 401 · Unauthenticated POST /encryption/test → 401 · Encryption round-trip test with real TDES DLL → success = true · Invalid customer request → 400 · Valid customer creation → 201 |

### Integration test design

Integration tests use `WebApplicationFactory<Program>` (requires `public partial class Program {}`
at the bottom of `Program.cs`). The test factory replaces two services:

- `ICustomerKycRepository` → `InMemoryCustomerKycRepository` (thread-safe dictionary)
- `IDbConnectionFactory` → `Mock<IDbConnectionFactory>` (prevents SQL connection on startup)

`TdesEncryptionService` is **not replaced**. The real `TDESEncrypt.dll` runs during every
integration test run.

> **⚠️ AutoMapper 16.x test requirement:** `MapperConfiguration` now requires `ILoggerFactory` as
> its second constructor argument. Use `NullLoggerFactory.Instance` in tests. Omitting it causes
> `CS1729` at compile time.

---

## 11. Linux Compatibility Report

The following was collected from the Docker multi-stage build output running on the
`mcr.microsoft.com/dotnet/sdk:10.0` image (Ubuntu 22.04 based). All tests ran inside the Linux
container; no Windows host was involved.

| Component | Details | Verdict |
|---|---|---|
| TDESEncrypt.dll | Managed .NET 10 assembly · TripleDES ECB PKCS7 · SHA-256 key derivation | ✅ LINUX COMPATIBLE |
| System.Configuration.ConfigurationManager | Version 10.0.8 · Reads from `*.dll.config` | ✅ LINUX COMPATIBLE |
| Microsoft.Data.SqlClient | Version 7.0.1 · Includes `linux-x64` SNI native lib | ✅ LINUX COMPATIBLE |
| AspNetCoreRateLimit | Version 5.0.0 · IMemoryCache only, no native deps | ✅ LINUX COMPATIBLE |
| AutoMapper | Version 16.1.1 · Fully managed | ⚠️ COMPATIBLE but COMMERCIAL |
| JWT / OpenAPI / FluentValidation | All fully managed .NET libraries | ✅ LINUX COMPATIBLE |

### Confirmed build output (actual evidence)

```
Runtime : .NET 10.0.10
OS      : Ubuntu 24.04.4 LTS
Linux   : True
Docker  : True
TDESEncrypt.dll: Loading. Runtime=.NET 10.0.10 OS=Ubuntu 24.04.4 LTS IsLinux=True IsDocker=True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Ubuntu 24.04.4 LTS.
Encryption test: original=Linux-TDES-Test-123 encrypted=zQYYeZEfGTdhk1Lh8Wj9aRATlIjr7chG decrypted=Linux-TDES-Test-123 success=True
System.Configuration.ConfigurationManager: LOADED OK.
TDESEncrypt.dll: present in image
Total tests: 36 / Passed: 36
Image customer-kyc-poc:latest Built ✔
```

### Why TDESEncrypt.dll works on Linux

The DLL is a **managed .NET assembly** — it contains only IL (Intermediate Language) bytecode that
the .NET 10 runtime JIT-compiles on any supported platform. It uses:

- `System.Security.Cryptography.TripleDES` — cross-platform cryptography built into .NET
- `System.Security.Cryptography.SHA256` — for key derivation
- No P/Invoke, no `DllImport`, no COM, no Windows registry access

> If the real production `TDESEncrypt.dll` is instead a **native Windows DLL** (C++, COM, or
> .NET Framework with Windows-native calls), it will not load on Linux and the startup self-test
> will throw immediately, surfacing the exact failure mode. The fix in that case would be to port
> the encryption logic to a managed .NET library, as done here.

---

## 12. Production Considerations

| Topic | POC approach | Production recommendation |
|---|---|---|
| AutoMapper license | AutoMapper 16.x (Lucky Penny Software) works but logs a warning: _"You do not have a valid license key."_ Dev/test use is permitted free of charge. | Either purchase a license from luckypennysoftware.com, or pin to **AutoMapper 13.x** which is MIT-licensed and free. |
| Authentication | Single hardcoded username/password via config. JWT tokens expire in 1 hour. | Replace with proper identity provider (Azure AD / Okta / Keycloak). Use refresh tokens. |
| Secrets management | Secrets passed via environment variables in `docker-compose.yml` (plain text). | Use Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault. Never store secrets in compose files. |
| Encryption algorithm | 3DES ECB — preserved as-is to match the legacy DLL behaviour. | ECB mode is deterministic (same plaintext → same ciphertext). Use AES-256-GCM for new data. 3DES key length (168-bit effective) is considered weak for new systems. |
| PII in responses | `GET /api/customers/{id}` returns decrypted PAN and Aadhaar to prove round-trip. | Never return decrypted PAN/Aadhaar in API responses. Return masked values only. Full decryption should happen server-side only where needed. |
| HTTPS | HTTP only inside Docker (port 8080). TLS termination expected upstream. | Terminate TLS at the load balancer or ingress controller (nginx, AGIC). Do not expose HTTP to the public internet. |
| Rate limiting | AspNetCoreRateLimit with in-memory counter store (resets on restart). | For multi-instance deployments, use a distributed counter store (Redis). Consider Azure API Management or AWS WAF for gateway-level limiting. |
| Database connection string | SA credentials with `TrustServerCertificate=true`. | Use a dedicated least-privilege SQL login. Enable encrypted connections. Use connection pooling and retry policies. |
| Health checks | Returns static JSON from `/health`. No DB probe. | Add a SQL Server probe to the health check. Register with Kubernetes liveness and readiness probes. |
| Observability | Console logging only. | Add OpenTelemetry tracing + structured logging to a sink (Application Insights, Grafana Loki, ELK). |

---

## 13. Systemd Deployment (bare-metal / VM)

If you need to run the API directly on a Linux host (without Docker), use systemd to manage the
process lifecycle. This also proves Linux compatibility outside of a container.

### Prerequisites

- .NET 10 Runtime installed: `sudo apt install -y dotnet-runtime-10.0`
- SQL Server accessible from the host
- Published output deployed to `/opt/customer-kyc-api/`

### Step 1 — Publish the application for linux-x64

```bash
dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
  -c Release -r linux-x64 --self-contained false \
  -o /opt/customer-kyc-api
```

### Step 2 — Create a dedicated system user

Never run .NET services as root.

```bash
sudo useradd -r -s /bin/false kyc-api
```

### Step 3 — Set ownership

```bash
sudo chown -R kyc-api:kyc-api /opt/customer-kyc-api
```

### Step 4 — Create the systemd unit file

```bash
sudo nano /etc/systemd/system/customer-kyc-api.service
```

```ini
[Unit]
Description=Customer KYC API (.NET 10)
After=network.target

[Service]
Type=notify
User=kyc-api
WorkingDirectory=/opt/customer-kyc-api
ExecStart=/usr/bin/dotnet /opt/customer-kyc-api/CustomerKyc.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT

# Environment variables — use EnvironmentFile for secrets in production
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000
Environment=Jwt__Issuer=CustomerKycApi
Environment=Jwt__Audience=CustomerKycApiUsers
# DO NOT put real secrets here — use EnvironmentFile=/etc/kyc-api/secrets.env
Environment=Jwt__Secret=REPLACE_WITH_REAL_SECRET
Environment=Encryption__Key=REPLACE_WITH_REAL_KEY
Environment=ConnectionStrings__DefaultConnection=Server=localhost;Database=CustomerKycDb;User Id=sa;Password=REPLACE;TrustServerCertificate=true

# Hardening
PrivateTmp=true
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/customer-kyc-api

[Install]
WantedBy=multi-user.target
```

### Step 5 — Enable and start the service

```bash
sudo systemctl daemon-reload
sudo systemctl enable customer-kyc-api
sudo systemctl start customer-kyc-api
```

### Step 6 — Check status and logs

```bash
sudo systemctl status customer-kyc-api
sudo journalctl -u customer-kyc-api -f
```

Look for the same startup banner and `TDESEncrypt.dll: Self-test PASSED` lines in the journal.

### Step 7 — Test the health endpoint

```bash
curl http://localhost:5000/health
```

> **⚠️ Secrets in unit files:** Environment variables in `[Service]` sections are readable by any
> user who can run `systemctl show`. Put real secrets in a file owned by root with mode 0600, and
> reference it with `EnvironmentFile=/etc/kyc-api/secrets.env`. Never commit that file to source
> control.

---

*Customer KYC API POC · .NET 10 · Ubuntu 24.04.4 LTS · All 36 tests passing*
