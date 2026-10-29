using System;
using System.Runtime.InteropServices;

namespace OverTheHillVR.Telemetry
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 311)]
    public struct ForzaPacket
    {
        public int IsRaceOn;
        public uint TimestampMS;
        public float EngineMaxRpm;
        public float EngineIdleRpm;
        public float CurrentEngineRpm;
        public float AccelerationX;
        public float AccelerationY;
        public float AccelerationZ;
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;
        public float AngularVelocityX;
        public float AngularVelocityY;
        public float AngularVelocityZ;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public float NormalizedSuspensionTravelFrontLeft;
        public float NormalizedSuspensionTravelFrontRight;
        public float NormalizedSuspensionTravelRearLeft;
        public float NormalizedSuspensionTravelRearRight;
        public float TireSlipRatioFrontLeft;
        public float TireSlipRatioFrontRight;
        public float TireSlipRatioRearLeft;
        public float TireSlipRatioRearRight;
        public float WheelRotationSpeedFrontLeft;
        public float WheelRotationSpeedFrontRight;
        public float WheelRotationSpeedRearLeft;
        public float WheelRotationSpeedRearRight;
        public int WheelOnRumbleStripFrontLeft;
        public int WheelOnRumbleStripFrontRight;
        public int WheelOnRumbleStripRearLeft;
        public int WheelOnRumbleStripRearRight;
        public float WheelInPuddleDepthFrontLeft;
        public float WheelInPuddleDepthFrontRight;
        public float WheelInPuddleDepthRearLeft;
        public float WheelInPuddleDepthRearRight;
        public float SurfaceRumbleFrontLeft;
        public float SurfaceRumbleFrontRight;
        public float SurfaceRumbleRearLeft;
        public float SurfaceRumbleRearRight;
        public float TireSlipAngleFrontLeft;
        public float TireSlipAngleFrontRight;
        public float TireSlipAngleRearLeft;
        public float TireSlipAngleRearRight;
        public float TireCombinedSlipFrontLeft;
        public float TireCombinedSlipFrontRight;
        public float TireCombinedSlipRearLeft;
        public float TireCombinedSlipRearRight;
        public float SuspensionTravelMetersFrontLeft;
        public float SuspensionTravelMetersFrontRight;
        public float SuspensionTravelMetersRearLeft;
        public float SuspensionTravelMetersRearRight;
        public int CarOrdinal;
        public int CarClass;
        public int CarPerformanceIndex;
        public int DrivetrainType;
        public int NumCylinders;
        // Dash data starts here (byte 232)
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float Speed;
        public float Power;
        public float Torque;
        public float TireTempFrontLeft;
        public float TireTempFrontRight;
        public float TireTempRearLeft;
        public float TireTempRearRight;
        public float Boost;
        public float Fuel;
        public float DistanceTraveled;
        public float BestLap;
        public float LastLap;
        public float CurrentLap;
        public float CurrentRaceTime;
        public ushort LapNumber;
        public byte RacePosition;
        public byte Accel;
        public byte Brake;
        public byte Clutch;
        public byte HandBrake;
        public byte Gear;
        public sbyte Steer;
        public sbyte NormalizedDrivingLine;
        public sbyte NormalizedAIBrakeDifference;
    }
}
