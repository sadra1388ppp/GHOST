# Enable MariaDB for GHOST

GHOST supports two database providers. SQLite remains the default and the existing file stays untouched. MariaDB mode stores new GHOST records in the server database, which you can inspect in HeidiSQL.

## 1. Create an empty database and a dedicated user

Connect to your local MariaDB server in HeidiSQL using an account allowed to create databases and users. Open a query tab, replace the example password with a strong password of your own, and execute:

```sql
CREATE DATABASE IF NOT EXISTS ghost
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'ghost_app'@'localhost'
  IDENTIFIED BY 'REPLACE_WITH_A_STRONG_PASSWORD';

GRANT ALL PRIVILEGES ON ghost.* TO 'ghost_app'@'localhost';
FLUSH PRIVILEGES;
```

If you already created this account with a different password, update it before continuing. Do not use `root` for day-to-day application access.

## 2. Create the GHOST configuration file

Run GHOST once with its default SQLite settings, then close it. The app creates this local file if it does not exist:

```text
%LOCALAPPDATA%\GHOST\database.json
```

Open it from PowerShell:

```powershell
notepad "$env:LOCALAPPDATA\GHOST\database.json"
```

Change its contents to the following, substituting the password used in step 1:

```json
{
  "Provider": "MariaDB",
  "MariaDb": {
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=ghost;User ID=ghost_app;Password=REPLACE_WITH_A_STRONG_PASSWORD;CharSet=utf8mb4;SslMode=None",
    "ServerVersion": ""
  }
}
```

Leave `ServerVersion` empty to let GHOST detect the server version automatically. The password in this local JSON file is stored as plain text, so keep the file private and never copy it into the Git repository. Alternatively, provide connection settings through environment variables: `GHOST_DB_PROVIDER`, `GHOST_MARIADB_CONNECTION_STRING`, and optionally `GHOST_MARIADB_VERSION`.

## 3. Start GHOST and inspect the tables

From the repository folder, run:

```powershell
git pull origin main
dotnet run --project .\src\GHOST.Client\GHOST.Client.csproj
```

On startup, GHOST creates its schema in the selected database if it is empty. In HeidiSQL, refresh the `ghost` database in the left tree; tables such as `Cases`, `Evidence`, `Experiments`, `Observations`, `Relationships`, `TimelineEvents`, and `AuditLogs` should appear. You can also run `ghost db` in the GHOST terminal to show the selected provider, connection target, server version, and record counts.

## Switch back to SQLite

Close GHOST and change `"Provider": "MariaDB"` back to `"Provider": "SQLite"`, then reopen the app. The old `GHOST.db` file is kept at `%LOCALAPPDATA%\GHOST\GHOST.db`.

## Important data note

Switching providers does not copy records between databases. If your cases currently live in SQLite, they remain there; a newly selected MariaDB database starts separately. Back up data before attempting any manual migration.
