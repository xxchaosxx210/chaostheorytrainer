using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace ChaosTheoryTrainer;

internal sealed record HealthState(bool? Enabled, bool Ready, string Detail, int Health = 0)
{
    public static HealthState Pending { get; } = new(false, false, "Infinite Health OFF; ready for in-game damage testing in a loaded mission.");
}

// All UObject access belongs to the x86 bridge on the verified window/game thread.
// The GUI never reads or writes Sam's pawn, nor retains a pawn address.
internal sealed class HealthBackend
{
    // Normal EPlayerController VM execution observed on its window thread (2026-11-10).
    // Exact executable hash, runtime code and ESam damage-script guards remain mandatory.
    internal static readonly bool GameThreadProfileVerified = true;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    private GameState? _bound;
    private bool _initialized;
    private bool _mayHaveNativeState;
    private static int _nextRequest = Environment.TickCount;
    public HealthState State { get; private set; } = HealthState.Pending;
    public bool Initialized => _initialized;
    public bool HasSession => _mayHaveNativeState;
    public bool Installed => File.Exists(Path.Combine(_directory, "CTHealthHost.exe")) && File.Exists(Path.Combine(_directory, "CTHealthBridge.dll"));
    public HealthBackend(string? directory = null) { _directory = directory ?? AppContext.BaseDirectory; }
    public bool CanToggle(GameState game) => GameThreadProfileVerified && Installed && game.FingerprintMatched && game.Window != IntPtr.Zero && State.Enabled.HasValue;

    public async Task<HealthState> ToggleAsync(GameState game)
    {
        if (!CanToggle(game)) throw new InvalidOperationException("Health is unavailable until the native bridge and game-thread profile are verified.");
        return await RequestAsync(game, State.Enabled == true ? 3u : 2u);
    }

    // Probe is intentionally read-only even before thread-profile verification.
    public Task<HealthState> ProbeAsync(GameState game) => RequestAsync(game, 1);
    public Task<HealthState> PollAsync(GameState game) => RequestAsync(game, 4);
    public async Task ShutdownAsync()
    {
        if (_bound is null || !_mayHaveNativeState) return;
        try { await RequestAsync(_bound, 5); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (!IsSameProcess(_bound)) { Reset(); return; }
            throw;
        }
    }

    public void Reset()
    {
        _bound = null; _initialized = false; _mayHaveNativeState = false; State = HealthState.Pending;
    }

    private static bool IsSameProcess(GameState game)
    {
        try { using var process = Process.GetProcessById(game.Pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == game.Started; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    private async Task<HealthState> RequestAsync(GameState game, uint command)
    {
        await _gate.WaitAsync();
        try
        {
            if (!Installed) throw new FileNotFoundException("The x86 health bridge has not been built or copied beside the trainer.");
            if (!game.FingerprintMatched || game.Window == IntPtr.Zero || !IsSameProcess(game))
                throw new InvalidOperationException("The matching game instance/window is no longer available.");
            if (command == 2 && !GameThreadProfileVerified) throw new InvalidOperationException("Health writes are gated until game-thread verification completes.");
            if (_bound is not null && (_bound.Pid, _bound.Started, _bound.Window) != (game.Pid, game.Started, game.Window))
            {
                if (IsSameProcess(_bound) && State.Enabled != false)
                    throw new InvalidOperationException("The previous window/session needs health restoration before rebinding.");
                Reset();
            }
            _bound = game;
            _mayHaveNativeState = true;
            Reply result;
            try { result = await Task.Run(() => Exchange(game, command)); }
            catch (Exception ex) when (ex is TimeoutException or Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                State = new(null, false, "Health command outcome unconfirmed: " + ex.Message);
                throw new InvalidOperationException(State.Detail, ex);
            }
            _initialized = result.Result != 0 && command != 5;
            if (result.GameThread == 0 || result.GameThread != Native.GetWindowThreadProcessId(game.Window, out _))
                throw new InvalidOperationException("Native reply did not identify the selected game window thread.");
            if (result.RestorePending != 0)
            {
                State = new(null, false, "Health restoration is pending. Keep the trainer open and retry Exit.");
                throw new InvalidOperationException(State.Detail);
            }
            State = new(result.Enabled != 0, result.Ready != 0,
                result.Result == 1 ? (result.Enabled != 0 ? "Infinite Health ON." : "Infinite Health OFF.") : Explain(result.Result),
                unchecked((int)result.Health));
            if (result.Lifecycle != 0) State = State with { Detail = $"Health session ended (lifecycle reason {result.Lifecycle}); enable again in the current mission." };
            if (result.Result != 1) throw new InvalidOperationException(Explain(result.Result));
            if (command == 5) _mayHaveNativeState = false;
            return State;
        }
        catch (Exception ex) when (ex is TimeoutException or Win32Exception)
        {
            State = new(null, false, "Health command outcome unconfirmed: " + ex.Message);
            throw new InvalidOperationException(State.Detail, ex);
        }
        finally { _gate.Release(); }
    }

    private sealed record Reply(uint Result, uint Enabled, uint Ready, uint GameThread, uint Health, uint RestorePending, uint Lifecycle);
    private Reply Exchange(GameState game, uint command)
    {
        uint request = unchecked((uint)Interlocked.Increment(ref _nextRequest));
        if (request == 0) request = unchecked((uint)Interlocked.Increment(ref _nextRequest));
        string name = $@"Local\ChaosTheoryTrainer.Health.v1.{game.Pid}";
        using var mapping = MemoryMappedFile.CreateNew(name, 48, MemoryMappedFileAccess.ReadWrite);
        using var view = mapping.CreateViewAccessor(0, 48, MemoryMappedFileAccess.ReadWrite);
        var input = new uint[] { 1, (uint)game.Pid, unchecked((uint)game.Window.ToInt64()), request, command, 0, 0, 0, 0, 0, (uint)Environment.ProcessId, 0 };
        view.WriteArray(0, input, 0, input.Length); view.Flush();
        if (!_initialized)
        {
            var start = new ProcessStartInfo(Path.Combine(_directory, "CTHealthHost.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = _directory
            };
            start.ArgumentList.Add(game.Pid.ToString()); start.ArgumentList.Add(input[2].ToString()); start.ArgumentList.Add(request.ToString());
            using var host = Process.Start(start) ?? throw new InvalidOperationException("Could not start health bridge host.");
            var output = host.StandardOutput.ReadToEndAsync(); var error = host.StandardError.ReadToEndAsync();
            if (!host.WaitForExit(8000))
            {
                try { host.Kill(); } catch (InvalidOperationException) { }
                throw new TimeoutException("Bootstrap timed out. No health state is confirmed.");
            }
            string detail = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            if (host.ExitCode != 0 && view.ReadUInt32(20) == 0)
                throw new InvalidOperationException("Bridge bootstrap failed: " + detail.Trim());
        }
        else
        {
            uint message = RegisterWindowMessage("ChaosTheoryTrainer.Health.v1");
            if (message == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (SendMessageTimeout(game.Window, message, new UIntPtr(request), IntPtr.Zero, 0x3, 2000, out _) == IntPtr.Zero)
                throw new TimeoutException("The game did not acknowledge the health command within two seconds.");
        }
        Thread.MemoryBarrier();
        if (view.ReadUInt32(12) != request) throw new InvalidOperationException("Health reply identity changed.");
        return new(view.ReadUInt32(20), view.ReadUInt32(24), view.ReadUInt32(28), view.ReadUInt32(32), view.ReadUInt32(36), view.ReadUInt32(40), view.ReadUInt32(44));
    }

    private static string Explain(uint result) => result switch
    {
        0 => "The health bridge did not acknowledge the command.",
        2 => "Health protocol identity mismatch.", 3 => "Health request ran on the wrong thread.",
        4 => "The running executable is incompatible with this health profile.",
        5 => "Engine object/name tables are unavailable.", 6 => "Health reflection metadata did not match the verified layout.",
        7 => "Load a playable single-player mission before enabling health.", 8 => "More than one candidate player controller was found.",
        9 => "Health restoration is pending.", 10 => "Engine data became unavailable; health access was stopped.",
        11 => "Could not start the health lifetime timer.", 12 => "Could not retain the native callback module safely.",
        13 => "Could not install the game-thread message handler.", 14 => "Invalid health command.",
        15 => "Health bridge is not initialized.", 16 => "The game window changed.",
        17 => "Could not monitor the owning trainer process.", _ => $"Health bridge error {result}."
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
}
