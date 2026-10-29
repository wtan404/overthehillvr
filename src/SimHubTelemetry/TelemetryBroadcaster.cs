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

            if (!isDriving)
            {
                if (_wasDriving)
                {
                    SendZeroPacket();
                    _wasDriving = false;
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

                // G-Forces (Y = vertical bumps, X = lateral, Z = longitudinal)
                float hitSpike = (MathF.Abs(ffbHit) > 0.05f) ? (ffbHit * 25f) : 0f;
                _packet.AccelerationY = accelY + hitSpike;
                _packet.AccelerationX = steer * speed * 0.15f;
                _packet.AccelerationZ = 0f;

                // Speed & Velocities
                _packet.Speed = MathF.Max(0f, speed);
                _packet.VelocityZ = MathF.Max(0f, speed);
                _packet.Power = 150000f;
                _packet.Torque = 300f;

                // Gear: 0 = Reverse, 0 = Neutral, 1..N = 1st..Nth
                byte fzGear = 0;
                if (gear > 0) fzGear = (byte)gear;
                else if (gear < 0) fzGear = 0;
                else fzGear = 0;
                _packet.Gear = fzGear;

                // Suspension & Surface Rumble (drives road vibrations / bumps)
                float bumpIntensity = Math.Clamp(MathF.Abs(accelY) / 12f, 0f, 1f);
                _packet.NormalizedSuspensionTravelFrontLeft = 0.5f;
                _packet.NormalizedSuspensionTravelFrontRight = 0.5f;
                _packet.NormalizedSuspensionTravelRearLeft = 0.5f;
                _packet.NormalizedSuspensionTravelRearRight = 0.5f;

                _packet.SurfaceRumbleFrontLeft = bumpIntensity;
                _packet.SurfaceRumbleFrontRight = bumpIntensity;
                _packet.SurfaceRumbleRearLeft = bumpIntensity;
                _packet.SurfaceRumbleRearRight = bumpIntensity;

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
                    LogMsg($"[SimHubTelemetry] Active telemetry transmitting to {TargetIP}:{TargetPort} ({PacketSize}B, RPM={rpm:0}, Spd={speed:0.0}m/s, Gear={fzGear})");
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
