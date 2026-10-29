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
        public static float HeartbeatInterval = 1f / 10f; // 10 Hz when idle/not driving

        private static UdpClient _udp;
        private static IPEndPoint _primaryEndPoint;
        private static IPEndPoint _secondaryEndPoint;
        private static float _lastSendTime;
        private static float _lastHeartbeatTime;
        private static float _lastSpeed;
        private static int _lastGear = -999;
        private static float _gearShiftPulseRemaining;
        private static readonly byte[] _buffer = new byte[PacketSize];
        private static ForzaPacket _packet;
        private static bool _initialized;
        private static bool _firstPacketLogged;
        private static int _errorCount;
        private static ulong _packetsSent;

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

                // Dual-port broadcast: if target is 8000, secondary is 5606; if target is 5606, secondary is 8000
                int secondaryPort = (TargetPort == 8000) ? 5606 : 8000;
                _secondaryEndPoint = new IPEndPoint(targetAddr, secondaryPort);

                _udp = new UdpClient();
                _udp.Client.SendBufferSize = 8192;

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
                _lastGear = -999;
                _gearShiftPulseRemaining = 0f;

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
            if (!_initialized)
            {
                Init();
            }

            if (!Enabled) return;

            // When not actively driving (e.g. menus or walking), send low-frequency alive heartbeat
            // Keep IsRaceOn = 1 and zero out RPM/speed so transducers naturally quiet down without SimHub muting FMOD
            if (!isDriving)
            {
                if (timeNow - _lastHeartbeatTime < HeartbeatInterval)
                {
                    return;
                }
                _lastHeartbeatTime = timeNow;
                SendHeartbeat(timeNow);
                return;
            }

            // Pacing check for 60 Hz
            float dt = timeNow - _lastSendTime;
            if (dt < BroadcastInterval)
            {
                return;
            }
            if (dt > 0.5f) dt = BroadcastInterval; // guard against large time steps after pause
            _lastSendTime = timeNow;

            try
            {
                _packet.IsRaceOn = 1;
                _packet.TimestampMS = (uint)(timeNow * 1000f);

                // Engine RPM harmonics: clamp to idle floor (850 RPM) so shakers constantly hum at idle
                const float idleFloor = 850f;
                const float maxRpm = 7500f;
                _packet.EngineIdleRpm = idleFloor;
                _packet.EngineMaxRpm = maxRpm;
                _packet.CurrentEngineRpm = Math.Clamp(MathF.Max(idleFloor, rpm), 0f, maxRpm);

                // Speed & Wheel Angular Rotation (omega = v / r, tire radius approx 0.33m -> factor ~3.03)
                float nonNegSpeed = MathF.Max(0f, speed);
                _packet.Speed = nonNegSpeed;
                _packet.VelocityZ = nonNegSpeed;
                float wheelRot = nonNegSpeed * 3.03f;
                _packet.WheelRotationSpeedFrontLeft = wheelRot;
                _packet.WheelRotationSpeedFrontRight = wheelRot;
                _packet.WheelRotationSpeedRearLeft = wheelRot;
                _packet.WheelRotationSpeedRearRight = wheelRot;

                // Longitudinal acceleration (az = deltaV / deltaT)
                float accelZ = (nonNegSpeed - _lastSpeed) / MathF.Max(0.001f, dt);
                _lastSpeed = nonNegSpeed;
                _packet.AccelerationZ = Math.Clamp(accelZ, -25f, 25f);

                // Lateral acceleration (ax ~ steer * speed)
                float accelX = steer * nonNegSpeed * 0.25f;
                _packet.AccelerationX = Math.Clamp(accelX, -20f, 20f);

                // Gear: 0 = Reverse, 0 = Neutral, 1..N = 1st..Nth
                byte fzGear = 0;
                if (gear > 0) fzGear = (byte)gear;
                _packet.Gear = fzGear;

                // Gear Shift Tactile Thump
                if (gear != _lastGear && _lastGear != -999)
                {
                    _gearShiftPulseRemaining = 0.12f; // 120ms tactile impulse
                }
                _lastGear = gear;

                float shiftKick = 0f;
                if (_gearShiftPulseRemaining > 0f)
                {
                    _gearShiftPulseRemaining -= dt;
                    shiftKick = 8f; // Add vertical impulse spike for gear engagement
                }

                // Road Bumps & Surface Rumble
                float bumpIntensity = Math.Clamp(MathF.Abs(accelY) / 6f, 0f, 1f);
                if (MathF.Abs(ffbHit) > 0.02f)
                {
                    bumpIntensity = MathF.Max(bumpIntensity, Math.Clamp(MathF.Abs(ffbHit), 0f, 1f));
                }

                // Procedural micro-texture rumble scaled with speed
                float speedRatio = Math.Clamp(nonNegSpeed / 35f, 0f, 1f);
                float roadMicro = speedRatio * (0.05f + 0.04f * MathF.Sin(timeNow * 45f));
                float totalRumble = Math.Clamp(roadMicro + bumpIntensity, 0f, 1f);

                _packet.SurfaceRumbleFrontLeft = totalRumble;
                _packet.SurfaceRumbleFrontRight = totalRumble;
                _packet.SurfaceRumbleRearLeft = totalRumble;
                _packet.SurfaceRumbleRearRight = totalRumble;

                // Vertical Acceleration (accelY + hit spikes + shift kicks)
                float hitSpike = (MathF.Abs(ffbHit) > 0.02f) ? (ffbHit * 20f) : 0f;
                _packet.AccelerationY = accelY + hitSpike + shiftKick;

                // Normalized Suspension Travel (pitch squat / dive based on accelZ)
                float suspSquat = Math.Clamp(accelZ * 0.02f, -0.2f, 0.2f);
                _packet.NormalizedSuspensionTravelFrontLeft = 0.5f - suspSquat;
                _packet.NormalizedSuspensionTravelFrontRight = 0.5f - suspSquat;
                _packet.NormalizedSuspensionTravelRearLeft = 0.5f + suspSquat;
                _packet.NormalizedSuspensionTravelRearRight = 0.5f + suspSquat;

                // Pedal & Brake Approximation for SimHub ABS / TC
                float decelG = (accelZ < -2f) ? Math.Clamp((-accelZ - 2f) / 12f, 0f, 1f) : 0f;
                _packet.Brake = (byte)(decelG * 255f);
                float accelG = (accelZ > 0.8f || rpm > 1100f) ? Math.Clamp((rpm - idleFloor) / 3500f, 0f, 1f) : 0f;
                _packet.Accel = (byte)(accelG * 255f);

                // Wheel Slip Ratios (drives Wheel Slip / Traction Loss effects)
                float latSlip = MathF.Abs(steer) * Math.Clamp(nonNegSpeed / 15f, 0f, 1f) * 0.35f;
                float combSlip = Math.Clamp(decelG * 0.5f + latSlip, 0f, 1f);
                _packet.TireSlipRatioFrontLeft = decelG * 0.5f;
                _packet.TireSlipRatioFrontRight = decelG * 0.5f;
                _packet.TireSlipRatioRearLeft = decelG * 0.4f;
                _packet.TireSlipRatioRearRight = decelG * 0.4f;
                _packet.TireCombinedSlipFrontLeft = combSlip;
                _packet.TireCombinedSlipFrontRight = combSlip;
                _packet.TireCombinedSlipRearLeft = combSlip;
                _packet.TireCombinedSlipRearRight = combSlip;

                // Steer (-127 to +127)
                _packet.Steer = (sbyte)(Math.Clamp(steer, -1f, 1f) * 127f);

                _packet.Power = 150000f;
                _packet.Torque = 300f;

                TransmitCurrentPacket();

                _packetsSent++;
                if (!_firstPacketLogged)
                {
                    _firstPacketLogged = true;
                    LogMsg($"[SimHubTelemetry] Active telemetry transmitting to {TargetIP}:{TargetPort} ({PacketSize}B, RPM={_packet.CurrentEngineRpm:0}, Spd={nonNegSpeed:0.0}m/s, Gear={fzGear})");
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

        private static void SendHeartbeat(float timeNow)
        {
            try
            {
                _packet.IsRaceOn = 1;
                _packet.TimestampMS = (uint)(timeNow * 1000f);
                _packet.EngineIdleRpm = 850f;
                _packet.EngineMaxRpm = 7500f;
                _packet.CurrentEngineRpm = 0f;
                _packet.Speed = 0f;
                _packet.VelocityZ = 0f;
                _packet.Gear = 0;
                _packet.AccelerationX = 0f;
                _packet.AccelerationY = 0f;
                _packet.AccelerationZ = 0f;
                _packet.SurfaceRumbleFrontLeft = 0f;
                _packet.SurfaceRumbleFrontRight = 0f;
                _packet.SurfaceRumbleRearLeft = 0f;
                _packet.SurfaceRumbleRearRight = 0f;
                _packet.TireSlipRatioFrontLeft = 0f;
                _packet.TireSlipRatioFrontRight = 0f;
                _packet.TireSlipRatioRearLeft = 0f;
                _packet.TireSlipRatioRearRight = 0f;
                _packet.TireCombinedSlipFrontLeft = 0f;
                _packet.TireCombinedSlipFrontRight = 0f;
                _packet.TireCombinedSlipRearLeft = 0f;
                _packet.TireCombinedSlipRearRight = 0f;
                _packet.Brake = 0;
                _packet.Accel = 0;
                _packet.Steer = 0;

                TransmitCurrentPacket();
            }
            catch {}
        }

        private static void TransmitCurrentPacket()
        {
            if (_udp == null || _primaryEndPoint == null) return;

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
        }

        public static void Shutdown()
        {
            try
            {
                // Send a clean disconnect packet (IsRaceOn = 0)
                if (_udp != null && _primaryEndPoint != null)
                {
                    Array.Clear(_buffer, 0, _buffer.Length);
                    _udp.Send(_buffer, PacketSize, _primaryEndPoint);
                    if (_secondaryEndPoint != null)
                    {
                        try { _udp.Send(_buffer, PacketSize, _secondaryEndPoint); } catch {}
                    }
                    _udp.Close();
                    _udp.Dispose();
                    _udp = null;
                }
                _initialized = false;
                _firstPacketLogged = false;
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
                string logDir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
            }
            catch {}
        }
    }
}
