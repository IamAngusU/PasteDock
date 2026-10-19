using PasteDock.Desktop.Interop;

namespace PasteDock.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    public AsyncRelayCommand PasteAsFileCommand { get; }
    public AsyncRelayCommand ExcludeSourceCommand { get; }
    public AsyncRelayCommand TogglePlainTargetCommand { get; }
    public RelayCommand RequestSourceDeleteCommand { get; }

    public string SelectedSourceApp
    {
        get
        {
            var ids = SelectedIds().ToHashSet();
            var sources = _workspace.History.Snapshot.Where(entry => ids.Contains(entry.Id))
                .Select(entry => entry.SourceApp).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            return sources.Length == 1 ? sources[0] : "";
        }
    }

    public string ExcludeSourceLabel => CanManageSource() ? $"Never capture from {SelectedSourceApp}" : "Never capture from source app";
    public string DeleteSourceLabel => CanManageSource() ? $"Delete history from {SelectedSourceApp}..." : "Delete history from source app...";


    public string TargetPlainRuleLabel
    {
        get
        {
            if (!CanManageTarget()) return "Always paste plain text into target app";
            var app = NormalizeProcessName(_target!.AppName);
            var active = _workspace.Settings.PlainTextPasteApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            return active ? $"Use original formatting in {app}" : $"Always paste plain text into {app}";
        }
    }

    private bool CanManageTarget()
    {
        if (IsWorking || IsEditing || _workspace.Clipboard.IsLocked || _target is null) return false;
        var app = NormalizeProcessName(_target.AppName);
        return app.Length > 0
            && !app.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            && !app.Equals("PasteDock", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string? value) => Path.GetFileNameWithoutExtension((value ?? "").Trim());

    private async Task TogglePlainTargetAsync()
    {
        if (!CanManageTarget()) return;
        var app = NormalizeProcessName(_target!.AppName);
        var current = _workspace.Settings.PlainTextPasteApps;
        var removing = current.Contains(app, StringComparer.OrdinalIgnoreCase);
        var nextRules = removing
            ? current.Where(value => !value.Equals(app, StringComparison.OrdinalIgnoreCase)).ToArray()
            : current.Concat([app]).ToArray();
        var next = (_workspace.Settings with { PlainTextPasteApps = nextRules }).Normalize();
        await _workspace.SaveSettingsAsync(next);
        if (SettingsOpen) Settings.Load(next);
        Raise(nameof(TargetPlainRuleLabel));
        Notify(removing ? $"{app} will receive original clipboard formatting again."
            : $"Text-capable clips will automatically paste as plain text into {app}.");
    }

    private bool ShouldForcePlain(PasteTarget? target, IReadOnlyCollection<Guid> ids)
    {
        if (target is null || ids.Count == 0
            || !_workspace.Settings.PlainTextPasteApps.Contains(NormalizeProcessName(target.AppName), StringComparer.OrdinalIgnoreCase))
            return false;
        var selected = ids.ToHashSet();
        var entries = _workspace.History.Snapshot.Where(entry => selected.Contains(entry.Id)).ToArray();
        return entries.Length == ids.Count && entries.All(IsTextCapable);
    }

    private static bool IsTextCapable(ClipEntry entry) => entry.Kind != ClipKind.Files
        && (entry.Kind != ClipKind.Image || entry.HasOcr);

    private bool CanManageSource()
    {
        var source = SelectedSourceApp;
        return !IsWorking && !IsEditing && !_workspace.Clipboard.IsLocked && !string.IsNullOrWhiteSpace(source)
            && !source.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("PasteDock", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanPasteAsFile()
    {
        var ids = SelectedIds();
        if (ids.Length != 1 || IsWorking || IsEditing || _workspace.Clipboard.IsLocked) return false;
        var entry = _workspace.History.Snapshot.FirstOrDefault(item => item.Id == ids[0]);
        return entry is not null && entry.Kind != ClipKind.Files;
    }

    private async Task ExcludeSourceAsync()
    {
        var source = SelectedSourceApp;
        if (!CanManageSource()) return;
        var normalized = NormalizeProcessName(source);
        if (_workspace.Settings.ExcludedApps.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            Notify($"{source} is already excluded from future capture.");
            return;
        }
        var next = (_workspace.Settings with { ExcludedApps = _workspace.Settings.ExcludedApps.Concat([normalized]).ToArray() }).Normalize();
        await _workspace.SaveSettingsAsync(next);
        if (SettingsOpen) Settings.Load(next);
        Raise(nameof(ExcludeSourceLabel));
        Notify($"Future copies from {source} will be ignored. Existing history was left untouched.");
    }

    private void RequestDeleteSource()
    {
        var source = SelectedSourceApp;
        if (!CanManageSource()) return;
        var ids = _workspace.History.Snapshot
            .Where(entry => entry.SourceApp.Equals(source, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Id).ToArray();
        RequestDeletion(ids, $"Removes every stored clip captured from {source}, including pins and favorites. "
            + "Delete keeps one encrypted undo copy for ten minutes, up to 128 MiB. The system clipboard is unchanged.");
    }

    private async Task PasteAsFileAsync()
    {
        var ids = SelectedIds();
        if (ids.Length != 1) throw new InvalidOperationException("Paste as file works with one clip at a time.");
        var id = ids[0];
        var entry = _workspace.History.Snapshot.FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException();
        var target = _target;
        IsWorking = true;
        try
        {
            var payload = await Task.Run(() => _workspace.History.LoadAsync(id));
            var path = await _workspace.PasteFiles.MaterializeAsync(payload, entry.Kind);
            var filePayload = new ClipPayload { Files = [path] };
            var result = await _workspace.Paste.PasteAsync(filePayload, target, _workspace.Settings.PreventCloudAndSystemHistory, () => HideRequested?.Invoke());
            if (result.InputSent)
                await Task.Run(() => _workspace.History.UpdateMetadataAsync([id], clip => clip with { LastPastedAt = DateTimeOffset.UtcNow }));
            else ShowRequested?.Invoke();
            var extension = Path.GetExtension(path);
            Notify(result.InputSent
                ? $"Pasted as a temporary {extension} file. It becomes eligible for PasteDock cleanup after 24 hours so the target has time to finish using it."
                : result.Message + $" A temporary {extension} file is ready and becomes cleanup-eligible after 24 hours.");
        }
        finally { IsWorking = false; }
    }

    public async Task PasteVisibleIndexAsync(int index)
    {
        if (index < 0 || index >= Items.Count || index >= 9 || IsWorking || IsEditing || _workspace.Clipboard.IsLocked) return;
        var item = Items[index];
        IsWorking = true;
        try
        {
            var target = _target;
            var payload = await Task.Run(() => _workspace.History.LoadAsync(item.Id));
            var forcePlain = ShouldForcePlain(target, [item.Id]);
            if (forcePlain) payload = new ClipPayload { Text = payload.GetPlainText() };
            var result = await _workspace.Paste.PasteAsync(payload, target, _workspace.Settings.PreventCloudAndSystemHistory, () => HideRequested?.Invoke());
            Notify(forcePlain && result.InputSent ? result.Message + $" Plain-text rule applied for {target!.AppName}." : result.Message);
            if (result.InputSent)
                await Task.Run(() => _workspace.History.UpdateMetadataAsync([item.Id], clip => clip with { LastPastedAt = DateTimeOffset.UtcNow }));
            else ShowRequested?.Invoke();
        }
        catch (Exception exception) { ReportError(exception); }
        finally { IsWorking = false; }
    }
}
