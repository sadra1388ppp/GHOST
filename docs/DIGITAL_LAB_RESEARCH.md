# GHOST Lab Rebuild — Research and Design Notes

GHOST is an offline-first Windows investigation workspace. The previous Digital Lab, Field Lab and Evidence Vault page implementations have been removed from the active UI so they can be rebuilt cleanly. This document retains public forensic-workstation references for that future work; it does not describe the retired screens as currently available and does not claim feature parity with specialist suites.

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

## Design principles for the fresh rebuild

- **Cases stays simple.** Keep the familiar archive-first Case Files page. Avoid duplicating its job with extra dashboards or a second permanent detail pane.
- **Case context remains visible.** The Lab should show the active case and its identifier without making the user open a dropdown to discover the current context.
- **Prefer visible choices over hidden menus.** Use compact, readable lists and explicit filter chips when there are only a few choices; make selected states unmistakable.
- **One analysis bench, clear hierarchy.** Use a navigation/results region and a selected-record viewer. Keep common actions close to the record they affect, and avoid crowded toolbars.
- **Separate evidence, results, and conclusions.** Distinguish original file metadata and hashes from derived observations, experiment methods, findings, and analyst conclusions.
- **Preserve provenance.** Link evidence and analytical records back to the owning case. Record meaningful changes in the audit log and timeline.
- **Read-only by default.** Inspection should not execute source files or modify evidence. Actions that change records should be explicit and auditable.
- **No destructive rebuild.** Reuse the existing MariaDB case, experiment, observation, evidence, relationship, audit, and timeline data unless a separately reviewed migration is genuinely required.
- **Do not overstate forensic capabilities.** The new UI should not claim automatic malware detection, full disk-image parsing, or specialist-suite parity unless those features are really implemented and validated.

## Data and validation

The fresh implementation should reuse GHOST's existing case, experiment, observation, evidence, relationship, audit, and timeline entities. Removing the old pages did not delete stored rows, source files, or vault files. Avoid a destructive database reset or new schema columns unless a separate reviewed change requires them.

Build validation should be followed by a manual pass against an existing MariaDB database: no cases; one case with multiple experiments; Draft and Completed experiments; no linked evidence; one and several evidence items; archived evidence; observation creation; searching and changing chips; and confirming that existing records persist after closing and reopening the app.
