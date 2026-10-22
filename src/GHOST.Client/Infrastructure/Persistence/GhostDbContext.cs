using Microsoft.EntityFrameworkCore;
using System.IO;
using GHOST.Models;

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

    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHOST");
    private static string DatabasePath => Path.Combine(DataDirectory, "GHOST.db");
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite($"Data Source={DatabasePath}");
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
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Evidence"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Backups"));
        using var db = new GhostDbContext();
        db.Database.EnsureCreated();
    }
}
