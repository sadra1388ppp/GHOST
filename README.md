# GHOST

Offline-first digital and field investigation lab for Windows.

> Every trace tells a story.

## Current status

GHOST is in active development. Its Command Center includes a live Windows system monitor for overall CPU load, physical RAM usage, Windows system-drive capacity, host name, and system uptime. CPU is calculated from Windows system-time deltas; RAM is read from Windows memory status; drive capacity refreshes every ten seconds. CPU, RAM, and disk tiles include bounded recent-sample histories displayed as neon-green smoothed area sparklines; the charts only plot observed telemetry and fill in as samples accumulate. Metrics are not hardware temperature or GPU telemetry. The dashboard also includes GHOST Shell, which starts a real persistent Windows PowerShell process (PowerShell 7 when found, otherwise Windows PowerShell) inside the panel. Commands are sent to that process and stdout/stderr stream back into the UI; the current directory and session state persist, and Up/Down browse recent command history. Ten app-native commands are intercepted by the GHOST panel: `ghost help`, `ghost status`, `ghost cases`, `ghost evidence`, `ghost labs`, `ghost audit [count]`, `ghost network`, `ghost vault`, `ghost db`, and `ghost open <id|case-number>`. These query local telemetry/database state or navigate to a case without mutating investigation records; all other commands continue to native PowerShell. Commands and scripts run with the current Windows account's permissions and current execution-policy rules, so they can modify files/system settings; only use commands you understand. This is a line-oriented embedded host rather than a full ConPTY terminal emulator, so interactive full-screen console apps and some cursor/ANSI features are not fully supported. The terminal UI has neon-green hacker-style chrome, a subtle grid texture, connection indicator, and one-click real PowerShell shortcuts for `Get-Location`, a short process list, a short directory listing, and `$PSVersionTable`. Case/evidence/lab metrics, quick operations, recent-case opening, and local-mode status remain in place. It includes local SQLite persistence, an in-window case archive/editor, first-pass Digital and Field Labs, and a searchable/type-filterable Investigation Network with zoom controls and record navigation. The active Workspace menu item is highlighted in neon green. All Workspace sections remain inside the original MainWindow.

The Evidence Vault now supports case-linked file-copy imports, SHA-256 recording and manual verification, audit entries, archive/restore, and revealing vault copies in File Explorer. This is an initial implementation that still needs Windows build/runtime validation and richer provenance metadata. Timeline/reporting, tested backup/restore, encryption hardening, and a packaged release remain on the roadmap. Features are not considered release-complete until persistence, audit history, error handling, and validation are tested.

## Run from source

Requires the .NET 10 SDK and Windows:

dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj

## Data location

Local database: %LOCALAPPDATA%\GHOST\GHOST.db

Evidence and backups are planned under the same application data folder. See docs/ROADMAP.md for the end-to-end plan and acceptance criteria.

## Integrity and safety

The current database does not have encryption at rest. Do not put sensitive evidence into the current build until data and backup protection have been implemented and tested. SHA-256 helps detect byte-for-byte changes; it does not independently prove who created a file or that the collection process was trustworthy. Preserve originals and document provenance.
