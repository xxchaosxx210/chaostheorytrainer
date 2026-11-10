using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ChaosTheoryTrainer;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _owns;
    private TrayApplication? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(false, @"Local\ChaosTheoryTrainer.Gui");
        try { _owns = _instance.WaitOne(0); } catch (AbandonedMutexException) { _owns = true; }
        if (!_owns)
        {
            try { using var signal = EventWaitHandle.OpenExisting(TrayApplication.ShowStatusEvent); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("The trainer is already running. Open its tray icon for status.", "Chaos Theory Trainer"); }
            Shutdown(); return;
        }
        try
        {
            _tray = new TrayApplication(this);
            _tray.Start();
            if (e.Args.Contains("--smoke-test"))
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "artifacts"));
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "artifacts", "smoke-test.txt"),
                        $"WPF/tray/hook startup succeeded.\nDetector: {_tray.State.Status}\nInstallation: {_tray.Installation}\nShutdown requested normally.\n");
                    Shutdown();
                };
                timer.Start();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Could not start trainer: " + ex.Message, "Chaos Theory Trainer"); Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_owns) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
