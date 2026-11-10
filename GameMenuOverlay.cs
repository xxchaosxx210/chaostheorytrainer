using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ChaosTheoryTrainer;

// Adapted from Splinter Assault's nonactivating desktop overlay. No game-process code.
internal sealed class GameMenuOverlay : Window
{
    private readonly TrainerModel _model;
    private readonly Func<GameState> _state;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Native.HookProc _callback;
    private readonly HashSet<int> _held = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _title = new() { Foreground = Brushes.White, Margin = new(0, 8, 0, 12) };
    private readonly TextBlock _message = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0), FontSize = 12 };
    private readonly Border _panel;
    private IntPtr _hook, _window;
    private bool _open, _closed;
    private bool _applying;
    private int _selected;
    private (int, long) _identity;
    private string? _lastRender;
    internal static readonly SolidColorBrush Accent = new(Color.FromRgb(181, 239, 101));

    public GameMenuOverlay(TrainerModel model, Func<GameState> state)
    {
        _model = model; _state = state; _callback = Keyboard;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Width = 1; Height = 1; Left = -10000; Top = -10000;
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "CHAOS THEORY", Foreground = Accent, FontSize = 20, FontWeight = FontWeights.Bold });
        body.Children.Add(new TextBlock { Text = "SINGLE-PLAYER TRAINER", Foreground = Accent, FontSize = 11 });
        body.Children.Add(_title); body.Children.Add(_rows);
        body.Children.Add(new TextBlock { Text = "↑ / ↓  Navigate    Enter / →  Select\n← / Esc  Back       F6  Close", Foreground = Accent, FontSize = 12, Margin = new(0, 14, 0, 0) });
        body.Children.Add(_message);
        _panel = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 11, 19, 14)), BorderBrush = Accent,
            BorderThickness = new(1), CornerRadius = new(6), Padding = new(16), Visibility = Visibility.Hidden,
            Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Content = _panel;
        SourceInitialized += (_, _) =>
        {
            _window = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(_window, -20, Native.GetWindowLong(_window, -20) | 0x20 | 0x80 | 0x08000000);
            _hook = Native.SetWindowsHookEx(13, _callback, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        };
        _timer.Tick += Tick;
        Closed += (_, _) => { _closed = true; _open = false; _timer.Stop(); _timer.Tick -= Tick;
            if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } _held.Clear(); };
    }

    public void Start() { Show(); _timer.Start(); }
    private static bool Focused(GameState state) => state.Window != IntPtr.Zero && Native.IsWindow(state.Window)
        && !Native.IsIconic(state.Window) && Native.GetForegroundWindow() == state.Window
        && Native.GetWindowThreadProcessId(state.Window, out uint pid) != 0 && pid == state.Pid;

    private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        var state = _state();
        if (code < 0 || _closed || !Focused(state)) return Native.CallNextHookEx(_hook, code, message, data);
        int key = Marshal.ReadInt32(data);
        bool down = message.ToInt64() is 0x100 or 0x104;
        bool up = message.ToInt64() is 0x101 or 0x105;
        bool handled = key == 0x75 || (_open && key is 0x26 or 0x28 or 0x25 or 0x27 or 0x0d or 0x1b) || _held.Contains(key);
        if (!handled) return Native.CallNextHookEx(_hook, code, message, data);
        if (up) { _held.Remove(key); return new(1); }
        if (!down) return Native.CallNextHookEx(_hook, code, message, data);
        if (!_held.Add(key)) return new(1);
        // Keep hooks short: schedule menu changes after returning to Windows.
        Dispatcher.BeginInvoke(new Action(() => HandleKey(key, state.Pid, state.Started)));
        return new(1);
    }

    private async void HandleKey(int key, int pid, long started)
    {
        if (_closed || (_state().Pid, _state().Started) != (pid, started) || !Focused(_state())) return;
        if (key == 0x75) { _open = !_open; if (_open) { _model.Reset(); _selected = 0; } }
        else if (_open && key is 0x25 or 0x1b) { if (_model.Page == "main") _open = false; else { _model.Back(); _selected = 0; } }
        else if (_open && key is 0x26 or 0x28) _selected = (_selected + (key == 0x28 ? 1 : _model.Entries.Count - 1)) % _model.Entries.Count;
        else if (_open && key is 0x0d or 0x27)
        {
            if (_applying) return;
            string page = _model.Page;
            _applying = true;
            try
            {
                var applying = _model.ActivateAsync(_model.Entries[_selected].Key);
                Render();
                await applying;
            }
            finally { _applying = false; }
            if (page != _model.Page) _selected = 0;
        }
        Render();
    }

    private void Tick(object? sender, EventArgs args)
    {
        var state = _state();
        if (_identity != (state.Pid, state.Started)) { _identity = (state.Pid, state.Started); _open = false; _model.Reset(); _selected = 0; }
        if (!Focused(state)) { _open = false; _held.Clear(); }
        if (!_open) { _panel.Visibility = Visibility.Hidden; return; }
        if (!Native.GetClientRect(state.Window, out var rect)) { _panel.Visibility = Visibility.Hidden; return; }
        var origin = new Native.Point();
        if (!Native.ClientToScreen(state.Window, ref origin)) { _panel.Visibility = Visibility.Hidden; return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        int marginX = (int)(16 * dpi.DpiScaleX), marginY = (int)(16 * dpi.DpiScaleY);
        int width = Math.Min((int)(360 * dpi.DpiScaleX), Math.Max(1, rect.Right - 2 * marginX));
        int height = Math.Min((int)(430 * dpi.DpiScaleY), Math.Max(1, rect.Bottom - 2 * marginY));
        Native.SetWindowPos(_window, new(-1), origin.X + marginX, origin.Y + marginY, width, height, 0x10 | 0x40);
        _panel.Visibility = Visibility.Visible;
        Render();
    }

    private void Render()
    {
        string stamp = $"{_model.Page}/{_selected}/{_model.Message}/{_model.HealthEnabled}/{_model.HealthAvailable}";
        if (_lastRender == stamp) return;
        _lastRender = stamp;
        _title.Text = _model.Page == "main" ? "TRAINER MENU" : "STEALTH";
        _message.Text = _model.Message;
        _rows.Children.Clear();
        for (int i = 0; i < _model.Entries.Count; i++)
        {
            var item = _model.Entries[i]; var line = new DockPanel();
            var value = new TextBlock { Text = item.Toggle ? (item.StateText ?? (item.On ? "ON" : item.Available ? "OFF" : "OFF · pending")) : "›", Foreground = item.On ? Accent : Brushes.Gray, Margin = new(8, 0, 0, 0) };
            DockPanel.SetDock(value, Dock.Right); line.Children.Add(value);
            line.Children.Add(new TextBlock { Text = (i == _selected ? "›  " : "   ") + item.Label, Foreground = item.Available ? Brushes.White : Brushes.LightGray });
            _rows.Children.Add(new Border { Child = line, Padding = new(5, 10, 5, 10), Background = i == _selected ? new SolidColorBrush(Color.FromRgb(42, 64, 30)) : Brushes.Transparent });
        }
    }
}
