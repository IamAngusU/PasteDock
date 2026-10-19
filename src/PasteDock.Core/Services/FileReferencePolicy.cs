namespace PasteDock.Core.Services;

/// <summary>File clips refer to existing files or directories; they do not contain a backup.</summary>
public static class FileReferencePolicy
{
    public static void EnsureAvailable(IEnumerable<string> paths, Func<string, bool>? exists = null,
        CancellationToken cancellationToken = default)
    {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        var missing = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!exists(path)) missing++;
        }
        if (missing > 0)
            throw new InvalidOperationException($"{missing} file reference{(missing == 1 ? " is" : "s are")} no longer available. " +
                "Restore the original files or copy the paths as plain text. The clipboard was not changed.");
    }
}
