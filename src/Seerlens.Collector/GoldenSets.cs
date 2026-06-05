using Seerlens.Evals;

namespace Seerlens.Collector;

// Loads the golden sets shipped next to the binary (./evals/*.json). Drop a JSON
// file in there and it shows up as a set you can run from the dashboard.
public sealed class GoldenSets
{
    readonly Dictionary<string, GoldenSet> _sets = new(StringComparer.OrdinalIgnoreCase);

    public GoldenSets()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "evals");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
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

    public IReadOnlyCollection<string> Names => _sets.Keys;

    public GoldenSet? Get(string name) => _sets.GetValueOrDefault(name);
}
