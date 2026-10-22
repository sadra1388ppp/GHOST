# GHOST — Session Handoff

Last updated: 2026-10-22

Read this note first in a future development session, then inspect the newest main commit and its Windows Actions build status.

## Repository and run commands

- GitHub: https://github.com/sadra1388ppp/GHOST
- Branch: main
- Solution: GHOST.sln
- WPF project: src/GHOST.Client/GHOST.Client.csproj
- Stack: C#, WPF, .NET 10, Entity Framework Core 10, SQLite
- Local database: %LOCALAPPDATA%\GHOST\GHOST.db
- Evidence/backup folders: under %LOCALAPPDATA%\GHOST
- From repository root:
  dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj
- From src\GHOST.Client: dotnet run

## Product/UI decisions

- GHOST is an offline-first Windows investigation workspace.
- Preserve the near-black/green/cyan workstation theme and avoid clutter.
- Shared resources are in src/GHOST.Client/App.xaml; font resource is Cascadia Code with Consolas fallback, resolved on the user's system rather than bundled.
- All Workspace navigation must remain inside the existing MainWindow: Command Center, Case Files, Digital Lab, Field Lab, Evidence Vault, Investigation Network, and terminal/placeholder modules swap content in the shared ModuleContent host. Do not create child Window instances or call ShowDialog for workspace views. New Case is embedded in the same host too.
- Dashboard metrics: Open Cases, Closed Cases (red), Experiments, Evidence Items in one horizontal row.
- Dashboard hero is a live Windows system monitor with CPU usage sampled from GetSystemTimes deltas, RAM from GlobalMemoryStatusEx, Windows system-drive usage refreshed every 10 seconds, machine name, available logical CPU count, and OS uptime. CPU/RAM/disk cards show neon-green smoothed area sparklines based on bounded observed-sample histories; no fabricated initial trend is added, so curves build as readings arrive. It intentionally does not claim GPU load, temperature, or other telemetry that the current implementation does not read. The lower-right Workspace Integrity card has been replaced with an embedded native PowerShell process (`pwsh.exe` if available, otherwise Windows `powershell.exe`). It has live redirected stdout/stderr, a working-directory-aware prompt, Enter/RUN input, RESTART, and Up/Down history. Commands run with the current Windows account's permissions and execution-policy rules, so they can modify or delete files; warn users to run only commands they understand. This is a line-oriented host, not a full ConPTY terminal, so full-screen native console apps and some cursor/ANSI interactions are not fully supported. The rest of the dashboard retains the accent-top case/evidence/lab metric cards, Create Case / Case Files / Digital Lab quick access, and OPEN SELECTED for recent cases.
- Main app startup size now adapts to the desktop work area and centers itself; XAML defaults are 1360x820 with smaller minimums for ordinary laptop screens.
- User wants assistant to implement UI/features and push repository changes; respond in concise, informal Persian when user uses Persian.

## Implemented in current main

- WPF shell, dashboard stats, recent cases, SQLite initialization.
- Create case and audit creation using an in-window form (no child window).
- Case Files archive supports search, open/close, double-click to open, and a dedicated in-window case workspace. Case titles/descriptions can be edited; evidence, experiments, observations, timeline, relationships, and audit history are summarized in separate tabs. Case updates/status changes are audited.
- Digital Lab and Field Lab now use a split-pane layout: structured entry forms on one side, saved experiment/field-entry history on the other; both retain case association, validation, and audit/timeline writes.
- Evidence Vault performs first-pass imports through a native file picker, copies source files (never moves/edits them) into %LOCALAPPDATA%\GHOST\Evidence with generated names, streams SHA-256, shows metadata, verifies stored bytes, audits verification results, archives/restores records, and reveals copies in File Explorer. GHOST does not execute evidence files from the vault.
- Investigation Network is embedded in the same MainWindow. Nodes are organized in type lanes, searchable and type-filterable, with zoom/fit controls, relationship display, and OPEN SELECTED RECORD navigation to the underlying case/evidence/experiment.
- Workspace navigation highlights the active sidebar item with a green background/border and neon text.
- Windows CI workflow at .github/workflows/windows-build.yml.

## Still not complete / critical limitations

- The app has NOT been run locally on the user's machine by the assistant, and a successful Windows build has not been confirmed in this session. Run the latest main commit through Windows Actions and a local smoke test before claiming verified build/runtime success.
- Evidence Vault is a first functional pass, not release-complete: provenance/source notes are not yet collected, file size is computed for display rather than persisted as record metadata, and verification requires manual invocation.
- Digital Lab and Field Lab have functional first-pass forms but need broader validation, editing workflows, experiment/evidence associations, and a dedicated field-record data model.
- Network editing of saved links, case-association filters, and a reset-view action remain incomplete.
- No completed timeline/reporting/backup-and-restore workflow yet.
- Database encryption at rest and protected backups are not implemented. Do not recommend storing sensitive evidence.
- SHA-256 identifies byte-for-byte changes but does not independently prove authenticity, authorship, or provenance.
- Keep existing records and audit history; prefer archive/soft-delete rather than permanent evidence deletion.

## Next exact work order

Follow docs/ROADMAP.md as the source of truth:

1. Check current main, run Windows Actions/build and test the workspace navigation, case editor, vault import/verification, and graph interactions.
2. Finish evidence metadata: persist file size and capture source/provenance notes; add tests for missing/mismatched files and path handling.
3. Add editing and richer association flows to Digital Lab/Field Lab, including a dedicated field-record model and links from experiments to evidence.
4. Add network relationship editing, case filters, reset-view, and visual tests for large graphs.
5. Build timeline/report and reliable backup/restore.
6. Add security controls, tests, migration and packaging.
8. Validate a Windows Release build and provide exact pull/run commands.

Do not claim a feature or test succeeded unless its result is observable.
