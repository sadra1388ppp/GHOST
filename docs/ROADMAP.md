# GHOST — End-to-End Product Roadmap

GHOST is an offline-first Windows investigation workspace. This roadmap is the single source of truth for building a usable, reliable release without losing the current dark workstation UI or the requirement that every workspace section, not just Investigation Network, stays inside the existing MainWindow. Workspace navigation must never create a child WPF Window.

## Product completion criteria

The project is ready for a first daily-use release only when a user can create and manage a case, safely import and verify evidence, record digital/field experiments and observations, connect records in the network, review a case timeline, export a report, recover from a backup, and run the app on a fresh Windows machine. Security warnings must be truthful; unencrypted data must not be presented as protected.

## Stage 0 — Stable foundation

- [x] WPF/.NET 10 application shell and consistent dark theme
- [x] SQLite database initialization under %LOCALAPPDATA%\GHOST
- [x] Dashboard metrics and recent cases
- [x] Create case and persist an audit record
- [x] Investigation Network embedded in the existing MainWindow
- [x] All workspace modules swap content inside the same MainWindow; no child windows for navigation
- [x] Create/delete typed links and record relationship changes in audit history
- [x] Windows Release build workflow
- [x] Standard startup size with screen-aware centering and minimum constraints
- [x] Search case archive; close/reopen a case; audit status changes
- [ ] Fresh-install and low-resolution monitor smoke test

## Stage 1 — Case management

Goal: cases are the central containers for the rest of an investigation.

- [x] Create and persist a case
- [x] Search/filter the case archive
- [x] Close and reopen a case with an audit entry
- [x] Edit case title and description with an audit entry
- [x] Show case workspace with evidence, experiments, observations, timeline, relationships, and audit history
- [x] Open a case from the archive or by double-clicking a recent dashboard case
- [ ] Filtering by status and sorting by date/title
- [ ] Confirm destructive actions and prefer archive/soft-delete to permanent deletion
- [ ] Validate required fields and enforce sensible text length limits

Acceptance: every case can be found, reviewed, updated, closed, and reopened; dashboard counts update immediately; no action silently erases investigation history.

## Stage 2 — Evidence Vault

- [x] Select one or more files through a native file picker
- [x] Copy files into the app-managed Evidence folder; never modify or move the source originals
- [x] Store original name, copied vault path, file type, SHA-256, import time, and case association
- [ ] Persist file size in the evidence record and collect source/provenance notes through the import form
- [x] Stream large files when hashing; avoid loading entire files into memory
- [x] Verify a stored file against its recorded hash on demand
- [x] Mark missing or changed copies during verification and record the result in audit history
- [x] Archive evidence while retaining the file, metadata, and audit records
- [x] Prevent path traversal and filename collisions; use generated vault filenames
- [x] Surface active evidence counts and records in the dashboard and Investigation Network
- [x] Reveal a vault copy in File Explorer without executing it from GHOST

Acceptance: the app never edits the original file; an import is traceable; re-verification correctly detects a changed or missing vault copy.

## Stage 3 — Digital Lab

- [x] Create experiment records linked to a case
- [x] Record hypothesis/objective, procedure, category, status, and results
- [x] Add observations to an experiment with optional numeric value and unit
- [x] Record experiment/observation changes in audit history and create timeline events
- [x] Show saved experiments and observations in the Investigation Network
- [ ] Record tools and start/end timestamps
- [ ] Associate evidence files with experiments
- [ ] Add safe, deterministic utilities: SHA-256, file metadata, text/log viewing, and structured notes
- [ ] Never execute untrusted evidence or arbitrary shell commands as a default action

Acceptance: every experiment and observation is persisted, searchable, tied to a case when applicable, and represented accurately in linked records.

## Stage 4 — Field Lab

- [x] Create case-linked field notes with a timestamp and optional location
- [x] Record basic field measurements with an optional numeric value and unit
- [x] Write field note/measurement entries into the case timeline and audit log
- [ ] Add dedicated field record types, separate occurred/recorded times, observer, conditions, and method
- [ ] Attach evidence files using the Evidence Vault workflow
- [ ] Edit entries with an audit trail and flag corrections rather than silently rewriting the past
- [x] Support offline use with no external account or network requirement
- Note: the current basic Field Lab stores field entries as timeline events. This is a usable first pass, not the final dedicated data model.

Acceptance: a field note or measurement retains when it happened, when it was recorded, who recorded it when available, and any correction history.

## Stage 5 — Investigation Network

- [x] Keep the graph inside MainWindow; navigation must not open a second network window
- [x] Show case, evidence, experiment, and observation nodes
- [x] Create/delete typed relationships with notes and confidence
- [x] Audit relationship creation/deletion
- [x] Dashed styling for hypothesis/contradiction relationships
- [x] Search nodes by title/type/ID and filter by node type
- [x] Zoom in/out and fit-to-view; use the graph's scrollbars to pan
- [ ] Add a reset-view action and filter by case association
- [ ] Improve layouts for larger graphs and reduce overlapping labels/edges
- [ ] Edit a saved relationship
- [ ] Show node details and navigate to the corresponding record
- [ ] Add field note/timeline nodes only when the underlying records exist
- [ ] Visually distinguish a claim from verified evidence; confidence is an investigator assessment, not proof

Acceptance: the graph remains responsive as records grow and does not make an unverified claim look like a confirmed fact.

## Stage 6 — Timeline and reports

- [ ] Generate a per-case timeline from creation, evidence imports/verifications, experiments, observations, notes, status transitions, and relationship changes
- [ ] Keep occurred time separate from record-created time when possible
- [ ] Filter timeline by date, event type, and record
- [ ] Export a human-readable case report to HTML and/or PDF, with stable ordering and metadata
- [ ] Include source filenames, hashes, verification state, and provenance notes where applicable
- [ ] Include clear limitations: hashes detect byte changes but do not prove authorship or authenticity
- [ ] Record report export in audit history

Acceptance: a report can be regenerated from saved records and identifies what is known, what is inferred, and what remains unverified.

## Stage 7 — Backup, restore, and data resilience

- [ ] Create consistent backups of SQLite plus the complete evidence store
- [ ] Avoid copying an active SQLite database in a way that omits WAL changes; use SQLite's supported backup mechanism or a safe closed/checkpointed workflow
- [ ] Manifest every backup with version, date, database hash, evidence file list, and hashes
- [ ] Verify backup integrity before declaring it successful
- [ ] Restore to a staging folder first and validate the manifest before replacing live data
- [ ] Never overwrite the only working copy without explicit confirmation and a rollback option
- [ ] Include restore/backup actions in audit history

Acceptance: a backup can be restored on another Windows profile and verified without missing evidence or silently corrupting the database.

## Stage 8 — Security and reliability

- [ ] Decide and document the local threat model and what encryption does/does not protect
- [ ] Add tested encryption at rest for sensitive local records and evidence, with recovery/key-loss behavior designed first
- [ ] Protect backups separately; do not store recovery secrets beside encrypted data
- [ ] Use explicit ownership/access controls if multi-user support is introduced; do not pretend a local single-user app has server-grade access controls
- [ ] Add EF Core migrations and a safe migration/backup path
- [ ] Add validation, bounded error messages, structured diagnostics, and recovery paths
- [ ] Automated tests for hashing, path validation, status changes, audit logging, backup manifests, and migrations
- [ ] UI smoke test on standard laptop and desktop resolutions, keyboard navigation, and scaling
- [ ] Remove placeholder wording from any feature that is presented as available

Acceptance: core integrity/security behavior is covered by automated tests, and unsupported protection claims are absent.

## Stage 9 — Release and onboarding

- [ ] Package a Release build for Windows with required runtime/native dependencies
- [ ] Add a first-run data-folder explanation and storage/security notice
- [ ] Add application version, diagnostics/log location, and a support/export path
- [ ] Verify startup on a clean Windows environment
- [ ] Test upgrade from an existing database without losing records
- [ ] Document install, update, backup, restore, and uninstall behavior
- [ ] Keep a changelog and record tested build artifacts

Acceptance: a new user can install, launch, create a test case, import/verify a file, produce a report, and restore a backup using the included documentation.

## Implementation order

Build and validate one vertical slice at a time. The immediate sequence is:

1. Screen-aware startup sizing and case lifecycle.
2. Evidence import, vault storage, hashing, verification, and archival.
3. Digital/Field Lab forms and saved observations.
4. Network navigation and graph interaction improvements.
5. Timeline and reports.
6. Backup/restore.
7. Security hardening, tests, packaging, and fresh-install validation.

Do not mark a stage complete merely because the UI exists. It is complete only when persistence, validation, audit trail, error handling, and the acceptance criteria have been exercised.
