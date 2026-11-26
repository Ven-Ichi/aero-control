using System;
namespace AeroControl {
    public static class ValueRules {
        public static double? Temperature(double value) { return Finite(value) && value > 0 && value <= 125 ? (double?)value : null; }
        public static double? Percent(double value) { return Finite(value) && value >= 0 && value <= 100 ? (double?)value : null; }
        public static double? Rpm(double value) { return Finite(value) && value >= 0 && value <= 20000 ? (double?)value : null; }
        // One live fan channel is enough to prove the Acer pipe carries real fan data and that fan control is meaningful here.
        public static bool FanTelemetrySufficient(double? cpuRpm, double? gpuRpm) { return cpuRpm.HasValue || gpuRpm.HasValue; }
        // Wire values recovered from the vendor app's own IL (see hardware-research/PROTOCOL.md). State 1 follows the automatic curve, state 3 follows the custom percentage.
        public static ulong FanBehavior(bool cpuAuto, bool gpuAuto) { return 9UL | ((cpuAuto ? 1UL : 3UL) << 16) | ((gpuAuto ? 1UL : 3UL) << 22); }
        public static ulong FanSpeed(int selector, int percent) { return (ulong)(selector | (percent << 8)); }
        // Firmware minimums are not published; keep a floor so a request cannot stall a fan.
        public static int FanPercent(int percent) { return percent < 20 ? 20 : percent > 100 ? 100 : percent; }
        // The legacy app writes 1. This firmware response family was verified live
        // to require 3 (0x30007); writing 1 returns status 228. No model-name check.
        public static ulong CoolBoostRequest(bool enabled,ulong readback) {
            if(!CoolBoostState(readback).HasValue)throw new ArgumentException("Unrecognized CoolBoost firmware state.");
            ulong on=(readback >> 16)==6UL?3UL:1UL;
            return 7UL | (enabled ? on << 16 : 0UL);
        }
        public static bool? CoolBoostState(ulong response) {
            if((response & 255) != 0) return null;
            ulong state=(response >> 8) & 255;
            if((response >> 16)==6UL)return state==0 ? (bool?)false : state==3 ? (bool?)true : null;
            return state==0 ? (bool?)false : state==1 ? (bool?)true : null;
        }
        public static bool CanCoolBoost(bool service,bool verified,bool? state,DateTime observed,DateTime now) {
            double age=(now-observed).TotalSeconds;
            return service && verified && state.HasValue && age>=0 && age<=8;
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static bool CanControl(bool service, bool compatible, DateTime observed, DateTime now) {
            double age = (now - observed).TotalSeconds;
            return service && compatible && age >= 0 && age <= 15;
        }
    }
    public sealed class Snapshot {
        public DateTime Time = DateTime.UtcNow;
        public double? CpuTemp, GpuTemp, CpuLoad, CpuRpm, GpuRpm;
        public bool ServiceConnected, FanControlSupported;
        public bool? CoolBoost;
        public string CoolBoostStatus = "CoolBoost is unavailable";
        public PowerPlan[] PowerPlans = new PowerPlan[0];
        public Guid? PowerPlanId;
        public string PowerStatus = "Checking Windows power plans…";
        public string Status = "Checking your device…", Mode = "Unknown", PowerPlan = "Unavailable";
    }
    public sealed class PowerPlan {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public PowerPlan(Guid id,string name) { Id=id;Name=name; }
        public override string ToString() { return Name; }
    }
    public sealed class DeviceInfo {
        public string Manufacturer = "Unknown", Model = "Your laptop", Cpu = "Processor", Gpu = "Graphics";
        public bool WmiAvailable;
    }
}
