using System.Collections.Concurrent;
using System.Text.Json;
using Seerlens.Evals;

namespace Seerlens.Collector;

// Holds the golden sets in ./evals next to the binary. Loaded at startup, and now
// writable too: the dashboard can create, edit and delete sets so you don't have
// to hand-edit JSON.
public sealed class GoldenSets
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly string _dir;
    readonly ConcurrentDictionary<string, GoldenSet> _sets = new(StringComparer.OrdinalIgnoreCase);

    public GoldenSets() : this(Path.Combine(AppContext.BaseDirectory, "evals")) { }

    public GoldenSets(string dir)
    {
        _dir = dir;
        Reload();
    }

    public void Reload()
    {
        _sets.Clear();
        if (!Directory.Exists(_dir)) return;

        foreach (var file in Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var set = GoldenSet.Load(file);
                _sets[set.Name] = set;
            }
            catch
            {
                // skip anything that isn't a valid golden set
            }
        }
    }

    public IReadOnlyCollection<string> Names => _sets.Keys.ToArray();

    public string Dir => _dir;

    public GoldenSet? Get(string name) => _sets.GetValueOrDefault(name);

    public void Save(GoldenSet set)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(set.Name), JsonSerializer.Serialize(set, Json));
        _sets[set.Name] = set;
    }

    public bool Delete(string name)
    {
        if (!_sets.TryRemove(name, out _)) return false;
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
        return true;
    }

    // Keep the set name from escaping the evals dir: drop any path parts, scrub
    // invalid chars, and never let it resolve to "." or "..".
    string PathFor(string name)
    {
        var bare = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(bare.Select(c => invalid.Contains(c) ? '_' : c)).Trim('.', ' ');
        if (safe.Length == 0) safe = "set";
        return Path.Combine(_dir, $"{safe}.json");
    }
}
