using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PasteDock.Desktop.Interop;
using PasteDock.Desktop.ViewModels;
using Forms = System.Windows.Forms;

namespace PasteDock.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private bool _allowClose;
    private bool _reducedMotion;
    private bool _positioned;

    internal MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SizeChanged += (_, _) =>
        {
            BrandTagline.Visibility = BrandDivider.Visibility = ActualWidth < 1010 ? Visibility.Collapsed : Visibility.Visible;
            // Keep a usable inspector when the user narrows a previously wide layout.
            var compact = ActualWidth < 840;
            NavigationPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            NavigationColumn.Width = new GridLength(compact ? 0 : 174);
            HistoryColumn.MinWidth = compact ? 230 : 270;
            var available = Math.Max(HistoryColumn.MinWidth, ActualWidth - (compact ? 0 : 174) - 12 - 302);
            if (HistoryColumn.ActualWidth > available) HistoryColumn.Width = new GridLength(available);
        };
        viewModel.HideRequested += HidePanel;
        viewModel.SearchFocusRequested += FocusSearch;
        viewModel.EditorFocusRequested += FocusEditor;
        PaletteSearch.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { viewModel.PaletteOpen = false; e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up)
            {
                var direction = e.Key == Key.Down ? 1 : -1;
                if (PaletteList.Items.Count > 0)
                {
                    PaletteList.SelectedIndex = Math.Clamp(PaletteList.SelectedIndex + direction, 0, PaletteList.Items.Count - 1);
                    PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (PaletteList.SelectedItem as PaletteAction ?? viewModel.PaletteItems.FirstOrDefault()) is { } action)
            { viewModel.ExecutePalette(action); e.Handled = true; }
        };
    }

    internal void ApplySettings(AppSettings settings, bool restoreSize = false)
    {
        Topmost = settings.KeepWindowOnTop;
        _reducedMotion = settings.ReducedMotion;
        if (restoreSize)
        {
            Width = settings.WindowWidth; Height = settings.WindowHeight;
            HistoryColumn.Width = new GridLength(settings.ListWidth);
        }
    }

    internal AppSettings WithCurrentGeometry(AppSettings settings) => (settings with
    {
        WindowWidth = WindowState == WindowState.Normal ? ActualWidth : RestoreBounds.Width,
        WindowHeight = WindowState == WindowState.Normal ? ActualHeight : RestoreBounds.Height,
        ListWidth = HistoryColumn.ActualWidth,
        HistorySort = _viewModel.SelectedSort.Sort, PreviewWrap = _viewModel.WrapText
    }).Normalize();

    internal void Reveal()
    {
        if (!IsVisible)
        {
            if (!_positioned) CenterOnCursorMonitor();
            Show();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _viewModel.OnShown();
        if (_viewModel.IsEditing) FocusEditor();
        else if (!_viewModel.SettingsOpen) FocusSearch();
    }

    private void CenterOnCursorMonitor()
    {
        // Screen uses physical pixels. Convert with the monitor's effective DPI before using WPF DIPs.
        var point = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(point).WorkingArea;
        var scale = NativeMethods.MonitorScale(point.X, point.Y);
        var width = area.Width / scale;
        var height = area.Height / scale;
        MinWidth = Math.Min(860, Math.Max(620, width - 24));
        MinHeight = Math.Min(560, Math.Max(420, height - 24));
        Width = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, width - 24));
        Height = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, height - 24));
        // Assign the physical monitor position first so the per-monitor WPF window picks up its DPI.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var targetWidth = (int)Math.Round(Width * scale);
        var targetHeight = (int)Math.Round(Height * scale);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            area.Left + (area.Width - targetWidth) / 2, area.Top + (area.Height - targetHeight) / 2,
            targetWidth, targetHeight, 0x0010 | 0x0004); // NOACTIVATE | NOZORDER
        _positioned = true;
    }

    internal void HidePanel()
    {
        MorePopup.IsOpen = false;
        _viewModel.ClosePopovers();
        // Drafts and the queue survive hiding the window. The close button is not Quit.
        BackupPassword.Clear();
        Hide();
    }

    internal void CloseForExit()
    {
        _allowClose = true;
        _viewModel.HideRequested -= HidePanel;
        _viewModel.SearchFocusRequested -= FocusSearch;
        _viewModel.EditorFocusRequested -= FocusEditor;
        Close();
    }

    private void FocusSearch() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { SearchBox.Focus(); SearchBox.SelectAll(); }));
    private void FocusEditor() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Editor.Focus()));
    private void HideClick(object sender, RoutedEventArgs e) => HidePanel();
    private void WindowClosing(object? sender, CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; HidePanel(); } }
    private void HistorySelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.SetSelection(HistoryList.SelectedItems.Cast<ClipItemViewModel>().ToArray());
    private void HistoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(HistoryList, origin) is ListBoxItem)
        { _viewModel.PasteCommand.Execute(null); e.Handled = true; }
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || ReferenceEquals(Keyboard.FocusedElement, HotkeyInput)) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers == ModifierKeys.Control;
        var shift = modifiers == ModifierKeys.Shift;
        var alt = modifiers == ModifierKeys.Alt;
        void Run(ICommand command) { command.Execute(null); e.Handled = true; }

        var focusedElement = Keyboard.FocusedElement;
        var quickPasteContext = !_viewModel.IsEditing && !_viewModel.SettingsOpen
            && (focusedElement is not TextBoxBase and not PasswordBox || ReferenceEquals(focusedElement, SearchBox));
        if (alt && quickPasteContext)
        {
            // Deliberately use the number row only. Alt+NumPad is reserved by Windows for character entry.
            var quickIndex = key switch
            {
                Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, Key.D5 => 4,
                Key.D6 => 5, Key.D7 => 6, Key.D8 => 7, Key.D9 => 8,
                _ => -1
            };
            if (quickIndex >= 0)
            {
                _ = _viewModel.PasteVisibleIndexAsync(quickIndex);
                e.Handled = true;
                return;
            }
        }
        if (key == Key.Escape)
        {
            if (MorePopup.IsOpen) MorePopup.IsOpen = false;
            else if (_viewModel.ClosePopovers()) { }
            else if (_viewModel.SettingsOpen) { BackupPassword.Clear(); _viewModel.SettingsOpen = false; }
            else if (_viewModel.IsEditing) _viewModel.CancelEditCommand.Execute(null);
            else HidePanel();
            e.Handled = true; return;
        }
        if (_viewModel.IsEditing)
        {
            if (ctrl && key == Key.Enter) Run(_viewModel.SaveEditCommand);
            return; // Never intercept normal editing shortcuts.
        }
        if (ctrl && key == Key.K) { Run(_viewModel.OpenPaletteCommand); return; }
        if (ctrl && key == Key.OemComma) { Run(_viewModel.OpenSettingsCommand); return; }
        if (ctrl && key == Key.OemQuestion) { Run(_viewModel.OpenHelpCommand); return; }
        if (_viewModel.SettingsOpen) return;
        if (key == Key.F2 && modifiers == ModifierKeys.None && (focusedElement is not TextBoxBase and not PasswordBox || ReferenceEquals(focusedElement, SearchBox)))
        { Run(_viewModel.RenameCommand); return; }
        if (ctrl && key == Key.F) { FocusSearch(); e.Handled = true; return; }
        if (ReferenceEquals(Keyboard.FocusedElement, SearchBox) && (key is Key.Down or Key.Up) && modifiers == ModifierKeys.None)
        {
            HistoryList.Focus();
            if (HistoryList.SelectedItem is null && HistoryList.Items.Count > 0) HistoryList.SelectedIndex = 0;
            if (HistoryList.SelectedItem is { } selected)
            {
                HistoryList.ScrollIntoView(selected);
                (HistoryList.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem)?.Focus();
            }
            e.Handled = true; return;
        }
        var typing = focusedElement is TextBoxBase or PasswordBox;
        if (typing)
        {
            if (ReferenceEquals(Keyboard.FocusedElement, SearchBox) && key == Key.Enter && modifiers is ModifierKeys.None or ModifierKeys.Shift)
                Run(shift ? _viewModel.PlainPasteCommand : _viewModel.PasteCommand);
            return; // Ctrl+C in the preview copies the highlighted text, not another clip.
        }
        if (key == Key.Enter && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        { Run(shift ? _viewModel.PlainPasteCommand : _viewModel.PasteCommand); return; }
        if (key == Key.Delete && modifiers == ModifierKeys.None) { Run(_viewModel.RequestDeleteCommand); return; }
        if (!ctrl) return;
        switch (key)
        {
            case Key.Z: Run(_viewModel.UndoDeleteCommand); break;
            case Key.C: Run(_viewModel.CopyCommand); break;
            case Key.P: Run(_viewModel.PinCommand); break;
            case Key.B: Run(_viewModel.FavoriteCommand); break;
            case Key.E: Run(_viewModel.EditCommand); break;
            case Key.N: Run(_viewModel.NewSnippetCommand); break;
            case Key.T: Run(_viewModel.OpenTagsCommand); break;
            case Key.Q: Run(_viewModel.QueueCommand); break;
            case Key.D: Run(_viewModel.CompareCommand); break;
            case Key.A: HistoryList.SelectAll(); e.Handled = true; break;
            case Key.Add: case Key.OemPlus: Run(_viewModel.ZoomTextInCommand); break;
            case Key.Subtract: case Key.OemMinus: Run(_viewModel.ZoomTextOutCommand); break;
            case Key.D0: _viewModel.PreviewFontSize = 13; e.Handled = true; break;
        }
    }

    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
        { _viewModel.Settings.Error = "The shortcut needs Ctrl or Alt, plus another key."; return; }
        var native = (uint)modifiers; // WPF ModifierKeys and RegisterHotKey use the same bit values.
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is < 0x20 or > 0xFE) return;
        if (native == 3 && virtualKey == 0x4E)
        { _viewModel.Settings.Error = "Ctrl+Alt+N is reserved for the paste queue."; return; }
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(((int)key >= (int)Key.A && (int)key <= (int)Key.Z) ? key.ToString() : KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString());
        _viewModel.Settings.SetHotkey(native, virtualKey, string.Join(" + ", parts));
        _viewModel.Settings.Error = "";
    }

    private void SettingsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) { BackupPassword?.Clear(); return; }
        if (_reducedMotion) { DrawerTranslate.BeginAnimation(TranslateTransform.XProperty, null); DrawerTranslate.X = 0; return; }
        DrawerTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private void ResizeSettings(object sender, DragDeltaEventArgs e)
        => SettingsDrawer.Width = Math.Clamp(SettingsDrawer.ActualWidth - e.HorizontalChange, 350, Math.Max(350, ActualWidth - 200));
    private async void ExportBackup(object sender, RoutedEventArgs e) { var passphrase = BackupPassword.Password; BackupPassword.Clear(); await _viewModel.BackupAsync(false, passphrase); }
    private async void ImportBackup(object sender, RoutedEventArgs e) { var passphrase = BackupPassword.Password; BackupPassword.Clear(); await _viewModel.BackupAsync(true, passphrase); }
    private void MoreClick(object sender, RoutedEventArgs e) { _viewModel.ClosePopovers(); MorePopup.IsOpen = !MorePopup.IsOpen; }
    private void CloseMore(object sender, RoutedEventArgs e) => MorePopup.IsOpen = false;
    private void PaletteOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { PaletteList.SelectedIndex = PaletteList.Items.Count > 0 ? 0 : -1; PaletteSearch.Focus(); }));
    private void RenameOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { RenameInput.Focus(); RenameInput.SelectAll(); }));
    private void RenamePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _viewModel.SaveRenameCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { _viewModel.CancelRenameCommand.Execute(null); e.Handled = true; }
    }
    private void PopoverPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { MorePopup.IsOpen = false; _viewModel.ClosePopovers(); e.Handled = true; }
        else if (e.Key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, TagInputBox))
        { _viewModel.AddTagsCommand.Execute(null); e.Handled = true; }
    }
    private void PaletteActionClick(object sender, RoutedEventArgs e) => _viewModel.PaletteOpen = false;
}
