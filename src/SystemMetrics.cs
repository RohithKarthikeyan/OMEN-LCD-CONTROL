using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace OmenLCDControl {
    public sealed class SystemMetrics : IDisposable {
        private readonly MetricHub hub;
        private PerformanceCounter cpu, frequency, performance;
        private DateTime nextSensors=DateTime.MinValue;
        public string SensorStatus="CPU temperature: waiting for sensor provider.";
        public string GpuStatus="GPU: waiting for first reading.";
        public SystemMetrics(MetricHub target){hub=target;}
        [StructLayout(LayoutKind.Sequential)]private class MemoryStatus{public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));public uint Load;public ulong Total,Available,PageTotal,PageAvailable,VirtualTotal,VirtualAvailable,Extended;}
        [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatus status);
        public void Poll(){
            try{
                if(cpu==null){cpu=new PerformanceCounter("Processor","% Processor Time","_Total");cpu.NextValue();}
                hub.Set("cpu.usage",Math.Max(0,Math.Min(100,cpu.NextValue())),null,"Windows performance counter");
            }catch{ }
            var memory=new MemoryStatus();if(GlobalMemoryStatusEx(memory))hub.Set("memory.usage",memory.Load,null,"Windows memory API");
            hub.Set("system.time",null,DateTime.Now.ToString("h:mm:ss tt"),"Windows clock");
            try{
                if(frequency==null)frequency=new PerformanceCounter("Processor Information","Processor Frequency","_Total");
                if(performance==null)performance=new PerformanceCounter("Processor Information","% Processor Performance","_Total");
                double mhz=frequency.NextValue(),ratio=performance.NextValue();
                if(mhz>0&&ratio>0)hub.Set("cpu.clock",mhz*ratio/100000.0,null,"Windows average frequency estimate");
            }catch{ }
            PollNvidia();
            if(DateTime.UtcNow>=nextSensors)PollHardwareMonitor();
        }
        private static double? Number(string text){double value;return double.TryParse(text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)?(double?)value:null;}
        private void PollNvidia(){
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");
            if(!File.Exists(path)){GpuStatus="GPU: NVIDIA tool missing; trying LibreHardwareMonitor.";return;}
            try{
                var info=new ProcessStartInfo(path,"--id=0 --query-gpu=utilization.gpu,temperature.gpu,clocks.current.graphics,memory.used,memory.total --format=csv,noheader,nounits"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var process=Process.Start(info)){
                    if(!process.WaitForExit(1500)){process.Kill();GpuStatus="NVIDIA query timed out.";return;}
                    if(process.ExitCode!=0){GpuStatus="NVIDIA query unavailable; trying LibreHardwareMonitor.";return;}
                    var parts=process.StandardOutput.ReadToEnd().Trim().Split(',');if(parts.Length<5)return;
                    var usage=Number(parts[0]);var temp=Number(parts[1]);var clock=Number(parts[2]);var used=Number(parts[3]);var total=Number(parts[4]);
                    if(usage.HasValue)hub.Set("gpu.usage",usage,null,"NVIDIA GPU 0");if(temp.HasValue)hub.Set("gpu.temperature",temp,null,"NVIDIA GPU 0");
                    if(clock.HasValue)hub.Set("gpu.clock",clock/1000,null,"NVIDIA GPU 0");if(used.HasValue&&total>0)hub.Set("gpu.memory",100*used/total,null,"NVIDIA GPU 0");
                    GpuStatus="GPU: NVIDIA GPU 0 connected.";
                }
            }catch(Exception e){GpuStatus="GPU: "+e.Message;}
        }
        private void PollHardwareMonitor(){
            nextSensors=DateTime.UtcNow.AddSeconds(2);
            try{
                var scope=new ManagementScope(@"\\.\root\LibreHardwareMonitor",new ConnectionOptions{Timeout=TimeSpan.FromSeconds(1)});scope.Connect();
                var options=new EnumerationOptions{Timeout=TimeSpan.FromSeconds(1),ReturnImmediately=false};
                using(var search=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT Name,Parent,Value,SensorType FROM Sensor WHERE SensorType='Temperature' OR SensorType='Clock' OR SensorType='Load'"),options))
                using(var results=search.Get()){
                    double? package=null,maxCore=null;double clockSum=0;int clocks=0;
                    foreach(ManagementObject item in results)using(item){
                        string name=Convert.ToString(item["Name"]),parent=Convert.ToString(item["Parent"]).ToLowerInvariant(),type=Convert.ToString(item["SensorType"]);
                        var value=Number(Convert.ToString(item["Value"],CultureInfo.InvariantCulture));if(!value.HasValue)continue;
                        if(parent.Contains("cpu")){
                            if(type=="Temperature"&&value>0&&value<150){
                                if(name.IndexOf("package",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("tctl",StringComparison.OrdinalIgnoreCase)>=0)package=value;
                                else maxCore=!maxCore.HasValue?value:Math.Max(maxCore.Value,value.Value);
                            }
                            if(type=="Clock"&&name.IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0&&value>0){clockSum+=value.Value;clocks++;}
                        }
                        if(parent.Contains("gpu")&&!GpuStatus.Contains("connected")){
                            if(type=="Temperature"&&name.IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0)hub.Set("gpu.temperature",value,null,"LibreHardwareMonitor GPU core");
                            if(type=="Load"&&name.IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0)hub.Set("gpu.usage",value,null,"LibreHardwareMonitor GPU core");
                            if(type=="Clock"&&name.IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0)hub.Set("gpu.clock",value/1000,null,"LibreHardwareMonitor GPU core");
                        }
                    }
                    if(package.HasValue||maxCore.HasValue){hub.Set("cpu.temperature",package??maxCore,null,package.HasValue?"LibreHardwareMonitor CPU package":"LibreHardwareMonitor highest CPU sensor",8);SensorStatus=package.HasValue?"CPU temperature: package sensor connected.":"CPU temperature: highest CPU sensor (package unavailable).";}
                    else SensorStatus="CPU temperature: no CPU sensor exposed by LibreHardwareMonitor.";
                    if(clocks>0)hub.Set("cpu.clock",clockSum/clocks/1000,null,"LibreHardwareMonitor mean core clocks",8);
                }
            }catch{SensorStatus="CPU temperature unavailable. Run LibreHardwareMonitor with CPU sensors enabled (usually as administrator).";nextSensors=DateTime.UtcNow.AddSeconds(10);}
        }
        public void Dispose(){if(cpu!=null)cpu.Dispose();if(frequency!=null)frequency.Dispose();if(performance!=null)performance.Dispose();}
    }
}
