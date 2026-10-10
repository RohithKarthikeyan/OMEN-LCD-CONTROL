using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace OmenLCDControl {
    public sealed class AppState {
        public string LastMedia;
        public MediaTransform Transform = new MediaTransform();
        public bool StartInTray;
        public DashboardConfig Dashboard = new DashboardConfig();
        public List<string> Recent = new List<string>();
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenScreen");
        public static string LogPath { get { return Path.Combine(Root, "app.log"); } }
        public static AppState Load() {
            try { return new JavaScriptSerializer().Deserialize<AppState>(File.ReadAllText(Path.Combine(Root, "preferences.json"))) ?? new AppState(); }
            catch { return new AppState(); }
        }
        public void Save() {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "preferences.json"), new JavaScriptSerializer().Serialize(this));
        }
        public static void Log(Exception error) {
            try {
                Directory.CreateDirectory(Root);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1000000) File.WriteAllText(LogPath, "");
                File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("o") + " " + error + Environment.NewLine);
            } catch { }
        }
    }
}
