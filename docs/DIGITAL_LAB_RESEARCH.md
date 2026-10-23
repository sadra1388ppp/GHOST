# GHOST Digital Lab — Research and Design Notes

GHOST is an offline-first Windows investigation workspace. This design uses established forensic-workstation patterns as references; it does not claim feature parity with specialist suites.

## Public references reviewed

### Autopsy / The Sleuth Kit
Autopsy keeps the analysis workflow discoverable through a left-side results tree, a result viewer, a content viewer, keyword search, and a status area. Its timeline and hash-filtering functions also show why an examiner should be able to move from a result to its underlying context.

Sources:
- https://www.sleuthkit.org/autopsy/
- https://www.sleuthkit.org/autopsy/docs/user-docs/4.19.1/uilayout_page.html
- https://www.sleuthkit.org/autopsy/docs/user-docs/4.21.0/timeline_page.html

### Heimdall DFIR
Heimdall brings case management, imported evidence, timelines, detections, notes, findings, and reporting into one investigation-oriented workspace. The key lesson for GHOST is to keep records attached to an investigation and make context and provenance easy to revisit.

Sources:
- https://github.com/RaiseiX/Heimdall-DFIR
- https://raiseix.github.io/Heimdall-DFIR/

### Timesketch
Timesketch's timeline workflow uses explicit filters, tags, saved searches, and event-oriented results instead of hiding common navigation behind nested controls. GHOST borrows the visible-filter principle; it does not yet implement a full forensic timeline explorer.

Sources:
- https://timesketch.org/
- https://timesketch.org/changelog/2023-02/

### Velociraptor
Velociraptor notebooks join explanatory Markdown with executable/query cells and keep collection context next to results. The relevant design idea is to record objective, method, findings, and observations together, so future review does not depend on an analyst's memory.

Source:
- https://docs.velociraptor.app/docs/notebooks/

### TRACE
TRACE demonstrates a compact evidence-review layout: an evidence tree, file results with timestamps, and a viewer for the selected item, with focused actions such as Browse, Timeline, Search, and Hex.

Source:
- https://trace.gadzhovski.com/

## UI decisions in the current GHOST redesign

- **Cases stays archive-first.** The prior simple archive/search/action layout is retained instead of adding a separate metrics dashboard and a second detail pane. Case rows show the case number, status, title, and creation time in a readable card.
- **No popup selectors in Digital Lab.** Investigation selection is a visible case list; category and status filters are persistent chips; the Evidence Vault picker is a visible list. This makes the current choice visible and avoids hard-to-read dropdown menus.
- **One consistent analysis workspace.** The left side holds investigation context and the searchable experiment archive; the right side contains the selected experiment's overview, observation history, and read-only file tools.
- **Visible, small-scope filters.** Search covers experiment title, category, status, objective, method, and conclusion. Category and status filters show their current state instead of hiding it in a menu.
- **An experiment records its reasoning.** Objective/question, method/steps, conclusion, recorded observations, and linked evidence stay connected to the selected case.
- **Evidence links preserve context.** Linking evidence checks that experiment and evidence belong to the same case, rejects duplicate links and archived evidence, and records audit/timeline entries.
- **Read-only first-pass file inspection.** The tool calculates SHA-256, reads basic metadata, and previews bounded excerpts from supported text files. It does not execute an evidence file or modify it. Findings can be explicitly saved as an experiment observation.
- **Keep the workflow honest.** This version does not claim to auto-detect malware, parse all disk images, or replace specialist tools. A hash is a fingerprint of bytes at a point in time, not independent proof of authorship or authenticity.

## Data and validation

The current redesign reuses GHOST's existing case, experiment, observation, evidence, relationship, audit, and timeline entities. It does not require a destructive database reset or new schema columns.

Build validation should be followed by a manual pass against an existing MariaDB database: no cases; one case with multiple experiments; Draft and Completed experiments; no linked evidence; one and several evidence items; archived evidence; observation creation; searching and changing chips; and confirming that existing records persist after closing and reopening the app.
