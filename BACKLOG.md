# Project Feature Queue & Backlog - Over the Hill VR

This document maintains the official, prioritized feature queue and technical roadmap for future enhancements, driver extensions, and secondary pull requests.

---

## 📋 Feature Queue

### 1. [PR 2] OpenXR Dual-Backend Architecture (`IVrBackend`)
- **Status**: Implemented & Staged; Quarantined on branch [`openxr-support`](https://github.com/wtan404/overthehillvr/tree/openxr-support).
- **Target**: Upstream PR to `segadreamcat/overthehillvr` following PR 1 merge.
- **Scope & Deliverables**:
  - `IVrBackend` abstraction interface for hot-switching between legacy SteamVR (OpenVR) and native OpenXR 1.0.
  - Native Khronos C# P/Invoke bindings (`OpenXR.cs`, `OpenXrEnums.cs`, `OpenXrStructs.cs`) targeting official `openxr_loader.dll`.
  - Direct execution bypass for Meta Quest Link and Virtual Desktop VDXR, eliminating SteamVR CPU/GPU translation overhead.
  - Runtime toggle in `UserData/MelonPreferences.cfg` (`VrRuntime = "Auto" | "OpenXR" | "SteamVR"`).

---

### 2. Fanatec ClubSport H-Pattern Discrete Gate Transmission Solver
- **Status**: Queued / Architecture Formulated.
- **Target Hardware**: Fanatec ClubSport Shifter SQ V1.5 (connected via RJ12 to CSL DD or standalone USB adapter).
- **Scope & Deliverables**:
  - Implement a discrete gate transmission state machine in `CockpitUpdate.cs` to translate constant physical gate contact signals (Gears 1st through 6th/7th, Reverse, and Neutral) into the engine's sequential transmission.
  - Auto-step `GearUp()` / `GearDown()` to match physical shifter gate position with neutral clutch disengagement.
  - Provide fallback to current Sequential (`SQ`) mode (`PaddleUpButton` / `PaddleDownButton`).

---

### 3. Multi-Device DirectInput & Secondary USB Button Box Poller
- **Status**: Completed & Verified on `dev` and `openxr-support`.
- **Target Hardware**: Secondary USB macro keypads, custom DIY button boxes (nullbits `VID 6E61` / `PID 6062`), and rotary dial controllers.
- **Scope & Deliverables**:
  - Extended input architecture via `ButtonBoxBridge` to support multi-device and composite keyboard/macro polling.
  - Full 256 virtual key monitoring with automatic real-time troubleshooting logging (`ButtonBoxDebugLog` to console and `UserData/ButtonBox.log`).
  - High-speed rotary encoder pulse capture and keycode mapping.
  - Unified pad bit synthesis injecting directly into virtual gamepad event stream.

---

### 4. Expanded In-Game Wheel & Cockpit Action Bindings
- **Status**: Completed & Verified on `dev` and `openxr-support`.
- **Target**: Expose vehicle actions previously locked to D-Pad / POV hat to dedicated, configurable wheel buttons and button box keys.
- **Scope & Deliverables**:
  - `WheelBtnHandbrake` / `BbKeyHandbrake`: Dedicated handbrake / parking brake toggle (Gamepad D-Pad Right, bit 3).
  - `WheelBtnHeadlights` / `BbKeyHeadlights`: Headlights toggle (Gamepad D-Pad Left, bit 2).
  - `WheelBtnRoof` / `BbKeyRoof`: Dedicated cockpit roof toggle.
  - `WheelBtnInteriorStyle` / `BbKeyInteriorStyle`: Direct interior trim style cycle.
  - `WheelBtnZoomIn` / `BbKeyZoomIn`: Camera zoom in (Gamepad D-Pad Up, bit 0).
  - `WheelBtnZoomOut` / `BbKeyZoomOut`: Camera zoom out (Gamepad D-Pad Down, bit 1).
  - `WheelBtnSeatForward` / `WheelBtnSeatBack` / `BbKeySeatForward` / `BbKeySeatBack`: Driver horizontal seated eye-line adjustment.
  - `BbKeySeatUp` / `BbKeySeatDown`: Driver vertical seated eye-line adjustment from secondary keypad.
  - `BbKeyRecenter` / `BbKeyHud`: One-touch VR recenter and HUD toggle from secondary keypad.

---

### 5. SimHub ShakeIt Tactile Telemetry UDP Broadcaster
- **Status**: Completed & Verified on dev; Automated deployment to Steam operational.
- **Target**: Transducer / Bass Shaker rigs via SimHub (FM7 / FH Dash UDP protocol).
- **Scope & Deliverables**:
  - 60 Hz UDP broadcast normalized to standard 311-byte Forza Motorsport 7 format with dual-port concurrent dispatch (8000 & 5606).
  - Active tactile synthesis: engine vibration with 850 RPM idle floor, 120ms gear shift tactile impulses, longitudinal deceleration G-forces, and speed-scaled procedural road micro-texture rumble.
  - Non-destructive 10 Hz heartbeat maintaining IsRaceOn = 1 during vehicle transitions, completely resolving transducer muting bugs.
