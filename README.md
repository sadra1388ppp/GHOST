# GHOST

Offline-first digital and field investigation lab for Windows.

> Every trace tells a story.

## Current status

GHOST is in active development. Its Command Center uses a context-aware next-action panel: it offers the latest open case, the archive when all cases are closed, or a first-case prompt when no cases exist. The dashboard also includes case/evidence/lab metrics, quick operations, recent-case opening, and local-mode status. It includes local SQLite persistence, an in-window case archive/editor, first-pass Digital and Field Labs, and a searchable/type-filterable Investigation Network with zoom controls and record navigation. The active Workspace menu item is highlighted in neon green. All Workspace sections remain inside the original MainWindow.

The Evidence Vault now supports case-linked file-copy imports, SHA-256 recording and manual verification, audit entries, archive/restore, and revealing vault copies in File Explorer. This is an initial implementation that still needs Windows build/runtime validation and richer provenance metadata. Timeline/reporting, tested backup/restore, encryption hardening, and a packaged release remain on the roadmap. Features are not considered release-complete until persistence, audit history, error handling, and validation are tested.

## Run from source

Requires the .NET 10 SDK and Windows:

dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj

## Data location

Local database: %LOCALAPPDATA%\GHOST\GHOST.db

Evidence and backups are planned under the same application data folder. See docs/ROADMAP.md for the end-to-end plan and acceptance criteria.

## Integrity and safety

The current database does not have encryption at rest. Do not put sensitive evidence into the current build until data and backup protection have been implemented and tested. SHA-256 helps detect byte-for-byte changes; it does not independently prove who created a file or that the collection process was trustworthy. Preserve originals and document provenance.
