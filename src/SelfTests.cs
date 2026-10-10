using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace OmenLCDControl {
    internal static class SelfTests {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static int Run(string report) {
            string root = Path.Combine(Path.GetTempPath(), "OmenLCDControl-test-" + Guid.NewGuid()); Directory.CreateDirectory(root);
            var lines = new List<string>();
            try {
                var transform = new MediaTransform();
                Check(transform.GetBounds(800, 400) == new Rectangle(0, 120, 480, 240), "Fit geometry");
                transform.Fill = true; Check(transform.GetBounds(800, 400) == new Rectangle(-240, 0, 960, 480), "Fill geometry");
                transform.Zoom = .5; transform.OffsetX = 37; transform.OffsetY = -29;
                Check(transform.GetBounds(800, 400) == new Rectangle(37, 91, 480, 240), "Pan and zoom geometry");
                lines.Add("PASS fit, fill, zoom, and offset geometry");
                string helper = Path.Combine(root, "helper.exe"); File.WriteAllText(helper, "test");
                string media = Path.Combine(root, "sample.jpg"); File.WriteAllText(media, "test");
                string global = "{\"$type\":\"kept\",\"$values\":[{\"LCDEffectItemId\":\"default\",\"LCDEffectMode\":\"System\",\"CurrentPath\":{\"$type\":\"list-kept\",\"$values\":[]},\"FutureHpField\":17}]}";
                string profile = "{\"LCDCurrentModeId\":\"default\",\"LCDOnOff\":false,\"FutureHpField\":\"keep\"}\r\n";
                string globalPath = Path.Combine(root, "globallcdsettings.json"), profilePath = Path.Combine(root, "profilelcdsettings.json");
                File.WriteAllText(globalPath, global); File.WriteAllText(profilePath, profile);
                byte[] originalGlobal = File.ReadAllBytes(globalPath), originalProfile = File.ReadAllBytes(profilePath);
                int restarts = 0;
                var backend = new HpDisplayBackend(root, Path.Combine(root, "backup"), helper, () => { restarts++; });
                backend.Apply(media, 15);
                Check(backend.HasBackup && restarts == 1, "Backup and restart");
                Check(File.ReadAllText(globalPath).Contains("FutureHpField") && File.ReadAllText(profilePath).Contains("FutureHpField"), "Unknown settings preservation");
                backend.Apply(media, 25); backend.Restore();
                Check(originalGlobal.SequenceEqual(File.ReadAllBytes(globalPath)) && originalProfile.SequenceEqual(File.ReadAllBytes(profilePath)), "Exact restore");
                Check(!backend.HasBackup, "Recovery marker cleanup");
                lines.Add("PASS profile updates preserve unknown fields and repeated Apply preserves original backups; exact byte restoration");
                var failingBackend = new HpDisplayBackend(root, Path.Combine(root, "failure-backup"), helper, () => { throw new IOException("simulated restart failure"); });
                try { failingBackend.Apply(media, 25); throw new Exception("Expected restart failure"); } catch (IOException) { }
                Check(originalGlobal.SequenceEqual(File.ReadAllBytes(globalPath)) && originalProfile.SequenceEqual(File.ReadAllBytes(profilePath)), "Rollback");
                lines.Add("PASS failed helper restart rolls settings back and keeps recovery available");
                File.WriteAllText(profilePath, "{\"LCDCurrentModeId\":\"missing\"}");
                try { backend.Apply(media, 25); throw new Exception("Expected missing profile rejection"); } catch (InvalidOperationException) { }
                Check(!backend.HasBackup, "Invalid settings must not create active recovery");
                lines.Add("PASS missing active profile rejected before mutation");
                string imagePath = Path.Combine(root, "landscape.png");
                using (var bitmap = new Bitmap(800, 400)) using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Lime); bitmap.Save(imagePath, ImageFormat.Png); }
                var service = new MediaService("unused");
                string rendered = service.Render(imagePath, new MediaTransform(), 800, 400, CancellationToken.None).GetAwaiter().GetResult();
                using (var image = new Bitmap(rendered)) { Check(image.Width == 480 && image.Height == 480 && image.GetPixel(240, 20).G < 20 && image.GetPixel(240, 240).G > 200, "Rendered fit composition"); }
                File.Delete(rendered);
                lines.Add("PASS actual image rendering fits to 480x480 with correct letterbox pixels");
                DashboardTests.Run(lines,root);
                lines.Add("All tests passed. No HP hardware/settings were accessed by these tests.");
                File.WriteAllLines(report, lines); return 0;
            } catch (Exception e) { lines.Add("FAIL " + e); File.WriteAllLines(report, lines); return 1; }
            // Temp fixtures are kept on failure for diagnosis. No broad filesystem deletion is needed.
        }
    }
}
