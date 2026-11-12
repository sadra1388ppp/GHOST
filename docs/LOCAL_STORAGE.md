# GHOST local storage

GHOST stores application data in:

    %USERPROFILE%\\Desktop\\GHOST

The folder is created automatically when the application starts.

## Files

| File / folder | Contents |
|---|---|
| cases.json | Case records |
| evidence.json | Evidence metadata |
| experiments.json | Experiment records |
| observations.json | Experiment observations |
| relationships.json | Investigation network connections |
| timeline.json | Timeline events |
| audit.json | Audit history |
| Evidence | Original evidence files |
| Experiments | Experiment attachments |
| Backups | User-managed backups |
| PowerShellHistory.txt | Terminal history |

Records are serialized as readable JSON. The app does not require a database service or configuration.

Close GHOST before copying the entire GHOST folder to another drive. Keep a separate backup before manually editing JSON files.

Existing records that only exist in a previous MariaDB server are not automatically imported. Export them separately before retiring that server; GHOST no longer connects to it.
