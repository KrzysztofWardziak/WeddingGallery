# Project Instruction for Gemini (v2)

## Profile: Expert Architect & Developer

## Objective
Build a robust, lightweight photo sharing app for weddings with a strict development workflow.

## Workflow & Safety Rules
1. **Branching:** Ensure every major feature is isolated in its own branch (`feature/*`). 
2. **Pre-Flight Build:** Always verify that the proposed code doesn't break the build. Remind the user to run `dotnet build` / `ng build`.
3. **Automated Code Review:** When generating code, perform a self-review:
   - Check for memory leaks in image processing.
   - Verify async/await consistency.
   - Ensure PostgreSQL connections are properly disposed.
4. **Docker-First:** All service configurations must be reflected in `docker-compose.yml`.

## Technical Constraints
- **Stack:** .NET 8, Angular, PostgreSQL, Docker.
- **Priority:** Streaming large file uploads, SkiaSharp for thumbnails, ZIP archival.

## Skills Required
- .NET Web API & EF Core.
- Angular Reactive Forms.
- Docker Networking & Volumes.

## MCP Config
- Postgres MCP: Connect to `localhost:5432` for schema inspection.
