# Smart Meter Production Tracker

Portfolio project simulating manufacturing software for smart energy devices; not EFR software or a certified MES.

## Stack
- ASP.NET Core 8 minimal API / C#
- PostgreSQL 16 / EF Core
- JavaScript dashboard
- Docker Compose and GitLab CI

## Run
Install Docker Desktop and run `docker compose up --build -d` in this folder. Open http://localhost:8080 and http://localhost:8080/swagger. Stop with `docker compose down` (which retains data). Do not run `docker compose down -v` unless you intend to erase the demo database.

Register a uniquely numbered device, run simulated tests, and inspect the pass/fail result and test history.

## Routes
- `GET /health`: DB health
- `GET /api/summary`: counts
- `GET /api/devices`: list devices (optional `?status=Passed`)
- `GET /api/devices/{id}`: device and test history
- `POST /api/devices`: `{ "serialNumber": "SGH-0001", "firmwareVersion": "2.00" }`
- `POST /api/devices/{id}/tests`: simulate measurement and store history

Test acceptance thresholds and voltage/signal values are **invented** for demo purposes and are not real specifications. For real manufacturing use, add authentication, authorization, migrations, idempotent test submissions, hardware connectivity, audit trails, concurrency controls, backup/restore tests, telemetry, and controlled release approvals. The credentials in Docker Compose are strictly local-demo defaults.
