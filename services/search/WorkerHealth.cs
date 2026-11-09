using System.Text.Json;

namespace SaveIT.Search;

public sealed record WorkerHealthSnapshot(int Schema, bool Ready, DateTimeOffset ObservedAt);

// No tenant IDs, queue counts, documents or connection details are written to the probe file.
public static class WorkerHealth
{
    public static void Report(string path, bool ready, TimeProvider? clock = null)
    {
        var full = Path.GetFullPath(path);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new WorkerHealthSnapshot(1, ready, (clock ?? TimeProvider.System).GetUtcNow())));
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool IsReady(string path, TimeProvider? clock = null) => Check(path,clock,true);
    public static bool IsLive(string path, TimeProvider? clock = null) => Check(path,clock,false);
    private static bool Check(string path, TimeProvider? clock, bool requireReady)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 1024) return false;
            var snapshot = JsonSerializer.Deserialize<WorkerHealthSnapshot>(File.ReadAllText(path));
            var age = (clock ?? TimeProvider.System).GetUtcNow() - snapshot!.ObservedAt;
            return snapshot.Schema == 1 && (!requireReady || snapshot.Ready) && age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(60);
        }
        catch (Exception) { return false; }
    }
}

public sealed class WorkerProgress(string path, TimeProvider? clock = null)
{
    private readonly HashSet<Guid> failures = [];
    public void Success(Guid tenant)
    {
        failures.Remove(tenant);
        WorkerHealth.Report(path, failures.Count == 0, clock);
    }
    public void Failure(Guid tenant)
    {
        failures.Add(tenant);
        WorkerHealth.Report(path, false, clock);
    }
}
