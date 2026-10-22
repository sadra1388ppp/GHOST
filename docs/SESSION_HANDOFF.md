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
- Investigation Network must stay within the existing MainWindow; navigation switches visibility, never launches a second network window.
- Dashboard metrics: Open Cases, Closed Cases (red), Experiments, Evidence Items in one horizontal row.
- Main app startup size now adapts to the desktop work area and centers itself; XAML defaults are 1360x820 with smaller minimums for ordinary laptop screens.
- User wants assistant to implement UI/features and push repository changes; respond in concise, informal Persian when user uses Persian.

## Implemented in current main

- WPF shell, dashboard stats, recent cases, SQLite initialization.
- Create case and audit creation.
- Searchable Case Files archive with Open/Closed transitions. Status changes create audit entries and refresh dashboard/network counts.
- Digital Lab: create case-linked experiments with category, hypothesis, procedure and results; add text/numeric observations; audit changes and create case timeline events. New experiments are selected after saving.
- Field Lab: create case-linked field notes or measurements with optional location and numeric value/unit; entries are persisted as timeline events and audit history. This is a first pass using timeline records, not a dedicated field-record data model.
- Investigation Network is embedded in the same MainWindow.
- Graph nodes for case, evidence, experiments, and observations; create/delete relationships with notes/confidence and audit records.
- Windows CI workflow at .github/workflows/windows-build.yml.

## Still not complete / critical limitations

- The app has NOT been run locally on the user's machine by the assistant.
- Latest startup sizing, case archive, Digital Lab, and Field Lab changes must be validated by the latest Windows Actions workflow before claiming a successful build.
- Evidence Vault remains a placeholder. Digital Lab and Field Lab have first-pass data-entry flows but need broader validation, record editing, evidence associations, and more complete specialized data models.
- No completed timeline/reporting/backup-and-restore workflow yet.
- Database encryption at rest and protected backups are not implemented. Do not recommend storing sensitive evidence.
- SHA-256 identifies byte-for-byte changes but does not independently prove authenticity, authorship, or provenance.
- Keep existing records and audit history; prefer archive/soft-delete rather than permanent evidence deletion.

## Next exact work order

Follow docs/ROADMAP.md as the source of truth:

1. Check current main, inspect latest CI.
2. Finish/verify the current case archive status workflow and screen-aware window sizing.
3. Implement Evidence Vault: copy originals (never move/edit), stream SHA-256, record provenance, verify/flag changes, archive with audit.
4. Test and improve Digital Lab/Field Lab workflows: edit records, show observation history, associate evidence, and use a dedicated field record model.
5. Improve graph search/filter/zoom/pan/edit.
6. Build timeline/report and reliable backup/restore.
7. Add security controls, tests, migration and packaging.
8. Validate a Windows Release build and provide exact pull/run commands.

Do not claim a feature or test succeeded unless its result is observable.
