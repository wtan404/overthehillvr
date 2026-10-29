using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OverTheHillVR.Hardware
{
    /// <summary>
    /// Dual input poller & bridge for secondary USB button boxes, QMK/VIA macro keypads
    /// (e.g. nullbits VID 6E61 / PID 6062), rotary encoders, and expanded steering wheel bindings.
    /// </summary>
    public static class ButtonBoxBridge
    {
        // --- Windows User32 P/Invoke ---
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static readonly uint CurrentPid = (uint)Environment.ProcessId;

        // --- Key & Button States ---
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

        private static bool _configLoaded;
        private static float _nextConfigCheck;

        // --- Cached Reflection for VrDriver Integration ---
        private static MethodInfo _miToggleRoof;
        private static MethodInfo _miNextInteriorStyle;
        private static MethodInfo _miRecenter;
        private static FieldInfo _fiHudOn;
        private static FieldInfo _fiSeatSaveAt;
        private static Type _modType;
        private static bool _reflectionCached;

        public static void Init()
        {
            LoadConfig();
            LogMsg("[ButtonBox] Bridge initialized. Monitoring keyboard/macro keys & secondary controllers.");
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

                    // Wheel bindings
                    if (key.Equals("WheelBtnHandbrake", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnHandbrake);
                    else if (key.Equals("WheelBtnHeadlights", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnHeadlights);
                    else if (key.Equals("WheelBtnRoof", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnRoof);
                    else if (key.Equals("WheelBtnInteriorStyle", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnInteriorStyle);
                    else if (key.Equals("WheelBtnZoomIn", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnZoomIn);
                    else if (key.Equals("WheelBtnZoomOut", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnZoomOut);
                    else if (key.Equals("WheelBtnSeatForward", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatForward);
                    else if (key.Equals("WheelBtnSeatBack", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out WheelBtnSeatBack);

                    // Button box settings & keys
                    else if (key.Equals("ButtonBoxEnabled", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out ButtonBoxEnabled);
                    else if (key.Equals("ButtonBoxDebugLog", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out ButtonBoxDebugLog);
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

                    // Secondary DirectInput controller bindings
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
                return true; // Fallback to allow input if check fails
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
        /// Process virtual gamepad bits and merges dedicated vehicle bindings from wheel and button box.
        /// Pad bits: Bit 0 = DPadUp (ZoomIn/Roof), Bit 1 = DPadDown (ZoomOut), Bit 2 = DPadLeft (Headlights), Bit 3 = DPadRight (Handbrake).
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

            _prevWheelButtons = wheelButtons;
            _prevSecondaryButtons = secondaryButtons;

            return currentPad;
        }

        /// <summary>
        /// Frame tick invoked from VrDriver.Update() to process direct driver commands:
        /// Roof, Trim Style, Recenter, HUD, and Seat adjustments.
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
            if (IsBtnDown(BbBtnRecenter, secondaryButtons, _prevSecondaryButtons) ||
                IsKeyDown(BbKeyRecenter))
            {
                try { _miRecenter?.Invoke(vrDriverInstance, null); } catch {}
            }

            // 4. HUD Toggle
            if (IsBtnDown(BbBtnHud, secondaryButtons, _prevSecondaryButtons) ||
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

            // 5. Driver Seat Adjustment (Horizontal Forward / Back)
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

            // 6. Driver Seat Adjustment (Vertical Up / Down)
            float seatUpDelta = 0f;
            if (IsBtnHeld(BbBtnSeatUp, secondaryButtons) ||
                IsKeyHeld(BbKeySeatUp))
            {
                seatUpDelta += 0.005f;
            }
            if (IsBtnHeld(BbBtnSeatDown, secondaryButtons) ||
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
                _fiHudOn = dt.GetField("hudOn", flags);
                _fiSeatSaveAt = dt.GetField("seatSaveAt", flags);
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
            // Standard function keys F1-F24
            if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x70 + 1}";

            // Numpad 0-9
            if (vk >= 0x60 && vk <= 0x69) return $"NumPad{vk - 0x60}";

            // Alphanumeric A-Z
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();

            // Digits 0-9
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
