# GHOST

Offline-first digital and field investigation lab for Windows.

> Every trace tells a story.

## Current status

GHOST is in active development. The app currently includes a dark WPF command center, local SQLite persistence, case creation and search, open/closed case status changes, first-pass Digital Lab experiments/observations, first-pass Field Lab notes/measurements, and an Investigation Network embedded in the main window. Startup sizing adapts to the available desktop workspace.

Evidence Vault import and integrity verification, editing/association workflows, a complete timeline/report UI, verified backup/restore, encryption hardening, and a packaged release are still on the roadmap. Features are not considered complete until their persistence, audit history, error handling, and validation are tested.

## Run from source

Requires the .NET 10 SDK and Windows:

dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj

## Data location

Local database: %LOCALAPPDATA%\GHOST\GHOST.db

Evidence and backups are planned under the same application data folder. See docs/ROADMAP.md for the end-to-end plan and acceptance criteria.

## Integrity and safety

The current database does not have encryption at rest. Do not put sensitive evidence into the current build until data and backup protection have been implemented and tested. SHA-256 helps detect byte-for-byte changes; it does not independently prove who created a file or that the collection process was trustworthy. Preserve originals and document provenance.
