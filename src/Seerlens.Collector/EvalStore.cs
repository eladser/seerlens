using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Seerlens.Collector;

// What an eval runner posts to /eval/runs.
public record EvalRunIn(
    string Id, string Set, string Target, string Scorer, long CreatedAt, double Score, List<EvalCaseIn> Cases);

public record EvalCaseIn(string Input, string Answer, double Score);

// Row on the trend; no per-case detail so the list stays light.
public record EvalRunSummary(
    string Id, string Set, string Target, string Scorer, long CreatedAt, double Score, int CaseCount);

public record EvalRunDetail(EvalRunSummary Run, IReadOnlyList<EvalCaseIn> Cases);

public sealed class EvalStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly string _conn;

    public EvalStore(string connectionString)
    {
        _conn = connectionString;
        using var db = Open();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            create table if not exists eval_runs (
                id text primary key,
                set_name text not null,
                target text not null,
                scorer text not null,
                created_at integer not null,
                score real not null,
                cases_json text not null
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static EvalStore ForFile(string path) => new($"Data Source={path}");

    public EvalRunSummary Add(EvalRunIn run)
    {
        using var db = Open();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            insert or replace into eval_runs (id, set_name, target, scorer, created_at, score, cases_json)
            values ($id, $set, $target, $scorer, $created, $score, $cases);
            """;
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$set", run.Set);
        cmd.Parameters.AddWithValue("$target", run.Target);
        cmd.Parameters.AddWithValue("$scorer", run.Scorer);
        cmd.Parameters.AddWithValue("$created", run.CreatedAt);
        cmd.Parameters.AddWithValue("$score", run.Score);
        cmd.Parameters.AddWithValue("$cases", JsonSerializer.Serialize(run.Cases, Json));
        cmd.ExecuteNonQuery();

        return new EvalRunSummary(run.Id, run.Set, run.Target, run.Scorer, run.CreatedAt, run.Score, run.Cases.Count);
    }

    // Oldest first, so the dashboard can plot the trend left to right.
    public List<EvalRunSummary> List(string? set = null)
    {
        using var db = Open();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            select id, set_name, target, scorer, created_at, score,
                   json_array_length(cases_json)
            from eval_runs
            where ($set is null or set_name = $set)
            order by created_at;
            """;
        cmd.Parameters.AddWithValue("$set", (object?)set ?? DBNull.Value);

        var rows = new List<EvalRunSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new EvalRunSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetInt64(4), r.GetDouble(5), r.GetInt32(6)));
        return rows;
    }

    public EvalRunDetail? Get(string id)
    {
        using var db = Open();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            select id, set_name, target, scorer, created_at, score, cases_json
            from eval_runs where id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var cases = JsonSerializer.Deserialize<List<EvalCaseIn>>(r.GetString(6), Json) ?? [];
        var summary = new EvalRunSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.GetInt64(4), r.GetDouble(5), cases.Count);
        return new EvalRunDetail(summary, cases);
    }

    SqliteConnection Open()
    {
        var db = new SqliteConnection(_conn);
        db.Open();
        return db;
    }
}
