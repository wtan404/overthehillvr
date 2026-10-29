using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace OverTheHillVR.Telemetry
{
    public static class TelemetryBroadcaster
    {
        public const int PacketSize = 311;
        public static bool Enabled = true;
        public static string TargetIP = "127.0.0.1";
        public static int TargetPort = 8000;
        public static float BroadcastInterval = 1f / 60f; // 60 Hz

        private static UdpClient _udp;
        private static IPEndPoint _primaryEndPoint;
        private static IPEndPoint _secondaryEndPoint;
        private static float _lastSendTime;
        private static readonly byte[] _buffer = new byte[PacketSize];
        private static ForzaPacket _packet;
        private static bool _initialized;
        private static bool _wasDriving;
        private static bool _firstPacketLogged;
        private static int _errorCount;
        private static ulong _packetsSent;

        // Gear shift tactile pulse tracking
        private static int _lastGear;
        private static float _gearShiftPulse;
        private static float _gearShiftPulseTime;

        public static void Init(string ip = null, int port = -1)
        {
            try
            {
                LoadConfig();

                if (!string.IsNullOrEmpty(ip)) TargetIP = ip;
                if (port > 0) TargetPort = port;

                if (!Enabled)
                {
                    LogMsg("[SimHubTelemetry] Telemetry disabled in configuration.");
                    _initialized = true;
                    return;
                }

                if (_udp != null)
                {
                    try { _udp.Close(); _udp.Dispose(); } catch {}
                    _udp = null;
                }

                var targetAddr = IPAddress.Parse(TargetIP);
                _primaryEndPoint = new IPEndPoint(targetAddr, TargetPort);

                // Setup dual-port fallback (if target is 8000, secondary is 5606; if target is 5606, secondary is 8000)
                int secondaryPort = (TargetPort == 8000) ? 5606 : 8000;
                _secondaryEndPoint = new IPEndPoint(targetAddr, secondaryPort);

                _udp = new UdpClient();
                _udp.Client.SendBufferSize = 4096;

                // Disable WSAECONNRESET on Windows when sending to an unlistened localhost UDP port
                try
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch {}

                _initialized = true;
                _errorCount = 0;
                _packetsSent = 0;
                _firstPacketLogged = false;
                _lastGear = 0;
                _gearShiftPulse = 0f;
                _gearShiftPulseTime = 0f;

                LogMsg($"[SimHubTelemetry] Initialized UDP broadcaster targeting {TargetIP}:{TargetPort} (fallback :{secondaryPort}) @ {(1f / BroadcastInterval):0}Hz [{PacketSize} bytes]");
            }
            catch (Exception ex)
            {
                LogMsg($"[SimHubTelemetry] Initialization error: {ex.Message}");
                _initialized = false;
            }
        }

        private static void LoadConfig()
        {
            try
            {
                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "MelonPreferences.cfg");
                if (!File.Exists(cfgPath)) cfgPath = "UserData/MelonPreferences.cfg";
                if (!File.Exists(cfgPath)) return;

                bool inSection = false;
                foreach (string line in File.ReadAllLines(cfgPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[SimHubTelemetry]", StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        continue;
                    }
                    if (trimmed.StartsWith("[") && inSection)
                    {
                        break;
                    }
                    if (inSection && trimmed.Contains("="))
                    {
                        string[] parts = trimmed.Split(new[] { '=' }, 2);
                        string key = parts[0].Trim();
                        string val = parts[1].Trim().Trim('"');

                        if (key.Equals("Enable", StringComparison.OrdinalIgnoreCase))
                        {
                            Enabled = !val.Equals("false", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key.Equals("TargetIP", StringComparison.OrdinalIgnoreCase))
                        {
                            TargetIP = val;
                        }
                        else if (key.Equals("TargetPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int p))
                        {
                            TargetPort = p;
                        }
                        else if (key.Equals("BroadcastRateHz", StringComparison.OrdinalIgnoreCase) && float.TryParse(val, out float hz) && hz > 0)
                        {
                            BroadcastInterval = 1f / hz;
                        }
                    }
                }
            }
            catch {}
        }

        public static void TickSimple(
            bool isDriving,
            float timeNow,
            float rpm,
            float speed,
            int gear,
            float accelY,
            float ffbHit,
            float steer)
        {
            TickPhysics(isDriving, timeNow, rpm, speed, gear, steer, ffbHit, 0f, accelY, 0f, 0f, 0f);
        }

        public static void TickPhysics(
            bool isDriving,
            float timeNow,
            float rpm,
            float speed,
            int gear,
            float steer,
            float ffbHit,
            float accelX,
            float accelY,
            float accelZ,
            float pitchRate,
            float rollRate)
        {
            if (!_initialized)
            {
                Init();
            }

            if (!Enabled) return;

            if (!isDriving)
            {
                if (_wasDriving)
                {
                    SendZeroPacket();
                    _wasDriving = false;
                    _lastGear = 0;
                }
                return;
            }

            _wasDriving = true;

            if (timeNow - _lastSendTime < BroadcastInterval)
            {
                return;
            }
            _lastSendTime = timeNow;

            try
            {
                _packet.IsRaceOn = 1;
                _packet.TimestampMS = (uint)(timeNow * 1000f);
                _packet.EngineMaxRpm = 7500f;
                _packet.EngineIdleRpm = 850f;
                _packet.CurrentEngineRpm = MathF.Max(0f, rpm);

                // Speed & Velocity (forward)
                _packet.Speed = MathF.Max(0f, speed);
                _packet.VelocityZ = MathF.Max(0f, speed);
                _packet.VelocityX = accelX * 0.2f;
                _packet.VelocityY = accelY * 0.1f;
                _packet.Power = 150000f;
                _packet.Torque = 300f;

                // Gear handling
                byte fzGear = 0;
                if (gear > 0) fzGear = (byte)gear;
                else if (gear < 0) fzGear = 0;
                else fzGear = 0;
                _packet.Gear = fzGear;

                // Detect gear shift event and inject authoritative surge impulse
                if (_lastGear != 0 && gear != _lastGear)
                {
                    _gearShiftPulse = (gear > _lastGear) ? -14f : 12f; // Shift shock pulse
                    _gearShiftPulseTime = timeNow;
                }
                _lastGear = gear;

                float surgeExtra = 0f;
                if (timeNow - _gearShiftPulseTime < 0.085f)
                {
                    surgeExtra = _gearShiftPulse;
                }

                // G-Forces: Heave (Y = vertical bumps/dips), Sway (X = lateral), Surge (Z = longitudinal)
                float hitSpike = (MathF.Abs(ffbHit) > 0.05f) ? (ffbHit * 30f) : 0f;
                _packet.AccelerationY = accelY + hitSpike;
                _packet.AccelerationX = accelX;
                _packet.AccelerationZ = accelZ + surgeExtra;

                // Angular velocities: Pitch (dips/rises) and Roll (chassis twist/curbs)
                _packet.AngularVelocityX = pitchRate;
                _packet.AngularVelocityY = 0f;
                _packet.AngularVelocityZ = rollRate;

                // Dynamic 4-Wheel Suspension Travel (drives SimHub's WheelsImpact, WheelsVibration, and RoadTexture)
                // In SimHub: SuspensionVelocity = (Travel - PrevTravel) / dt
                float baseSusp = 0.5f;
                float heaveDeflect = Math.Clamp(accelY / 22f, -0.4f, 0.4f);
                float pitchDeflect = Math.Clamp(pitchRate * 0.15f, -0.2f, 0.2f);
                float rollDeflect = Math.Clamp(rollRate * 0.15f, -0.2f, 0.2f);

                _packet.NormalizedSuspensionTravelFrontLeft  = Math.Clamp(baseSusp + heaveDeflect - pitchDeflect + rollDeflect, 0.05f, 0.95f);
                _packet.NormalizedSuspensionTravelFrontRight = Math.Clamp(baseSusp + heaveDeflect - pitchDeflect - rollDeflect, 0.05f, 0.95f);
                _packet.NormalizedSuspensionTravelRearLeft   = Math.Clamp(baseSusp + heaveDeflect + pitchDeflect + rollDeflect, 0.05f, 0.95f);
                _packet.NormalizedSuspensionTravelRearRight  = Math.Clamp(baseSusp + heaveDeflect + pitchDeflect - rollDeflect, 0.05f, 0.95f);

                // High-frequency Surface Rumble (scales with vertical road roughness & ground speed)
                float verticalJitter = MathF.Abs(accelY);
                float speedFactor = Math.Clamp(speed / 12f, 0.15f, 1f);
                float rumble = Math.Clamp((verticalJitter / 6f) * speedFactor, 0f, 1f);

                _packet.SurfaceRumbleFrontLeft  = rumble;
                _packet.SurfaceRumbleFrontRight = rumble;
                _packet.SurfaceRumbleRearLeft   = rumble * 0.9f;
                _packet.SurfaceRumbleRearRight  = rumble * 0.9f;

                // Rumble strip / curb trigger on severe bump, dip compression, or collision jolt
                if (MathF.Abs(ffbHit) > 0.12f || MathF.Abs(accelY) > 14f)
                {
                    _packet.WheelOnRumbleStripFrontLeft = 1;
                    _packet.WheelOnRumbleStripFrontRight = 1;
                    _packet.WheelOnRumbleStripRearLeft = 1;
                    _packet.WheelOnRumbleStripRearRight = 1;
                }
                else
                {
                    _packet.WheelOnRumbleStripFrontLeft = 0;
                    _packet.WheelOnRumbleStripFrontRight = 0;
                    _packet.WheelOnRumbleStripRearLeft = 0;
                    _packet.WheelOnRumbleStripRearRight = 0;
                }

                // Tire slip angle / traction loss on hard lateral sliding
                float lateralSlip = (speed > 1.5f) ? Math.Clamp(MathF.Abs(accelX) / (speed * 0.5f), 0f, 1f) : 0f;
                _packet.TireSlipAngleFrontLeft = lateralSlip;
                _packet.TireSlipAngleFrontRight = lateralSlip;
                _packet.TireSlipAngleRearLeft = lateralSlip * 1.25f;
                _packet.TireSlipAngleRearRight = lateralSlip * 1.25f;
                _packet.TireCombinedSlipFrontLeft = lateralSlip;
                _packet.TireCombinedSlipFrontRight = lateralSlip;
                _packet.TireCombinedSlipRearLeft = lateralSlip * 1.25f;
                _packet.TireCombinedSlipRearRight = lateralSlip * 1.25f;

                // Steer (-127 to +127)
                _packet.Steer = (sbyte)(Math.Clamp(steer, -1f, 1f) * 127f);

                // Fast pointer copy to byte buffer (zero garbage allocation)
                unsafe
                {
                    fixed (byte* pBuf = _buffer)
                    {
                        *(ForzaPacket*)pBuf = _packet;
                    }
                }

                _udp.Send(_buffer, PacketSize, _primaryEndPoint);
                if (_secondaryEndPoint != null)
                {
                    try { _udp.Send(_buffer, PacketSize, _secondaryEndPoint); } catch {}
                }

                _packetsSent++;
                if (!_firstPacketLogged)
                {
                    _firstPacketLogged = true;
                    LogMsg($"[SimHubTelemetry] Active telemetry transmitting to {TargetIP}:{TargetPort} ({PacketSize}B, RPM={rpm:0}, Spd={speed:0.0}m/s, AccelY={accelY:0.0}, AccelZ={accelZ:0.0})");
                }
            }
            catch (Exception ex)
            {
                if (++_errorCount <= 3)
                {
                    LogMsg($"[SimHubTelemetry] UDP socket send error: {ex.Message} (muted after 3)");
                }
            }
        }

        private static void SendZeroPacket()
        {
            try
            {
                if (_udp == null || _primaryEndPoint == null) return;
                Array.Clear(_buffer, 0, _buffer.Length);
                _udp.Send(_buffer, PacketSize, _primaryEndPoint);
                if (_secondaryEndPoint != null)
                {
                    try { _udp.Send(_buffer, PacketSize, _secondaryEndPoint); } catch {}
                }
            }
            catch {}
        }

        public static void Shutdown()
        {
            try
            {
                SendZeroPacket();
                if (_udp != null)
                {
                    _udp.Close();
                    _udp.Dispose();
                    _udp = null;
                }
                _initialized = false;
                _firstPacketLogged = false;
                _lastGear = 0;
                LogMsg($"[SimHubTelemetry] Broadcaster shut down cleanly. Total packets sent: {_packetsSent}");
            }
            catch {}
        }

        private static void LogMsg(string msg)
        {
            try
            {
                Console.WriteLine(msg);
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "SimHubTelemetry.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
            }
            catch {}
        }
    }
}
