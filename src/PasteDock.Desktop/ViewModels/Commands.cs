using System.Windows.Input;

namespace PasteDock.Desktop.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<bool>? _canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null) : this(_ => execute(), canExecute) { }
    public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) _execute(parameter); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Action<Exception> _error;
    private readonly Func<bool>? _canExecute;
    private bool _running;
    public AsyncRelayCommand(Func<Task> execute, Action<Exception> error, Func<bool>? canExecute = null)
    { _execute = execute; _error = error; _canExecute = canExecute; }
    public bool IsRunning => _running;
    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        Refresh();
        try { await _execute(); }
        catch (OperationCanceledException exception) { _error(exception); }
        catch (Exception exception) { _error(exception); }
        finally { _running = false; Refresh(); }
    }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
