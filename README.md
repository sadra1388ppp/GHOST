# GHOST

Offline-first digital and field investigation lab for Windows.

> Every trace tells a story.

## Current status

GHOST is in active development. Its Command Center includes a live Windows system monitor for overall CPU load, physical RAM usage, Windows system-drive capacity, host name, and system uptime. CPU is calculated from Windows system-time deltas; RAM is read from Windows memory status; drive capacity refreshes every ten seconds. CPU, RAM, and disk tiles include bounded recent-sample histories displayed as neon-green smoothed area sparklines; charts use observed telemetry, not fabricated data. The dashboard includes GHOST Shell, and the left-side GHOST TERMINAL opens a full-window view of the same persistent native Windows PowerShell process (PowerShell 7 when found, otherwise Windows PowerShell), session, current directory, output transcript, and Up/Down history. The former command-library panel has been removed so the terminal uses the full workspace. All GHOST-specific application commands start with `ghost`; `ghost help` displays all 20 built-in commands: `ghost help`, `ghost status`, `ghost cases`, `ghost case add "<title>" [--description "<text>"]`, `ghost case delete <id|case-number> --confirm`, `ghost evidence`, `ghost labs`, `ghost audit [count]`, `ghost network`, `ghost vault`, `ghost db`, `ghost open <id|case-number>`, `ghost sysinfo`, `ghost processes`, `ghost ipconfig`, `ghost diskspace`, `ghost uptime`, `ghost services`, `ghost policy`, and `ghost history`. The eight system commands execute predefined read-oriented PowerShell scripts from inside the app; there is no command-library UI for creating or editing custom scripts. Commands other than `ghost ...` continue to native PowerShell as entered, so native PowerShell commands do not need the `ghost` prefix. The built-in investigation commands read local records, navigate to cases, and create or delete cases. Case deletion requires `--confirm`, removes related case-linked database records and relationships, preserves audit history, and retains evidence files on disk. System commands may inspect Windows process, network, disk, service, and policy information using the current Windows account. Native PowerShell can modify files and settings, so only run commands you understand. Up/Down recall up to 500 recent command entries, with consecutive duplicates suppressed; history is stored at `%LOCALAPPDATA%\GHOST\PowerShellHistory.txt`. The terminal uses neon-green workstation styling, a connection indicator, machine/session label, and one-click native PowerShell shortcuts for `Get-Location`, a short process list, a short directory listing, and `$PSVersionTable`. This is a line-oriented embedded host rather than a full ConPTY terminal emulator, so full-screen console apps and some cursor/ANSI features are not fully supported. Case/evidence/lab metrics, quick operations, recent-case opening, and local-mode status remain. GHOST supports SQLite by default and can be switched to MariaDB with a local database configuration file. It includes an in-window case archive/editor, first-pass Digital and Field Labs, and a searchable/type-filterable Investigation Network with zoom controls and record navigation. Workspace sections remain in the original MainWindow.

The Evidence Vault now supports case-linked file-copy imports, SHA-256 recording and manual verification, audit entries, archive/restore, and revealing vault copies in File Explorer. This is an initial implementation that still needs Windows build/runtime validation and richer provenance metadata. Timeline/reporting, tested backup/restore, encryption hardening, and a packaged release remain on the roadmap. Features are not considered release-complete until persistence, audit history, error handling, and validation are tested.

## Run from source

Requires the .NET 10 SDK and Windows:

dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj

## Data location

SQLite database: `%LOCALAPPDATA%\\GHOST\\GHOST.db`.

Database provider settings: `%LOCALAPPDATA%\\GHOST\\database.json`. SQLite stays the default. To use MariaDB, create a database and a dedicated local user in HeidiSQL, set `Provider` to `MariaDB` in the configuration file, and configure its connection string. GHOST creates its tables on startup when the selected database is empty. Existing SQLite records are not automatically copied into MariaDB. See [docs/MARIADB_SETUP.md](docs/MARIADB_SETUP.md) and docs/ROADMAP.md.

## Integrity and safety

The current database does not have encryption at rest. Do not put sensitive evidence into the current build until data and backup protection have been implemented and tested. SHA-256 helps detect byte-for-byte changes; it does not independently prove who created a file or that the collection process was trustworthy. Preserve originals and document provenance.
