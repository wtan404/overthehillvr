using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OverTheHillVR.Telemetry;

namespace SimHubTelemetry.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "jit")
            {
                return RunJitTest(args.Length > 1 ? args[1] : @"D:\Projects\overthehillvr-fan\Mods\OverTheHillVR.dll.patched");
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

            TelemetryBroadcaster.TickSimple(
                isDriving: true,
                timeNow: 5.67f,
                rpm: 5200f,
                speed: 31.4f,
                gear: 4,
                accelY: 4.5f,
                ffbHit: 0.2f,
                steer: 0.5f
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
                packet.AccelerationY > 4.5f)
            {
                Console.WriteLine("[SUCCESS] Telemetry packet verified cleanly!");
                TelemetryBroadcaster.Shutdown();
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

                string[] targets = { "TelemetryTick", "WatchdogCheck", "SafeFfb", "SafeDamper", "CheckGuardianRecenter", "FfbTick", "Update", "Quit" };
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
