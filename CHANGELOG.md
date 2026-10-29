# Changelog - Over the Hill VR (Fan Edition)

All notable changes to this project will be documented in this file.

## [1.7.7-dev] - 2026-10-28
### Documentation & Roadmap
- **Official Project Feature Queue & Backlog Provisioned (BACKLOG.md)**:
  - Established formal roadmap tracking 5 prioritized future deliverables:
    1. OpenXR Dual-Backend Architecture (PR 2).
    2. Fanatec ClubSport H-Pattern discrete gate transmission solver.
    3. Multi-device DirectInput & secondary USB button box / rotary encoder poller.
    4. Dedicated wheel bindings for Handbrake, Headlights, Roof, and Zoom.
    5. SimHub ShakeIt tactile bass shaker UDP telemetry broadcaster.

## [1.7.6-dev] - 2026-10-28
### Fixed
- **SimHub ShakeIt Bass Shakers Port & Packet Size Alignment**:
  - **Packet Size Normalization to 311 Bytes**: Aligned `ForzaPacket` with SimHub's native Forza Motorsport 7 (`FM7Manager`) Dash packet specification (`Size = 311`, removing 13-byte trailing FH5 pad). This satisfies SimHub's strict length validation (`bytes.Length == 311`) in `ForzaReader.Forzareader_OnAcceptBytes`, preventing packets from being discarded as corrupt/unrecognized.
  - **Universal Dual-Port Broadcasting (`8000` & `5606`)**: Updated default UDP broadcast port from `5606` to `8000` (SimHub's default Forza listener). Implemented concurrent dual-port transmission to both `8000` (FM7) and `5606` (FH standard) on `127.0.0.1` so SimHub immediately detects active telemetry regardless of which profile is selected.
  - **Socket Resilience & Windows SIO_UDP_CONNRESET**: Configured `SIO_UDP_CONNRESET = -1744830452` on `TelemetryBroadcaster._udp` to prevent Windows from generating `WSAECONNRESET` exceptions when broadcasting to secondary localhost ports. Removed non-blocking mode to ensure reliable, zero-drop UDP delivery under high CPU/rendering load.
  - **Telemetry Logging & Audit Trail**: Added automatic diagnostic logging to `UserData/SimHubTelemetry.log` and console output confirming telemetry transmission status, packet size, live RPM, speed, and gear.
  - **Verified End-to-End**: Tested live against active SimHub instance; verified SimHub logs `UDP Reader on port 8000 started receiving valid data`, `Game connected`, and successfully allocates the FMOD transducer output for `Speakers (USB2.0 Device)`.

## [1.7.5-dev] - 2026-10-28
### Infrastructure & Repository Consolidation
- **Upstream Lineage & Official Fork Provisioning**:
  - Forked official repository `segadreamcat/overthehillvr` to `wtan404/overthehillvr` via GitHub API to establish formal upstream fork network ancestry.
  - Reconfigured local remotes: `origin` now points to `wtan404/overthehillvr.git` and `upstream` points to `segadreamcat/overthehillvr.git`.
  - Established 2-repository consolidated architecture, preparing retirement of standalone ZIP export repository `wtan404/overthehill-WT`.
- **PR 1 Branch Assembly (feat/fanatec-DD)**:
  - Branched cleanly from `upstream/main` (`b19cd42`) with zero commit divergence.
  - Isolated and published PR 1 (`feat/fanatec-DD`) containing Fanatec CSL DD / Direct Drive support, hardware watchdog safeties, torque slew rate limiter, Unity 6 input fix, evaluation stack JIT fix, and setup guide.
  - Successfully quarantined OpenXR backend architecture (`IVrBackend`, P/Invoke layer) on `openxr-support` branch for future independent PR submission.

## [1.7.4-dev] - 2026-10-28
### Added
- **SimHub ShakeIt Bass Shakers & Tactile Telemetry UDP Broadcaster**:
  - Implemented real-time UDP telemetry streaming adhering to the **Forza Motorsport / Horizon (Dash protocol, 324-byte payload)** broadcast standard targeting `127.0.0.1:5606` at 60 Hz.
  - Automatically interfaces with SimHub's native Forza Horizon / Motorsport game profile with zero manual scripting required.
  - Channels hooked directly from engine physics into tactile effects:
    - **Engine Vibration**: Live RPM harmonics (`carStatus.EngineRpm`).
    - **Gear Shift Thump**: Instantaneous tactile impulse triggered on paddle upshift/downshift (`carStatus.GearboxGear`).
    - **Road Bumps & Surface Rumble**: Vertical frame-delta acceleration (`accelY`) and suspension ride height state.
    - **Road Speed / Wind**: Scaled vehicle ground velocity (`carStatus.VehicleSpeed`).
    - **Impacts & Collisions**: Shock impulse pulses (`ffbHit`).
    - **Steering Dynamics**: Real-time steering input angle (`CarSteer()`).
  - Added sleep / mute safeguards: automatically sends a single zeroed packet to silence transducers when the vehicle is paused, exited, or parked, preventing persistent low-frequency idling rumble in menus.
  - Added dedicated configuration settings in `UserData/MelonPreferences.cfg` under `[SimHubTelemetry]`.
- **CLR JIT Verifier Optimization (`SafeFfb`)**:
  - Resolved an evaluation stack discrepancy in `VrDriver.SafeFfb` where an extra evaluation load was emitted before checking the Direct Drive clamp bounds, guaranteeing 100% clean JIT verification across all patched methods.

## [1.7.3-dev] - 2026-10-28
### Fixed
- **Direct Drive Force Feedback Activation & Evaluation Stack Resolution**:
  - Fixed an evaluation stack misalignment in `VrDriver.FfbTick` where an extraneous `ldarg.0` instruction was erroneously emitted before calling the static method `GameFocused()`. This caused a stack offset violation and triggered an `InvalidProgramException` at the CLR JIT boundary, halting force feedback dispatch after three warning ticks.
  - Declared explicit stack ceiling (`MaxStackSize = 32`) on `FfbTick`, `SafeFfb`, `SafeDamper`, and `WatchdogCheck`, fully resolving the JIT verifier exception and restoring active force feedback to Fanatec Direct Drive hardware.
- **Meta Quest Guardian Recenter & View Re-Alignment**:
  - Implemented automatic Guardian tracking space recenter detection in `VrDriver.ReadPoses()`. When the user holds the Meta button to recenter via Quest OS, the game detects the instantaneous tracking origin teleportation ($d > 0.25\text{ m}$) and automatically re-anchors the in-game seated origin (`needRecenter = true; seatValid = false;`), properly locking direction straight forward and height to the driver seat eye level.
  - Enabled `WheelButtonLog = true` in `UserData/MelonPreferences.cfg` to allow direct in-game mapping of physical Fanatec wheel rim buttons to `WheelBtnRecenter`.
## [1.7.2-dev] - 2026-10-28
### Added
- **Direct Drive Wheelbase Hardware Safeties & Watchdog Protection**:
  - **Asynchronous Watchdog Heartbeat**: Injected `WatchdogCheck()` into the primary game update loop (`VrDriver.Update`). If the physics loop freezes, crashes, or drops frames for more than 200ms (`unscaledTime - ffbLastTick > 0.20s`), constant force is instantly zeroed (`DIManager.UpdateConstantForceSimple(ffbDev, 0)`) to prevent open-loop torque latching.
  - **Application Focus & Menu Auto-Zeroing**: Integrated foreground focus validation (`GameFocused()`) and vehicle active state (`flag`). If the player pauses, enters a menu, alt-tabs, or opens a VR dashboard overlay, FFB torque is immediately disengaged.
  - **Torque Jerk & Slew Rate Limiter (`SafeFfb`)**: Implemented dynamic frame-delta slew rate clamping limited to 12,000 units/second (`maxDelta = max(50, 12000 * dt)`). Prevents sudden high-torque impulse spikes from physics collisions or wheel glitches from whipping the rim violently.
  - **Direct Drive Dynamic Torque Scaling & Ceiling Clamp**: Implemented auto-scaling (0.65x multiplier) for detected Direct Drive wheelbases, capped strictly at `[-8500, +8500]` (85% hardware saturation ceiling) to protect motor coils and user wrists during testing.
  - **Anti-Oscillation Damping Floor (`SafeDamper`)**: Injected dynamic damping protection for Direct Drive bases with a 12% minimum damper floor (`targetDamper >= 1200`) while driving. Prevents violent hands-off high-speed resonance oscillation ("death wobble") on straightaways.
  - **Teardown & Crash Safety**: Patched `VrDriver.Quit()` to ensure `FfbStop()` is unconditionally called upon game exit, and patched error handling to disengage torque if FFB errors exceed thresholds.
- **Reproducible Safety Patch Script**: Added `scripts/patch_safeties.ps1` to version control for deterministic IL patching and verification of Direct Drive hardware safety systems.
## [1.7.1-dev] - 2026-10-28
### Fixed
- **Unity 6 Driving Gatekeeper & Fanatec Wheel Inactivity**:
  - Fixed `CockpitUpdate.IsDriving` which previously evaluated to `false` due to Unity 6's disabled `playerInputActions.Driving` state when using DirectInput wheel peripherals.
  - Now correctly resolves driving state via `GameInput.IsMapActive((Map)0) && OwnsVehicle` and automatically activates the `Driving` action map.
  - Fixed `WheelInputFix.Prepare` which was forcing steering, throttle, and brake inputs to zero whenever `IsDriving` returned `false`.
  - Fixed paddle gear shifting (`RequestGear`) which was previously dropping left/right paddle gear requests while in Neutral.
- **Direct Drive FFB Test-Kick Bypass**:
  - Automatically initialized standard DirectInput constant force direction (`ffbDir = 1`) upon FFB connection, bypassing the inconclusive 8-second auto-direction test loop on Fanatec high-torque direct drive wheel bases.
- **Binary Deployment**:
  - Rebuilt and deployed patched `OverTheHillVR.dll` and `OverTheHillVR.CockpitUpdate.dll` directly to the active Steam installation directory.

## [1.7.0-dev] - 2026-10-28
### Added
- **Dual-Backend VR Architecture (`IVrBackend`)**:
  - Implemented `IVrBackend` abstraction interface for hot-switching between OpenVR (SteamVR) and native OpenXR.
  - Implemented `OpenVrBackend` encapsulating Valve OpenVR (`openvr_api.dll`) with 100% legacy SteamVR preservation.
  - Implemented `OpenXrBackend` for direct hardware execution on Meta Quest Link and Virtual Desktop VDXR, bypassing SteamVR entirely.
  - Added native OpenXR 1.0 C# P/Invoke bindings (`OpenXR.cs`, `OpenXrEnums.cs`, `OpenXrStructs.cs`) targeting `openxr_loader.dll`.
  - Added official Khronos x64 `openxr_loader.dll` under `Mods/OverTheHillVR/`.
  - Added `VrRuntime = "Auto"` setting in `UserData/MelonPreferences.cfg` with options `"Auto"`, `"OpenXR"`, and `"SteamVR"`.
- **Branch & Release Infrastructure**:
  - Pushed stable release baseline to `origin/main` as repository default branch.
  - Created and published `openxr-support` feature branch.

## [1.6.3-dev] - 2026-10-28
### Infrastructure & Version Control
- **GitHub Remote Repository Provisioned**:
  - Created remote repository [`wtan404/overthehill-WT`](https://github.com/wtan404/overthehill-WT).
  - Linked `origin` remote to `https://github.com/wtan404/overthehill-WT.git`.
  - Configured Git HTTP post buffer (`http.postBuffer = 524288000`) for large binary payloads (`version.dll` and runtime assemblies).
  - Pushed and established upstream tracking for the active `dev` branch.

## [1.6.2-dev] - 2026-10-28
### Added
- **Step-by-Step Installation & Setup Guide (`INSTALL_GUIDE.md`)**:
  - Comprehensive guide covering end-to-end setup for Over the Hill VR with MelonLoader 0.7.3.
  - Non-destructive archival and backup protocol preventing accidental overwrite of existing or original game files (`version.dll`, `MelonLoader/`, `Mods/`, `UserData/`).
  - Clear rollback and uninstallation procedures for instant reversion to pure vanilla state.
  - Dedicated hardware configuration sections for Fanatec CSL DD (Topology A daisy-chain and Topology B split USB), Logitech G29, Meta Quest Touch controllers, and standard gamepads.
  - Steam launch parameters (`-force-d3d11`) and in-game calibration/hotkey reference (`F6`, `F8`, `F11`, `F12`, seat adjustments).
- **Documentation Linkage**: Linked `INSTALL_GUIDE.md` in `README.md`.

## [1.6.1-dev] - 2026-10-27
### Added
- **Fanatec CSL DD & Direct Drive Wheel Base Support**:
  - Implemented device identification for Fanatec hardware (CSL DD, GT DD Pro, ClubSport DD, Podium) with dedicated DirectInput scoring (+20 priority).
  - Implemented **Dual-Topology Input Architecture**:
    - **Topology A (Unified Hub)**: Supports single-connection setups with pedals, shifters, and handbrakes daisy-chained via RJ12 directly into the back of the CSL DD base.
    - **Topology B (Split USB)**: Auto-detects and processes modular setups where pedals or shifters are plugged into separate USB ports on the PC.
  - **Load Cell Brake Support**: Dynamically reads load cell brake axis signals (`lZ`) alongside standard potentiometer axes (`lRz`) and throttle (`lY`/`lZ`).
  - **Direct Drive Torque Safety**: Softened the startup calibration torque kick (reduced from 30% to 8%) to prevent sudden 8 Nm steering snaps on high-torque direct drive motors.
  - **Input Conflict Prevention**: Registered Fanatec device signatures (`fanatec`, `csl`, `clubsport`, `podium`) in `DisableRawWheelDevices` to prevent Unity's default InputSystem from interfering with DirectInput polling.
  - **C# Source Scaffold**: Extracted structured, editable C# source trees for both `OverTheHillVR` and `OverTheHillVR.CockpitUpdate` under `src/`.

### Fixed
- **MelonLoader Packaging Hierarchy**: Restructured root directories by relocating `net35/`, `net6/`, and `Documentation/` into standard `MelonLoader/` structure to ensure the native bootstrap `version.dll` resolves runtime assemblies properly.
- **MelonLoader Runtime Dependencies**: Restored 84 missing runtime dependency assemblies from official MelonLoader v0.7.3 x64 distribution that were lost during upstream web upload truncation.

