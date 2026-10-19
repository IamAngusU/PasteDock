using System.Text;

namespace PasteDock.Core.Services;

public enum DiffLineKind { Unchanged, Added, Removed }

public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record TextDiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, int Unchanged, bool UsedFallback)
{
    public int Changed => Added + Removed;

    public string ToUnifiedText(string leftLabel = "left", string rightLabel = "right")
    {
        static string SafeLabel(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var builder = new StringBuilder();
        builder.Append("--- ").Append(SafeLabel(leftLabel)).Append('\n');
        builder.Append("+++ ").Append(SafeLabel(rightLabel)).Append('\n');
        builder.Append("@@\n");
        foreach (var line in Lines)
        {
            builder.Append(line.Kind switch
            {
                DiffLineKind.Added => '+',
                DiffLineKind.Removed => '-',
                _ => ' '
            });
            builder.Append(line.Text).Append('\n');
        }
        return builder.ToString();
    }
}

/// <summary>
/// Bounded line diff for interactive clipboard comparisons. Myers is used for normal edits;
/// very large edit distances deliberately fall back to a safe replace block instead of
/// allocating unbounded trace state or stalling the UI.
/// </summary>
public static class TextDiff
{
    public const int MaxCharactersPerSide = 1_000_000;
    public const int MaxLinesPerSide = 20_000;
    public const int MaxEditDistance = 512;

    public static TextDiffResult Compare(string left, string right, bool ignoreWhitespace = false)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Length > MaxCharactersPerSide || right.Length > MaxCharactersPerSide)
            throw new InvalidOperationException($"Diff is limited to {MaxCharactersPerSide:N0} characters per side to keep comparisons responsive.");

        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);
        if (leftLines.Length > MaxLinesPerSide || rightLines.Length > MaxLinesPerSide)
            throw new InvalidOperationException($"Diff is limited to {MaxLinesPerSide:N0} lines per side to keep comparisons responsive.");

        var leftKeys = ignoreWhitespace ? leftLines.Select(NormalizeWhitespace).ToArray() : leftLines;
        var rightKeys = ignoreWhitespace ? rightLines.Select(NormalizeWhitespace).ToArray() : rightLines;

        var prefix = 0;
        while (prefix < leftLines.Length && prefix < rightLines.Length &&
               string.Equals(leftKeys[prefix], rightKeys[prefix], StringComparison.Ordinal)) prefix++;

        var suffix = 0;
        while (suffix < leftLines.Length - prefix && suffix < rightLines.Length - prefix &&
               string.Equals(leftKeys[leftLines.Length - 1 - suffix], rightKeys[rightLines.Length - 1 - suffix], StringComparison.Ordinal)) suffix++;

        var lines = new List<DiffLine>(leftLines.Length + rightLines.Length);
        for (var i = 0; i < prefix; i++) lines.Add(new(DiffLineKind.Unchanged, leftLines[i]));

        var middleLeft = leftLines[prefix..(leftLines.Length - suffix)];
        var middleRight = rightLines[prefix..(rightLines.Length - suffix)];
        var middleLeftKeys = leftKeys[prefix..(leftKeys.Length - suffix)];
        var middleRightKeys = rightKeys[prefix..(rightKeys.Length - suffix)];
        var middle = Myers(middleLeft, middleRight, middleLeftKeys, middleRightKeys);
        var fallback = middle is null;
        if (middle is null)
        {
            foreach (var line in middleLeft) lines.Add(new(DiffLineKind.Removed, line));
            foreach (var line in middleRight) lines.Add(new(DiffLineKind.Added, line));
        }
        else lines.AddRange(middle);

        for (var i = suffix; i > 0; i--) lines.Add(new(DiffLineKind.Unchanged, leftLines[leftLines.Length - i]));

        return new(
            lines,
            lines.Count(line => line.Kind == DiffLineKind.Added),
            lines.Count(line => line.Kind == DiffLineKind.Removed),
            lines.Count(line => line.Kind == DiffLineKind.Unchanged),
            fallback);
    }

    private static DiffLine[]? Myers(string[] left, string[] right, string[] leftKeys, string[] rightKeys)
    {
        var n = left.Length;
        var m = right.Length;
        if (n == 0) return right.Select(line => new DiffLine(DiffLineKind.Added, line)).ToArray();
        if (m == 0) return left.Select(line => new DiffLine(DiffLineKind.Removed, line)).ToArray();

        var max = n + m;
        var limit = Math.Min(max, MaxEditDistance);
        var offset = limit + 1;
        var size = checked(limit * 2 + 3);
        var v = Enumerable.Repeat(-1, size).ToArray();
        v[offset + 1] = 0;
        var trace = new List<int[]>(Math.Min(limit + 1, 256));

        for (var d = 0; d <= limit; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                var index = offset + k;
                int x;
                if (k == -d || (k != d && v[index - 1] < v[index + 1])) x = v[index + 1];
                else x = v[index - 1] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(leftKeys[x], rightKeys[y], StringComparison.Ordinal)) { x++; y++; }
                v[index] = x;
                if (x >= n && y >= m) return Backtrack(trace, left, right, offset, d);
            }
        }
        return null;
    }

    private static DiffLine[] Backtrack(IReadOnlyList<int[]> trace, string[] left, string[] right, int offset, int distance)
    {
        var result = new List<DiffLine>(left.Length + right.Length);
        var x = left.Length;
        var y = right.Length;

        for (var d = distance; d >= 0; d--)
        {
            if (d == 0)
            {
                while (x > 0 && y > 0)
                {
                    result.Add(new(DiffLineKind.Unchanged, left[x - 1]));
                    x--; y--;
                }
                break;
            }
            var v = trace[d];
            var k = x - y;
            var index = offset + k;
            var previousK = k == -d || (k != d && v[index - 1] < v[index + 1]) ? k + 1 : k - 1;
            var previousX = v[offset + previousK];
            var previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                result.Add(new(DiffLineKind.Unchanged, left[x - 1]));
                x--; y--;
            }

            if (x == previousX)
            {
                result.Add(new(DiffLineKind.Added, right[y - 1]));
                y--;
            }
            else
            {
                result.Add(new(DiffLineKind.Removed, left[x - 1]));
                x--;
            }
        }

        result.Reverse();
        return result.ToArray();
    }

    private static string[] SplitLines(string text) => text.Length == 0
        ? []
        : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = builder.Length > 0; continue; }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(character);
        }
        return builder.ToString();
    }
}
