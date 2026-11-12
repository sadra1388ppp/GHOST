# GHOST

Offline-first investigation workspace for Windows.

> Every trace tells a story.

## Local storage

GHOST has no database engine, server, SQL provider, or network database connection. All application records are stored locally as JSON files in the automatically created Desktop/GHOST folder.

The folder contains cases.json, evidence.json, experiments.json, observations.json, relationships.json, timeline.json, audit.json, Evidence/, Experiments/, Backups/, and PowerShellHistory.txt.

GHOST creates the folder and initial JSON files automatically at startup. No MariaDB, HeidiSQL, SQLite, or database configuration is required.

## Run from source

Requires the .NET 10 SDK and Windows:

    dotnet run --project .\\src\\GHOST.Client\\GHOST.Client.csproj

## Terminal

All GHOST application commands start with ghost. Use ghost help to list commands. Use ghost storage to view the local data folder and record counts. Native PowerShell commands continue to run normally.

## Data safety

Keep the Desktop/GHOST folder backed up. GHOST writes JSON through temporary files before replacing the previous file. The application does not connect to or delete any existing MariaDB database; records that exist only in a former MariaDB server must be exported separately if they are needed.
