# GHOST Digital Lab — Research and Design Notes

This note records the publicly documented workflows used as inspiration for the GHOST Digital Lab redesign. GHOST remains an offline-first Windows investigation workspace; it is not represented as a replacement for a full forensic suite.

## Patterns reviewed

### Autopsy / The Sleuth Kit
Autopsy emphasizes an approachable interface, a navigable results tree, timeline analysis, and hash-based filtering. GHOST applies the usability lesson by keeping experiments in a searchable archive, then presenting one selected experiment's objective, method, conclusion, and observation history together.

Source: https://www.sleuthkit.org/autopsy/

### TheHive
TheHive organizes investigations around cases, observable data, case details, and an activity trail that analysts can review. GHOST adopts the case-first approach: a Digital Lab experiment belongs to a selected case; a preserved Evidence Vault item can be linked to that experiment; creation and updates continue to write audit entries and timeline events.

Sources:
- https://docs.strangebee.com/thehive/user-guides/analyst-corner/cases/about-cases/
- https://docs.strangebee.com/thehive/how-to/alert-management/

### Velociraptor
Velociraptor documentation highlights self-describing investigative artifacts, recording collection context, and reviewing results in notebooks rather than relying on undocumented one-off actions. GHOST applies the documentation principle with explicit objective/question and method/steps fields, readable observation history, and a small read-only file inspection workflow.

Sources:
- https://docs.velociraptor.app/docs/notebooks/
- https://docs.velociraptor.app/docs/forensic/

## What changed in GHOST

- Clearer information hierarchy: investigation selector and filters, summary counts, experiment archive, and separate Overview, Observations, and Safe File Tools tabs.
- Search across experiment title, category, status, objective, method, and conclusion.
- Case-linked evidence relationships, with duplicate-link and cross-case checks plus audit/timeline entries.
- A read-only utility to compute SHA-256, view basic file metadata, and preview a bounded excerpt from supported text formats. It does not execute a chosen file or modify it.
- File inspection findings can be explicitly saved as an observation on the selected experiment.
- Observation history is sorted newest-first and bounded to the latest 150 entries in the visible list to keep the interface responsive.

## Integrity and scope notes

A SHA-256 value is a fingerprint of the bytes read at that time; it does not prove authorship, provenance, or authenticity. The text preview is for supported text formats only. The Digital Lab does not execute evidence, automatically label a file malicious, or claim to replace specialist tools such as Autopsy or Velociraptor. This first pass reuses GHOST's existing database entities and relationship table, so it does not require a destructive database reset or new schema columns.

## Validation still required

The change should be built on Windows and manually exercised against an existing MariaDB database, including an empty case, a case with several experiments, observations, archived evidence, and both small and large files. Existing persisted records must remain intact.
