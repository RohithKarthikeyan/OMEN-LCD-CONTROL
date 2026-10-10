using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenLCDControl {
    public sealed class DashboardSlot {
        public bool Enabled = true;
        public string Metric = "cpu.usage";
        public string Label = "CPU";
        public string Color = "#35D6CC";
        public double Min, Max = 100;
    }
    public sealed class DashboardConfig {
        public string Layout = "Rings";
        public double SlideSeconds = .75;
        public List<DashboardSlot> Slots = new List<DashboardSlot> {
            new DashboardSlot { Metric="cpu.usage", Label="CPU", Color="#35D6CC" },
            new DashboardSlot { Metric="gpu.usage", Label="GPU", Color="#A78BFA" },
            new DashboardSlot { Metric="cpu.temperature", Label="CPU TEMP", Color="#FFB45C", Max=100 },
            new DashboardSlot { Metric="gpu.temperature", Label="GPU TEMP", Color="#FA749B", Max=100 },
            new DashboardSlot { Metric="custom.fps", Label="GAME FPS", Color="#FFFFFF", Max=240 },
            new DashboardSlot { Metric="custom.status", Label="STATUS", Color="#A8B9D1" }
        };
        public void Validate() {
            if (Layout != "Rings" && Layout != "Bars" && Layout != "Spotlight") throw new ArgumentException("Unknown dashboard layout.");
            if (double.IsNaN(SlideSeconds) || double.IsInfinity(SlideSeconds) || SlideSeconds < .5 || SlideSeconds > 1) throw new ArgumentException("Slide duration must be 0.5 to 1 second.");
            if (Slots == null || Slots.Count != 6) throw new ArgumentException("Dashboard must contain six slots.");
            foreach (var slot in Slots) {
                if (slot == null || !MetricHub.ValidId(slot.Metric)) throw new ArgumentException("A slot has an invalid metric ID.");
                if (slot.Label == null || slot.Label.Length > 24) throw new ArgumentException("Labels must have at most 24 characters.");
                if (double.IsNaN(slot.Min) || double.IsNaN(slot.Max) || double.IsInfinity(slot.Min) || double.IsInfinity(slot.Max) || slot.Max <= slot.Min) throw new ArgumentException("Each maximum must be greater than its minimum.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(slot.Color ?? "", "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("Colors must be six-digit hex values, such as #35D6CC.");
            }
        }
    }
    public sealed class MetricReading {
        public string Id, Label, Unit, Text, Source;
        public double? Value;
        public DateTime UpdatedUtc;
        public int TtlSeconds = 5;
        public bool IsFresh(DateTime now) { return (now - UpdatedUtc).TotalSeconds <= TtlSeconds; }
        public MetricReading Copy() { return (MetricReading)MemberwiseClone(); }
    }
    public sealed class MetricHub {
        private readonly object gate = new object();
        private readonly Dictionary<string, MetricReading> metrics = new Dictionary<string, MetricReading>(StringComparer.Ordinal);
        public MetricHub() {
            Register("cpu.usage", "CPU usage", "%", "Windows performance counter");
            Register("memory.usage", "RAM usage", "%", "Windows memory API");
            Register("cpu.clock", "CPU speed", "GHz", "Windows frequency estimate");
            Register("cpu.temperature", "CPU temperature", "C", "LibreHardwareMonitor CPU package sensor");
            Register("gpu.usage", "GPU usage", "%", "NVIDIA / LibreHardwareMonitor");
            Register("gpu.temperature", "GPU temperature", "C", "NVIDIA / LibreHardwareMonitor");
            Register("gpu.clock", "GPU speed", "GHz", "NVIDIA / LibreHardwareMonitor");
            Register("gpu.memory", "GPU VRAM", "%", "NVIDIA");
            Register("system.time", "Local time", "", "Windows clock");
            Register("custom.fps", "Game FPS", "fps", "Custom API");
            Register("custom.status", "Status", "", "Custom API");
        }
        public static bool ValidId(string id) { return id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9_.-]{0,63}$"); }
        public void Register(string id, string label, string unit, string source) {
            if (!ValidId(id)) throw new ArgumentException("Invalid metric ID.");
            lock (gate) { if (!metrics.ContainsKey(id)) { if(metrics.Count>=128)throw new ArgumentException("Metric limit reached.");metrics[id] = new MetricReading { Id=id, Label=label, Unit=unit, Source=source, UpdatedUtc=DateTime.MinValue }; } }
        }
        public void Set(string id, double? value, string text, string source, int ttl=5) {
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value))) throw new ArgumentException("Metric values must be finite.");
            lock (gate) {
                MetricReading reading; if (!metrics.TryGetValue(id, out reading)) throw new ArgumentException("Unknown metric: " + id);
                reading.Value = value; reading.Text = text; reading.Source = source; reading.UpdatedUtc=DateTime.UtcNow; reading.TtlSeconds=ttl;
            }
        }
        public void PutCustom(string id, string label, string unit, double? value, string text, int ttl) {
            if (!ValidId(id) || !id.StartsWith("custom.", StringComparison.Ordinal)) throw new ArgumentException("Custom IDs must start with custom. and use lowercase letters, numbers, dots, dashes, or underscores.");
            if (label == null || label.Length > 48 || unit == null || unit.Length > 16 || (text != null && text.Length > 80)) throw new ArgumentException("Label, unit, or text is too long.");
            if (ttl < 1 || ttl > 86400) throw new ArgumentException("ttlSeconds must be between 1 and 86400.");
            if (!value.HasValue && text == null) throw new ArgumentException("Supply value or text.");
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value))) throw new ArgumentException("Metric values must be finite.");
            lock (gate) {
                if (!metrics.ContainsKey(id) && metrics.Count >= 128) throw new ArgumentException("Custom metric limit reached (128 total metrics).");
                metrics[id] = new MetricReading { Id=id, Label=label, Unit=unit, Value=value, Text=text, Source="Custom API", TtlSeconds=ttl, UpdatedUtc=DateTime.UtcNow };
            }
        }
        public Dictionary<string, MetricReading> Snapshot() { lock (gate) return metrics.ToDictionary(p => p.Key, p => p.Value.Copy(), StringComparer.Ordinal); }
        public void PutBatch(List<Tuple<string,string,string,double?,string,int>> batch) {
            lock(gate){
                int added=batch.Select(r=>r.Item1).Distinct().Count(id=>!metrics.ContainsKey(id));
                if(metrics.Count+added>128)throw new ArgumentException("Metric limit reached. No readings were changed.");
                foreach(var row in batch)PutCustom(row.Item1,row.Item2,row.Item3,row.Item4,row.Item5,row.Item6);
            }
        }
    }
}
