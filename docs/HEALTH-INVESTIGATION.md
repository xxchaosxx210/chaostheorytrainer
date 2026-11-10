# First-mission health investigation

Session: 2026-11-09 / 2026-11-10, Europe/London. Installed executable identity is recorded in `INVESTIGATION.md`. The live health apply/restore cycle passed, and the user confirmed the guard-damage OFF/ON/OFF test on 2026-11-10.

## Observed live facts

The detector, executed outside the command sandbox, found the installed `splintercell3.exe` and confirmed its on-disk SHA-256 and loaded Widescreen Fix. The trainer's status window showed the same result. The command sandbox exposes only its own processes; its waiting-state output is not evidence that the desktop game is absent.

REA/Frida attached to the selected single-player process. The module is ia32, pointer size 4, observed image base `0x10900000`, image size 11,177,984. Runtime module evidence: `ev_5f3fc1a3df0c2584b5d363fe63febbe2b23b501a1c59516f9444fca7359d2e79`.

Bounded diagnostic passes established these fresh CT layouts:

| Item | Observed layout |
| --- | --- |
| Global UObject pointer TArray | Module RVA `0xa12084` |
| Global FNameEntry pointer TArray | Module RVA `0xa0dfc0` |
| FNameEntry | index at `+0`, ANSI name at `+12` |
| UObject header | index `+4`, owner/Outer `+0x18`, name index `+0x20`, class `+0x24` |
| EPlayerController.EPawn | ObjectProperty, instance offset `0x4a0` |
| Pawn.Health | IntProperty, instance offset `0x420` |
| EPlayerController.bInvincible | BoolProperty, offset `0x554`, mask `0x100000` |
| EPawn.bIgnoreDamage | BoolProperty, offset `0x744`, mask `0x40000` |
| Actor.bIsPlayerPawn | BoolProperty, offset `0x2c`, mask `0x20` |

One diagnostic object pass sampled the controller's ESam pawn and read integer Health `450`; a float interpretation was not plausible. The pass visited 33,926 slots in 122 ms. This is a single Frida diagnostic measurement, not the production timer cost or an FPS guarantee. The metadata pass independently identified the Health field as an IntProperty at `0x420`.

The final name-entry decoder used bounded raw bytes terminated at NUL. Earlier UTF-16/fixed-length string decoding probes were incorrect and are superseded; they are retained in the evidence ledger rather than treated as valid name matches. Final name discovery visited 56,968 entries with four guarded read failures; it does not establish exhaustive absence of any name.

## Damage path and chosen health mechanism

The native TakeDamage event wrapper at RVA `0x27b2a0` dispatches the reflected function through UObject's event machinery. Relevant instruction evidence: `ev_05b61c2e002f3ff7386e338ce27f9c4df989db23d3efd95e3a826d9851062892` and follow-up records in the saved evidence bundle.

The game's own loaded reflected script bytes were sampled for Pawn.GetHealthPCT, EPlayerController.Health/Invincible, EPawn.TakeDamage and ESam.TakeDamage: `ev_5396feb82ed33fe2c893b10fd73bd2a0229dbadbbb692384597130be8b338254`.

- EPawn.TakeDamage starts with a bIgnoreDamage check and return. ESam has its own override; applying this inherited flag alone has no established coverage for Sam.
- ESam.TakeDamage has an early return guard that includes EPlayerController.bInvincible. The conditional starts at script offset `0x1a`, its false branch targets `0x71`, and Return/Nothing is at `0x6f/0x70`. The embedded bInvincible property reference is checked against the current reflected descriptor by the bridge.
- The loaded VM interpreter and selected handlers were inspected to confirm conditional-jump, Boolean-property mask and short-circuit OR behavior. This is analysis of the actual CT VM, rather than an assumption based on the original game's source.

The implemented health prototype changes only that controller's reflected health-specific bit. It does not execute the Invincible console command, set a global developer/debug-mode flag, modify Sam's rendered visibility, replenish HP in a polling loop, or install a damage-code hook. Gameplay must still establish coverage of bullets, melee, falls, explosions, environmental damage and any alternate script/state paths.

## Native bridge and validation

The native host/DLL use a nonce-checked request mapping and Windows message/subclass handler on the selected game window thread. Only Probe/Enable resolve reflected metadata and player candidates. The one-second ownership/lifetime monitor does not scan objects. Cached controller references are checked against an independent table slot before dereference; the cached pawn is checked through its slot and the live controller's pointer value, with no cached pawn dereference during monitoring/restoration/shutdown. Module code is pinned until game exit to protect returning callbacks.

Probe owns no flag changes. OFF restores the pre-enable masked value, preserving other bits. A held owner-process handle lets the native monitor restore after the trainer exits unexpectedly. Window destruction discards state without traversing game objects. Native tests cover protected/no-access memory cases; see `Native/README.md` for exact behavior and limitations.

Automated checks:

- x86 host/DLL compile with `/W4 /WX`: passed.
- 15 native synthetic policy checks: passed, including Probe ownership and shutdown with inaccessible cached objects.
- Host known-file SHA-256 acceptance, wrong-file rejection and malformed input rejection: passed.
- 15 C# foundation checks: passed, including asynchronous acknowledgement and uncertain-state presentation.
- WPF Release build: passed with zero warnings/errors after closing the previous running executable that had locked the apphost.
- Refreshed WPF/tray/keyboard-hook smoke test: real desktop detection reported fingerprint matched and Widescreen Fix loaded; normal exit code 0. The verification-gated trainer was then restarted. No health session was enabled by the GUI.

Live read-only integration probe:

```
Ready=True; Enabled=False; sampled Health=450
Window thread: 5712
ESam damage-script guard verified
Native message-handler shutdown acknowledged
```

That read-only probe proves the helper can resolve the metadata and guard and acknowledge requests/shutdown on the window thread. By itself it does not prove normal engine script-thread ownership, damage protection or lifecycle safety while enabled. Follow-up thread and active state checks are recorded below.

## Execution-thread verification and active state check

A temporary one-shot health-query-wrapper observer initially timed out while the game was minimized. The user subsequently confirmed F6 and menu navigation work and restored the game. In the new game process, a one-shot observer at the verified script-VM entry (RVA `0x3ee340`) recorded normal `EPlayerController.NoGE` execution on thread 2812, matching that window's thread. It changed no arguments/results and removed its interceptor after this sample; the Frida session was then closed. Evidence is saved in `artifacts\script-thread-evidence.json` (load record `ev_df71286a72149a6ce3da15b8d0ff9eb31803fe0e008f5e97d80babf004a48779` and subsequent status record).

`HealthBackend.GameThreadProfileVerified` is now true for this hash-bound build. The live `HealthProbe --toggle-cycle` check passed Probe (Health450), Enable with native flag readback, active Status, Disable/restoration, and message-handler Shutdown. All operations reported the expected window thread. The cycle finished OFF. This verifies native command/state handling; no damage was requested or observed during it.

User gameplay confirmation (2026-11-10): after being asked to test a small guard hit OFF, further hits ON, then damage OFF again, the user replied "works". Record this as confirmation of the requested guard-damage sequence: normal damage OFF, prevention ON, and restored damage OFF. This is user-reported in-game evidence, separate from the command/state and automated checks above.

Next: test other damage sources, death/reload, level travel, trainer exit and game exit. Restore/cleanup must be acknowledged before claiming success. The current confirmation does not establish Alt+Tab behavior, FPS, every damage path or enabled-cheat exit stability.

Object slot/name/class/Outer checks are not an engine generation counter. Exact address/slot reuse across a completely missed transition remains an ABA limitation. A game script writing true to an already-true owned bit cannot be distinguished from the trainer's write; cutscene behavior needs live tests. The helper is pinned in this game process, so changing its binary requires restarting the game. The three Stealth cheats remain unimplemented.

Ghidra's first import exceeded its 330-second server deadline. A retry with a larger server budget then exceeded the client's 450-second tool deadline. Those failures establish no function absence or completed decompilation. Live instruction/script evidence supplied the health findings. Longer future Ghidra runs require the client's `mcp_servers.rea.tool_timeout_sec` to cover startup, query and cleanup.
