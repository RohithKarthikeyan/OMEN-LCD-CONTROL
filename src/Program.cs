using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OmenLCDControl {
    internal static class Program {
        internal static bool StartHidden;
        internal static bool OpenStudio;
        [STAThread] private static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--render-dashboards") { DashboardTests.RenderExamples(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--dashboard-smoke") {
                using (var form = new MainForm()) form.RenderSmoke(args[1], null, true);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--smoke-test") {
                using (var form = new MainForm()) form.RenderSmoke(args[1], args.Length > 2 ? args[2] : null);
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\OmenScreen.Desktop.v1", out created)) {
                if (!created) { MessageBox.Show("Omen LCD control is already running. Double-click its teal ring icon in the system tray.", "Omen LCD control"); return 0; }
                try {
                    Directory.CreateDirectory(AppState.Root);
                    StartHidden = Array.IndexOf(args, "--tray") >= 0;
                    OpenStudio = Array.IndexOf(args, "--studio") >= 0;
                    Application.ThreadException += (s, e) => { AppState.Log(e.Exception); MessageBox.Show(e.Exception.Message, "Omen LCD control"); };
                    using (var form = new MainForm()) Application.Run(form);
                    return 0;
                } catch (Exception e) { AppState.Log(e); MessageBox.Show(e.Message + "\n\nLog: " + AppState.LogPath, "Omen LCD control could not start"); return 1; }
            }
        }
    }
}
