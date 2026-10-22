# GHOST database setup (MariaDB only)

GHOST uses **MariaDB as its only database provider**. SQLite support and its NuGet packages have been removed. GHOST will not silently fall back to a local database file.

## 1. Create the database and application user in HeidiSQL

Connect to your local MariaDB server with an account allowed to create databases and users. Open a query tab, replace the example password with a strong one, and execute:

```sql
CREATE DATABASE IF NOT EXISTS ghost
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'ghost_app'@'127.0.0.1'
  IDENTIFIED BY 'REPLACE_WITH_A_STRONG_PASSWORD';

GRANT ALL PRIVILEGES ON ghost.* TO 'ghost_app'@'127.0.0.1';
FLUSH PRIVILEGES;
```

If the user already exists with another password, update it before continuing. Use a dedicated application account rather than your MariaDB root account for day-to-day use.

## 2. Configure GHOST

The settings file is:

```text
%LOCALAPPDATA%\GHOST\database.json
```

Create the folder and open the file from PowerShell:

```powershell
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\GHOST" | Out-Null
notepad "$env:LOCALAPPDATA\GHOST\database.json"
```

Set the file content below, replacing the password with the one used above:

```json
{
  "Provider": "MariaDB",
  "MariaDb": {
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=ghost;User ID=ghost_app;Password=REPLACE_WITH_A_STRONG_PASSWORD;CharSet=utf8mb4;SslMode=None",
    "ServerVersion": ""
  }
}
```

Keep `ServerVersion` empty for automatic version detection. The password in this local JSON file is plain text: keep the file private and never commit it to Git. Alternatively, set `GHOST_MARIADB_CONNECTION_STRING` as an environment variable; `GHOST_MARIADB_VERSION` is optional.

If an older `database.json` says `"Provider": "SQLite"`, GHOST updates that setting to MariaDB automatically. It still needs a valid MariaDB connection string before startup can continue.

## 3. Pull and run GHOST

From the repository folder:

```powershell
git pull origin main
dotnet restore .\GHOST.sln
dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj
```

On startup, GHOST creates the schema in the selected MariaDB database if it is empty. In HeidiSQL, refresh the `ghost` database (press F5). Tables such as `Cases`, `Evidence`, `Experiments`, `Observations`, `Relationships`, `TimelineEvents`, and `AuditLogs` should appear. In the GHOST terminal, run `ghost db` to inspect the connection, database name, server version, and record counts.

## Troubleshooting

- **Connection refused:** make sure the MariaDB Windows service is running and confirm the port (commonly 3306).
- **Access denied:** verify the username, password, and host part of the MariaDB account. This guide connects to `127.0.0.1`.
- **Unknown database:** execute the `CREATE DATABASE` statement above and ensure `Database=ghost` matches its name.
- **Configuration error:** edit `%LOCALAPPDATA%\GHOST\database.json`; do not put database passwords into repository files.

## Existing data note

GHOST no longer reads the legacy `%LOCALAPPDATA%\GHOST\GHOST.db` file. It does not delete that file, but records formerly saved there are **not copied automatically** into MariaDB. Keep the old file as a backup until any records you need have been exported and imported into MariaDB.
