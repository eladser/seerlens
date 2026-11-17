using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Seerlens.Collector;

// T is "eq" | "del" | "ins" | "mod" | "skip". Word pieces are only set on mod rows.
public record TextRow(
    string T,
    int? ALine,
    int? BLine,
    string? A,
    string? B,
    IReadOnlyList<WordPiece>? AWords = null,
    IReadOnlyList<WordPiece>? BWords = null,
    int? Skipped = null);

// T is "eq" | "del" | "ins".
public record WordPiece(string T, string Text);

public record TextDiffResult(IReadOnlyList<TextRow> Rows, bool TooLarge = false, int? SizeA = null, int? SizeB = null);

public static class TextDiff
{
    public const int MaxChars = 200_000;
    const int Keep = 3;
    public const int MaxWordLine = 5_000;

    // a line past MaxWordLine stays one chunk, so the builder has no word-level work to do on it
    sealed class WordChunker : IChunker
    {
        public IReadOnlyList<string> Chunk(string str) => str.Length > MaxWordLine ? [str] : DiffPlex.Chunkers.WordChunker.Instance.Chunk(str);
    }

    public static TextDiffResult Diff(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        if (a.Length > MaxChars || b.Length > MaxChars)
            return new TextDiffResult([], true, a.Length, b.Length);
        if (a.Length == 0 && b.Length == 0)
            return new TextDiffResult([]);

        var model = SideBySideDiffBuilder.Diff(Differ.Instance, a, b, false, false, LineChunker.Instance, new WordChunker());
        var rows = new List<TextRow>(model.OldText.Lines.Count);
        for (var i = 0; i < model.OldText.Lines.Count; i++)
            rows.Add(ToRow(model.OldText.Lines[i], model.NewText.Lines[i]));
        return new TextDiffResult(Collapse(rows));
    }

    static TextRow ToRow(DiffPiece o, DiffPiece n)
    {
        if (o.Type == ChangeType.Unchanged)
            return new TextRow("eq", o.Position, n.Position, o.Text, n.Text);
        if (o.Type == ChangeType.Deleted)
            return new TextRow("del", o.Position, null, o.Text, null);
        if (n.Type == ChangeType.Inserted)
            return new TextRow("ins", null, n.Position, null, n.Text);
        if ((o.Text?.Length ?? 0) > MaxWordLine || (n.Text?.Length ?? 0) > MaxWordLine)
            return new TextRow("mod", o.Position, n.Position, o.Text, n.Text);
        return new TextRow("mod", o.Position, n.Position, o.Text, n.Text, Words(o), Words(n));
    }

    static List<WordPiece> Words(DiffPiece line) =>
        line.SubPieces
            .Where(p => p.Type != ChangeType.Imaginary)
            .Select(p => new WordPiece(
                p.Type switch { ChangeType.Deleted => "del", ChangeType.Inserted => "ins", _ => "eq" },
                p.Text ?? ""))
            .ToList();

    static List<TextRow> Collapse(List<TextRow> rows)
    {
        var res = new List<TextRow>(rows.Count);
        for (var i = 0; i < rows.Count;)
        {
            if (rows[i].T != "eq") { res.Add(rows[i++]); continue; }
            var j = i;
            while (j < rows.Count && rows[j].T == "eq") j++;
            var run = j - i;
            if (run > 2 * Keep)
            {
                for (var k = i; k < i + Keep; k++) res.Add(rows[k]);
                res.Add(new TextRow("skip", null, null, null, null, Skipped: run - 2 * Keep));
                for (var k = j - Keep; k < j; k++) res.Add(rows[k]);
            }
            else
                for (var k = i; k < j; k++) res.Add(rows[k]);
            i = j;
        }
        return res;
    }
}
