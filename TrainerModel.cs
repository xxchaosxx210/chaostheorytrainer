namespace ChaosTheoryTrainer;

internal sealed record MenuEntry(string Key, string Label, bool Toggle = true, bool On = false, bool Available = false, string? StateText = null);

// The UI reports applied state only. Until a verified backend exists, it cannot report ON.
internal sealed class TrainerModel
{
    public string Page { get; private set; } = "main";
    public string Message { get; private set; } = "Foundation build: cheats await Chaos Theory verification.";
    public bool? HealthEnabled { get; private set; } = false;
    public bool HealthAvailable { get; private set; }
    public Func<Task<string>>? ToggleHealth { get; set; }
    public IReadOnlyList<MenuEntry> Entries => Page == "main"
        ? new[] { new MenuEntry("health", "Infinite Health", On: HealthEnabled == true, Available: HealthAvailable, StateText: HealthEnabled is null ? "CHECK" : null), new MenuEntry("stealth", "Stealth", false, Available: true) }
        : new[] { new MenuEntry("silent", "Silent Sam"), new MenuEntry("invisible", "Invisible Sam"), new MenuEntry("lights", "Lights Out") };

    public void Reset() { Page = "main"; }
    public void Back() { Page = "main"; }
    public void SetHealth(bool? enabled, bool available) { HealthEnabled = enabled; HealthAvailable = available; }
    public async Task ActivateAsync(string key)
    {
        if (!Entries.Any(e => e.Key == key)) return;
        if (key == "health" && HealthAvailable && ToggleHealth is not null)
        {
            Message = "Applying health request on the game thread…";
            try { Message = await ToggleHealth(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
            { Message = ex.Message; }
            return;
        }
        Activate(key);
    }
    public void Activate(string key)
    {
        if (!Entries.Any(e => e.Key == key)) return;
        if (key == "stealth") { Page = "stealth"; return; }
        Message = "Unavailable: this cheat has no verified Chaos Theory backend yet. State remains OFF.";
    }
}
