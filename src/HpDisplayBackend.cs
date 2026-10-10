using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace OmenLCDControl {
    public sealed class HpDisplayBackend {
        public const string DefaultHelper = @"C:\ProgramData\HP\LCDDisplayHelper\OMENLCDDisplayHelper.exe";
        public static readonly string DefaultShare = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Publishers\v10z8vjag6ke6\LCDDisplay");
        private readonly string share, backup, helper;
        private readonly Action restart;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly string[] names = { "globallcdsettings.json", "profilelcdsettings.json" };
        public bool HasBackup { get { return File.Exists(Path.Combine(backup, "active")); } }
        public string Ffmpeg { get { return Path.Combine(Path.GetDirectoryName(helper), @"bin\ffmpeg.exe"); } }
        public HpDisplayBackend() : this(DefaultShare, Path.Combine(AppState.Root, "recovery"), DefaultHelper, null) { }
        internal HpDisplayBackend(string directory, string recovery, string helperPath, Action restartAction) {
            share = directory; backup = recovery; helper = helperPath; restart = restartAction ?? RestartHelper;
        }
        public bool IsAvailable { get { return File.Exists(helper) && names.All(n => File.Exists(Path.Combine(share, n))); } }
        private Dictionary<string, object> Read(string path) { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
        internal static void AtomicWrite(string path, string content) {
            string temp = path + ".omenscreen-" + Guid.NewGuid() + ".tmp";
            try { File.WriteAllText(temp, content); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private void Backup() {
            if (HasBackup) return;
            Directory.CreateDirectory(backup);
            foreach (string name in names) File.Copy(Path.Combine(share, name), Path.Combine(backup, name), true);
            AtomicWrite(Path.Combine(backup, "active"), DateTimeOffset.Now.ToString("o"));
        }
        public void Apply(string media, int fps) {
            if (!IsAvailable) throw new InvalidOperationException("HP LCD software/settings not found. Open OMEN Gaming Hub's LCD Display page once, then restart this app.");
            if (!File.Exists(media)) throw new FileNotFoundException("Prepared media is missing.", media);
            var global = Read(Path.Combine(share, names[0]));
            var profile = Read(Path.Combine(share, names[1]));
            var entries = (System.Collections.IEnumerable)global["$values"];
            var entry = entries.Cast<Dictionary<string, object>>().FirstOrDefault(e => (string)e["LCDEffectItemId"] == (string)profile["LCDCurrentModeId"]);
            if (entry == null) throw new InvalidOperationException("The active HP display profile was not found. No settings were changed.");
            var currentPath = entry["CurrentPath"] as Dictionary<string, object>;
            if (currentPath == null) throw new InvalidOperationException("Unrecognized HP settings format. No settings were changed.");
            entry["LCDEffectMode"] = MediaService.IsImage(media) ? "Image" : "Video";
            entry["LCDEffectFPS"] = fps; currentPath["$values"] = new[] { Path.GetFullPath(media) }; entry["SlideShowDuration"] = "1";
            profile["LCDOnOff"] = true; profile["LCDDisplayCurrentGame"] = false; profile["LCDDisplayOGHEvent"] = false;
            Backup();
            // Preserve pre-apply state too, so a failed change doesn't interrupt the last working selection.
            var before = names.Select(n => File.ReadAllText(Path.Combine(share, n))).ToArray();
            try {
                AtomicWrite(Path.Combine(share, names[0]), json.Serialize(global));
                AtomicWrite(Path.Combine(share, names[1]), json.Serialize(profile));
                restart();
            } catch {
                for (int i = 0; i < names.Length; i++) AtomicWrite(Path.Combine(share, names[i]), before[i]);
                try { restart(); } catch (Exception e) { AppState.Log(e); }
                throw;
            }
        }
        public void Restore() {
            if (!HasBackup) return;
            foreach (string name in names) if (!File.Exists(Path.Combine(backup, name))) throw new IOException("Recovery backup incomplete. Saved files have been kept in " + backup);
            foreach (string name in names) {
                // Copy bytes exactly, including encoding and original whitespace.
                string temp = Path.Combine(share, name + ".omenscreen-restore.tmp");
                File.Copy(Path.Combine(backup, name), temp, true);
                File.Replace(temp, Path.Combine(share, name), null);
            }
            restart();
            File.Delete(Path.Combine(backup, "active"));
        }
        private void RestartHelper() {
            foreach (var process in Process.GetProcessesByName("OMENLCDDisplayHelper")) using (process) {
                if (!string.Equals(process.MainModule.FileName, helper, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected HP helper location. Refusing to stop it.");
                process.Kill(); if (!process.WaitForExit(5000)) throw new TimeoutException("HP LCD helper did not stop.");
            }
            Thread.Sleep(450);
            Process.Start(new ProcessStartInfo(helper) { WorkingDirectory = Path.GetDirectoryName(helper), WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false, CreateNoWindow = true });
        }
    }
}
