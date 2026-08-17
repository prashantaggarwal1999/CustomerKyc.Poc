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
14. [GitHub Actions CI/CD](#14-github-actions-cicd)
15. [Manual Deployment on RHEL 9.8 Without Docker](#15-manual-deployment-on-rhel-98-without-docker)
16. [Verify Existing SQL Server Connectivity](#16-verify-existing-sql-server-connectivity)
17. [Deploy from Windows to RHEL 9.8 — Publish Locally and Copy Manually](#17-deploy-from-windows-to-rhel-98--publish-locally-and-copy-manually)

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
| Docker base image (build) | `ubi9/ubi:9.8` + Microsoft RPM | Red Hat UBI 9.8 full image — dnf, bash, curl available. .NET SDK 10.0 installed from Microsoft's RHEL 9 package feed. |
| Docker base image (runtime) | `ubi9/ubi:9.8` + Microsoft RPM | Red Hat UBI 9.8 full image — retains bash/curl. ASP.NET Core Runtime 10.0 installed from Microsoft's RHEL 9 package feed. |

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

- **Stage 1 (build):** Uses `registry.access.redhat.com/ubi9/ubi:9.8` (Red Hat UBI 9.8 — full image with dnf and all standard RHEL 9 tools)
- Adds Microsoft's official RPM package repository for RHEL 9 via `packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm`
- Installs `dotnet-sdk-10.0` from that repository
- Copies `.slnx` and all `.csproj` files first → restores NuGet (layer cached on subsequent builds)
- Copies source → compiles in Release mode
- Runs `dotnet test` — all 36 tests must pass or the build fails and no image is produced
- Runs `dotnet publish` → outputs to `/app/publish`
- **Stage 2 (runtime):** Uses `registry.access.redhat.com/ubi9/ubi:9.8` — same UBI 9.8 base
- Adds Microsoft's RPM repo and installs `aspnetcore-runtime-10.0` (runtime only, not full SDK)
- Creates a non-root `app` user (UID 1654) to match the convention in official Microsoft .NET images
- Copies publish output into runtime image
- Asserts `TDESEncrypt.dll` is present in the image (`test -f TDESEncrypt.dll`)
- Switches to the `app` user — no root at runtime

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
 OS      : Red Hat Enterprise Linux 9.8 (Plow)
 Linux   : True
 Docker  : True
TDESEncrypt.dll: Loading. Runtime=.NET 10.0.10 OS=Red Hat Enterprise Linux 9.8 (Plow) IsLinux=True IsDocker=True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
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
  "os": "Red Hat Enterprise Linux 9.8 (Plow)",
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

A successful response will show `"success": true`, the ciphertext, and `"platform": "Red Hat Enterprise Linux 9.8 (Plow)"`.

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

> **Why not `mcr.microsoft.com/dotnet/sdk:10.0-rhel.9`?**
> Microsoft does not publish RHEL-flavoured tags on MCR. Their MCR images are Ubuntu (default)
> or Alpine. For RHEL 9.8, the correct approach is to start from Red Hat's own UBI 9 base image
> and install .NET from Microsoft's official RHEL 9 RPM package feed.

```dockerfile
# ── Stage 1: Build & Test ────────────────────────────────────────────────
# Full UBI 9.8 — has dnf, bash, curl, useradd, all standard RHEL 9 tools.
FROM registry.access.redhat.com/ubi9/ubi:9.8 AS build

# Add Microsoft's RPM repo for RHEL 9, then install the full .NET SDK 10.0.
# This is the officially documented installation method for RHEL 9.
# packages-microsoft-prod.rpm adds the Microsoft package repository.
RUN dnf install -y --allowerasing curl \
    && dnf install -y \
        https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm \
    && dnf install -y dotnet-sdk-10.0 \
    && dnf clean all \
    && rm -rf /var/cache/dnf

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

# Tests run inside RHEL 9.8. This is the primary RHEL compatibility proof.
# Any failure exits non-zero — the Docker build fails and no image is produced.
RUN dotnet test tests/CustomerKyc.Api.Tests/CustomerKyc.Api.Tests.csproj \
    --no-build -c Release \
    --logger "console;verbosity=normal"

RUN dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    -c Release --no-build -o /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────────────────
# Full UBI 9.8 — retains bash, curl, standard shell utilities.
# Install only the ASP.NET Core runtime (not the full SDK).
FROM registry.access.redhat.com/ubi9/ubi:9.8 AS runtime

RUN dnf install -y --allowerasing curl \
    && dnf install -y \
        https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm \
    && dnf install -y aspnetcore-runtime-10.0 \
    && dnf clean all \
    && rm -rf /var/cache/dnf

# Create non-root service account — UID 1654 matches the convention in
# official Microsoft .NET images so tooling referencing that UID works consistently.
RUN useradd --system --uid 1654 --gid 0 --no-create-home app

WORKDIR /app
COPY --from=build /app/publish .

# Build-time assertion: TDESEncrypt.dll must be present in the publish output.
RUN test -f TDESEncrypt.dll && echo "TDESEncrypt.dll: present in image" \
    || (echo "TDESEncrypt.dll: MISSING" && exit 1)

USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "CustomerKyc.Api.dll"]
```

### docker-compose.yml — service details

| Service | Image | Port | Health check |
|---|---|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 → 1433 | `sqlcmd SELECT 1` every 10s, 10 retries, 30s start period |
| `sqlserver-init` | same SQL Server image | — | Runs `schema.sql` once, then exits (`restart: "no"`) |
| `customer-kyc-api` | `customer-kyc-poc:latest` (UBI 9.8) | 5000 → 8080 | `curl /health` every 15s, 5 retries, 20s start period |

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
`mcr.microsoft.com/dotnet/sdk:10.0-rhel.9` image (Red Hat UBI 9 / RHEL 9.8-compatible).
All tests ran inside the Linux container; no Windows host was involved.

| Component | Details | Verdict |
|---|---|---|
| TDESEncrypt.dll | Managed .NET 10 assembly · TripleDES ECB PKCS7 · SHA-256 key derivation | ✅ RHEL 9 COMPATIBLE |
| System.Configuration.ConfigurationManager | Version 10.0.8 · Reads from `*.dll.config` | ✅ RHEL 9 COMPATIBLE |
| Microsoft.Data.SqlClient | Version 7.0.1 · Includes `linux-x64` SNI native lib | ✅ RHEL 9 COMPATIBLE |
| AspNetCoreRateLimit | Version 5.0.0 · IMemoryCache only, no native deps | ✅ RHEL 9 COMPATIBLE |
| AutoMapper | Version 16.1.1 · Fully managed | ⚠️ COMPATIBLE but COMMERCIAL |
| JWT / OpenAPI / FluentValidation | All fully managed .NET libraries | ✅ RHEL 9 COMPATIBLE |

### Confirmed build output (Ubuntu — original baseline evidence)

The initial compatibility was verified on Ubuntu 24.04.4 LTS (before switching to RHEL).
After switching to `10.0-rhel.9` images, the startup banner will show RHEL:

```
Runtime : .NET 10.0.10
OS      : Red Hat Enterprise Linux 9.8 (Plow)
Linux   : True
Docker  : True
TDESEncrypt.dll: Loading. Runtime=.NET 10.0.10 OS=Red Hat Enterprise Linux 9.8 (Plow) IsLinux=True IsDocker=True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
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

## 14. GitHub Actions CI/CD

File: `.github/workflows/ci.yml`

The pipeline has two jobs:

| Job | Trigger | What it does |
|---|---|---|
| `build-and-test` | Every push and PR | Runs the full multi-stage Docker build on RHEL 9 UBI. All 36 tests execute inside the container and stream to the Actions log. |
| `push-to-ghcr` | Push to `main` only (after `build-and-test` passes) | Pushes the verified image to GitHub Container Registry (`ghcr.io`). |

### How test output appears in Actions

The build uses `--progress=plain` which makes Docker print every layer's stdout to the Actions
log. Because `dotnet test` runs inside the build stage, all test results are visible directly in
the log — no separate test step or artifact upload is needed.

Look for this in the `Build Docker image` step log:

```
#12 [build 7/8] RUN dotnet test ...
#12 0.512 Starting test execution, please wait...
#12 2.301 A total of 1 test files matched the specified pattern.
#12 ...
#12 Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36
```

### Smoke-test step

After the build, the workflow starts the image without SQL Server and hits `GET /health`. The
container logs are printed to the Actions log and will show:

```
OS      : Red Hat Enterprise Linux 9.8 (Plow)
Linux   : True
Docker  : True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
```

This is the live in-CI proof that the DLL works on RHEL 9.

### Setup steps

**1. No secrets required for CI builds and GHCR pushes within the same repository.**
`GITHUB_TOKEN` is auto-provided. The `packages: write` permission is declared in the workflow.

**2. Verify Actions is enabled** for your repository under
`Settings → Actions → General → Allow all actions`.

**3. After first push to `main`**, the image will be available at:
```
ghcr.io/<your-org-or-username>/customer-kyc-poc:latest
ghcr.io/<your-org-or-username>/customer-kyc-poc:sha-<short-sha>
```

**4. To pull the image** (requires a PAT with `read:packages` scope or `GITHUB_TOKEN` in Actions):
```bash
docker pull ghcr.io/<your-org-or-username>/customer-kyc-poc:latest
```

### Branch strategy

| Branch | Build runs? | Image pushed? |
|---|---|---|
| `main` | Yes | Yes — tagged `latest` + `sha-*` |
| `develop` | Yes | No |
| Any PR → `main` | Yes | No |
| Manual trigger | Yes | No (unless on `main`) |

---

## 15. Manual Deployment on RHEL 9.8 Without Docker

This section covers a complete, end-to-end installation of the API directly on a RHEL 9.8 server
— no Docker, no container runtime. Every command is shown exactly as you would run it.

**You will need two machines (or two roles on one machine):**
- **Build machine** — has the .NET 10 SDK installed; compiles and publishes the app
- **Server machine** — RHEL 9.8, receives the published output and runs the app

Both can be the same machine for a POC.

---

### Phase 1 — Prepare the RHEL 9.8 Server

Run all commands in this phase on the **server**.

#### Step 1 — Update the system

```bash
sudo dnf update -y
```

#### Step 2 — Add Microsoft's package repository for RHEL 9

Microsoft's repository provides both .NET and SQL Server packages for RHEL.

```bash
# Import Microsoft's GPG signing key
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc

# Add the Microsoft package repository for RHEL 9
sudo dnf install -y \
    https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm
```

#### Step 3 — Install the ASP.NET Core Runtime 10.0

Install only the runtime on the server — not the full SDK. The SDK is only needed on the
build machine.

```bash
sudo dnf install -y aspnetcore-runtime-10.0
```

Verify the installation:

```bash
dotnet --list-runtimes
# Expected: Microsoft.AspNetCore.App 10.0.x [/usr/lib/dotnet/shared/Microsoft.AspNetCore.App]
```

#### Step 4 — Create a dedicated service account

Never run the API as root. Create a locked-down system account.

```bash
sudo useradd --system --uid 1654 --gid 0 --no-create-home --shell /sbin/nologin kyc-api
```

#### Step 5 — Create the application directory

```bash
sudo mkdir -p /opt/customer-kyc-api
sudo chown kyc-api:root /opt/customer-kyc-api
sudo chmod 750 /opt/customer-kyc-api
```

---

### Phase 2 — Install SQL Server on RHEL 9.8

Skip this phase if you are connecting to an existing SQL Server instance (on Windows or elsewhere).

#### Step 6 — Add the SQL Server 2022 repository

```bash
sudo curl -o /etc/yum.repos.d/mssql-server.repo \
    https://packages.microsoft.com/config/rhel/9/mssql-server-2022.repo
```

#### Step 7 — Install SQL Server

```bash
sudo dnf install -y mssql-server
```

#### Step 8 — Run the SQL Server setup wizard

Sets the SA password and accepts the EULA. Choose the **Developer** edition (free for
non-production use).

```bash
sudo /opt/mssql/bin/mssql-conf setup
```

When prompted:
- Choose edition: `2` (Developer)
- Accept the EULA: `Yes`
- Set the SA password: use a strong password, e.g. `YourStrong@Password123`

#### Step 9 — Enable and start SQL Server

```bash
sudo systemctl enable mssql-server --now
sudo systemctl status mssql-server
```

Wait until the status shows `active (running)`.

#### Step 10 — Install SQL Server command-line tools

```bash
sudo curl -o /etc/yum.repos.d/msprod.repo \
    https://packages.microsoft.com/config/rhel/9/prod.repo

sudo dnf install -y mssql-tools18 unixODBC-devel

# Add sqlcmd to PATH permanently
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' | sudo tee -a /etc/profile.d/mssql-tools.sh
source /etc/profile.d/mssql-tools.sh
```

#### Step 11 — Verify SQL Server connectivity

```bash
sqlcmd -S localhost -U sa -P "YourStrong@Password123" -C -Q "SELECT @@VERSION"
```

Expected: a line beginning with `Microsoft SQL Server 2022`.

---

### Phase 3 — Build the Application

Run all commands in this phase on the **build machine** (requires .NET SDK 10.0).

#### Step 12 — Install the .NET SDK 10.0 on the build machine

If the build machine is also RHEL 9.8:

```bash
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
sudo dnf install -y \
    https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm
sudo dnf install -y dotnet-sdk-10.0
dotnet --version
# Expected: 10.0.x
```

If the build machine is Windows, .NET 10 SDK is installed from https://dot.net.

#### Step 13 — Clone or copy the source code

```bash
# If using git:
git clone <your-repo-url> /tmp/customer-kyc-src
cd /tmp/customer-kyc-src/CustomerKyc.Poc

# Or copy the source folder to the build machine by any method (SCP, shared drive, etc.)
```

#### Step 14 — Run all tests before publishing

Always run tests before deploying. The test suite proves TDESEncrypt.dll works on the current
platform.

```bash
cd /tmp/customer-kyc-src/CustomerKyc.Poc

dotnet test tests/CustomerKyc.Api.Tests/ \
    --configuration Release \
    --logger "console;verbosity=normal"
```

All 36 tests must pass. The output will include:

```
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36
```

#### Step 15 — Publish the application for linux-x64

```bash
dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /tmp/kyc-publish
```

`--self-contained false` means the publish output contains only the app DLLs — the .NET runtime
already installed on the server (Step 3) is used at runtime. This keeps the deployment package
small (~10 MB vs ~200 MB for self-contained).

#### Step 16 — Verify TDESEncrypt.dll is in the publish output

```bash
ls /tmp/kyc-publish/TDESEncrypt.dll
# Must exist — the DLL is the primary subject of this POC
```

---

### Phase 4 — Deploy to the Server

#### Step 17 — Copy the publish output to the server

**From a Linux build machine:**

```bash
scp -r /tmp/kyc-publish/* rhel-user@your-server-ip:/opt/customer-kyc-api/
```

**From a Windows build machine** (using PowerShell + SCP):

```powershell
scp -r C:\tmp\kyc-publish\* rhel-user@your-server-ip:/opt/customer-kyc-api/
```

Or use WinSCP, rsync, Ansible, or any other file transfer method.

#### Step 18 — Fix ownership on the server

After copying, make sure the service account owns all files:

```bash
sudo chown -R kyc-api:root /opt/customer-kyc-api
sudo chmod -R 750 /opt/customer-kyc-api
```

---

### Phase 5 — Apply the Database Schema

Run on the **server** (or any machine that can reach SQL Server).

#### Step 19 — Apply the schema script

The script is idempotent — it creates the database and table only if they do not already exist.

```bash
sqlcmd -S localhost -U sa -P "YourStrong@Password123" -C \
    -i /opt/customer-kyc-api/../schema.sql
```

If the schema.sql file is not on the server, copy it first:

```bash
scp /tmp/customer-kyc-src/CustomerKyc.Poc/database/schema.sql \
    rhel-user@your-server-ip:/tmp/schema.sql

# Then on the server:
sqlcmd -S localhost -U sa -P "YourStrong@Password123" -C \
    -i /tmp/schema.sql
```

Expected output:

```
Table CustomerKyc created.
```

#### Step 20 — Verify the table exists

```bash
sqlcmd -S localhost -U sa -P "YourStrong@Password123" -C \
    -Q "USE CustomerKycDb; SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CustomerKyc'"
```

---

### Phase 6 — Configure and Run the Application

#### Step 21 — Create a secrets environment file

Store real secrets in a file owned by root with mode 0600 — never in a world-readable location.

```bash
sudo mkdir -p /etc/customer-kyc-api
sudo bash -c 'cat > /etc/customer-kyc-api/secrets.env << EOF
Jwt__Secret=your-production-secret-minimum-32-characters-long!!
Encryption__Key=your-production-encryption-key-32chars!!
Auth__Username=poc-user
Auth__Password=your-strong-password
ConnectionStrings__DefaultConnection=Server=localhost;Database=CustomerKycDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=true;Encrypt=false
EOF'

# Lock down the file — only root can read it
sudo chmod 600 /etc/customer-kyc-api/secrets.env
sudo chown root:root /etc/customer-kyc-api/secrets.env
```

> **Important:** `Encryption__Key` must never change after the first record is written — changing it
> makes all encrypted PAN/Aadhaar records in the database permanently unreadable.

#### Step 22 — Create the systemd unit file

```bash
sudo bash -c 'cat > /etc/systemd/system/customer-kyc-api.service << EOF
[Unit]
Description=Customer KYC API (.NET 10 on RHEL 9.8)
After=network.target mssql-server.service
Wants=mssql-server.service

[Service]
Type=notify
User=kyc-api
WorkingDirectory=/opt/customer-kyc-api
ExecStart=/usr/bin/dotnet /opt/customer-kyc-api/CustomerKyc.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=customer-kyc-api

# Load secrets from the locked-down file
EnvironmentFile=/etc/customer-kyc-api/secrets.env

# Non-secret environment
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000
Environment=Jwt__Issuer=CustomerKycApi
Environment=Jwt__Audience=CustomerKycApiUsers
Environment=DOTNET_RUNNING_IN_CONTAINER=false

# Systemd hardening
PrivateTmp=true
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/customer-kyc-api

[Install]
WantedBy=multi-user.target
EOF'
```

#### Step 23 — Enable and start the service

```bash
sudo systemctl daemon-reload
sudo systemctl enable customer-kyc-api
sudo systemctl start customer-kyc-api
```

#### Step 24 — Check that it started successfully

```bash
sudo systemctl status customer-kyc-api
```

Look for `Active: active (running)`. Then check the logs:

```bash
sudo journalctl -u customer-kyc-api -n 50 --no-pager
```

You should see:

```
Runtime : .NET 10.0.x
OS      : Red Hat Enterprise Linux 9.8 (Plow)
Linux   : True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
System.Configuration.ConfigurationManager: LOADED OK.
Now listening on: http://[::]:5000
```

---

### Phase 7 — Open the Firewall

#### Step 25 — Allow port 5000 through the RHEL firewall

```bash
sudo firewall-cmd --add-port=5000/tcp --permanent
sudo firewall-cmd --reload

# Verify
sudo firewall-cmd --list-ports
```

#### Step 26 — SELinux — allow the process to bind and serve on port 5000

RHEL 9.8 runs SELinux in enforcing mode by default. .NET on a non-standard port needs a
policy label.

```bash
# Install SELinux policy tools if not present
sudo dnf install -y policycoreutils-python-utils

# Label port 5000 as an HTTP port so the .NET process can bind to it
sudo semanage port -a -t http_port_t -p tcp 5000

# Verify
sudo semanage port -l | grep 5000
```

If `semanage port -a` fails because the port is already labelled, use `-m` (modify) instead:

```bash
sudo semanage port -m -t http_port_t -p tcp 5000
```

---

### Phase 8 — Verify the Running API

Run these from the server itself or from any machine that can reach port 5000.

#### Step 27 — Health check (no auth required)

```bash
curl -s http://localhost:5000/health | python3 -m json.tool
```

Expected:

```json
{
    "status": "Healthy",
    "runtime": ".NET 10.0.x",
    "os": "Red Hat Enterprise Linux 9.8 (Plow)",
    "isLinux": true,
    "isDocker": false,
    "utcNow": "2026-08-17T..."
}
```

`"isDocker": false` confirms you are running bare-metal, not in a container.

#### Step 28 — Get a JWT token

```bash
curl -s -X POST http://localhost:5000/api/auth/token \
    -H "Content-Type: application/json" \
    -d '{"username":"poc-user","password":"your-strong-password"}' \
    | python3 -m json.tool
```

Copy the `token` value from the response.

#### Step 29 — Run the TDES Linux proof

```bash
curl -s -X POST http://localhost:5000/api/encryption/test \
    -H "Authorization: Bearer <paste-token-here>" \
    -H "Content-Type: application/json" \
    -d '{"value":"RHEL98-TDES-Test"}' \
    | python3 -m json.tool
```

Expected — `"platform"` confirms RHEL 9.8:

```json
{
    "success": true,
    "original": "RHEL98-TDES-Test",
    "encrypted": "...",
    "decrypted": "RHEL98-TDES-Test",
    "runtime": ".NET 10.0.x",
    "platform": "Red Hat Enterprise Linux 9.8 (Plow)"
}
```

#### Step 30 — Create and retrieve a KYC record (end-to-end SQL test)

```bash
# Create (PAN and Aadhaar are encrypted and stored in SQL Server)
curl -s -X POST http://localhost:5000/api/customers \
    -H "Authorization: Bearer <token>" \
    -H "Content-Type: application/json" \
    -d '{"customerReference":"RHEL-CUST-001","firstName":"Test","lastName":"User","pan":"ABCDE1234F","aadhaar":"111122223333"}' \
    | python3 -m json.tool

# Retrieve (PAN and Aadhaar are decrypted from SQL Server and returned)
# Replace 1 with the id returned above
curl -s http://localhost:5000/api/customers/1 \
    -H "Authorization: Bearer <token>" \
    | python3 -m json.tool
```

The `pan` and `aadhaar` fields in the GET response should match exactly what you submitted,
proving the full TDES encrypt → SQL Server → TDES decrypt round-trip on bare-metal RHEL 9.8.

---

### Quick Reference — Service Management

```bash
# Start
sudo systemctl start customer-kyc-api

# Stop
sudo systemctl stop customer-kyc-api

# Restart (e.g. after a config change)
sudo systemctl restart customer-kyc-api

# View live logs
sudo journalctl -u customer-kyc-api -f

# View last 100 log lines
sudo journalctl -u customer-kyc-api -n 100 --no-pager

# Disable autostart
sudo systemctl disable customer-kyc-api

# Check all .NET runtimes installed on the server
dotnet --list-runtimes

# Check SQL Server status
sudo systemctl status mssql-server
```

---

## 16. Verify Existing SQL Server Connectivity

> **Use this section when SQL Server is already running** — either on a Windows server, a separate
> Linux server, or an Azure SQL / managed instance — and you simply need to confirm the RHEL 9.8
> application server can reach it before deploying.
>
> You do not need to install SQL Server. Just follow the steps below in order.

---

### What information you need before you start

Collect the following details from your DBA or system administrator. You will use them throughout
this section. Write them down somewhere safe (not in a public file).

| What | Example | Where to get it |
|---|---|---|
| SQL Server hostname or IP address | `sql-server-01` or `192.168.1.50` | DBA / network team |
| SQL Server port | `1433` (default) | DBA (ask if a non-default port is used) |
| Database name | `CustomerKycDb` | DBA |
| Username | `sa` or a service account | DBA |
| Password | `YourStrong@Password123` | DBA (ask for a dedicated app account) |

---

### Step 1 — Install the SQL Server command-line tool (`sqlcmd`)

`sqlcmd` is the tool you use to connect to SQL Server from a Linux terminal.
Run this on your **RHEL 9.8 application server**.

```bash
# Add Microsoft's package repository (if not already done)
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc

sudo dnf install -y \
    https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm

# Install sqlcmd and its dependency (unixODBC)
sudo dnf install -y mssql-tools18 unixODBC-devel

# Add sqlcmd to your PATH so you can run it from anywhere
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' | \
    sudo tee /etc/profile.d/mssql-tools.sh

# Apply the PATH change in your current session
source /etc/profile.d/mssql-tools.sh
```

**Check it installed correctly:**

```bash
sqlcmd -?
```

You should see a help message starting with `Microsoft (R) SQL Server Command Line Tool`.
If you see `command not found`, the PATH was not applied — run the `source` line again.

---

### Step 2 — Check basic network connectivity to the SQL Server

Before attempting a database login, confirm the network can reach the SQL Server port at all.
This catches firewall blocks and wrong IP addresses early, so you get a clear error instead of
a confusing timeout.

**Check if the server is reachable on the network (ping):**

```bash
ping -c 4 sql-server-01
# Replace sql-server-01 with your actual hostname or IP address
```

What to look for:
- `64 bytes from ...` lines — the server is reachable on the network ✅
- `Request timeout` or `Destination Host Unreachable` — network or firewall issue ❌

**Check if port 1433 (SQL Server) is open and accepting connections:**

```bash
# nc (netcat) is the standard tool for port checks on RHEL
nc -zv sql-server-01 1433
```

What to look for:
- `Connection to sql-server-01 1433 port [tcp/ms-sql-s] succeeded!` ✅
- `Connection refused` — SQL Server is not listening on that port (wrong port, or SQL Server is stopped) ❌
- `No route to host` or timeout — a firewall is blocking port 1433 ❌

> **If port 1433 is blocked:** Ask your network team to open TCP port 1433 from your RHEL server's
> IP to the SQL Server's IP. This is a firewall rule change, not something you can fix yourself.

---

### Step 3 — Connect to SQL Server with `sqlcmd`

Now try an actual database login. Replace the values in angle brackets with your real details.

```bash
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -Q "SELECT 'Connection successful' AS Result, @@VERSION AS SqlVersion"
```

**What each flag means (in plain English):**

| Flag | What it does |
|---|---|
| `-S sql-server-01,1433` | The address of the SQL Server. Format is `hostname,port`. |
| `-U sa` | The username. Replace with your service account name. |
| `-P "..."` | The password. Keep it in quotes in case it has special characters. |
| `-C` | Trust the server's SSL certificate without verifying it against a CA. Required for most dev/test SQL Servers that use self-signed certificates. |
| `-Q "..."` | Run this SQL query, print the result, then exit. |

**Expected output — connection worked:**

```
Result                 SqlVersion
---------------------- --------------------------------------------------
Connection successful  Microsoft SQL Server 2022 (RTM-CU...) on Linux...

(1 rows affected)
```

**Common errors and what they mean:**

| Error message | What went wrong | How to fix it |
|---|---|---|
| `Login failed for user 'sa'` | Wrong username or password | Double-check credentials with your DBA |
| `Cannot open server ... Login timeout expired` | Can't reach the server at all | Re-do Step 2 — network/firewall issue |
| `Cannot open server ... Connection was refused` | Reached the server but SQL Server port is not open | SQL Server may be stopped; check with DBA |
| `SSL Provider: certificate verify failed` | Certificate trust issue | Add `-C` flag (Trust Server Certificate) |
| `sqlcmd: command not found` | sqlcmd not in PATH | Run `source /etc/profile.d/mssql-tools.sh` |

---

### Step 4 — Check if the database exists

Once you can connect, verify that the `CustomerKycDb` database exists on the SQL Server.

```bash
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -Q "SELECT name AS DatabaseName, state_desc AS Status FROM sys.databases WHERE name = 'CustomerKycDb'"
```

**Expected output — database exists:**

```
DatabaseName    Status
--------------- -------
CustomerKycDb   ONLINE

(1 rows affected)
```

**If the output shows `(0 rows affected)`** — the database does not exist yet.
Create it by running the schema script:

```bash
# Copy schema.sql to this server first if you haven't already
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -i /path/to/CustomerKyc.Poc/database/schema.sql
```

Expected output after running the script:

```
Table CustomerKyc created.
```

---

### Step 5 — Check if the `CustomerKyc` table exists

```bash
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -Q "USE CustomerKycDb; SELECT TABLE_NAME, TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CustomerKyc'"
```

**Expected output — table exists:**

```
TABLE_NAME   TABLE_SCHEMA
------------ ------------
CustomerKyc  dbo

(1 rows affected)
```

**If `(0 rows affected)`** — the table has not been created yet. Run the schema script (shown
in Step 4 above). The script is idempotent — it is safe to run more than once.

---

### Step 6 — Check the application user's permissions

If you are using a dedicated application account (recommended) rather than `sa`, verify it has
the correct permissions on the `CustomerKycDb` database.

```bash
# First connect as sa (admin)
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -Q "
USE CustomerKycDb;
-- Check what roles/permissions the app account has
SELECT dp.name AS UserName, drm.role_principal_id, r.name AS RoleName
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members drm ON dp.principal_id = drm.member_principal_id
LEFT JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
WHERE dp.name = 'kyc_app_user';
"
```

Replace `kyc_app_user` with your actual application account name.

**Minimum permissions the application account needs:**

```sql
-- Run as sa to grant minimum permissions for the application
USE CustomerKycDb;

-- Create the user if it doesn't exist
CREATE USER kyc_app_user FOR LOGIN kyc_app_login;

-- Grant only what the app needs: read and write to CustomerKyc table
GRANT SELECT, INSERT ON dbo.CustomerKyc TO kyc_app_user;
```

---

### Step 7 — Do a test insert and select to confirm read/write works

This confirms the permissions are correct end-to-end, not just that the login works.

```bash
sqlcmd \
    -S sql-server-01,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -Q "
USE CustomerKycDb;

-- Insert a test row
INSERT INTO dbo.CustomerKyc
    (CustomerReference, FirstName, LastName, EncryptedPan, EncryptedAadhaar, Status, CreatedOn)
VALUES
    ('CONN-TEST-001', 'Connectivity', 'Test', 'test-pan', 'test-aadhaar', 'Test', SYSUTCDATETIME());

-- Read it back
SELECT Id, CustomerReference, Status, CreatedOn
FROM dbo.CustomerKyc
WHERE CustomerReference = 'CONN-TEST-001';

-- Clean up the test row
DELETE FROM dbo.CustomerKyc WHERE CustomerReference = 'CONN-TEST-001';
PRINT 'Test row cleaned up.';
"
```

**Expected output:**

```
Id   CustomerReference  Status  CreatedOn
---- ------------------ ------- --------------------------
1    CONN-TEST-001       Test    2026-08-17 ...

(1 rows affected)
Test row cleaned up.
```

If the INSERT succeeds and the SELECT returns the row — SQL Server connectivity is fully verified ✅

---

### Step 8 — Build the connection string for `appsettings.json`

Once all the above steps pass, construct the connection string to put into the application
configuration. The format used by this application is:

```
Server=<hostname>,<port>;Database=CustomerKycDb;User Id=<username>;Password=<password>;TrustServerCertificate=true;Encrypt=false
```

**Example with the values from this guide:**

```
Server=sql-server-01,1433;Database=CustomerKycDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=true;Encrypt=false
```

> **Do not paste this into `appsettings.json` with a real password.** Use an environment variable
> or the secrets file instead (see Phase 6, Step 21 in Section 15). The connection string goes
> into `/etc/customer-kyc-api/secrets.env` under the key
> `ConnectionStrings__DefaultConnection`.

---

### Connectivity Check — Quick Summary

Run through this checklist in order. Stop at the first failure and fix it before moving on.

```
[ ] Step 1  — sqlcmd installed and found in PATH
[ ] Step 2  — ping to SQL Server hostname/IP succeeds
[ ] Step 2  — nc -zv to port 1433 shows "succeeded"
[ ] Step 3  — sqlcmd login returns "Connection successful"
[ ] Step 4  — CustomerKycDb database exists and is ONLINE
[ ] Step 5  — CustomerKyc table exists in dbo schema
[ ] Step 6  — Application account has SELECT + INSERT on CustomerKyc
[ ] Step 7  — Test insert and select completes without errors
[ ] Step 8  — Connection string built and ready for secrets file
```

All eight boxes checked = your SQL Server connection is ready for deployment.

---

## 17. Deploy from Windows to RHEL 9.8 — Publish Locally and Copy Manually

> **This is the recommended workflow for the current phase.**
> You build and publish the application on your local Windows development machine, then copy
> the output files to the RHEL 9.8 server. The server only needs the .NET runtime — not the
> SDK, not Docker, not any build tools.
>
> **Who does what:**
> - **Developer (Windows machine):** runs Steps 1–5
> - **Someone with SSH access to the RHEL server:** runs Steps 6–16

---

### What you need before you start

| Item | Where to get it |
|---|---|
| .NET 10 SDK installed on your Windows machine | https://dot.net — download the SDK installer |
| IP address or hostname of the RHEL 9.8 server | Ask your system administrator |
| SSH username and password for the RHEL server | Ask your system administrator |
| SQL Server hostname, port, database name, username, password | Follow Section 16 first to verify connectivity |
| WinSCP installed (for copying files) | https://winscp.net — free download |

---

### Part A — On Your Windows Machine (Build and Publish)

#### Step 1 — Open PowerShell and go to the project folder

Press `Win + X` and click **Terminal** or **PowerShell**. Then type:

```powershell
cd D:\POC\CustomerKyc.Poc
```

> If your project is in a different location, change the path accordingly.
> You can verify you are in the right folder by running `dir` — you should see `Dockerfile`,
> `docker-compose.yml`, and `src` listed.

#### Step 2 — Run the tests first

**Always run tests before publishing.** This confirms everything works correctly on your machine
before you put anything on the server.

```powershell
dotnet test tests\CustomerKyc.Api.Tests\ --configuration Release
```

Wait for it to finish. You must see this before continuing:

```
Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36
```

If any tests fail, do not continue — fix the failure first.

#### Step 3 — Publish the application targeting Linux

This command compiles the application and packages it into a folder of files ready to run on
a Linux server.

```powershell
dotnet publish src\CustomerKyc.Api\CustomerKyc.Api.csproj `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained false `
    --output D:\POC\CustomerKyc.Poc\publish-linux
```

**What each flag does:**

| Flag | Plain English meaning |
|---|---|
| `--configuration Release` | Build in Release mode (optimised, not debug) |
| `--runtime linux-x64` | Package for 64-bit Linux — this is what the RHEL server runs |
| `--self-contained false` | Do not bundle the .NET runtime — the server will have it already |
| `--output ...publish-linux` | Put all the output files in this folder |

Wait for the command to finish. You will see: `Build succeeded.`

#### Step 4 — Verify the output folder contains TDESEncrypt.dll

Open File Explorer and navigate to `D:\POC\CustomerKyc.Poc\publish-linux`.

Or run this in PowerShell:

```powershell
dir D:\POC\CustomerKyc.Poc\publish-linux | Select-Object Name
```

You must see `TDESEncrypt.dll` in the list. This is the DLL whose Linux compatibility we are
proving. If it is missing, the publish step had an error — do not copy the files until this
file is present.

You should also see:
- `CustomerKyc.Api.dll` — the main application
- `appsettings.json` — the base configuration file
- Several other `.dll` files — library dependencies

#### Step 5 — Note the folder size

```powershell
(Get-ChildItem D:\POC\CustomerKyc.Poc\publish-linux -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
```

This prints the total size in MB. It is typically around 10–20 MB, which is small enough to
copy over SSH without any issues.

---

### Part B — Copy the Files to the RHEL Server

You have two options. **Option A (WinSCP)** is easier if you are not familiar with the command
line. **Option B (SCP)** is faster if you are comfortable with a terminal.

---

#### Option A — Copy using WinSCP (GUI, recommended for beginners)

WinSCP is a free Windows application that lets you copy files to a Linux server using a
drag-and-drop interface, similar to Windows Explorer.

**A1 — Download and install WinSCP**

Go to https://winscp.net and click **Download WinSCP**. Run the installer with default options.

**A2 — Open WinSCP and create a new connection**

1. Open WinSCP
2. A login dialog appears automatically
3. Fill in the fields:

| Field | Value |
|---|---|
| File protocol | `SFTP` |
| Host name | The IP address or hostname of your RHEL server, e.g. `192.168.1.100` |
| Port number | `22` (SSH default — do not change unless told otherwise) |
| User name | Your SSH username on the RHEL server |
| Password | Your SSH password |

4. Click **Login**
5. If a warning appears saying "The server's host key is not cached" — click **Accept**.
   This is normal the first time you connect to a server.

**A3 — Navigate to the target folder on the server**

In the right panel (the server side), navigate to `/opt/customer-kyc-api`.

If the folder does not exist yet, right-click in the right panel and choose
**New → Directory**, then type `customer-kyc-api`.

**A4 — Copy the files**

1. In the left panel (your Windows machine), navigate to
   `D:\POC\CustomerKyc.Poc\publish-linux`
2. Press `Ctrl + A` to select all files
3. Drag them to the right panel (the server side)
4. A confirmation dialog appears — click **Copy**
5. Wait for the transfer to finish (the progress bar will reach 100%)

**A5 — Verify the files are on the server**

In the right panel (server side), you should now see `CustomerKyc.Api.dll`, `TDESEncrypt.dll`,
and all the other files listed. If you can see them, the copy was successful.

---

#### Option B — Copy using SCP from Git Bash or PowerShell

If you have **Git for Windows** installed, you already have `scp` available in Git Bash.

**Open Git Bash** (right-click on the Desktop or any folder → **Git Bash Here**) and run:

```bash
scp -r /d/POC/CustomerKyc.Poc/publish-linux/* \
    youruser@192.168.1.100:/opt/customer-kyc-api/
```

Replace:
- `youruser` with your SSH username
- `192.168.1.100` with the server IP or hostname
- The path `/d/POC/CustomerKyc.Poc/publish-linux/*` means `D:\POC\CustomerKyc.Poc\publish-linux\*`
  (Git Bash uses forward slashes and `/d/` instead of `D:\`)

You will be asked for your password. Type it and press Enter (nothing appears on screen while
typing — this is normal).

The files will transfer. When the command prompt returns, the copy is done.

---

### Part C — On the RHEL 9.8 Server (Setup and Run)

Log in to the RHEL 9.8 server via SSH. On Windows, open **PowerShell** and run:

```powershell
ssh youruser@192.168.1.100
```

Type your password when prompted. You are now on the Linux server.

#### Step 6 — Install the .NET ASP.NET Core Runtime

The server needs the .NET runtime to run the application. It does **not** need the full SDK
(the SDK is only needed for building, which you already did on Windows).

```bash
# Add Microsoft's package repository for RHEL 9
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc

sudo dnf install -y \
    https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm

# Install the ASP.NET Core runtime (not the SDK)
sudo dnf install -y aspnetcore-runtime-10.0
```

This will take 1–2 minutes to download and install.

**Verify it installed correctly:**

```bash
dotnet --list-runtimes
```

You should see a line like:

```
Microsoft.AspNetCore.App 10.0.x [/usr/lib/dotnet/shared/Microsoft.AspNetCore.App]
```

#### Step 7 — Create the application folder and a service user

Create a dedicated system account for running the application. This is a security best practice
— the application will not run as root.

```bash
# Create the service account (a locked-down system user with no home directory or login shell)
sudo useradd --system --uid 1654 --gid 0 --no-create-home --shell /sbin/nologin kyc-api

# Create the folder where the application files will live
sudo mkdir -p /opt/customer-kyc-api

# Give ownership of that folder to the kyc-api service account
sudo chown kyc-api:root /opt/customer-kyc-api
sudo chmod 750 /opt/customer-kyc-api
```

> **Why a separate user?** If the application is ever compromised, the attacker can only do
> what the `kyc-api` user is allowed to do — which is very limited. Running as root would give
> them full control of the server.

#### Step 8 — Fix ownership of the copied files

After copying the files in Part B, set the correct owner so the service account can read them:

```bash
sudo chown -R kyc-api:root /opt/customer-kyc-api
sudo chmod -R 750 /opt/customer-kyc-api
```

**Verify the files are there:**

```bash
ls /opt/customer-kyc-api
```

You should see `CustomerKyc.Api.dll`, `TDESEncrypt.dll`, `appsettings.json`, and others.
If the folder is empty, go back to Part B and copy the files again.

#### Step 9 — Apply the database schema

Run the schema script against your SQL Server. Replace the values with your actual SQL Server
details. (If you already did this in Section 16, skip this step.)

```bash
# Install sqlcmd if not already installed
sudo dnf install -y mssql-tools18 unixODBC-devel
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' | sudo tee /etc/profile.d/mssql-tools.sh
source /etc/profile.d/mssql-tools.sh

# Run the schema script
# The schema.sql file is inside your publish output
sqlcmd \
    -S YOUR-SQL-SERVER-IP,1433 \
    -U sa \
    -P "YourStrong@Password123" \
    -C \
    -i /opt/customer-kyc-api/../../database/schema.sql
```

> **Tip:** If the schema.sql file is not available on the server, copy it from your Windows
> machine the same way you copied the publish output — just drag it to the server using WinSCP,
> or use `scp database/schema.sql youruser@server:/tmp/schema.sql` and then run sqlcmd against
> `/tmp/schema.sql`.

#### Step 10 — Create the secrets configuration file

The application needs passwords and secret keys to start. Store them in a file that only root
can read — never put real passwords directly in the service configuration.

```bash
# Create a private directory for secrets
sudo mkdir -p /etc/customer-kyc-api

# Write the secrets file
sudo bash -c 'cat > /etc/customer-kyc-api/secrets.env << EOF
Jwt__Secret=replace-this-with-a-random-string-at-least-32-characters-long!!
Encryption__Key=replace-this-with-your-encryption-key-32-chars!!
Auth__Username=poc-user
Auth__Password=replace-with-a-strong-password
ConnectionStrings__DefaultConnection=Server=YOUR-SQL-SERVER-IP,1433;Database=CustomerKycDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=true;Encrypt=false
EOF'

# Lock down the file — only root can read it
sudo chmod 600 /etc/customer-kyc-api/secrets.env
sudo chown root:root /etc/customer-kyc-api/secrets.env
```

> **Important:** Replace every `replace-this-...` placeholder with real values.
> The `Encryption__Key` must never change once you have data in the database —
> changing it makes all stored PAN and Aadhaar values permanently unreadable.

**Verify the file looks correct:**

```bash
sudo cat /etc/customer-kyc-api/secrets.env
```

All five lines should be present with real values (not the placeholder text).

#### Step 11 — Create the systemd service file

Systemd is the process manager on RHEL. It will start your application automatically when the
server boots, and restart it if it crashes.

```bash
sudo bash -c 'cat > /etc/systemd/system/customer-kyc-api.service << EOF
[Unit]
Description=Customer KYC API (.NET 10 on RHEL 9.8)
After=network.target

[Service]
Type=notify
User=kyc-api
WorkingDirectory=/opt/customer-kyc-api
ExecStart=/usr/bin/dotnet /opt/customer-kyc-api/CustomerKyc.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=customer-kyc-api

EnvironmentFile=/etc/customer-kyc-api/secrets.env
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000
Environment=Jwt__Issuer=CustomerKycApi
Environment=Jwt__Audience=CustomerKycApiUsers

PrivateTmp=true
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/customer-kyc-api

[Install]
WantedBy=multi-user.target
EOF'
```

#### Step 12 — Start the application

```bash
# Tell systemd about the new service file
sudo systemctl daemon-reload

# Set it to start automatically on server reboot
sudo systemctl enable customer-kyc-api

# Start it now
sudo systemctl start customer-kyc-api
```

#### Step 13 — Check the application started correctly

```bash
sudo systemctl status customer-kyc-api
```

Look for this line:

```
Active: active (running) since ...
```

If you see `failed` instead, check the logs in Step 14.

#### Step 14 — Read the startup logs

```bash
sudo journalctl -u customer-kyc-api -n 60 --no-pager
```

You must see these lines — they confirm .NET is running on RHEL 9.8 and TDESEncrypt.dll
passed its self-test:

```
OS      : Red Hat Enterprise Linux 9.8 (Plow)
Linux   : True
TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on Red Hat Enterprise Linux 9.8 (Plow).
Now listening on: http://[::]:5000
```

If you see an error about a missing connection string or secret key, open
`/etc/customer-kyc-api/secrets.env`, fix the value, then run:

```bash
sudo systemctl restart customer-kyc-api
```

#### Step 15 — Open the firewall port

By default RHEL blocks all incoming ports. Allow port 5000 so the API is reachable.

```bash
sudo firewall-cmd --add-port=5000/tcp --permanent
sudo firewall-cmd --reload
```

**Confirm the port is open:**

```bash
sudo firewall-cmd --list-ports
# Should include: 5000/tcp
```

#### Step 16 — Verify the application is working

Run these commands from the server itself to confirm everything is working end to end.

**Health check — confirms the app is running on RHEL 9.8:**

```bash
curl -s http://localhost:5000/health
```

Expected response:

```json
{
  "status": "Healthy",
  "runtime": ".NET 10.0.x",
  "os": "Red Hat Enterprise Linux 9.8 (Plow)",
  "isLinux": true,
  "isDocker": false
}
```

`"isDocker": false` confirms this is a direct deployment, not a container.

**Get a JWT token:**

```bash
curl -s -X POST http://localhost:5000/api/auth/token \
    -H "Content-Type: application/json" \
    -d '{"username":"poc-user","password":"replace-with-a-strong-password"}'
```

Copy the `token` value from the response.

**Run the TDES proof — confirms TDESEncrypt.dll works on RHEL 9.8:**

```bash
curl -s -X POST http://localhost:5000/api/encryption/test \
    -H "Authorization: Bearer PASTE-TOKEN-HERE" \
    -H "Content-Type: application/json" \
    -d '{"value":"RHEL98-Manual-Deploy-Test"}'
```

Expected — `"success": true` and platform showing RHEL 9.8:

```json
{
  "success": true,
  "original": "RHEL98-Manual-Deploy-Test",
  "encrypted": "...",
  "decrypted": "RHEL98-Manual-Deploy-Test",
  "platform": "Red Hat Enterprise Linux 9.8 (Plow)"
}
```

If you see this, the deployment is complete and verified ✅

---

### How to deploy an updated version

When you make code changes and need to redeploy, repeat only these steps:

```
On Windows:
  Step 2  — Re-run the tests
  Step 3  — Re-publish (output goes to the same publish-linux folder, overwriting old files)
  Step 4  — Check TDESEncrypt.dll is still in the output

Copy to server:
  Option A or B — Copy the new files to /opt/customer-kyc-api (overwrite existing)

On RHEL server:
  sudo systemctl restart customer-kyc-api
  sudo journalctl -u customer-kyc-api -n 30 --no-pager   ← check logs after restart
```

---

### Deployment Checklist

Print this out or tick it off before telling anyone the deployment is done.

```
Part A — Windows (build machine)
  [ ] Tests ran and all 36 passed
  [ ] dotnet publish completed with "Build succeeded"
  [ ] TDESEncrypt.dll is present in publish-linux folder

Part B — File copy
  [ ] All files copied to /opt/customer-kyc-api on the RHEL server
  [ ] File count on server matches file count in publish-linux folder

Part C — RHEL server setup
  [ ] .NET runtime installed — dotnet --list-runtimes shows 10.0.x
  [ ] kyc-api service user created
  [ ] /opt/customer-kyc-api is owned by kyc-api
  [ ] Database schema applied — CustomerKyc table exists
  [ ] /etc/customer-kyc-api/secrets.env created with real values (no placeholders)
  [ ] systemd service enabled and started
  [ ] systemctl status shows: active (running)
  [ ] Logs show: TDESEncrypt.dll: Self-test PASSED on Red Hat Enterprise Linux 9.8
  [ ] Logs show: Now listening on: http://[::]:5000
  [ ] Firewall port 5000 opened
  [ ] GET /health returns "status": "Healthy" and "os": "Red Hat Enterprise Linux 9.8"
  [ ] POST /api/encryption/test returns "success": true
```

---

*Customer KYC API POC · .NET 10 · Red Hat Enterprise Linux 9.8 (RHEL 9.8 / UBI 9) · All 36 tests passing*
