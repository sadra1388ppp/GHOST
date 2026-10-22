# GHOST Roadmap

## Milestone 0 — Foundation
- [x] WPF solution and dark tactical theme
- [x] Local SQLite database bootstrap
- [x] Initial investigation models
- [x] Dashboard and case creation/listing
- [x] Relationship storage schema
- [ ] Build validation on Windows/.NET 10

## Milestone 1 — Evidence Vault
- Import copies of files into the local evidence vault
- Calculate SHA-256 on import and verify integrity later
- Archive-first deletion policy and audit history
- Metadata inspection with explicit source/original preservation

## Milestone 2 — Experiments
- Scientific and physical experiment templates
- Field notes, observations, measurements and attachments
- Digital lab: hashes, file metadata and log inspection

## Milestone 3 — Investigation Board
- Interactive node graph for cases, evidence, experiments and observations
- Typed relationships with description, source and confidence
- Search, filtering, zoom, pan and node detail panel
- Distinguish hypotheses from verified facts

## Milestone 4 — Reporting and resilience
- Timeline and case reports
- Consistent backup/restore including database and evidence files
- Automated tests, migrations, error handling and accessibility

## Integrity notes
SHA-256 helps identify byte-for-byte changes but does not prove who created a file or whether the collection process was trustworthy. Record provenance and preserve originals.

## Security notes
The first foundation build stores local data without database encryption. Do not store sensitive evidence until encryption at rest and backup protection are implemented.
