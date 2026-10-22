using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using GHOST.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace GHOST.Infrastructure.Persistence;

public sealed class GhostDbContext : DbContext
{
    public DbSet<CaseFile> Cases => Set<CaseFile>();
    public DbSet<Experiment> Experiments => Set<Experiment>();
    public DbSet<EvidenceItem> Evidence => Set<EvidenceItem>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<EvidenceRelationship> Relationships => Set<EvidenceRelationship>();
    public DbSet<TimelineEvent> TimelineEvents => Set<TimelineEvent>();
    public DbSet<AuditEntry> AuditLogs => Set<AuditEntry>();

    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly ConcurrentDictionary<string, ServerVersion> DetectedServerVersions =
        new(StringComparer.Ordinal);

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHOST");

    private static string DatabasePath => Path.Combine(DataDirectory, "GHOST.db");

    public static string SqliteDatabasePath => DatabasePath;

    public static string ProviderConfigurationPath => Path.Combine(DataDirectory, "database.json");

    public static string ActiveProvider => ResolveProvider(LoadConfiguration());

    public static string ActiveDatabaseTarget
    {
        get
        {
            var configuration = LoadConfiguration();
            var provider = ResolveProvider(configuration);
            if (provider == "SQLite")
                return DatabasePath;

            try
            {
                var connectionString = ResolveMariaDbConnectionString(configuration);
                var parsed = new MySqlConnectionStringBuilder(connectionString);
                var endpoint = $"{parsed.Server}:{parsed.Port}";
                return string.IsNullOrWhiteSpace(parsed.Database)
                    ? endpoint
                    : $"{endpoint}/{parsed.Database}";
            }
            catch
            {
                return $"Not configured — edit {ProviderConfigurationPath}";
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var configuration = LoadConfiguration();
        var provider = ResolveProvider(configuration);

        if (provider == "SQLite")
        {
            options.UseSqlite($"Data Source={DatabasePath}");
            return;
        }

        var connectionString = ResolveMariaDbConnectionString(configuration);
        options.UseMySql(connectionString, ResolveMariaDbServerVersion(connectionString, configuration));
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CaseFile>().HasIndex(x => x.CaseNumber).IsUnique();
        model.Entity<CaseFile>().Property(x => x.Title).HasMaxLength(200).IsRequired();
        model.Entity<EvidenceItem>().HasIndex(x => x.Sha256);
        model.Entity<EvidenceRelationship>().Property(x => x.Confidence).HasDefaultValue(0.5);
        model.Entity<EvidenceRelationship>().HasIndex(x => new { x.SourceType, x.SourceId, x.TargetType, x.TargetId });
        model.Entity<Experiment>().HasIndex(x => x.CaseFileId);
        model.Entity<EvidenceItem>().HasIndex(x => x.CaseFileId);
        model.Entity<Observation>().HasIndex(x => x.ExperimentId);
        model.Entity<TimelineEvent>().HasIndex(x => new { x.CaseFileId, x.OccurredAt });
        model.Entity<AuditEntry>().HasIndex(x => x.CreatedAt);
    }

    public static void Initialize()
    {
        _ = LoadConfiguration();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Evidence"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Backups"));

        using var db = new GhostDbContext();
        db.Database.EnsureCreated();
    }

    private static GhostDatabaseConfiguration LoadConfiguration()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(ProviderConfigurationPath))
        {
            var defaults = new GhostDatabaseConfiguration();
            File.WriteAllText(
                ProviderConfigurationPath,
                JsonSerializer.Serialize(defaults, ConfigurationJsonOptions));
            return defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<GhostDatabaseConfiguration>(
                       File.ReadAllText(ProviderConfigurationPath),
                       ConfigurationJsonOptions)
                   ?? new GhostDatabaseConfiguration();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The GHOST database configuration is invalid. Fix or restore this file: {ProviderConfigurationPath}. {ex.Message}",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"GHOST could not read its database configuration at {ProviderConfigurationPath}. {ex.Message}",
                ex);
        }
    }

    private static string ResolveProvider(GhostDatabaseConfiguration configuration)
    {
        var configured = Environment.GetEnvironmentVariable("GHOST_DB_PROVIDER");
        if (string.IsNullOrWhiteSpace(configured))
            configured = configuration.Provider;

        if (string.IsNullOrWhiteSpace(configured) ||
            string.Equals(configured, "SQLite", StringComparison.OrdinalIgnoreCase))
            return "SQLite";

        if (string.Equals(configured, "MariaDB", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configured, "MySQL", StringComparison.OrdinalIgnoreCase))
            return "MariaDB";

        throw new InvalidOperationException(
            $"Unsupported GHOST database provider '{configured}'. Use 'SQLite' or 'MariaDB' in {ProviderConfigurationPath}.");
    }

    private static string ResolveMariaDbConnectionString(GhostDatabaseConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable("GHOST_MARIADB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration.MariaDb.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"MariaDB is selected but no connection string is configured. Edit {ProviderConfigurationPath} or set GHOST_MARIADB_CONNECTION_STRING.");
        }

        return connectionString;
    }

    private static ServerVersion ResolveMariaDbServerVersion(
        string connectionString,
        GhostDatabaseConfiguration configuration)
    {
        var versionText = Environment.GetEnvironmentVariable("GHOST_MARIADB_VERSION");
        if (string.IsNullOrWhiteSpace(versionText))
            versionText = configuration.MariaDb.ServerVersion;

        if (!string.IsNullOrWhiteSpace(versionText))
        {
            if (!Version.TryParse(versionText.Trim(), out var version))
            {
                throw new InvalidOperationException(
                    $"Invalid MariaDB server version '{versionText}'. Use a numeric version such as '11.4.2', or leave ServerVersion empty to auto-detect.");
            }

            return new MariaDbServerVersion(version);
        }

        return DetectedServerVersions.GetOrAdd(
            connectionString,
            static cs => ServerVersion.AutoDetect(cs));
    }
}

public sealed class GhostDatabaseConfiguration
{
    public string Provider { get; set; } = "SQLite";
    public GhostMariaDbConfiguration MariaDb { get; set; } = new();
}

public sealed class GhostMariaDbConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
}
