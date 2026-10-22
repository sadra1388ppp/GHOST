# GHOST Roadmap

## Milestone 0 — Foundation
- [x] WPF solution and dark tactical theme
- [x] Local SQLite database bootstrap
- [x] Initial investigation models
- [x] Dashboard and case creation/listing
- [x] Relationship storage schema
- [x] Build validation on Windows/.NET 10 (Release build verified through GitHub Actions)

## Milestone 1 — Evidence Vault
- Import copies of files into the local evidence vault
- Calculate SHA-256 on import and verify integrity later
- Archive-first deletion policy and audit history
- Metadata inspection with explicit source/original preservation

## Milestone 2 — Experiments
- Scientific and physical experiment templates
- Field notes, observations, measurements and attachments
- Digital lab: hashes, file metadata and log inspection

## Milestone 3 — Investigation Network (in progress)
- [x] Network embedded in the main window; navigation swaps in-window views
- [x] Nodes for cases, evidence, experiments and observations
- [x] Create and delete typed relationships with description and confidence
- [x] Record relationship changes in the local audit log
- [x] Visually distinguish hypothesis/contradiction links with dashed styling
- [ ] Search and filter nodes
- [ ] Zoom/pan and improve graph layout for large networks
- [ ] Add nodes for timeline events and future record types
- [ ] Record evidence provenance and mark claims verified, unverified or disputed

## Milestone 4 — Reporting and resilience
- Timeline and case reports
- Consistent backup/restore including database and evidence files
- Automated tests, migrations, error handling and accessibility

## Integrity notes
SHA-256 helps identify byte-for-byte changes but does not prove who created a file or whether the collection process was trustworthy. Record provenance and preserve originals.

## Security notes
The first foundation build stores local data without database encryption. Do not store sensitive evidence until encryption at rest and backup protection are implemented.


## Current UI decisions
- The Command Center does not display a graph preview. Investigation Network has its own workspace section inside the existing MainWindow.
- Opening Investigation Network must never create another WPF Window; use visibility/navigation within the existing MainWindow.
- Global typography uses Cascadia Code with Consolas fallback. The font is system-resolved and is not bundled.
- Dashboard metrics are shown in one horizontal row: Open Cases, Closed Cases, Experiments, Evidence Items. Closed Cases uses red.
