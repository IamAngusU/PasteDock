using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

/// <summary>
/// Chooses a useful extension for temporary text files using deliberately conservative,
/// local heuristics. Unknown or ambiguous content stays .txt rather than pretending to
/// know a language.
/// </summary>
public static class TextFileFormatGuesser
{
    public static string GuessExtension(string text, ClipKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (kind == ClipKind.Json) return ".json";
        if (kind != ClipKind.Code) return ".txt";

        var sample = text.Length > 32_768 ? text[..32_768] : text;
        var trimmed = sample.TrimStart();
        if (trimmed.StartsWith("<?php", StringComparison.OrdinalIgnoreCase)) return ".php";
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return ".xml";
        if (trimmed.StartsWith("#!/", StringComparison.Ordinal))
        {
            var firstLine = trimmed.Split('\n', 2)[0];
            if (firstLine.Contains("python", StringComparison.OrdinalIgnoreCase)) return ".py";
            if (firstLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase) || firstLine.Contains("powershell", StringComparison.OrdinalIgnoreCase)) return ".ps1";
            if (firstLine.Contains("bash", StringComparison.OrdinalIgnoreCase) || firstLine.Contains("/sh", StringComparison.OrdinalIgnoreCase)) return ".sh";
        }

        if (LooksLikeSql(sample)) return ".sql";
        if (LooksLikePowerShell(sample)) return ".ps1";
        if (LooksLikeCSharp(sample)) return ".cs";
        if (LooksLikePython(sample)) return ".py";
        if (LooksLikeCss(sample)) return ".css";
        if (LooksLikeHtml(sample)) return ".html";
        if (LooksLikeTypeScript(sample)) return ".ts";
        if (LooksLikeJavaScript(sample)) return ".js";
        return ".txt";
    }

    private static bool LooksLikeSql(string value)
    {
        var upper = value.ToUpperInvariant();
        return (upper.Contains("SELECT ", StringComparison.Ordinal) && upper.Contains(" FROM ", StringComparison.Ordinal))
            || upper.Contains("CREATE TABLE ", StringComparison.Ordinal)
            || upper.Contains("INSERT INTO ", StringComparison.Ordinal)
            || upper.Contains("ALTER TABLE ", StringComparison.Ordinal);
    }

    private static bool LooksLikePowerShell(string value) =>
        value.Contains("param(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("$ErrorActionPreference", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Write-Host", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Get-ChildItem", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Set-StrictMode", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCSharp(string value) =>
        value.Contains("using System", StringComparison.Ordinal)
        || value.Contains("namespace ", StringComparison.Ordinal)
        || value.Contains("public sealed class ", StringComparison.Ordinal)
        || value.Contains("public static class ", StringComparison.Ordinal)
        || value.Contains("record ", StringComparison.Ordinal) && value.Contains('{');

    private static bool LooksLikePython(string value) =>
        value.Contains("def ", StringComparison.Ordinal)
        || value.Contains("from ", StringComparison.Ordinal) && value.Contains(" import ", StringComparison.Ordinal)
        || value.Contains("if __name__ == ", StringComparison.Ordinal);

    private static bool LooksLikeCss(string value) =>
        value.Contains(":root", StringComparison.OrdinalIgnoreCase)
        || value.Contains("@media ", StringComparison.OrdinalIgnoreCase)
        || value.Contains("@keyframes ", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeHtml(string value) =>
        value.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase)
        || value.Contains("<html", StringComparison.OrdinalIgnoreCase)
        || value.Contains("</div>", StringComparison.OrdinalIgnoreCase)
        || value.Contains("</body>", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTypeScript(string value) =>
        value.Contains("interface ", StringComparison.Ordinal)
        || value.Contains("type ", StringComparison.Ordinal) && value.Contains(" = ", StringComparison.Ordinal)
        || value.Contains(" as const", StringComparison.Ordinal)
        || value.Contains("satisfies ", StringComparison.Ordinal);

    private static bool LooksLikeJavaScript(string value) =>
        value.Contains("function ", StringComparison.Ordinal)
        || value.Contains("const ", StringComparison.Ordinal)
        || value.Contains("let ", StringComparison.Ordinal)
        || value.Contains("console.", StringComparison.Ordinal);
}
