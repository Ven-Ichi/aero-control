using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace AeroControl {
    // Independently implemented wire protocol; no Acer code or assemblies are shipped.
    public sealed class Hardware : IDisposable {
        public DeviceInfo Device { get; private set; }
        public string DeviceFingerprint { get; private set; }
        public static string VerificationPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroControl", "verified-device.txt"); } }
        private readonly object gate = new object();
        private uint servicePid;
        private string servicePath, fingerprintServiceVersion;
        private bool trustedService, fanMethod;
        private DateTime identityChecked = DateTime.MinValue, lastGood = DateTime.MinValue;
        private ulong previousIdle, previousKernel, previousUser;
        private bool haveCpu;
        private string lastRequestedMode;

        public Hardware() {
            Device = new DeviceInfo();
            try {
                using (var s = Search("root\\cimv2", "SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                using (var results = s.Get()) foreach (ManagementObject item in results) using(item) { Device.Manufacturer = Convert.ToString(item["Manufacturer"]); Device.Model = Convert.ToString(item["Model"]); }
                Device.Cpu = First("Win32_Processor", "Name", "Processor");
                Device.Gpu = First("Win32_VideoController", "Name", "Graphics");
                string uuid = First("Win32_ComputerSystemProduct", "UUID", "");
                string bios = First("Win32_BIOS", "SMBIOSBIOSVersion", "");
                RefreshService();
                string version = trustedService ? FileVersionInfo.GetVersionInfo(servicePath).FileVersion : "unavailable";
                fingerprintServiceVersion = version;
                if (uuid.Length > 0 && bios.Length > 0 && trustedService) using (var sha = SHA256.Create()) DeviceFingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(uuid + "|" + Device.Manufacturer + "|" + Device.Model + "|" + bios + "|" + version))).Replace("-", "");
            } catch { }
            try {
                using(var c = new ManagementClass(new ManagementScope("\\\\.\\root\\WMI"), new ManagementPath("AcerGamingFunction"), null)) {
                    c.Get(); Device.WmiAvailable = true;
                    fanMethod = c.Methods["SetGamingFanBehavior"] != null;
                }
            } catch { }
        }

        private static ManagementObjectSearcher Search(string space, string query) {
            return new ManagementObjectSearcher(new ManagementScope("\\\\.\\" + space), new ObjectQuery(query), new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true });
        }
        private static string First(string cls, string property, string fallback) {
            using(var s = Search("root\\cimv2", "SELECT " + property + " FROM " + cls))
            using(var results = s.Get()) foreach(ManagementObject o in results) using(o) return Convert.ToString(o[property]);
            return fallback;
        }
        private bool Enrolled() {
            try { return !String.IsNullOrEmpty(DeviceFingerprint) && File.ReadAllText(VerificationPath).Trim().Equals(DeviceFingerprint, StringComparison.Ordinal); } catch { return false; }
        }
        public string Enroll() {
            lock(gate) {
                RefreshService();
                if(!trustedService) return "A signed Acer NitroSense service is required first.";
                if(!fanMethod) return "This laptop does not report the Acer fan-control interface.";
                if(String.IsNullOrEmpty(DeviceFingerprint)) return "Device identity could not be read.";
                try { Directory.CreateDirectory(Path.GetDirectoryName(VerificationPath)); File.WriteAllText(VerificationPath, DeviceFingerprint); lastRequestedMode = RegistryMode(); return null; }
                catch(Exception e) { return "Could not save verification: " + e.Message; }
            }
        }
        public string Probe() {
            lock(gate) {
                var text = new StringBuilder();
                text.AppendLine("Aero Control compatibility probe");
                text.AppendLine("Aero Control build: " + typeof(Hardware).Assembly.GetName().Version);
                text.AppendLine("Device: " + Device.Manufacturer + " " + Device.Model);
                text.AppendLine("CPU: " + Device.Cpu);
                text.AppendLine("GPU: " + Device.Gpu);
                RefreshService();
                text.AppendLine("Acer service PSSvc present and signed: " + trustedService + (trustedService ? " (" + servicePath + " v" + FileVersionInfo.GetVersionInfo(servicePath).FileVersion + ")" : ""));
                text.AppendLine("WMI class AcerGamingFunction: " + Device.WmiAvailable);
                text.AppendLine("WMI method SetGamingFanBehavior: " + fanMethod);
                var powerState=new Snapshot(); WindowsPower.Read(powerState);
                text.AppendLine("Windows power plan: "+powerState.PowerPlan+" ("+powerState.PowerPlanId+")");
                text.AppendLine("Selectable Windows power plans: "+powerState.PowerPlans.Length);
                bool pipe = false; double cpuTemperature = Double.NaN, gpuTemperature = Double.NaN, cpuRpm = Double.NaN, gpuRpm = Double.NaN;
                if(!trustedService) text.AppendLine("Pipe: not attempted, the Acer service is unavailable.");
                else try {
                    cpuTemperature = ReadSensor(1); gpuTemperature = ReadSensor(10); cpuRpm = ReadSensor(2); gpuRpm = ReadSensor(6);
                    pipe = true;
                    text.AppendLine("Pipe PredatorSense_service_namedpipe: connected");
                    text.AppendLine("  CPU temperature (selector 1): " + cpuTemperature);
                    text.AppendLine("  GPU temperature (selector 10): " + gpuTemperature);
                    text.AppendLine("  CPU fan RPM (selector 2): " + cpuRpm);
                    text.AppendLine("  GPU fan RPM (selector 6): " + gpuRpm);
                    try {
                        ulong raw=ReadCoolBoost(); bool? boost=ValueRules.CoolBoostState(raw);
                        text.AppendLine("CoolBoost raw response: 0x"+raw.ToString("X"));
                        text.AppendLine("CoolBoost state: "+(boost.HasValue?(boost.Value?"On":"Off"):"Unrecognized; control disabled"));
                    } catch(Exception e) {text.AppendLine("CoolBoost: unavailable - "+e.Message);}
                } catch(Exception e) { text.AppendLine("Pipe PredatorSense_service_namedpipe: FAILED - " + e.Message); }
                bool fanTelemetry = ValueRules.FanTelemetrySufficient(ValueRules.Rpm(cpuRpm), ValueRules.Rpm(gpuRpm));
                text.AppendLine("Fan control gate (service + WMI method + one live fan channel): " + (trustedService && fanMethod && fanTelemetry) + (Enrolled() ? " · this device is enrolled" : " · not enrolled yet; the app enrolls it when you verify"));
                string verdict;
                if(!trustedService) verdict = "Blocked: install and run the Acer NitroSense service (PSSvc) first.";
                else if(!pipe) verdict = "Incompatible: the Acer service is running but its pipe did not answer, so this may be an older or different Acer protocol generation.";
                else if(!fanTelemetry) verdict = "Incompatible: the pipe answered but returned no usable fan telemetry.";
                else if(!fanMethod) verdict = "Monitoring only: fan telemetry works but the WMI fan-control method was not detected, so fan writes stay disabled.";
                else verdict = "Compatible: monitoring and fan control are available on this device.";
                text.AppendLine("Verdict: " + verdict);
                return text.ToString();
            }
        }
        private void RefreshService() {
            if ((DateTime.UtcNow - identityChecked).TotalSeconds < 15) return;
            identityChecked = DateTime.UtcNow; trustedService = false; servicePid = 0;
            using(var s = Search("root\\cimv2", "SELECT ProcessId,PathName,State FROM Win32_Service WHERE Name='PSSvc'"))
            using(var results = s.Get()) foreach(ManagementObject o in results) using(o) {
                if (Convert.ToString(o["State"]) != "Running") continue;
                string path = Convert.ToString(o["PathName"]).Trim();
                if (path.StartsWith("\"")) { int end = path.IndexOf('"', 1); if(end < 0) continue; path = path.Substring(1, end-1); }
                else { int end = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if(end < 0) continue; path = path.Substring(0,end+4); }
                if (!File.Exists(path) || !Path.GetFileName(path).Equals("PSSvc.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if(!ValidAcerSignature(path)) continue;
                servicePath = path; servicePid = Convert.ToUInt32(o["ProcessId"]); trustedService = servicePid != 0;
                if(fingerprintServiceVersion != null && fingerprintServiceVersion != FileVersionInfo.GetVersionInfo(path).FileVersion) DeviceFingerprint = null;
            }
        }

        public Snapshot Read() {
            lock(gate) {
                var result = new Snapshot(); result.CpuLoad = CpuLoad(); WindowsPower.Read(result);
                try {
                    RefreshService();
                    if(!trustedService) { result.Status = "A signed Acer NitroSense service is required for thermal readings."; return result; }
                    result.CpuTemp = ValueRules.Temperature(ReadSensor(1));
                    result.GpuTemp = ValueRules.Temperature(ReadSensor(10));
                    result.CpuRpm = ValueRules.Rpm(ReadSensor(2));
                    result.GpuRpm = ValueRules.Rpm(ReadSensor(6));
                    result.ServiceConnected = true;
                    bool valid = ValueRules.FanTelemetrySufficient(result.CpuRpm, result.GpuRpm);
                    if(valid) lastGood = DateTime.UtcNow;
                    result.FanControlSupported = valid && fanMethod && Enrolled();
                    result.Status = result.FanControlSupported ? "Live Acer service · fan controls verified on this device" : "Live monitoring · fan controls need device verification";
                    result.Mode = lastRequestedMode ?? RegistryMode();
                    // An unavailable optional feature must not discard working fan telemetry.
                    try {
                        ulong raw=ReadCoolBoost(); result.CoolBoost=ValueRules.CoolBoostState(raw);
                        result.CoolBoostStatus=result.CoolBoost.HasValue ? (result.CoolBoost.Value?"On":"Off")+" · firmware readback" :
                            "CoolBoost unavailable: unrecognized firmware state (0x"+raw.ToString("X")+")";
                    } catch(Exception e) {result.CoolBoostStatus="CoolBoost unavailable: "+e.Message;}
                } catch(Exception e) { result.Status = "Acer connection unavailable: " + e.Message; lastGood = DateTime.MinValue; }
                return result;
            }
        }
        private double ReadSensor(uint selector) {
            byte[] reply = Exchange(13, BitConverter.GetBytes(1u | (selector << 8)), 8);
            ulong value = BitConverter.ToUInt64(reply,5);
            if((value & 255) != 0) return Double.NaN;
            return (value >> 8) & 65535;
        }
        private ulong ReadCoolBoost() {
            return BitConverter.ToUInt64(Exchange(20,BitConverter.GetBytes(519u),8),5);
        }
        public string SetCoolBoost(bool enabled) {
            lock(gate) {
                try {
                    RefreshService();
                    double age=(DateTime.UtcNow-lastGood).TotalSeconds;
                    if(!trustedService || !fanMethod || !Enrolled() || age<0 || age>8)
                        return "Device verification and fresh fan telemetry are required for CoolBoost.";
                    // Do not send a setter when this firmware does not expose a known boolean state.
                    ulong beforeRaw=ReadCoolBoost();bool? before=ValueRules.CoolBoostState(beforeRaw);
                    if(!before.HasValue)return "CoolBoost is unavailable: the firmware returned an unrecognized state.";
                    uint status=BitConverter.ToUInt32(Exchange(17,BitConverter.GetBytes(ValueRules.CoolBoostRequest(enabled,beforeRaw)),4),5);
                    if((status & 255)!=0)return "Acer firmware rejected CoolBoost (status "+(status & 255)+").";
                    bool? after=ValueRules.CoolBoostState(ReadCoolBoost());
                    if(!after.HasValue || after.Value!=enabled)return "CoolBoost request was accepted, but firmware readback did not confirm the requested state.";
                    return null;
                } catch(Exception e) {lastGood=DateTime.MinValue;return "CoolBoost command failed: "+e.Message;}
            }
        }
        public string SetFanMode(string mode) {
            lock(gate) {
                if(mode != "Auto" && mode != "Max" && mode != "Custom") return "Only Auto, Max and Custom are implemented.";
                if(!fanMethod || !Enrolled()) return "Fan control has not been verified for this device.";
                try {
                    RefreshService();
                    double age = (DateTime.UtcNow - lastGood).TotalSeconds;
                    if(!trustedService || age < 0 || age > 8) return "Fresh fan telemetry is required before changing mode.";
                    // Match NitroSense CommonFunction.set_all_fan_mode (0x060002ff): Auto 0x410009, Max 0x820009, Custom 0xC30009.
                    ulong input = mode == "Auto" ? ValueRules.FanBehavior(true,true) : mode == "Max" ? 0x820009UL : ValueRules.FanBehavior(false,false);
                    byte[] reply = Exchange(15,BitConverter.GetBytes(input),4);
                    uint status = BitConverter.ToUInt32(reply,5);
                    if((status & 255) != 0) return "Acer firmware rejected the fan mode (status " + (status & 255) + ").";
                    lastRequestedMode = mode;
                    return null;
                } catch(Exception e) { lastGood = DateTime.MinValue; return "Fan command failed: " + e.Message; }
            }
        }
        public string SetFanSpeed(string fan, int percent) {
            lock(gate) {
                if(fan != "CPU" && fan != "GPU") return "Unknown fan \"" + fan + "\".";
                if(!fanMethod || !Enrolled()) return "Fan control has not been verified for this device.";
                percent = ValueRules.FanPercent(percent);
                try {
                    RefreshService();
                    double age = (DateTime.UtcNow - lastGood).TotalSeconds;
                    if(!trustedService || age < 0 || age > 8) return "Fresh fan telemetry is required before changing fan speed.";
                    // Match NitroSense set_single_custom_fan_speed (0x06000303): selector 1 CPU / 4 GPU, percent byte in bits 8..15.
                    ulong input = ValueRules.FanSpeed(fan == "CPU" ? 1 : 4, percent);
                    byte[] reply = Exchange(16,BitConverter.GetBytes(input),4);
                    uint status = BitConverter.ToUInt32(reply,5);
                    if((status & 255) != 0) return "Acer firmware rejected the " + fan + " fan speed (status " + (status & 255) + ").";
                    return null;
                } catch(Exception e) { lastGood = DateTime.MinValue; return "Fan speed command failed: " + e.Message; }
            }
        }
        private byte[] Exchange(ushort command, byte[] input, int resultLength) {
            using(var pipe = new NamedPipeClientStream(".","PredatorSense_service_namedpipe",PipeDirection.InOut,PipeOptions.Asynchronous)) {
                pipe.Connect(1000);
                uint pid;
                if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid) || pid != servicePid) throw new IOException("Pipe server identity does not match Acer's service.");
                byte[] request = new byte[7 + input.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(command),0,request,0,2); request[2]=1;
                Buffer.BlockCopy(BitConverter.GetBytes(input.Length),0,request,3,4);
                Buffer.BlockCopy(input,0,request,7,input.Length);
                IAsyncResult write=pipe.BeginWrite(request,0,request.Length,null,null);
                using(write.AsyncWaitHandle) { if(!write.AsyncWaitHandle.WaitOne(1000)) throw new TimeoutException("Acer write timed out."); pipe.EndWrite(write); }
                byte[] reply=new byte[5+resultLength]; int offset=0; var timer=Stopwatch.StartNew();
                while(offset<reply.Length) {
                    IAsyncResult read=pipe.BeginRead(reply,offset,reply.Length-offset,null,null);
                    using(read.AsyncWaitHandle) {
                        int left=Math.Max(0,1000-(int)timer.ElapsedMilliseconds);
                        if(!read.AsyncWaitHandle.WaitOne(left)) throw new TimeoutException("Acer reply timed out.");
                        int count=pipe.EndRead(read); if(count==0) throw new IOException("Acer closed the connection."); offset+=count;
                    }
                }
                if(reply[0]!=1 || BitConverter.ToUInt32(reply,1)!=resultLength) throw new IOException("Unsupported Acer reply format.");
                return reply;
            }
        }
        private string RegistryMode() {
            // This is the last selection saved by Acer, not a hardware readback.
            try { using(var key=Registry.LocalMachine.OpenSubKey("SOFTWARE\\OEM\\NitroSense\\FanControl")) { if(key==null)return "Unknown"; object value=key.GetValue("CurrentFanMode"); if(value==null)return "Unknown"; int v=Convert.ToInt32(value); return v==0 ? "Auto" : v==1 ? "Max" : "Custom"; } } catch { return "Unknown"; }
        }
        private double? CpuLoad() {
            ulong idle,kernel,user;
            if(!GetSystemTimes(out idle,out kernel,out user))return null;
            double? result=null;
            if(haveCpu && kernel>=previousKernel && user>=previousUser && idle>=previousIdle) {
                ulong total=(kernel-previousKernel)+(user-previousUser), free=idle-previousIdle;
                if(total>0 && free<=total)result=ValueRules.Percent(100.0*(total-free)/total);
            }
            previousIdle=idle;previousKernel=kernel;previousUser=user;haveCpu=true;return result;
        }
        private static bool ValidAcerSignature(string path) {
            IntPtr filePtr=IntPtr.Zero; var data=new TrustData();
            try {
                var file=new TrustFile { Size=(uint)Marshal.SizeOf(typeof(TrustFile)), Path=path };
                filePtr=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile))); Marshal.StructureToPtr(file,filePtr,false);
                data.Size=(uint)Marshal.SizeOf(typeof(TrustData));data.UiChoice=2;data.UnionChoice=1;data.File=filePtr;data.ProviderFlags=0x1000;
                Guid action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)return false;
                using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))return cert.GetNameInfo(X509NameType.SimpleName,false).Equals("Acer Incorporated",StringComparison.OrdinalIgnoreCase);
            } catch {return false;} finally { if(filePtr!=IntPtr.Zero){Marshal.DestroyStructure(filePtr,typeof(TrustFile));Marshal.FreeHGlobal(filePtr);} }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TrustFile {public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public IntPtr File,KnownSubject;}
        [StructLayout(LayoutKind.Sequential)] private struct TrustData {public uint Size;public IntPtr Policy,Sip;public uint UiChoice,Revocation,UnionChoice;public IntPtr File;public uint StateAction;public IntPtr StateData,Url;public uint ProviderFlags,UiContext;}
        [DllImport("wintrust.dll",ExactSpelling=true)] private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
        public void Dispose() { }
    }
}
