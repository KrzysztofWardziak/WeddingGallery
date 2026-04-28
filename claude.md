# Project Context for Claude (v2)

## Role: Senior Full-Stack Engineer (.NET & Angular)

## Project Overview
Wedding Photo Gallery: A self-hosted (Docker) app where guests scan a QR code to upload photos to a PostgreSQL-backed system.

## Workflow & Development Rules
1. **Branching Strategy:**
   - NEVER work directly on the `main` or `master` branch.
   - Always create a new branch for each task: `feature/short-description` or `fix/short-description`.
2. **Build Verification:**
   - Before completing any task, ensure the project builds successfully.
   - Commands: `dotnet build` (backend) and `ng build` (frontend).
3. **Docker Verification:**
   - If changes involve infrastructure or environment variables, run `docker-compose build` to verify the containerization logic.
4. **Code Review Protocol:**
   - Provide a "Summary of Changes" after each major code generation.
   - Point out any potential side effects or technical debt introduced.
5. **Commit Style:**
   - Use Conventional Commits (e.g., `feat: add image upload service`, `fix: resolve thumbnail scaling issue`).

## Tech Stack Rules
- **Backend:** .NET 8 Web API. Use Clean Architecture. Entity Framework Core + Npgsql.
- **Frontend:** Angular. Tailwind CSS. Mobile-first.
- **Storage:** Local filesystem with Docker volume mapping.

## Key Commands
- `docker-compose up --build`: Start the environment.
- `dotnet ef migrations add InitialCreate`: Database migrations.
- `ng serve --host 0.0.0.0`: Frontend development.

## MCP Integration (Model Context Protocol)
If using Filesystem MCP:
- Access path: `/mnt/data/project` (adjust to your VM path).
- Allow Claude to read/write code directly to the project folder.
