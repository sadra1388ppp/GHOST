using System.IO;
using System.Reflection;
using System.Text.Json;
using GHOST.Models;

namespace GHOST.Infrastructure.Persistence;

public sealed class GhostLocalStore : IDisposable
{
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GHOST");
    public LocalCollection<CaseFile> Cases { get; }
    public LocalCollection<Experiment> Experiments { get; }
    public LocalCollection<EvidenceItem> Evidence { get; }
    public LocalCollection<Observation> Observations { get; }
    public LocalCollection<EvidenceRelationship> Relationships { get; }
    public LocalCollection<TimelineEvent> TimelineEvents { get; }
    public LocalCollection<AuditEntry> AuditLogs { get; }
    public GhostLocalStore()
    {
        MigrateLegacyFiles();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Evidence"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Experiments"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Backups"));
        Cases = Load<CaseFile>("cases.json");
        Experiments = Load<Experiment>("experiments.json");
        Evidence = Load<EvidenceItem>("evidence.json");
        Observations = Load<Observation>("observations.json");
        Relationships = Load<EvidenceRelationship>("relationships.json");
        TimelineEvents = Load<TimelineEvent>("timeline.json");
        AuditLogs = Load<AuditEntry>("audit.json");
    }
    public static void Initialize() { using var store = new GhostLocalStore(); store.Save(); }

    private static void MigrateLegacyFiles()
    {
        var oldRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHOST");
        if (!Directory.Exists(oldRoot)) return;

        foreach (var folder in new[] { "Evidence", "Experiments" })
        {
            var source = Path.Combine(oldRoot, folder);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var destination = Path.Combine(DataDirectory, folder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Copy(file, destination);
            }
        }

        var oldHistory = Path.Combine(oldRoot, "PowerShellHistory.txt");
        var newHistory = Path.Combine(DataDirectory, "PowerShellHistory.txt");
        if (File.Exists(oldHistory) && !File.Exists(newHistory)) File.Copy(oldHistory, newHistory);
    }
    public int Save()
    {
        lock (FileLock)
        {
            Save("cases.json", Cases);
            Save("experiments.json", Experiments);
            Save("evidence.json", Evidence);
            Save("observations.json", Observations);
            Save("relationships.json", Relationships);
            Save("timeline.json", TimelineEvents);
            Save("audit.json", AuditLogs);
        }
        return 1;
    }
    private static LocalCollection<T> Load<T>(string name) where T : class, new()
    {
        var path = Path.Combine(DataDirectory, name);
        if (!File.Exists(path)) return new LocalCollection<T>();
        try { return new LocalCollection<T>(JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? []); }
        catch (JsonException ex) { throw new InvalidDataException($"Invalid JSON in local GHOST file {name}. Restore it from a backup.", ex); }
    }
    private static void Save<T>(string name, IEnumerable<T> items)
    {
        var path = Path.Combine(DataDirectory, name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, JsonOptions));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    public void Dispose() { }
}

public sealed class LocalCollection<T> : List<T> where T : class
{
    public LocalCollection() { }
    public LocalCollection(IEnumerable<T> items) : base(items) { }
    public new void Add(T item)
    {
        var id = typeof(T).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        if (id?.PropertyType == typeof(int) && (int)(id.GetValue(item) ?? 0) <= 0)
        {
            var next = this.Select(x => (int)(id.GetValue(x) ?? 0)).DefaultIfEmpty(0).Max() + 1;
            id.SetValue(item, next);
        }
        base.Add(item);
    }
    public new void AddRange(IEnumerable<T> items) { foreach (var item in items) Add(item); }
    public void RemoveRange(IEnumerable<T> items) { foreach (var item in items.ToList()) Remove(item); }
}

