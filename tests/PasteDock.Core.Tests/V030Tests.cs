using PasteDock.Core.Models;
using PasteDock.Core.Search;
using PasteDock.Core.Services;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static void RegisterV030()
    {
        Test("Plain-text target rules normalize executable names", () =>
        {
            var settings = (new AppSettings { PlainTextPasteApps = [" WindowsTerminal.exe ", "windowsterminal", "Code.exe"] }).Normalize();
            Equal("WindowsTerminal,Code", string.Join(',', settings.PlainTextPasteApps));
        });
        Test("Power search parses protected OCR copies and before", () =>
        {
            var query = SearchQuery.Parse("is:protected has:ocr copies:3 before:2026-06-01");
            True(query.ProtectedOnly); True(query.OcrOnly); Equal(3, query.MinCopies!.Value);
            Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), query.Before!.Value);
        });
        Test("Invalid copies filter remains searchable literal text", () =>
        {
            var query = SearchQuery.Parse("copies:0 copies:nope");
            Equal("copies:0,copies:nope", string.Join(',', query.Terms));
            True(query.MinCopies is null);
        });
        Test("Before filter is an exclusive day boundary", () =>
        {
            var entry = Entry() with { LastCopiedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero) };
            True(!SearchMatcher.MatchesMetadata(entry, SearchQuery.Parse("before:2026-06-01")));
            True(SearchMatcher.MatchesMetadata(entry, SearchQuery.Parse("before:2026-06-02")));
        });
        Test("Protected OCR and copy count filters are all conjunctive", () =>
        {
            var entry = Entry() with { IsFavorite = true, HasOcr = true, CopyCount = 4 };
            True(SearchMatcher.MatchesMetadata(entry, SearchQuery.Parse("is:protected has:ocr copies:4")));
            True(!SearchMatcher.MatchesMetadata(entry with { HasOcr = false }, SearchQuery.Parse("is:protected has:ocr copies:4")));
            True(!SearchMatcher.MatchesMetadata(entry with { CopyCount = 3 }, SearchQuery.Parse("is:protected has:ocr copies:4")));
        });
        Test("Temporary file format keeps ambiguous text as txt", () =>
            Equal(".txt", TextFileFormatGuesser.GuessExtension("hello world", ClipKind.Text)));
        Test("Temporary file format recognizes developer languages conservatively", () =>
        {
            Equal(".json", TextFileFormatGuesser.GuessExtension("{\"ok\":true}", ClipKind.Json));
            Equal(".cs", TextFileFormatGuesser.GuessExtension("using System;\npublic sealed class Demo {}", ClipKind.Code));
            Equal(".py", TextFileFormatGuesser.GuessExtension("def run():\n    return True", ClipKind.Code));
            Equal(".sql", TextFileFormatGuesser.GuessExtension("SELECT id FROM users WHERE id = 1;", ClipKind.Code));
            Equal(".ps1", TextFileFormatGuesser.GuessExtension("$ErrorActionPreference = 'Stop'\nWrite-Host 'ok'", ClipKind.Code));
            Equal(".ts", TextFileFormatGuesser.GuessExtension("interface User { id: number }", ClipKind.Code));
        });
        Test("Diff identical content has only unchanged lines", () =>
        {
            var result = TextDiff.Compare("alpha\nbeta", "alpha\nbeta");
            Equal(0, result.Added); Equal(0, result.Removed); Equal(2, result.Unchanged); True(!result.UsedFallback);
        });
        Test("Diff replacement records removal and addition", () =>
        {
            var result = TextDiff.Compare("alpha\nold\nomega", "alpha\nnew\nomega");
            Equal(1, result.Added); Equal(1, result.Removed); Equal(2, result.Unchanged);
            True(result.Lines.Any(line => line.Kind == DiffLineKind.Removed && line.Text == "old"));
            True(result.Lines.Any(line => line.Kind == DiffLineKind.Added && line.Text == "new"));
        });
        Test("Diff insertion does not manufacture a removal", () =>
        {
            var result = TextDiff.Compare("a\nc", "a\nb\nc");
            Equal(1, result.Added); Equal(0, result.Removed); Equal(2, result.Unchanged);
        });
        Test("Diff empty side has no phantom blank removal", () =>
        {
            var result = TextDiff.Compare("", "first");
            Equal(1, result.Added); Equal(0, result.Removed); Equal(0, result.Unchanged);
            Equal("first", result.Lines.Single().Text);
        });
        Test("Diff normalizes CRLF and LF", () =>
        {
            var result = TextDiff.Compare("a\r\nb", "a\nb");
            Equal(0, result.Changed); Equal(2, result.Unchanged);
        });
        Test("Whitespace-insensitive diff compares normalized line content", () =>
        {
            var strict = TextDiff.Compare("  alpha   beta  ", "alpha beta");
            var relaxed = TextDiff.Compare("  alpha   beta  ", "alpha beta", ignoreWhitespace: true);
            True(strict.Changed > 0); Equal(0, relaxed.Changed); Equal(1, relaxed.Unchanged);
        });
        Test("Unified diff sanitizes labels but preserves line prefixes", () =>
        {
            var output = TextDiff.Compare("a", "b").ToUnifiedText("left\r\nname", "right\nname");
            True(output.StartsWith("--- left  name", StringComparison.Ordinal));
            True(output.Contains("\n-a\n+b", StringComparison.Ordinal));
        });
        Test("Large edit distance uses bounded fallback", () =>
        {
            var left = string.Join('\n', Enumerable.Range(0, TextDiff.MaxEditDistance + 4).Select(i => $"left-{i}"));
            var right = string.Join('\n', Enumerable.Range(0, TextDiff.MaxEditDistance + 4).Select(i => $"right-{i}"));
            var result = TextDiff.Compare(left, right);
            True(result.UsedFallback); Equal(TextDiff.MaxEditDistance + 4, result.Removed); Equal(TextDiff.MaxEditDistance + 4, result.Added);
        });
        Test("Diff character budget is explicit", () =>
        {
            Throws<InvalidOperationException>(() => TextDiff.Compare(new string('x', TextDiff.MaxCharactersPerSide + 1), "x"));
        });
        Test("Diff line budget is explicit", () =>
        {
            var tooMany = string.Join('\n', Enumerable.Repeat("x", TextDiff.MaxLinesPerSide + 1));
            Throws<InvalidOperationException>(() => TextDiff.Compare(tooMany, "x"));
        });
    }
}
