# GHOST — Session Handoff

Last updated: 2026-10-22

This file is the durable continuation note for future development chats. Read it before coding, then check the newest main commit and its Windows build status.

## Repository and run commands

- GitHub: https://github.com/sadra1388ppp/GHOST
- Branch: main
- Solution: GHOST.sln
- WPF project: src/GHOST.Client/GHOST.Client.csproj
- Stack: C#, WPF, .NET 10, Entity Framework Core 10, SQLite
- Local database: %LOCALAPPDATA%\GHOST\GHOST.db
- Evidence and Backups folders are created under %LOCALAPPDATA%\GHOST.
- From repository root run:
  ```powershell
  dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj
  ```
- If terminal is already in src\GHOST.Client, run dotnet run.

## Product and UI intent

GHOST is an offline-first digital and field investigation lab. UI should be a polished hacker/investigation workstation: near-black green background, neon green and cyan accents, carefully restrained danger red, subtle grid texture, consistent compact monospace typography. Keep it professional and legible.

- Shared UI resources/styles live in src/GHOST.Client/App.xaml.
- Global font resource is Cascadia Code with Consolas fallback. The font is resolved from the host system and is not currently bundled in the repository.
- Preserve existing UI when changing unrelated behavior. User prefers complete repository changes, exact PowerShell commands and concise Persian explanations.
- Local data is not encrypted at rest yet; do not recommend storing sensitive evidence until encryption and backup protection are implemented.
- SHA-256 detects byte-level changes; by itself it does not prove authenticity or provenance.

## Currently implemented

- WPF shell and dark hacker theme with common typography/control styles.
- SQLite database initialization through GhostDbContext.Initialize().
- Main dashboard queries live SQLite statistics.
- Create Case dialog persists CaseFile and AuditEntry records; Case Files view lists local cases.
- MainWindow is the only main application window for normal navigation. Dashboard and Investigation Network switch using DashboardView and NetworkView visibility. Do not make a standalone window for Investigation Network.
- Investigation Network is embedded within MainWindow; graph nodes are loaded for Case, Evidence, Experiment and Observation records.
- Users can create local connections between two records, choose relationship type, record notes and confidence, review stored connections, and delete a selected connection. Create/delete operations are written to the audit log.
- Hypothesis and Contradicts links are visually dashed; graph positions currently use a simple grid layout.
- Sidebar includes Command Center, Case Files, Digital Lab (placeholder), Field Lab (placeholder), Evidence Vault (placeholder), Investigation Network and GHOST Terminal (placeholder).
- Windows CI workflow is at .github/workflows/windows-build.yml. It restores and Release-builds the solution on a Windows runner.

## Latest requested dashboard change

The dashboard metric cards now use one horizontal row with four columns, in this order: Open Cases, Closed Cases, Experiments, Evidence Items. Closed Cases uses a red border/accent. Code-behind counts rows with Status == "Closed". Verify the corresponding current-main Windows Actions build before telling the user the newest commit has passed.

## Important recent commits

- 8274dc34e38b504372f603b19b7972087d62f7d2 — removed the standalone network window and verified build success for the single-window navigation implementation.
- a47e2484077ec77aaa251033f2f4ae576d3c2db4 — adds the red Closed Cases dashboard card and updates roadmap; next commit also records this session handoff.

## Known gaps / next steps

1. Continue developing the current dashboard and keep the four metric cards horizontally aligned; Closed Cases must stay red.
2. Case status transition/close workflow is not implemented yet, so existing UI may not currently provide an easy way to set a case to Closed even though the dashboard counts that status.
3. Implement Evidence Vault import that copies files rather than modifying originals, SHA-256-on-import, integrity verification, provenance metadata, archive-first removal and audit trail.
4. Implement Digital Lab and Field Lab forms for experiments, observations and measurement records.
5. Improve Investigation Network: search/filter, zoom/pan, less crowded layout for many nodes, timeline event nodes, edit-connection flow and node selection details.
6. Add timeline and report generation, consistent database+evidence backup/restore, EF migrations, automated tests, input validation and error handling.
7. Add database encryption at rest and protected backup storage before using sensitive investigation data.

## Validation notes

- A GitHub Actions Windows Release build succeeded for the single-window network revision in run https://github.com/sadra1388ppp/GHOST/actions/runs/39767138738.
- There was an earlier failed intermediate build due duplicate NetworkNodeView/NetworkConnectionView records after embedding the network; the standalone window files were removed afterwards and commit 8274dc34e38b504372f603b19b7972087d62f7d2 built successfully.
- The newest Closed Cases card change is newer than that successful build, so check its own CI run before claiming the exact latest state passes.
- Do not claim the app was run on the user's local machine unless the user reports it.

## Collaboration preferences

- Speak Persian when the user does, informal warm tone such as داداش; keep explanations short and direct.
- The user asks the assistant to handle UI/frontend and expects changes pushed to main for this repo.
- Provide the actual Git pull/run commands after repository commits.