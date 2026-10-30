using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OverTheHillVR.Hardware
{
    /// <summary>
    /// Universal input bridge for secondary USB button boxes, QMK/VIA macro keypads
    /// (such as nullbits VID 6E61 / PID 6062), and secondary DirectInput gamepads.
    /// Polling runs asynchronously via GetAsyncKeyState to capture full 256 Virtual Key states.
    /// Provides 100% 1:1 parity with steering wheel controls.
    /// </summary>
    public static class ButtonBoxBridge
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static readonly uint CurrentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        private static readonly bool[] KeyNow = new bool[256];
        private static readonly bool[] KeyPrev = new bool[256];
        private static ulong _prevWheelButtons;
        private static ulong _prevSecondaryButtons;

        // --- Configuration Settings ---
        public static bool ButtonBoxEnabled = true;
        public static bool ButtonBoxDebugLog = true;

        // Expanded Wheel Button Bindings (1-based DirectInput button numbers)
        public static int WheelBtnHandbrake = 0;
        public static int WheelBtnHeadlights = 0;
        public static int WheelBtnRoof = 0;
        public static int WheelBtnInteriorStyle = 0;
        public static int WheelBtnZoomIn = 0;
        public static int WheelBtnZoomOut = 0;
        public static int WheelBtnSeatForward = 0;
        public static int WheelBtnSeatBack = 0;
        public static int WheelBtnSeatUp = 0;
        public static int WheelBtnSeatDown = 0;
        public static int WheelBtnRecenter = 0;
        public static int WheelBtnHud = 0;
        public static int WheelBtnScreenshot = 0;
        public static int WheelBtnVrHold = 0;
        public static int WheelBtnMenu = 0;
        public static int WheelBtnView = 0;
        public static int WheelBtnA = 0;
        public static int WheelBtnB = 0;
        public static int WheelBtnX = 0;
        public static int WheelBtnY = 0;
        public static int WheelBtnLB = 0;
        public static int WheelBtnRB = 0;
        public static int WheelBtnL3 = 0;
        public static int WheelBtnR3 = 0;

        // Button Box Virtual Key (VK) Bindings (e.g. for nullbits QMK / VIA macro pads)
        public static int BbKeyHandbrake = 0;
        public static int BbKeyHeadlights = 0;
        public static int BbKeyRoof = 0;
        public static int BbKeyInteriorStyle = 0;
        public static int BbKeyZoomIn = 0;
        public static int BbKeyZoomOut = 0;
        public static int BbKeySeatForward = 0;
        public static int BbKeySeatBack = 0;
        public static int BbKeySeatUp = 0;
        public static int BbKeySeatDown = 0;
        public static int BbKeyRecenter = 0;
        public static int BbKeyHud = 0;
        public static int BbKeyScreenshot = 0;
        public static int BbKeyVrHold = 0;
        public static int BbKeyMenu = 0;
        public static int BbKeyView = 0;
        public static int BbKeyA = 0;
        public static int BbKeyB = 0;
        public static int BbKeyX = 0;
        public static int BbKeyY = 0;
        public static int BbKeyLB = 0;
        public static int BbKeyRB = 0;
        public static int BbKeyL3 = 0;
        public static int BbKeyR3 = 0;

        // Secondary DirectInput Controller Button Bindings (1-based)
        public static int BbBtnHandbrake = 0;
        public static int BbBtnHeadlights = 0;
        public static int BbBtnRoof = 0;
        public static int BbBtnInteriorStyle = 0;
        public static int BbBtnZoomIn = 0;
        public static int BbBtnZoomOut = 0;
        public static int BbBtnSeatForward = 0;
        public static int BbBtnSeatBack = 0;
        public static int BbBtnSeatUp = 0;
        public static int BbBtnSeatDown = 0;
        public static int BbBtnRecenter = 0;
        public static int BbBtnHud = 0;
        public static int BbBtnScreenshot = 0;
        public static int BbBtnVrHold = 0;
        public static int BbBtnMenu = 0;
        public static int BbBtnView = 0;
        public static int BbBtnA = 0;
        public static int BbBtnB = 0;
        public static int BbBtnX = 0;
        public static int BbBtnY = 0;
        public static int BbBtnLB = 0;
        public static int BbBtnRB = 0;
        public static int BbBtnL3 = 0;
        public static int BbBtnR3 = 0;

        private static bool _configLoaded;
        private static float _nextConfigCheck;

        // --- Cached Reflection for VrDriver Integration ---
        private static MethodInfo _miToggleRoof;
        private static MethodInfo _miNextInteriorStyle;
        private static MethodInfo _miRecenter;
        private static MethodInfo _miStopVr;
        private static MethodInfo _miStartVr;
        private static FieldInfo _fiHudOn;
        private static FieldInfo _fiSeatSaveAt;
        private static FieldInfo _fiShotPending;
        private static FieldInfo _fiVrOn;
        private static Type _modType;
        private static bool _reflectionCached;

        public static void Init()
        {
            LoadConfig();
            LogMsg("[ButtonBox] Bridge initialized with 1:1 action parity. Monitoring macro keys & controllers.");
        }

        public static void LoadConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "MelonPreferences.cfg");
                if (!File.Exists(cfgPath)) cfgPath = "UserData/MelonPreferences.cfg";
                if (!File.Exists(cfgPath)) return;

                string[] lines = File.ReadAllLines(cfgPath);
                bool inOverTheHill = false;
                bool inButtonBox = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        inOverTheHill = line.StartsWith("[OverTheHillVR]", StringComparison.OrdinalIgnoreCase);
                        inButtonBox = line.StartsWith("[ButtonBox]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if ((!inOverTheHill && !inButtonBox) || !line.Contains("=")) continue;

                    string[] parts = line.Split(new[] { '=' }, 2);
                    string key = parts[0].Trim();
                    string val = parts[1].Trim().Trim('"');

                    // Global enable / debug settings
                    if (key.Equals("ButtonBoxEnabled", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out ButtonBoxEnabled);
                    else if (key.Equals("ButtonBoxDebugLog", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out ButtonBoxDebugLog);

                    // Wheel bindings (DirectInput 1-based)
                    else if (key.Equals("WheelBtnHandbrake", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnHandbrake);
                    else if (key.Equals("WheelBtnHeadlights", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnHeadlights);
                    else if (key.Equals("WheelBtnRoof", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnRoof);
                    else if (key.Equals("WheelBtnInteriorStyle", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnInteriorStyle);
                    else if (key.Equals("WheelBtnZoomIn", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnZoomIn);
                    else if (key.Equals("WheelBtnZoomOut", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnZoomOut);
                    else if (key.Equals("WheelBtnSeatForward", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatForward);
                    else if (key.Equals("WheelBtnSeatBack", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatBack);
                    else if (key.Equals("WheelBtnSeatUp", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatUp);
                    else if (key.Equals("WheelBtnSeatDown", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatDown);
                    else if (key.Equals("WheelBtnRecenter", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnRecenter);
                    else if (key.Equals("WheelBtnHud", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnHud);
                    else if (key.Equals("WheelBtnScreenshot", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnScreenshot);
                    else if (key.Equals("WheelBtnVrHold", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnVrHold);
                    else if (key.Equals("WheelBtnMenu", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnMenu);
                    else if (key.Equals("WheelBtnView", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnView);
                    else if (key.Equals("WheelBtnA", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnA);
                    else if (key.Equals("WheelBtnB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnB);
                    else if (key.Equals("WheelBtnX", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnX);
                    else if (key.Equals("WheelBtnY", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnY);
                    else if (key.Equals("WheelBtnLB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnLB);
                    else if (key.Equals("WheelBtnRB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnRB);
                    else if (key.Equals("WheelBtnL3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnL3);
                    else if (key.Equals("WheelBtnR3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnR3);

                    // Button Box Virtual Key (VK) Bindings
                    else if (key.Equals("BbKeyHandbrake", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyHandbrake);
                    else if (key.Equals("BbKeyHeadlights", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyHeadlights);
                    else if (key.Equals("BbKeyRoof", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyRoof);
                    else if (key.Equals("BbKeyInteriorStyle", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyInteriorStyle);
                    else if (key.Equals("BbKeyZoomIn", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyZoomIn);
                    else if (key.Equals("BbKeyZoomOut", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyZoomOut);
                    else if (key.Equals("BbKeySeatForward", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeySeatForward);
                    else if (key.Equals("BbKeySeatBack", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeySeatBack);
                    else if (key.Equals("BbKeySeatUp", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeySeatUp);
                    else if (key.Equals("BbKeySeatDown", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeySeatDown);
                    else if (key.Equals("BbKeyRecenter", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyRecenter);
                    else if (key.Equals("BbKeyHud", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyHud);
                    else if (key.Equals("BbKeyScreenshot", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyScreenshot);
                    else if (key.Equals("BbKeyVrHold", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyVrHold);
                    else if (key.Equals("BbKeyMenu", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyMenu);
                    else if (key.Equals("BbKeyView", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyView);
                    else if (key.Equals("BbKeyA", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyA);
                    else if (key.Equals("BbKeyB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyB);
                    else if (key.Equals("BbKeyX", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyX);
                    else if (key.Equals("BbKeyY", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyY);
                    else if (key.Equals("BbKeyLB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyLB);
                    else if (key.Equals("BbKeyRB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyRB);
                    else if (key.Equals("BbKeyL3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyL3);
                    else if (key.Equals("BbKeyR3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbKeyR3);

                    // Secondary DirectInput Gamepad Bindings (1-based)
                    else if (key.Equals("BbBtnHandbrake", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnHandbrake);
                    else if (key.Equals("BbBtnHeadlights", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnHeadlights);
                    else if (key.Equals("BbBtnRoof", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnRoof);
                    else if (key.Equals("BbBtnInteriorStyle", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnInteriorStyle);
                    else if (key.Equals("BbBtnZoomIn", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnZoomIn);
                    else if (key.Equals("BbBtnZoomOut", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnZoomOut);
                    else if (key.Equals("BbBtnSeatForward", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnSeatForward);
                    else if (key.Equals("BbBtnSeatBack", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnSeatBack);
                    else if (key.Equals("BbBtnSeatUp", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnSeatUp);
                    else if (key.Equals("BbBtnSeatDown", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnSeatDown);
                    else if (key.Equals("BbBtnRecenter", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnRecenter);
                    else if (key.Equals("BbBtnHud", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnHud);
                    else if (key.Equals("BbBtnScreenshot", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnScreenshot);
                    else if (key.Equals("BbBtnVrHold", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnVrHold);
                    else if (key.Equals("BbBtnMenu", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnMenu);
                    else if (key.Equals("BbBtnView", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnView);
                    else if (key.Equals("BbBtnA", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnA);
                    else if (key.Equals("BbBtnB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnB);
                    else if (key.Equals("BbBtnX", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnX);
                    else if (key.Equals("BbBtnY", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnY);
                    else if (key.Equals("BbBtnLB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnLB);
                    else if (key.Equals("BbBtnRB", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnRB);
                    else if (key.Equals("BbBtnL3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnL3);
                    else if (key.Equals("BbBtnR3", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out BbBtnR3);
                }

                _configLoaded = true;
            }
            catch (Exception ex)
            {
                LogMsg($"[ButtonBox] Error loading configuration: {ex.Message}");
            }
        }

        private static bool IsWindowFocused()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == CurrentPid;
            }
            catch
            {
                return true;
            }
        }

        public static void PollKeys(float timeNow)
        {
            if (!_configLoaded || timeNow > _nextConfigCheck)
            {
                _nextConfigCheck = timeNow + 5f;
                LoadConfig();
            }

            if (!ButtonBoxEnabled) return;

            bool focused = IsWindowFocused();

            for (int vk = 1; vk < 256; vk++)
            {
                KeyPrev[vk] = KeyNow[vk];
                if (focused)
                {
                    KeyNow[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                else
                {
                    KeyNow[vk] = false;
                }

                if (KeyNow[vk] && !KeyPrev[vk])
                {
                    if (ButtonBoxDebugLog)
                    {
                        string name = GetKeyName(vk);
                        LogMsg($"[ButtonBox] Key Down: VK {vk} (0x{vk:X2}) -> '{name}'");
                    }
                }
            }
        }

        public static bool IsKeyDown(int vk) => vk > 0 && vk < 256 && KeyNow[vk] && !KeyPrev[vk];
        public static bool IsKeyHeld(int vk) => vk > 0 && vk < 256 && KeyNow[vk];

        private static bool IsBtnDown(int oneBasedBtn, ulong currentMask, ulong prevMask)
        {
            if (oneBasedBtn < 1 || oneBasedBtn > 64) return false;
            ulong mask = 1UL << (oneBasedBtn - 1);
            return (currentMask & mask) != 0 && (prevMask & mask) == 0;
        }

        private static bool IsBtnHeld(int oneBasedBtn, ulong currentMask)
        {
            if (oneBasedBtn < 1 || oneBasedBtn > 64) return false;
            return (currentMask & (1UL << (oneBasedBtn - 1))) != 0;
        }

        /// <summary>
        /// Merges wheel buttons, secondary gamepads, and button box virtual keys into the game's virtual gamepad pad bits.
        /// </summary>
        public static uint ProcessPad(uint currentPad, ulong wheelButtons, ulong secondaryButtons)
        {
            // Handbrake (Bit 3, mask 0x08)
            if (IsBtnHeld(WheelBtnHandbrake, wheelButtons) ||
                IsBtnHeld(BbBtnHandbrake, secondaryButtons) ||
                IsKeyHeld(BbKeyHandbrake))
            {
                currentPad |= 8u;
            }

            // Headlights (Bit 2, mask 0x04)
            if (IsBtnHeld(WheelBtnHeadlights, wheelButtons) ||
                IsBtnHeld(BbBtnHeadlights, secondaryButtons) ||
                IsKeyHeld(BbKeyHeadlights))
            {
                currentPad |= 4u;
            }

            // Camera Zoom In (Bit 0, mask 0x01)
            if (IsBtnHeld(WheelBtnZoomIn, wheelButtons) ||
                IsBtnHeld(BbBtnZoomIn, secondaryButtons) ||
                IsKeyHeld(BbKeyZoomIn))
            {
                currentPad |= 1u;
            }

            // Camera Zoom Out (Bit 1, mask 0x02)
            if (IsBtnHeld(WheelBtnZoomOut, wheelButtons) ||
                IsBtnHeld(BbBtnZoomOut, secondaryButtons) ||
                IsKeyHeld(BbKeyZoomOut))
            {
                currentPad |= 2u;
            }

            // Menu / Pause / Start (Bit 12, mask 0x1000 = 4096)
            if (IsBtnHeld(WheelBtnMenu, wheelButtons) ||
                IsBtnHeld(BbBtnMenu, secondaryButtons) ||
                IsKeyHeld(BbKeyMenu))
            {
                currentPad |= 4096u;
            }

            // View / Back / Select (Bit 13, mask 0x2000 = 8192)
            if (IsBtnHeld(WheelBtnView, wheelButtons) ||
                IsBtnHeld(BbBtnView, secondaryButtons) ||
                IsKeyHeld(BbKeyView))
            {
                currentPad |= 8192u;
            }

            // South / A (Bit 6, mask 0x40 = 64)
            if (IsBtnHeld(WheelBtnA, wheelButtons) ||
                IsBtnHeld(BbBtnA, secondaryButtons) ||
                IsKeyHeld(BbKeyA))
            {
                currentPad |= 64u;
            }

            // East / B (Bit 5, mask 0x20 = 32)
            if (IsBtnHeld(WheelBtnB, wheelButtons) ||
                IsBtnHeld(BbBtnB, secondaryButtons) ||
                IsKeyHeld(BbKeyB))
            {
                currentPad |= 32u;
            }

            // West / X (Bit 7, mask 0x80 = 128)
            if (IsBtnHeld(WheelBtnX, wheelButtons) ||
                IsBtnHeld(BbBtnX, secondaryButtons) ||
                IsKeyHeld(BbKeyX))
            {
                currentPad |= 128u;
            }

            // North / Y (Bit 4, mask 0x10 = 16)
            if (IsBtnHeld(WheelBtnY, wheelButtons) ||
                IsBtnHeld(BbBtnY, secondaryButtons) ||
                IsKeyHeld(BbKeyY))
            {
                currentPad |= 16u;
            }

            // Left Shoulder / LB (Bit 10, mask 0x400 = 1024)
            if (IsBtnHeld(WheelBtnLB, wheelButtons) ||
                IsBtnHeld(BbBtnLB, secondaryButtons) ||
                IsKeyHeld(BbKeyLB))
            {
                currentPad |= 1024u;
            }

            // Right Shoulder / RB (Bit 11, mask 0x800 = 2048)
            if (IsBtnHeld(WheelBtnRB, wheelButtons) ||
                IsBtnHeld(BbBtnRB, secondaryButtons) ||
                IsKeyHeld(BbKeyRB))
            {
                currentPad |= 2048u;
            }

            // Left Thumbstick Click / L3 (Bit 8, mask 0x100 = 256)
            if (IsBtnHeld(WheelBtnL3, wheelButtons) ||
                IsBtnHeld(BbBtnL3, secondaryButtons) ||
                IsKeyHeld(BbKeyL3))
            {
                currentPad |= 256u;
            }

            // Right Thumbstick Click / R3 / Horn (Bit 9, mask 0x200 = 512)
            if (IsBtnHeld(WheelBtnR3, wheelButtons) ||
                IsBtnHeld(BbBtnR3, secondaryButtons) ||
                IsKeyHeld(BbKeyR3))
            {
                currentPad |= 512u;
            }

            _prevWheelButtons = wheelButtons;
            _prevSecondaryButtons = secondaryButtons;

            return currentPad;
        }

        /// <summary>
        /// Frame tick invoked from VrDriver.Update() to process direct driver commands:
        /// Roof, Trim Style, Recenter, HUD, Screenshot, VR Toggle, and Seat adjustments.
        /// </summary>
        public static void FrameTick(object vrDriverInstance, float timeNow, ulong wheelButtons, ulong secondaryButtons)
        {
            PollKeys(timeNow);

            if (vrDriverInstance == null) return;

            CacheReflection(vrDriverInstance);

            // 1. Cockpit Roof Toggle
            if (IsBtnDown(WheelBtnRoof, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnRoof, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyRoof))
            {
                try { _miToggleRoof?.Invoke(vrDriverInstance, null); } catch {}
            }

            // 2. Interior Trim Style Cycle
            if (IsBtnDown(WheelBtnInteriorStyle, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnInteriorStyle, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyInteriorStyle))
            {
                try { _miNextInteriorStyle?.Invoke(vrDriverInstance, null); } catch {}
            }

            // 3. VR Recenter
            if (IsBtnDown(WheelBtnRecenter, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnRecenter, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyRecenter))
            {
                try { _miRecenter?.Invoke(vrDriverInstance, null); } catch {}
            }

            // 4. HUD Toggle
            if (IsBtnDown(WheelBtnHud, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnHud, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyHud))
            {
                try
                {
                    if (_fiHudOn != null)
                    {
                        bool cur = (bool)_fiHudOn.GetValue(vrDriverInstance);
                        _fiHudOn.SetValue(vrDriverInstance, !cur);
                        LogMsg($"[ButtonBox] HUD panel {(!cur ? "on" : "off")}");
                    }
                }
                catch {}
            }

            // 5. Screenshot
            if (IsBtnDown(WheelBtnScreenshot, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnScreenshot, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyScreenshot))
            {
                try
                {
                    _fiShotPending?.SetValue(vrDriverInstance, true);
                    LogMsg("[ButtonBox] Screenshot requested.");
                }
                catch {}
            }

            // 6. VR Toggle
            if (IsBtnDown(WheelBtnVrHold, wheelButtons, _prevWheelButtons) ||
                IsBtnDown(BbBtnVrHold, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyVrHold))
            {
                try
                {
                    bool vrOn = _fiVrOn != null && (bool)_fiVrOn.GetValue(vrDriverInstance);
                    if (vrOn) _miStopVr?.Invoke(vrDriverInstance, null);
                    else _miStartVr?.Invoke(vrDriverInstance, null);
                    LogMsg($"[ButtonBox] VR toggled {(vrOn ? "off" : "on")}.");
                }
                catch {}
            }

            // 7. Driver Seat Adjustment (Horizontal Forward / Back)
            float seatDelta = 0f;
            if (IsBtnHeld(WheelBtnSeatForward, wheelButtons) ||
                IsBtnHeld(BbBtnSeatForward, secondaryButtons) ||
                IsKeyHeld(BbKeySeatForward))
            {
                seatDelta += 0.005f;
            }
            if (IsBtnHeld(WheelBtnSeatBack, wheelButtons) ||
                IsBtnHeld(BbBtnSeatBack, secondaryButtons) ||
                IsKeyHeld(BbKeySeatBack))
            {
                seatDelta -= 0.005f;
            }

            if (Math.Abs(seatDelta) > 0.0001f)
            {
                AdjustSeatForward(vrDriverInstance, seatDelta, timeNow);
            }

            // 8. Driver Seat Adjustment (Vertical Up / Down)
            float seatUpDelta = 0f;
            if (IsBtnHeld(WheelBtnSeatUp, wheelButtons) ||
                IsBtnHeld(BbBtnSeatUp, secondaryButtons) ||
                IsKeyHeld(BbKeySeatUp))
            {
                seatUpDelta += 0.005f;
            }
            if (IsBtnHeld(WheelBtnSeatDown, wheelButtons) ||
                IsBtnHeld(BbBtnSeatDown, secondaryButtons) ||
                IsKeyHeld(BbKeySeatDown))
            {
                seatUpDelta -= 0.005f;
            }

            if (Math.Abs(seatUpDelta) > 0.0001f)
            {
                AdjustSeatUp(vrDriverInstance, seatUpDelta, timeNow);
            }
        }

        private static void AdjustSeatForward(object driver, float delta, float timeNow)
        {
            try
            {
                if (_modType == null) _modType = driver.GetType().Assembly.GetType("OverTheHillVR.Mod");
                if (_modType != null)
                {
                    var fld = _modType.GetField("SeatForward", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var entry = fld?.GetValue(null);
                    if (entry != null)
                    {
                        var valProp = entry.GetType().GetProperty("Value");
                        if (valProp != null)
                        {
                            float cur = (float)valProp.GetValue(entry);
                            valProp.SetValue(entry, cur + delta);
                            _fiSeatSaveAt?.SetValue(driver, timeNow + 1f);
                        }
                    }
                }
            }
            catch {}
        }

        private static void AdjustSeatUp(object driver, float delta, float timeNow)
        {
            try
            {
                if (_modType == null) _modType = driver.GetType().Assembly.GetType("OverTheHillVR.Mod");
                if (_modType != null)
                {
                    var fld = _modType.GetField("SeatUp", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var entry = fld?.GetValue(null);
                    if (entry != null)
                    {
                        var valProp = entry.GetType().GetProperty("Value");
                        if (valProp != null)
                        {
                            float cur = (float)valProp.GetValue(entry);
                            valProp.SetValue(entry, cur + delta);
                            _fiSeatSaveAt?.SetValue(driver, timeNow + 1f);
                        }
                    }
                }
            }
            catch {}
        }

        private static void CacheReflection(object driver)
        {
            if (_reflectionCached) return;
            try
            {
                Type dt = driver.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                _miToggleRoof = dt.GetMethod("ToggleRoof", flags);
                _miNextInteriorStyle = dt.GetMethod("NextInteriorStyle", flags);
                _miRecenter = dt.GetMethod("Recenter", flags);
                _miStopVr = dt.GetMethod("StopVr", flags);
                _miStartVr = dt.GetMethod("StartVr", flags);
                _fiHudOn = dt.GetField("hudOn", flags);
                _fiSeatSaveAt = dt.GetField("seatSaveAt", flags);
                _fiShotPending = dt.GetField("shotPending", flags);
                _fiVrOn = dt.GetField("vrOn", flags);
                _modType = dt.Assembly.GetType("OverTheHillVR.Mod");

                _reflectionCached = true;
            }
            catch {}
        }

        private static void LogMsg(string msg)
        {
            try
            {
                Console.WriteLine(msg);
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "ButtonBox.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
            }
            catch {}
        }

        public static string GetKeyName(int vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x70 + 1}";
            if (vk >= 0x60 && vk <= 0x69) return $"NumPad{vk - 0x60}";
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();

            return vk switch
            {
                0x01 => "LeftMouseButton",
                0x02 => "RightMouseButton",
                0x04 => "MiddleMouseButton",
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                0x10 => "Shift",
                0x11 => "Control",
                0x12 => "Alt",
                0x13 => "Pause",
                0x14 => "CapsLock",
                0x1B => "Escape",
                0x20 => "Space",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "LeftArrow",
                0x26 => "UpArrow",
                0x27 => "RightArrow",
                0x28 => "DownArrow",
                0x2C => "PrintScreen",
                0x2D => "Insert",
                0x2E => "Delete",
                0x6A => "Multiply",
                0x6B => "Add",
                0x6C => "Separator",
                0x6D => "Subtract",
                0x6E => "Decimal",
                0x6F => "Divide",
                0x90 => "NumLock",
                0x91 => "ScrollLock",
                0xA0 => "LeftShift",
                0xA1 => "RightShift",
                0xA2 => "LeftControl",
                0xA3 => "RightControl",
                0xA4 => "LeftAlt",
                0xA5 => "RightAlt",
                0xAD => "VolumeMute",
                0xAE => "VolumeDown",
                0xAF => "VolumeUp",
                0xB0 => "MediaNextTrack",
                0xB1 => "MediaPrevTrack",
                0xB2 => "MediaStop",
                0xB3 => "MediaPlayPause",
                0xBA => "OEM_Semicolon",
                0xBB => "OEM_Plus",
                0xBC => "OEM_Comma",
                0xBD => "OEM_Minus",
                0xBE => "OEM_Period",
                0xBF => "OEM_QuestionMark",
                0xC0 => "OEM_Tilde",
                0xDB => "OEM_OpenBracket",
                0xDC => "OEM_Backslash",
                0xDD => "OEM_CloseBracket",
                0xDE => "OEM_Quotes",
                _ => $"VK_0x{vk:X2}"
            };
        }
    }
}
