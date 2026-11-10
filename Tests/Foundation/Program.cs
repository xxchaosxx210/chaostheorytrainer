using ChaosTheoryTrainer;
using System.Security.Cryptography;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

var model = new TrainerModel();
Check(model.Entries.Select(e => e.Key).SequenceEqual(new[] { "health", "stealth" }), "Main menu has only Health and Stealth");
model.Activate("health");
Check(!model.Entries[0].On && !model.Entries[0].Available, "Unverified Health cannot report ON");
model.Activate("stealth");
Check(model.Entries.Select(e => e.Key).SequenceEqual(new[] { "silent", "invisible", "lights" }), "Stealth contains the three requested cheats");
foreach (var entry in model.Entries)
{
    model.Activate(entry.Key);
    Check(model.Entries.All(e => !e.On && !e.Available), entry.Label + " remains OFF without a verified backend");
}
model.Activate("invalid");
Check(model.Page == "stealth", "Unknown commands do not change navigation");
model.Back(); Check(model.Page == "main", "Back returns to main");
model.Activate("stealth"); model.Reset(); Check(model.Page == "main", "Session reset discards submenu");
model.SetHealth(false, true);
model.ToggleHealth = () => throw new InvalidOperationException("Native request rejected");
await model.ActivateAsync("health");
Check(model.HealthEnabled == false && model.Message == "Native request rejected", "A failed backend request cannot report Health ON");
var acknowledgement = new TaskCompletionSource<bool>();
model.ToggleHealth = async () => { await acknowledgement.Task; model.SetHealth(true, true); return "Applied"; };
var applying = model.ActivateAsync("health");
Check(model.HealthEnabled == false && !applying.IsCompleted, "Health remains OFF while its apply acknowledgement is pending");
acknowledgement.SetResult(true); await applying;
Check(model.HealthEnabled == true, "Health reports ON only after acknowledged application");
model.SetHealth(null, false);
Check(model.Entries[0].StateText == "CHECK" && !model.Entries[0].On, "Uncertain outcomes are shown as CHECK, never a guessed ON/OFF");
model.SetHealth(false, false); model.ToggleHealth = null;
var detector = new GameDetection();
Check(detector.Executable is not null && File.Exists(detector.Executable), "Installed single-player executable found through registry");
using (var stream = File.OpenRead(detector.Executable!))
    Check(Convert.ToHexString(SHA256.HashData(stream)) == GameDetection.ExpectedHash, "Installed executable matches recorded fingerprint");
var state = detector.Probe();
Console.WriteLine("Read-only process observation: " + state.Status);
Console.WriteLine("No game-memory writes or gameplay assertions were made.");
