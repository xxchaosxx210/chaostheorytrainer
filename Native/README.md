# Chaos Theory health bridge

This is an x86 Windows bootstrap host and game-window-thread DLL for the health milestone. It reuses the original trainer's Windows hook bootstrap pattern, with freshly investigated Chaos Theory globals, metadata and script guards. It does not load original-game engine DLLs or use their exported offsets. The build does not install files into the game directory.

`build.cmd` uses the installed VS2022 Community x86 toolchain with `/W4 /WX /O2 /MT`. Outputs are `out\CTHealthHost.exe`, `out\CTHealthBridge.dll`, and `out\CTHealthPolicyTests.exe`. Copy the host and DLL together beside the GUI as part of the managed project's build. The host's only dependencies are Windows system libraries.

**The user confirmed the requested guard-damage OFF/ON/OFF test on 2026-11-10.** Other damage sources and enabled-cheat lifecycle paths remain unverified. Compilation, fingerprint/input validation, synthetic lifecycle checks and live command/state checks are separate from that gameplay confirmation. Normal script execution was independently observed on the game window thread; the native callback alone establishes only window-thread execution.

A successful **read-only integration probe** in the earlier game process reported Ready=true, Enabled=false, Health=450, callback/window thread=5712 and acknowledged Shutdown. In the subsequent game process, normal EPlayerController script execution was observed on window thread 2812. The GUI's profile gate is now enabled for the hash-bound build. A live Probe/Enable/Status/Disable/Shutdown cycle then passed on thread 2812 and finished OFF. This confirms request, state/readback and restoration handling, but does not confirm protection against damage, travel or exit stability while enabled. The injected DLL is pinned until game exit, so do not update its loaded binary in place.

## Protocol

Named mapping: `Local\ChaosTheoryTrainer.Health.v1.{gamePid}`. Registered window message: `ChaosTheoryTrainer.Health.v1`. The GUI creates a new mapping for each serialized request, populates a nonzero request ID, then sends that ID in WPARAM. Initial bootstrap uses `CTHealthHost.exe <pid> <windowHandle> <requestId>` with decimal unsigned 32-bit values. Subsequent commands use the installed subclass directly. The target opens/unmaps the mapping inside the callback and never retains its pointer. A timed-out request has unknown completion; do not automatically repeat a toggle or claim it is OFF. Old request IDs cannot act on a newly populated mapping with a different ID.

The request is exactly 48 bytes: twelve little-endian uint32 values in this order:

| Offset | Field | Meaning |
| --- | --- | --- |
| 0 | Version | 1 |
| 4 | Pid | Target game PID |
| 8 | Window | Target game HWND |
| 12 | RequestId | Nonzero request nonce |
| 16 | Command | 1 Probe, 2 Enable, 3 Disable, 4 Status, 5 Shutdown |
| 20 | Result | Initialize to 0; target writes result code |
| 24 | Enabled | Output: acknowledged trainer ownership active |
| 28 | Ready | Output: prior Probe/Enable still identifies a suitable mission |
| 32 | GameThread | Output: callback's game window thread ID |
| 36 | Health | Output: integer health sampled by successful inactive Probe/Enable; 0 otherwise |
| 40 | Reserved0 | Enable input: trainer owner PID. Output: restoration pending (0/1) |
| 44 | Reserved1 | Output: lifecycle reason |

Result codes: 1 success; 2 invalid header; 3 wrong thread; 4 incompatible loaded image; 5 invalid tables; 6 metadata/script guard mismatch; 7 no suitable living Sam; 8 ambiguous controllers; 9 restoration pending; 10 guarded access fault; 11 timer failure; 12 DLL pin failure; 13 subclass failure; 14 invalid command; 15 not initialized (reserved); 16 wrong window; 17 owner process unavailable. The bootstrap host exits 0 only when the target wrote result 1. It prints explicit timeouts and protocol failures.

Lifecycle reasons: 0 normal; 1 controller/pawn/world changed; 2 game cleared the health flag; 3 window destroyed; 4 access fault; 5 trainer owner process exited.

Probe never changes health flags and owns no restoration debt. Status does not scan objects. Ready becomes false on lifecycle change; a new explicit Probe is required to locate the new mission. Enable obtains a SYNCHRONIZE handle to the supplied trainer owner PID before changing the health bit. A one-second window-thread timer checks that handle and cached identities; it performs no object scans. Trainer exit/crash requests restoration when the game message pump next dispatches that timer.

## Profile and safety boundaries

- Host verifies the exact installed single-player executable SHA-256, x86 architecture, target PID and supplied top-level window. The DLL independently checks PE architecture/size and observed code bytes.
- Probe/Enable resolve reflected property owner/type/name and verify controller `EPawn` offset `0x4a0`, controller `bInvincible` offset `0x554` / mask `0x100000`, pawn `Health` offset `0x420`, and actor `bIsPlayerPawn` offset `0x2c` / mask `0x20`. `Actor.bDeleteMe` must also resolve as an aligned scalar BoolProperty; its offset/mask are read from this game's metadata.
- Enable additionally checks the observed `ESam.TakeDamage` script's early OR guard, return bytes and pointer to the exact reflected `bInvincible` descriptor. Modified `.u` scripts that change this guard fail closed.
- Only the health-specific controller mask is changed. No developer debug-mode switch, console command, repeated health replenishment, damage hook, sound event or rendering flag is used.
- Cached controller slot membership is checked before reading its identity or flags. Cached pawn/world references are compared with table slots and pointer values, never dereferenced by Status, timer, OFF or shutdown. The active timer does not read pawn health.
- OFF restores the pre-enable masked value while preserving unrelated bits. A game-side clear terminates ownership rather than being repeatedly overwritten. Probe followed by Shutdown performs no actor reads or writes.
- WM_NCDESTROY clears state, stops the timer and drops references with no actor reads or restoration attempts. DllMain reads no game state and waits on nothing. Callback code is pinned for the game process lifetime so no callback returns into an unloaded DLL. Updating an already injected helper requires restarting the game.

These policies do not prove every shutdown/transition/cutscene behavior. Slot/index/name/class/Outer comparisons are not a verified engine generation counter; exact ABA reuse across an entire transition missed by the timer is still a limitation. A game-side write of `true` while the trainer's bit is already `true` cannot be distinguished from the trainer's own value. OFF restores the original masked value, so scripted invincibility changes during active use require live testing. Alternative state-specific damage implementations, scripted kills and other damage bypasses remain unverified.

## Automated checks

`out\CTHealthPolicyTests.exe` uses only synthetic memory in its own process, including inaccessible pages for cached pawn/world/controller pointers. Fifteen checks cover validation order, freed-reference restoration, bit preservation, read-only Probe ownership, external clears, world changes, pending-delete objects and the owner-exit branch. No game is opened or injected.

`out\CTHealthHost.exe --version` reports protocol/architecture without touching the game. `--verify-file <path>` performs only a file SHA-256 check. The installed executable passes; a nonmatching header file is rejected with exit 5; zero PID arguments are rejected with exit 2 before process access.
