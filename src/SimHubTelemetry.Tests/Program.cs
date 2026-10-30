using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OverTheHillVR.Telemetry;
using OverTheHillVR.Hardware;

namespace SimHubTelemetry.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "jit")
            {
                string defaultPath = File.Exists(@"D:\Projects\overthehillvr-fan\Mods\OverTheHillVR.dll.patched")
                    ? @"D:\Projects\overthehillvr-fan\Mods\OverTheHillVR.dll.patched"
                    : @"D:\Projects\overthehillvr-fan\Mods\OverTheHillVR.dll";
                return RunJitTest(args.Length > 1 ? args[1] : defaultPath);
            }

            Console.WriteLine("--- Testing SimHub Telemetry Pipeline ---");

            int size = Marshal.SizeOf<ForzaPacket>();
            Console.WriteLine($"ForzaPacket size: {size} bytes");
            if (size != 311)
            {
                Console.WriteLine($"[FAIL] Expected 311 bytes, got {size}");
                return 1;
            }

            const int testPort = 8055;
            using var listener = new UdpClient(testPort);
            listener.Client.ReceiveTimeout = 2000;

            TelemetryBroadcaster.Init("127.0.0.1", testPort);

            TelemetryBroadcaster.TickPhysics(
                isDriving: true,
                timeNow: 5.67f,
                rpm: 5200f,
                speed: 31.4f,
                gear: 4,
                steer: 0.5f,
                ffbHit: 0.2f,
                accelX: 1.5f,
                accelY: 4.5f,
                accelZ: -2.0f,
                pitchRate: 0.12f,
                rollRate: -0.08f
            );

            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            byte[] received = listener.Receive(ref sender);

            Console.WriteLine($"Received packet: {received.Length} bytes");
            if (received.Length != 311) return 1;

            ForzaPacket packet;
            unsafe
            {
                fixed (byte* p = received)
                {
                    packet = *(ForzaPacket*)p;
                }
            }

            Console.WriteLine($"IsRaceOn: {packet.IsRaceOn}");
            Console.WriteLine($"RPM: {packet.CurrentEngineRpm}");
            Console.WriteLine($"Speed: {packet.Speed}");
            Console.WriteLine($"Gear: {packet.Gear}");
            Console.WriteLine($"AccelY: {packet.AccelerationY}");
            Console.WriteLine($"SurfaceRumble: {packet.SurfaceRumbleFrontLeft}");

            if (packet.IsRaceOn == 1 &&
                Math.Abs(packet.CurrentEngineRpm - 5200f) < 0.1f &&
                Math.Abs(packet.Speed - 31.4f) < 0.1f &&
                packet.Gear == 4 &&
                packet.AccelerationY > 4.5f &&
                Math.Abs(packet.AccelerationX - 1.5f) < 0.01f &&
                Math.Abs(packet.AngularVelocityX - 0.12f) < 0.01f &&
                packet.NormalizedSuspensionTravelFrontLeft > 0f)
            {
                Console.WriteLine("[SUCCESS] Dynamic physics telemetry packet verified cleanly!");
                TelemetryBroadcaster.Shutdown();

                Console.WriteLine("--- Testing ButtonBoxBridge Input and Action Mapping ---");
                string f13 = ButtonBoxBridge.GetKeyName(0x7C);
                string num0 = ButtonBoxBridge.GetKeyName(0x60);
                string volUp = ButtonBoxBridge.GetKeyName(0xAF);
                if (f13 != "F13" || num0 != "NumPad0" || volUp != "VolumeUp")
                {
                    Console.WriteLine($"[FAIL] KeyName mismatch: F13='{f13}', NumPad0='{num0}', VolumeUp='{volUp}'");
                    return 3;
                }
                Console.WriteLine($"[OK] Key name resolution: 0x7C -> '{f13}', 0x60 -> '{num0}', 0xAF -> '{volUp}'");

                ButtonBoxBridge.WheelBtnHandbrake = 5;
                ButtonBoxBridge.WheelBtnHeadlights = 6;
                ulong testWheelButtons = (1UL << 4) | (1UL << 5);
                uint mappedPad = ButtonBoxBridge.ProcessPad(0u, testWheelButtons, 0UL);
                if ((mappedPad & 8u) == 0 || (mappedPad & 4u) == 0)
                {
                    Console.WriteLine($"[FAIL] Pad bits mismatch: expected 0x0C (Handbrake 8 + Headlights 4), got {mappedPad}");
                    return 4;
                }
                Console.WriteLine($"[OK] Pad bit mapping: wheel buttons 5 and 6 correctly asserted Handbrake (8) and Headlights (4) -> pad=0x{mappedPad:X2}");

                // Test Menu (Pause), View (Back), and Face Button mappings
                ButtonBoxBridge.BbBtnMenu = 10;
                ButtonBoxBridge.BbBtnView = 11;
                ButtonBoxBridge.BbBtnA = 1;
                ulong testSecButtons = (1UL << 9) | (1UL << 10) | (1UL << 0);
                uint padWithMenu = ButtonBoxBridge.ProcessPad(0u, 0UL, testSecButtons);
                if ((padWithMenu & 4096u) == 0 || (padWithMenu & 8192u) == 0 || (padWithMenu & 64u) == 0)
                {
                    Console.WriteLine($"[FAIL] Menu/View/A mapping failed! pad=0x{padWithMenu:X4}");
                    return 5;
                }
                Console.WriteLine($"[OK] Menu (Bit 12 = 4096), View (Bit 13 = 8192), Button A (Bit 6 = 64) verified cleanly -> pad=0x{padWithMenu:X4}");
                Console.WriteLine("[ALL BUTTONBOX BRIDGE TESTS PASSED]");
                return 0;
            }

            return 2;
        }

        static int RunJitTest(string dllPath)
        {
            Console.WriteLine($"--- Verifying CLR JIT for {dllPath} ---");
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                string[] searchDirs = {
                    @"D:\Projects\overthehillvr-fan\Mods",
                    @"D:\Projects\overthehillvr-fan\UserLibs",
                    @"D:\Projects\overthehillvr-fan\MelonLoader\net6",
                    @"D:\Projects\overthehillvr-fan\MelonLoader\Il2CppAssemblies"
                };
                foreach (var dir in searchDirs)
                {
                    string p = Path.Combine(dir, name + ".dll");
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };

            try
            {
                var asm = Assembly.LoadFrom(dllPath);
                var type = asm.GetType("OverTheHillVR.VrDriver");
                if (type == null)
                {
                    Console.WriteLine("[FAIL] Type OverTheHillVR.VrDriver not found.");
                    return 10;
                }

                string[] targets = { "TelemetryTick", "WatchdogCheck", "SafeFfb", "SafeDamper", "CheckGuardianRecenter", "FfbTick", "Update", "Quit", "SendPad", "WheelTick" };
                int failCount = 0;
                foreach (var target in targets)
                {
                    var m = type.GetMethod(target, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (m == null)
                    {
                        Console.WriteLine($"[FAIL] Method not found: {target}");
                        failCount++;
                        continue;
                    }
                    try
                    {
                        RuntimeHelpers.PrepareMethod(m.MethodHandle);
                        Console.WriteLine($"[JIT OK] {target}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JIT FAIL] {target}: {ex.Message}");
                        failCount++;
                    }
                }

                if (failCount == 0)
                {
                    Console.WriteLine("[ALL JIT TESTS PASSED]");
                    return 0;
                }
                return failCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Assembly load failed: {ex.Message}");
                return 99;
            }
        }
    }
}
