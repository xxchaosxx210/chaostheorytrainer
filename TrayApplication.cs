using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ChaosTheoryTrainer;

// Reuses Splinter Assault's tray/status/single-instance architecture with separate CT identity.
internal sealed class TrayApplication : IDisposable
{
    internal const string ShowStatusEvent = @"Local\ChaosTheoryTrainer.ShowStatus";
    private readonly App _app;
    private readonly GameDetection _detection = new();
    private readonly TrainerModel _model = new();
    private readonly HealthBackend _health = new();
    private readonly GameMenuOverlay _overlay;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _game = new("Waiting for Chaos Theory");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Icon _waiting = CreateIcon(System.Drawing.Color.Gray), _connected = CreateIcon(System.Drawing.Color.FromArgb(181, 239, 101));
    private readonly EventWaitHandle _showStatus = new(false, EventResetMode.AutoReset, ShowStatusEvent);
    private readonly RegisteredWaitHandle _showWait;
    private Window? _status;
    private TextBlock? _statusText;
    private bool _disposed, _probing, _exiting, _healthBusy;
    private string? _healthError;
    public GameState State { get; private set; } = GameState.Waiting;
    public string? Installation => _detection.Executable;

    public TrayApplication(App app)
    {
        _app = app;
        _model.ToggleHealth = ToggleHealthAsync;
        _overlay = new GameMenuOverlay(_model, () => State);
        _icon = new Forms.NotifyIcon { Icon = _waiting, Text = "Chaos Theory Trainer / waiting", ContextMenuStrip = _menu };
        _game.Click += (_, _) => ShowStatus(); _menu.Items.Add(_game);
        _menu.Items.Add("Status…", null, (_, _) => ShowStatus());
        _menu.Items.Add("Launch Chaos Theory", null, (_, _) => Launch());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _icon.DoubleClick += (_, _) => ShowStatus();
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showStatus, (_, _) =>
        {
            if (!_disposed && !_app.Dispatcher.HasShutdownStarted)
                _app.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) ShowStatus(); }));
        }, null, Timeout.Infinite, false);
        _timer.Tick += Tick;
    }

    public void Start() { _overlay.Start(); _icon.Visible = true; ShowStatus(); _timer.Start(); Tick(null, EventArgs.Empty); }
    private async void Tick(object? sender, EventArgs e)
    {
        if (_disposed || _probing || _exiting) return;
        _probing = true;
        try
        {
            var state = await Task.Run(_detection.Probe);
            if (_disposed) return;
            State = state;
            if (_health.HasSession && !_healthBusy)
            {
                try
                {
                    if (state.Pid != 0 && state.FingerprintMatched && state.Window != IntPtr.Zero)
                        await _health.PollAsync(state);
                    else await _health.ShutdownAsync();
                    _healthError = null;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
                { _healthError = ex.Message; }
            }
            if (_disposed) return;
            _model.SetHealth(_health.State.Enabled, !_healthBusy && !_exiting && _health.CanToggle(state));
            _game.Text = state.Status;
            _icon.Icon = state.Pid != 0 && state.FingerprintMatched ? _connected : _waiting;
            _icon.Text = state.Pid != 0 ? "Chaos Theory Trainer / F6 menu" : "Chaos Theory Trainer / waiting";
            UpdateStatus();
        }
        finally { _probing = false; }
    }

    private async Task<string> ToggleHealthAsync()
    {
        if (_healthBusy || _exiting) return "A health/shutdown request is already running.";
        _healthBusy = true;
        _model.SetHealth(_health.State.Enabled, false);
        try
        {
            var result = await _health.ToggleAsync(State);
            _healthError = null;
            return result.Detail;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        { _healthError = ex.Message; return ex.Message; }
        finally
        {
            _healthBusy = false;
            _model.SetHealth(_health.State.Enabled, !_exiting && _health.CanToggle(State));
            UpdateStatus();
        }
    }

    private async Task ExitAsync()
    {
        if (_disposed || _exiting) return;
        _exiting = true; _timer.Stop();
        _model.SetHealth(_health.State.Enabled, false);
        try
        {
            await _health.ShutdownAsync();
            _app.Shutdown();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            _healthError = ex.Message;
            _exiting = false; _timer.Start(); UpdateStatus();
            MessageBox.Show("The game has not acknowledged health restoration. The trainer remains open.\n\n" + ex.Message,
                "Chaos Theory Trainer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Launch()
    {
        try { _detection.Launch(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        { MessageBox.Show(ex.Message, "Chaos Theory Trainer"); }
    }

    public void ShowStatus()
    {
        if (_disposed) return;
        if (_status is null)
        {
            var panel = new StackPanel { Margin = new(24) };
            panel.Children.Add(new TextBlock { Text = "CHAOS THEORY TRAINER", Foreground = GameMenuOverlay.Accent, FontSize = 23, FontWeight = FontWeights.Bold });
            _statusText = new TextBlock { Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new(0, 20, 0, 20), FontSize = 14 };
            panel.Children.Add(_statusText);
            var launch = new Button { Content = "Launch Chaos Theory", Padding = new(12, 8, 12, 8), Margin = new(0, 0, 0, 8) };
            launch.Click += (_, _) => Launch(); panel.Children.Add(launch);
            var hide = new Button { Content = "Hide to tray", Padding = new(12, 8, 12, 8), Margin = new(0, 0, 0, 8) };
            hide.Click += (_, _) => _status?.Hide(); panel.Children.Add(hide);
            var exit = new Button { Content = "Exit trainer", Padding = new(12, 8, 12, 8) };
            exit.Click += async (_, _) => await ExitAsync(); panel.Children.Add(exit);
            _status = new Window { Title = "Chaos Theory Trainer — status", Width = 540, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.CanMinimize, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 19, 14)), Content = panel };
            _status.Closing += (_, e) => { if (!_disposed) { e.Cancel = true; _status.Hide(); } };
        }
        UpdateStatus(); _status.Show(); _status.WindowState = WindowState.Normal; _status.Activate();
    }

    private void UpdateStatus()
    {
        if (_statusText is null) return;
        string health = _healthError ?? (_health.Installed ? _health.State.Detail : "Native health bridge not built.");
        _statusText.Text = $"{State.Status}\n\nInstallation:\n{Installation ?? "Not found in Ubisoft registry"}\n\nF6: open/close • Arrows: navigate • Enter: select • Escape: back\n\n{health}\nSilent Sam, Invisible Sam and Lights Out remain OFF pending implementation.\n\nUse windowed or borderless mode for the desktop overlay.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= Tick;
        _showWait.Unregister(null); _showStatus.Dispose();
        _overlay.Close(); _status?.Close();
        _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); _waiting.Dispose(); _connected.Dispose();
    }

    private static Icon CreateIcon(System.Drawing.Color color)
    {
        using var bitmap = new Bitmap(32, 32); using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var background = new SolidBrush(System.Drawing.Color.FromArgb(11, 19, 14)); using var dot = new SolidBrush(color);
        graphics.FillEllipse(background, 1, 1, 30, 30); graphics.FillEllipse(dot, 7, 7, 18, 18);
        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
    }
}
