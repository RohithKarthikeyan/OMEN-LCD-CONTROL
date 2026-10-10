using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OmenLCDControl {
    public sealed class MediaService {
        public readonly string Ffmpeg;
        public MediaService(string ffmpeg) { Ffmpeg = ffmpeg; }
        public static bool IsImage(string path) {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
        }
        public static bool IsGif(string path) { return Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase); }
        private async Task Run(string arguments, CancellationToken token) {
            if (!File.Exists(Ffmpeg)) throw new FileNotFoundException("HP's media converter was not found. Open OMEN Gaming Hub's LCD page once, then retry.", Ffmpeg);
            var psi = new ProcessStartInfo(Ffmpeg, "-hide_banner -loglevel error -nostdin " + arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (var process = Process.Start(psi)) {
                using (token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } })) {
                    string errors = await process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0) throw new InvalidOperationException("Media conversion failed. " + errors);
                }
            }
        }
        private static string Quote(string value) {
            if (value.IndexOf('"') >= 0) throw new ArgumentException("Invalid file path.");
            return "\"" + value + "\"";
        }
        public async Task<Image> LoadPreview(string path, CancellationToken token) {
            if (IsImage(path) || IsGif(path)) {
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(path), token);
                var stream = new MemoryStream(bytes);
                try {
                    var image = Image.FromStream(stream);
                    if (IsGif(path)) { image.Tag = stream; return image; }
                    using (image) { var bitmap = new Bitmap(image); stream.Dispose(); return bitmap; }
                } catch { stream.Dispose(); throw; }
            }
            string temp = Path.Combine(AppState.Root, "cache", Guid.NewGuid() + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(temp));
            try {
                await Run("-i " + Quote(path) + " -frames:v 1 -vf scale=1024:1024:force_original_aspect_ratio=decrease -y " + Quote(temp), token);
                using (var image = Image.FromFile(temp)) return new Bitmap(image);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void DisposePreview(Image image) {
            if (image == null) return;
            var stream = image.Tag as Stream;
            image.Dispose(); if (stream != null) stream.Dispose();
        }
        public async Task<string> Render(string path, MediaTransform transform, int width, int height, CancellationToken token) {
            Directory.CreateDirectory(Path.Combine(AppState.Root, "cache"));
            string target = Path.Combine(AppState.Root, "cache", Guid.NewGuid() + (IsImage(path) ? ".jpg" : ".mp4"));
            try {
                if (IsImage(path)) {
                    await Task.Run(() => {
                        using (var source = Image.FromFile(path))
                        using (var bitmap = new Bitmap(480, 480))
                        using (var graphics = Graphics.FromImage(bitmap)) {
                            graphics.Clear(Color.Black); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(source, transform.GetBounds(source.Width, source.Height));
                            bitmap.Save(target, ImageFormat.Jpeg);
                        }
                    }, token);
                } else {
                    // Video preview may be downscaled, but preserves aspect ratio; bounds are relative to that ratio.
                    await Run("-i " + Quote(path) + " -vf " + Quote(transform.Filter(width, height)) +
                        " -an -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -y " + Quote(target), token);
                }
                token.ThrowIfCancellationRequested();
                return target;
            } catch { if (File.Exists(target)) File.Delete(target); throw; }
        }
    }
}
