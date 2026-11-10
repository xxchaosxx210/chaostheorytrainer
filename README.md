# Chaos Theory Trainer

Single-player trainer development for the installed PC version of Splinter Cell: Chaos Theory.

Repository: https://github.com/xxchaosxx210/chaostheorytrainer.git

## Current milestone: Infinite Health

Implemented: installation discovery, single-player process identification, executable SHA-256 check, loaded Widescreen Fix observation, C# / WPF overlay, tray/status application, single-instance signaling, and game launcher. Adapted the overlay, green palette, tray and launcher patterns from the original Splinter Assault Trainer. No original-game memory addresses, class offsets, native helpers or cheat code were copied.

**Infinite Health starts OFF and has passed the user's guard-damage OFF/ON/OFF test.** Normal script execution on the game window thread and the native apply/status/restore cycle are also verified. This confirms the tested damage case, not every hazard or lifecycle path. The three Stealth cheats are unimplemented. See `docs\HEALTH-INVESTIGATION.md` and `Native\README.md` for evidence and limitations.

Main menu: Infinite Health, Stealth. Stealth: Silent Sam, Invisible Sam, Lights Out. No additional cheats are included.

## Run

Run `Launch-Trainer.ps1`, or build `Native\build.cmd`, then `dotnet build ChaosTheoryTrainer.csproj -c Release`, and run `bin\Release\net7.0-windows\ChaosTheoryTrainer.exe`. Uses the installed .NET 7 Windows Desktop toolchain and Visual Studio 2022 Community x86 C++ toolchain. The launcher builds the trainer and starts its status/tray application. Launch the game separately or use **Launch Chaos Theory** in the status window/tray menu. Close the running trainer before rebuilding its executable. An injected native DLL is pinned until game exit; updating that helper requires restarting the game.

- F6 opens/closes the overlay while the detected game is focused.
- Up/Down select an entry; Enter selects it. Right also selects; Left/Escape go back. Escape at the main menu closes the overlay.
- Alt+Tab or minimizing hides the menu. Press F6 again after returning to the game.
- Closing the status window hides it to the tray. **Exit trainer** releases the keyboard hook and closes the application. A second trainer launch opens the existing instance's status window.

The desktop overlay requires windowed/borderless rendering. The installed Widescreen Fix currently has `ForceWindowedMode=1`. Placement uses the actual client rectangle and per-monitor DPI, with no hardcoded 2560×1600 screen coordinates. The user confirmed that the overlay and keyboard navigation work in their running game.

The game's existing F6 binding is `open ?restart`. The trainer swallows F6 down/up while the identified game is focused. When the trainer is closed, the game's binding remains active. No game settings or installation files are changed.

## Verification status

Automated and diagnostic checks, 2026-11-09–10:

- Release build: zero warnings, zero errors.
- `dotnet run --project Tests\Foundation\Foundation.csproj -c Release`: 15 checks passed for menu scope, unavailable-state handling, navigation, acknowledged/uncertain health state, installation discovery, and installed executable fingerprint. This command includes a machine-specific installation check.
- `ChaosTheoryTrainer.exe --smoke-test`: real WPF/tray/keyboard-hook startup, process detector waiting state, and normal shutdown; process exit code 0. Writes `artifacts\smoke-test.txt` beneath the executable directory.

Native bridge: x86 builds with `/W4 /WX`, 15 synthetic lifecycle/ownership checks passed, and host fingerprint/input rejection checks passed. A live read-only probe confirmed metadata, script guard, Health 450, window thread 5712, inactive state, and acknowledged message-handler shutdown. This loads a pinned helper but does not enable health.

Follow-up runtime checks: normal `EPlayerController.NoGE` script execution was observed on the same thread as the game window (thread 2812 in the new process). The one-shot observer removed its interceptor and its Frida session was closed. The native health Enable/readback, active Status, Disable/restoration and handler shutdown all acknowledged success; the session finished OFF. These are live API/state checks, not a damage-prevention test.

User-confirmed gameplay (2026-11-10): F6 opens/closes the overlay; arrows/Enter navigate into Stealth; Escape returns. In the requested guard-damage OFF/ON/OFF test, damage works normally OFF, is prevented ON, and returns after switching OFF again. Other damage sources, death/reload, transitions, cutscenes, FPS, and exit stability while enabled remain unverified. The three Stealth cheats still require implementation and testing.

## Development order

1. Done: detection and user-confirmed overlay/navigation with the installed Widescreen Fix.
2. Infinite Health is implemented without enabling developer debug mode; the user's guard-damage ON/OFF test passed. Validate other damage sources, death/reload, transitions and shutdown next.
3. Implement Silent Sam by filtering Sam-originated AI noise events while retaining other actors' noise. Test running, landing, weapons and interactions separately.
4. Implement Invisible Sam for guards, dogs, cameras and turrets, including dog scent. Preserve Sam's on-screen rendering and leave noise governed solely by Silent Sam. Test each observer type, sound with Silent Sam OFF, level changes and exit.
5. Implement Lights Out with reversible, quiet lighting changes, preserving object lifetime safety and avoiding damage, alarm, switch or sound events. Verify restoration and transition/shutdown behavior.

Each cheat gets an independent ON/OFF state only after its backend applies successfully. Build checks and in-game checks remain separate. See `docs\INVESTIGATION.md` for identity evidence and lifecycle requirements.
