# Changelog - Over the Hill VR (Fan Edition)

All notable changes to this project will be documented in this file.

## [1.8.1-dev] - 2026-10-29
### Added & Enhanced
- **Dedicated Feature Branch (`feat/buttonbox-support`)**:
  - Isolated the full button box poller, 256 VK diagnostic logger, expanded cockpit/wheel action mappings, and Steam synchronization pipeline into dedicated remote branch `feat/buttonbox-support` directly rooted on `main` (v1.8.0 release).
  - Maintained full synchronization across active development branches (`dev`, `openxr-support`, and `feat/buttonbox-support`).
- **Non-Destructive Steam Configuration Synchronization Pipeline**:
  - Enhanced `scripts/patch_safeties.ps1` (Step 14) to perform automated, non-destructive configuration reconciliation between the workspace repository and the active Steam installation (`.../over the hill/UserData/MelonPreferences.cfg`).
  - Automatically detects and merges missing vehicle action bindings (`WheelBtnHandbrake`, `WheelBtnHeadlights`, `WheelBtnRoof`, `WheelBtnInteriorStyle`, `WheelBtnZoomIn`, `WheelBtnZoomOut`, `WheelBtnSeatForward`, `WheelBtnSeatBack`) directly under `[OverTheHillVR]`.
  - Appends the complete `[ButtonBox]` configuration section with full keyboard VK macro entries and secondary controller bindings.
  - Strictly preserves 100% of the player's existing custom button mappings (e.g., custom Fanatec button bindings for seat, view, menu, and gamepad buttons).
  - Enhanced `SimHubTelemetry.Tests` test harness to dynamically resolve and verify JIT IL compilation against active release DLLs.
- **Secondary USB Button Box, QMK/VIA Macro Keypad & Rotary Encoder Poller**:
  - **Identified Hardware Root Cause**: Diagnosed nullbits device (`VID 6E61` / `PID 6062`, e.g., TIDBIT / Bit-C macro pad) operating as a composite USB HID Keyboard + Mouse rather than DirectInput game controller, causing standard DirectInput enumeration in `VrDriver` to bypass it.
  - **Universal 256 Virtual Key Poller (`ButtonBoxBridge`)**: Implemented high-performance background key state poller (`GetAsyncKeyState`) capturing all 256 virtual keys including extended macro keys (`F13`–`F24`, Numpad, and media controls) and rotary encoder pulses.
  - **Real-Time Button Box Diagnostic Logger**: Enabled automatic logging (`ButtonBoxDebugLog` to console and `UserData/ButtonBox.log`) resolving keycodes and friendly names (e.g., `0x7C` -> `F13`, `0x60` -> `NumPad0`, `0xAF` -> `VolumeUp`) immediately upon key down to facilitate instantaneous button mapping.
  - **Multi-Device Support**: Provisioned secondary controller button mapping (`BbBtn...`) alongside primary wheel buttons (`wheelButtons`) and keyboard macro keys (`BbKey...`).
- **Expanded In-Game Wheel & Cockpit Action Bindings**:
  - **Dedicated Handbrake Binding**: Mapped `WheelBtnHandbrake` and `BbKeyHandbrake` to trigger virtual gamepad D-Pad Right (bit 3 / mask `0x08`), allowing dedicated handbrake paddles/buttons without requiring D-Pad navigation.
  - **Dedicated Headlights Binding**: Mapped `WheelBtnHeadlights` and `BbKeyHeadlights` to trigger virtual gamepad D-Pad Left (bit 2 / mask `0x04`).
  - **Cockpit Roof & Trim Style Controls**: Added dedicated triggers for `WheelBtnRoof` / `BbKeyRoof` (calling `ToggleRoof()`) and `WheelBtnInteriorStyle` / `BbKeyInteriorStyle` (calling `NextInteriorStyle()`).
  - **Camera Orbit Zoom In / Out**: Mapped `WheelBtnZoomIn` / `BbKeyZoomIn` to D-Pad Up (bit 0 / mask `0x01`) and `WheelBtnZoomOut` / `BbKeyZoomOut` to D-Pad Down (bit 1 / mask `0x02`).
  - **Horizontal & Vertical Seated Eye-Line Adjustment**: Implemented smooth fine-tuning for `WheelBtnSeatForward` / `WheelBtnSeatBack` and `BbKeySeatForward` / `BbKeySeatBack` directly updating `Mod.SeatForward` in meters. Added secondary keypad bindings for `BbKeySeatUp` / `BbKeySeatDown`, `BbKeyRecenter`, and `BbKeyHud`.
- **Deterministic IL Patcher & Verification Pipeline**:
  - Updated `scripts/patch_safeties.ps1` to inject `wheelButtons` and `secondaryButtons` fields, filter pad state via `ButtonBoxBridge.ProcessPad()`, and dispatch frame ticks from `VrDriver.Update()`.
  - Added full test suite in `SimHubTelemetry.Tests` verifying key resolution, pad bit synthesis, and 100% clean CLR JIT compilation across all 10 target methods.
  - Automatically compiled and deployed patched binaries directly to Steam game directories (`Mods/` and `UserLibs/`).

## [1.8.0] - 2026-10-28
### Production Release
- **Milestone Production Release v1.8.0 (Fanatec DD, Quest Recenter & SimHub ShakeIt)**:
  - **SimHub ShakeIt Dynamic 3D Tactile Telemetry**: Integrated continuous 60 Hz UDP telemetry pipeline emitting standardized 311-byte Forza Motorsport Dash packets on ports 8000 & 5606. Derives true local 3D vehicle acceleration (vertical heave $a_y$, longitudinal surge $a_z$, lateral sway $a_x$), chassis pitch and roll rates, dynamic 4-corner suspension travel deflect generating active suspension velocity in SimHub, gear shift shock impulses, and curb rumble strip telemetry.
  - **Fanatec Direct Drive Wheelbase Hardware Safeties**: Embedded an asynchronous watchdog safety heartbeat (200ms auto-zero), dynamic torque slew rate limiter (12,000 units/s jerk clamp), 85% hardware torque saturation ceiling, and anti-oscillation damping floor (`targetDamper >= 1200`) to safeguard Direct Drive hardware and driver wrists.
  - **Meta Quest Guardian Space Auto-Recenter**: Automated tracking space jump detection ($d > 0.25\text{ m}$) in `VrDriver.ReadPoses()` that instantly re-anchors seated camera orientation and eye-level height whenever the player recenters via the Meta Quest OS menu.
  - **Unity 6 Driving Action Map Fix**: Rectified DirectInput wheel bypass logic so the vehicle driving map activates reliably, restoring full pedal response and paddle shifting functionality.
  - **Deterministic IL Patcher & Automated Steam Pipeline**: Deterministic Mono.Cecil patcher (`scripts/patch_safeties.ps1`) with full CLR JIT verifier validation and automatic deployment to Steam game directories.

## [1.7.10-dev] - 2026-10-28
### Added & Enhanced
- **Full 3D Dynamic Physics Injection for SimHub ShakeIt Bass Shakers**:
  - **Real-Time 3D Vehicle Local Acceleration**: Replaced hardcoded vertical and longitudinal acceleration with real frame-delta acceleration computed from `Rigidbody.velocity` and projected into local vehicle space via `Transform.InverseTransformDirection(worldAccel)`:
    - **Local Y (Heave)**: True road bumps, dips, compressions, and crests.
    - **Local Z (Surge)**: Authoritative braking deceleration and throttle squat.
    - **Local X (Sway)**: Lateral weight transfer and cornering g-forces.
  - **Chassis Angular Dynamics (Pitch & Roll)**: Resolved `Rigidbody.angularVelocity` into vehicle local frame:
    - **Pitch Rate**: Transmits forward/rear chassis pitch when transitioning over rises and dips.
    - **Roll Rate**: Drives left/right chassis lean and asymmetric curb vibrations.
  - **Dynamic 4-Wheel Suspension Travel & Velocity**: Synthesized dynamic suspension travel deflection across all four corners (`FL`, `FR`, `RL`, `RR`) from instantaneous heave, pitch rate, and roll rate. SimHub calculates `SuspensionVelocity = (Travel - PrevTravel) / dt`, which now produces active, non-zero velocities and completely un-mutes SimHub's **Wheels Impact**, **Wheels Vibration**, and **Road Texture** bass shaker effects.
  - **Authoritative Gear Shift Surge Pulse**: Injected instantaneous $\pm 14\text{ m/s}^2$ longitudinal surge shock pulse on paddle gear shifts for a physical kick through the transducers.
  - **Hard Impact & Rumble Strip Telemetry**: Automatically raises `WheelOnRumbleStrip` flags and high-frequency surface rumble during heavy vertical g-force events ($|a_y| > 14\text{ m/s}^2$) or collision impulses (`ffbHit > 0.12f`).
  - **Deterministic IL Injection (`patch_safeties.ps1`)**: Added `prevTelemVel`, `prevTelemTime`, and `prevTelemValid` fields to `VrDriver` and injected full physics calculation directly into `VrDriver.TelemetryTick` calling `TelemetryBroadcaster.TickPhysics`.
  - **CLR JIT Validation**: Verified patched assembly via `SimHubTelemetry.Tests -- jit`, confirming all 8 patched methods (`TelemetryTick`, `WatchdogCheck`, `SafeFfb`, `SafeDamper`, `CheckGuardianRecenter`, `FfbTick`, `Update`, `Quit`) compile and verify cleanly.
  - **Steam Deployment**: Rebuilt and copied patched `OverTheHillVR.dll` and `SimHubTelemetry.dll` directly to active Steam directories (`Mods/` and `UserLibs/`).

## [1.7.9-dev] - 2026-10-28
### Fixed
- **SimHub ShakeIt Bass Shakers Continuous Telemetry & Transducer Muting Resolution**:
  - **Eliminated Paused=True Mute Bug**: Removed destructive SendZeroPacket() zeroing calls that set IsRaceOn = 0 whenever vehicle speed was zero or during vehicle state transitions. In Forza UDP telemetry, IsRaceOn == 0 causes SimHub to transition to Paused=True and instantly tear down / mute the FMOD transducer audio interface.
  - **Non-Destructive Idle Heartbeat Stream**: Implemented a continuous low-frequency heartbeat (10 Hz) when outside the vehicle or idling that maintains IsRaceOn = 1 while keeping physics signals at 0. Transducers smoothly quiet down without triggering SimHub audio interface destruction or pops.
  - **Stationary Engine Vibration Floor (850 RPM)**: Injected a guaranteed 850 RPM idle floor (Math.Max(850f, rpm)) whenever the vehicle is occupied. Prevents bass shakers from dropping to complete silence when stopped at lights or on hills, providing persistent seat and pedal rumble that dynamically scales up to 7500 RPM under throttle.
  - **Dynamic Deceleration & Braking G-Force**: Derived longitudinal acceleration (a_z = \Delta v / \Delta t) clamped between $[-25, 25]\text{ m/s}^2$ and wired into AccelerationZ and Brake. Directly feeds SimHub's DecelerationGforceContainer and WheelsLockContainer during heavy braking.
  - **Procedural Surface Rumble & Road Micro-Texture**: Added speed-scaled procedural surface texture and road impact synthesis to SurfaceRumble (FL/FR/RL/RR) and AccelerationY. Ensures SimHub's RoadTextureContainer, WheelsRumbleContainer, and WheelsVibrationContainer deliver active road feel instead of receiving flat zeros.
  - **Tactile Gear Shift Impulse**: Implemented a 120ms vertical acceleration shock pulse ($+8\text{ m/s}^2$) on gear change events to deliver a physical thump through the transducers.
  - **Automated Steam Binary Deployment Pipeline**: Enhanced scripts/patch_safeties.ps1 to automatically deploy patched OverTheHillVR.dll and SimHubTelemetry.dll directly to the Steam game directories (Mods/ and UserLibs/), eliminating manual file copy steps.

## [1.7.8-dev] - 2026-10-28
### Maintenance & PR Preparation
- **Upstream PR 1 Streamlined (feat/fanatec-DD)**:
  - Removed internal developer documentation (`CHANGELOG.md` and `INSTALL_GUIDE.md`) from the PR branch.
  - Cleaned callout reference in `README.md` to prevent broken documentation links for upstream users.
  - Amended commit `de3c8f0` and updated `origin/feat/fanatec-DD`, maintaining a single atomic commit diff based on `upstream/main`.

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

