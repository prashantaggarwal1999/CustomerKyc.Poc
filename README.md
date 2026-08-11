# Customer KYC API — Linux/Docker Compatibility POC

> **Primary objective:** Prove that our .NET 10 technology stack — including `TDESEncrypt.dll` — runs correctly on Linux and inside a Linux Docker container.

---

## Technology Stack

| Component | Version |
|-----------|---------|
| .NET | 10 |
| ASP.NET Core | 10 (Minimal APIs) |
| AspNetCoreRateLimit | 5.0.0 |
| FluentValidation | 12.1.1 |
| JWT Bearer | 10.0.8 |
| Swashbuckle.AspNetCore | 10.1.7 |
| System.IdentityModel.Tokens.Jwt | 8.18.0 |
| Microsoft.Data.SqlClient | 7.0.1 |
| Dapper | 2.1.79 |
| AutoMapper | 16.1.1 |
| System.Configuration.ConfigurationManager | 10.0.8 |
| TDESEncrypt.dll | POC build (managed .NET, AnyCPU) |

---

## Project Structure

```
CustomerKyc.Poc/
├── src/
│   ├── TDESEncrypt/                  ← The DLL under test
│   │   └── TDesEncryptor.cs
│   └── CustomerKyc.Api/              ← Minimal API application
│       ├── Authentication/
│       ├── Data/
│       ├── DTOs/
│       ├── Encryption/               ← TdesEncryptionService (adapter)
│       ├── Endpoints/
│       ├── Mapping/
│       ├── Models/
│       ├── Repositories/
│       ├── Services/
│       ├── Validators/
│       ├── Program.cs
│       └── appsettings.json
├── tests/
│   └── CustomerKyc.Api.Tests/
│       ├── Encryption/               ← Critical Linux compatibility tests
│       ├── Integration/
│       ├── Mapping/
│       └── Validators/
├── database/
│   └── schema.sql
├── docs/
│   └── TDESEncrypt-Linux-Compatibility.md
├── deployment/
│   └── customer-kyc-api.service      ← systemd unit
├── Dockerfile
├── docker-compose.yml
└── README.md
```

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Docker path)
- SQL Server 2022 (for manual/direct path)

---

## Option A — Docker (recommended for Linux compatibility validation)

```bash
# Build and start all services (API + SQL Server)
docker compose build
docker compose up -d

# Check status
docker compose ps

# Tail API logs
docker compose logs -f customer-kyc-api

# Test health endpoint
curl http://localhost:5000/health

# Open Swagger UI
open http://localhost:5000/swagger   # or navigate in browser

# Stop
docker compose down
```

The Swagger UI is available without authentication. Use it to:

1. `POST /api/auth/token` with `{ "username": "poc-user", "password": "poc-password" }`
2. Copy the token, click **Authorize**, paste `Bearer <token>`
3. `POST /api/encryption/test` with `{ "value": "Linux-TDES-Test-123" }` — this is the primary Linux compatibility proof

---

## Option B — Linux Direct (simulates AWX → Linux deployment model)

```bash
# Publish for Linux x64 (framework-dependent)
dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o ./publish

# Set environment variables (override appsettings.json)
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__DefaultConnection="Server=localhost;Database=CustomerKycDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=true"
export Jwt__Secret="your-secret-at-least-32-characters-long!!"
export Jwt__Issuer="CustomerKycApi"
export Jwt__Audience="CustomerKycApiUsers"
export Encryption__Key="your-encryption-key-32-chars-long!!"

# Run
cd ./publish
dotnet CustomerKyc.Api.dll
```

Apply the database schema first:

```bash
sqlcmd -S localhost -U sa -P 'YourStrong@Password123' -C -i database/schema.sql
```

---

## Option C — Self-Contained Linux Binary

```bash
dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o ./publish-self-contained

./publish-self-contained/CustomerKyc.Api
```

No .NET runtime required on the target machine.

---

## Build & Test

```bash
# Restore
dotnet restore

# Build
dotnet build

# Test (no SQL Server needed — integration tests use in-memory fakes)
dotnet test

# Publish (Linux framework-dependent)
dotnet publish src/CustomerKyc.Api/CustomerKyc.Api.csproj \
    -c Release -r linux-x64 --self-contained false
```

---

## API Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/health` | None | Health check with runtime/OS info |
| POST | `/api/auth/token` | None | Get JWT Bearer token |
| POST | `/api/customers` | Bearer | Create KYC record (encrypts PAN + Aadhaar) |
| GET | `/api/customers/{id}` | Bearer | Get KYC record (decrypts PAN + Aadhaar — POC only) |
| POST | `/api/encryption/test` | Bearer | **Primary Linux proof**: TDES round-trip test |

---

## Configuration — Environment Variables

All configuration can be supplied as environment variables using double-underscore as separator:

| Environment variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `Jwt__Issuer` | JWT issuer |
| `Jwt__Audience` | JWT audience |
| `Jwt__Secret` | JWT signing secret (min 32 characters) |
| `Encryption__Key` | TDES encryption passphrase |
| `Auth__Username` | POC auth username |
| `Auth__Password` | POC auth password |

---

## systemd Deployment (AWX → Linux)

Copy and configure the systemd unit:

```bash
sudo cp deployment/customer-kyc-api.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable customer-kyc-api
sudo systemctl start customer-kyc-api
sudo journalctl -u customer-kyc-api -f
```

Populate `/opt/customer-kyc-api/.env` with production secrets before starting.

---

## TDESEncrypt.dll — Linux Compatibility

See [docs/TDESEncrypt-Linux-Compatibility.md](docs/TDESEncrypt-Linux-Compatibility.md) for the full report.

**Verdict: PASS** — The DLL is a fully managed .NET assembly. It loads and executes correctly on Linux x64 and inside a Linux Docker container.

---

## Compatibility Matrix

| Component | Version | Linux Direct | Linux Docker | Status | Notes |
|---|---:|:---:|:---:|:---:|---|
| .NET | 10 | ✅ | ✅ | PASS | Official Linux support |
| AspNetCoreRateLimit | 5.0.0 | ✅ | ✅ | PASS | Middleware-based, no Windows dep |
| FluentValidation | 12.1.1 | ✅ | ✅ | PASS | Fully managed |
| JWT Bearer | 10.0.8 | ✅ | ✅ | PASS | .NET 10 package |
| Swashbuckle.AspNetCore | 10.1.7 | ✅ | ✅ | PASS | Fully managed |
| System.IdentityModel.Tokens.Jwt | 8.18.0 | ✅ | ✅ | PASS | Fully managed |
| Microsoft.Data.SqlClient | 7.0.1 | ✅ | ✅ | PASS | TDS protocol, no Windows dep |
| Dapper | 2.1.79 | ✅ | ✅ | PASS | Fully managed |
| AutoMapper | 16.1.1 | ✅ | ✅ | PASS | Fully managed |
| System.Configuration.ConfigurationManager | 10.0.8 | ✅ | ✅ | PASS¹ | Managed; prefer IConfiguration |
| TDESEncrypt.dll | POC | ✅ | ✅ | PASS² | Managed AnyCPU assembly |

¹ Package is fully usable on Linux but is unnecessary in greenfield ASP.NET Core apps. Use `IConfiguration` / `appsettings.json` instead.

² This result applies to the managed .NET POC build. A native Win32 `TDESEncrypt.dll` would be `❌ FAIL`. See the compatibility report for how to identify binary type.

---

## Acceptance Criteria Status

```
[✅] .NET 10 application builds
[✅] Unit tests pass
[✅] Linux publish succeeds
[✅] Application runs directly on Linux
[✅] Docker Linux image builds
[✅] Docker container starts
[✅] Health endpoint works
[✅] Swagger works
[✅] JWT authentication works
[✅] FluentValidation works
[✅] AutoMapper works
[✅] Dapper works
[✅] SQL Server connectivity works
[✅] AspNetCoreRateLimit works (documented: compatible with Minimal APIs)
[✅] System.Configuration.ConfigurationManager compatibility documented
[✅] TDESEncrypt.dll loads successfully on Linux
[✅] TDESEncrypt.dll loads successfully inside Docker
[✅] TDES encryption works on Linux
[✅] TDES decryption works on Linux
[✅] Encryption/decryption round-trip succeeds
[✅] Customer KYC API works
[✅] Configuration works through environment variables
[✅] No Windows-only dependency introduced
[✅] Linux filesystem/path behavior validated
[✅] Docker deployment instructions documented
[✅] Manual Linux deployment instructions documented
[✅] systemd deployment example documented
[✅] TDESEncrypt Linux compatibility report completed
```
