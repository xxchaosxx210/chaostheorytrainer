# Chaos Theory investigation ledger

Date: 2026-11-09. Scope: installed single-player game only. No installation files modified.

## Observed installation facts

- Ubisoft 32-bit registry key `SOFTWARE\Ubisoft\Tom Clancy's Splinter Cell Chaos Theory`, value `installdir`, identifies `C:\Program Files (x86)\Steam\steamapps\common\Splintercell Chaos Theory`.
- Single-player executable: `System\splintercell3.exe`, 9,875,456 bytes, SHA-256 `54df25b238356024248085ca7fa6af9005b0be4ec40beef3c32faa8c06b6d4e5`.
- REA `inspect_native_exports`: PE x86 / 32-bit; export directory name `SplinterCell3_static.exe`; 150 exports examined with complete export-table coverage. Parent evidence `ev_a808d78fb501f874c52dc06888b3f57191f1f8b54a9a84e04f808c42eaa4a92a`. Summary evidence `ev_661c07933eda390a673c4681a73c781340bfa17cc9348d9c0032676fbca53ceb`.
- Installed `Readme.txt` title identifies September 2005 / 1.05. This is accompanying documentation, not proof of the binary's internal version. Windows FileVersion/ProductVersion fields are empty.
- `System\scripts\SplinterCellChaosTheory.WidescreenFix.asi`: SHA-256 `531c6c64d8ee9c28f10b619864536d0a7631350564ea27e3b329c6a84ade8dc6`.
- `System\dinput8.dll`: SHA-256 `6890bbefd8f0255f8f1fa4cdc53fb28c9b0f2a5c03bc053e34b34602cf3336e8`.
- Widescreen Fix configuration observed: `ResX=0`, `ResY=0`, `ForceWindowedMode=1`, `ForceWindowStyle=2`, `AlwaysOnTop=0`, `FPSLimit=60`. Zero resolution values do not establish the running resolution. User reports 2560×1600.
- `System\SplinterCell3User.ini` contains `F6=open ?restart`. Trainer intercepts F6 only for the selected foreground game window; it does not edit this binding.
- `System` contains `Core.u`, `Engine.u`, `Echelon.u`, `EchelonAI.u`, `EchelonCharacter.u`, `EchelonGameObject.u`, `EchelonIngredient.u`, `EchelonEffect.u`, and `EchelonPattern.u`. The original trainer's Engine.dll/Echelon.dll export lookup mechanism has no established compatibility here.

These are static observations. Matching an executable hash does not prove a memory layout, hook ABI, live patch compatibility, or feature behavior. The UI deliberately says fingerprint matched, rather than cheats compatible.

Evidence saved locally in `artifacts\foundation-evidence.json` and `artifacts\foundation-analysis-snapshot.json` (generated artifacts are ignored by Git). Ghidra was selected as the native provider, but the requested function/string searches returned no results before cancellation at the foundation/live-test checkpoint. The session was closed successfully; its snapshot contains three evidence records and zero primitive/workflow query entries. No function decompilation, object layout or candidate cheat address has been established.

## Original trainer findings to carry forward

Source reviewed: `GameMenuOverlay.cs`, `TrayApplication.cs`, `App.xaml.cs`, `GameWindowFocus.cs`, `Native\SilentNoise.h`, `Native\InvisibleSam.h`, `Native\QuietLights.h`, and relevant references in `Native\Cheats.h`.

- Reusable: nonactivating click-through WPF window, foreground-scoped keyboard hook, client-coordinate positioning, tray/status lifetime, single-instance signaling and green palette.
- Original Silent Sam resolves `AActor::MakeNoise` / `AEPawn::MakeNoise` exports and changes a vtable slot. Its ABI, exports and slots require fresh proof for Chaos Theory.
- Original Invisible Sam changes perception dispatch and temporarily touches pawn flags, plus periodic observer discovery. Do not port that implementation or assume its observer coverage applies.
- Original Lights Out uses reflected fields plus saved object identity and original light values. Field names/offsets, light cache invalidation and renderer behavior all require Chaos Theory evidence.

## Required backend design constraints (not implemented)

- All object access and mutation must be serialized on a verified game-thread boundary. A pointer's readable page alone is not a lifetime check and does not close a destruction race.
- Identify the current world/session with a generation. Invalidate references on travel, player replacement and exit before calling anything that may free them. Never restore old-world objects into a new world.
- Use validated object-array membership and an independently checked slot identity/lifetime mechanism before dereferencing an actor. An index alone is insufficient when object slots can be reused. Determine what identity mechanism this engine actually provides.
- Prefer scoped native event filtering over per-frame world scans for noise and perception. Any needed discovery must have a bounded per-tick budget and cached classes; measure cost in game. Never build an unbounded scan into a high-frequency callback.
- Clear active masks before restoring/removing hooks. Avoid a shutdown callback that resolves or reads Sam after destruction. Process-detach/DllMain must not traverse game objects or wait on the game thread.
- Preserve independent noise and perception policies. Invisible Sam must not set rendering-hidden flags as a persistent substitute for detection filtering.
- Light restoration records need world generation, object identity, original values and a defined policy for lights destroyed/changed by gameplay while enabled. Restore only live objects owned by that generation. Drop dead records without reading the dead actor.
- Hook removal must account for in-flight callbacks. Do not unload a helper while any callback can return into its code. If pinning is necessary, document that process lifetime ownership rather than claiming an unsafe unload.
- A backend must acknowledge apply/restore completion before the UI reports ON/OFF. Define behavior for timeout, partial failure, unsupported build and game termination.

## Questions still unresolved

1. Object/name arrays, core reflection layouts and the window/script-thread dispatch relationship have been investigated for health; see `HEALTH-INVESTIGATION.md`. Durable generation, travel and destruction notification paths remain unresolved.
2. Sam's health-specific controller flag and its damage guard are implemented without enabling global developer/debug mode. The user confirmed guard-damage OFF/ON/OFF. Other damage sources and lifecycle restoration still require live tests.
3. Sam noise event dispatch and all relevant event types.
4. Guard/dog/camera/turret visual tests, dog scent tests, and independence from sound and rendering.
5. Lighting ownership, baked/dynamic illumination, renderer/AI light-cache updates and reversible restoration.
6. Live detection, loaded Widescreen Fix presence, overlay/navigation and the tested health damage case are confirmed. Frame-time impact, level transitions, player death/reload and clean game shutdown with a cheat enabled remain unverified.

Do not treat a string match, candidate address or build success as an answer to these questions.
