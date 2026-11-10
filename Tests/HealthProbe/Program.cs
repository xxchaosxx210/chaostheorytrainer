using ChaosTheoryTrainer;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--toggle-cycle"))
    throw new ArgumentException("Pass the bridge directory and optionally --toggle-cycle for an explicit live apply/restore check.");
var game = new GameDetection().Probe();
Console.WriteLine(game.Status);
if (!game.FingerprintMatched || game.Window == IntPtr.Zero) throw new InvalidOperationException("Matching live game/window is required.");
var backend = new HealthBackend(Path.GetFullPath(args[0]));
try
{
    var result = await backend.ProbeAsync(game);
    Console.WriteLine($"Read-only native probe: Ready={result.Ready}; Enabled={result.Enabled}; sampled Health={result.Health}");
    Console.WriteLine($"Window thread: {Native.GetWindowThreadProcessId(game.Window, out _)}");
    if (!result.Ready || result.Enabled != false) throw new InvalidOperationException("Probe did not confirm ready and inactive.");
    Console.WriteLine("PASS: metadata and ESam damage-script guard verified by the native bridge. No cheat enabled.");
    if (args.Length == 2)
    {
        var enabled = await backend.ToggleAsync(game);
        if (enabled.Enabled != true) throw new InvalidOperationException("Enable was not acknowledged.");
        Console.WriteLine("PASS: Enable acknowledged and native bit readback succeeded.");
        var status = await backend.PollAsync(game);
        if (status.Enabled != true) throw new InvalidOperationException("Active health session was not retained.");
        Console.WriteLine("PASS: Status reports the active session without an object scan.");
        var disabled = await backend.ToggleAsync(game);
        if (disabled.Enabled != false) throw new InvalidOperationException("Disable was not acknowledged.");
        Console.WriteLine("PASS: Disable/restoration acknowledged; session is OFF.");
        Console.WriteLine("These checks establish command/state handling, not gameplay damage prevention.");
    }
}
finally
{
    await backend.ShutdownAsync();
    Console.WriteLine("Native message handler shutdown acknowledged; pinned code remains until game exit.");
}
