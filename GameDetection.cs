using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace ChaosTheoryTrainer;

internal sealed record GameState(int Pid, long Started, IntPtr Window, string Status, bool FingerprintMatched = false,
    bool WidescreenLoaded = false)
{
    public static GameState Waiting { get; } = new(0, 0, IntPtr.Zero, "Waiting for single-player Chaos Theory");
}

internal sealed class GameDetection
{
    public const string ExpectedHash = "54DF25B238356024248085CA7FA6AF9005B0BE4EC40BEEF3C32FAA8C06B6D4E5";
    public string? Executable { get; }
    private (int Pid, long Started)? _identity;
    private bool _matched;
    public GameDetection(string? executable = null)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Ubisoft\Tom Clancy's Splinter Cell Chaos Theory");
        var directory = key?.GetValue("installdir") as string;
        Executable = executable ?? (directory is null ? null : Path.Combine(directory, "System", "splintercell3.exe"));
    }

    public GameState Probe()
    {
        var processes = Process.GetProcessesByName("splintercell3");
        try
        {
            if (processes.Length == 0) { _identity = null; return GameState.Waiting; }
            if (processes.Length != 1) { _identity = null; return new(0, 0, IntPtr.Zero, "Multiple game instances: close extras"); }
            var process = processes[0];
            string? path = process.MainModule?.FileName;
            if (path is null || Executable is null || !Path.GetFullPath(path).Equals(Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase))
                return new(0, 0, IntPtr.Zero, "Game path differs from the detected single-player installation");
            var identity = (process.Id, process.StartTime.ToUniversalTime().Ticks);
            if (_identity != identity)
            {
                _identity = null;
                using var stream = File.OpenRead(path);
                _matched = Convert.ToHexString(SHA256.HashData(stream)) == ExpectedHash;
                _identity = identity;
            }
            bool widescreen = process.Modules.Cast<ProcessModule>().Any(m =>
                string.Equals(m.ModuleName, "SplinterCellChaosTheory.WidescreenFix.asi", StringComparison.OrdinalIgnoreCase));
            var window = process.MainWindowHandle;
            return new(process.Id, identity.Item2, window,
                _matched ? $"Game detected / fingerprint matched / {(widescreen ? "Widescreen Fix loaded" : "Widescreen Fix not observed loaded")}" : "Unknown executable fingerprint / cheats unavailable",
                _matched, widescreen);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _identity = null;
            return new(0, 0, IntPtr.Zero, "Detection unavailable: " + ex.Message);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Launch()
    {
        if (Executable is null || !File.Exists(Executable)) throw new FileNotFoundException("Chaos Theory installation not found.");
        if (Process.GetProcessesByName("splintercell3") is var running)
        {
            try { if (running.Length != 0) throw new InvalidOperationException("Chaos Theory is already running."); }
            finally { foreach (var process in running) process.Dispose(); }
        }
        Process.Start(new ProcessStartInfo(Executable) { WorkingDirectory = Path.GetDirectoryName(Executable)!, UseShellExecute = true })?.Dispose();
    }
}
