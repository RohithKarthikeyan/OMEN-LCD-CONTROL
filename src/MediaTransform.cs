using System;
using System.Drawing;
using System.Globalization;

namespace OmenLCDControl {
    public sealed class MediaTransform {
        public double Zoom = 1;
        public int OffsetX, OffsetY;
        public int Fps = 25;
        public bool Fill;
        public Rectangle GetBounds(int width, int height) {
            if (width < 1 || height < 1) throw new ArgumentException("Invalid media dimensions.");
            double scale = (Fill ? Math.Max(480.0 / width, 480.0 / height) : Math.Min(480.0 / width, 480.0 / height)) * Zoom;
            int w = Math.Max(2, (int)Math.Round(width * scale / 2) * 2);
            int h = Math.Max(2, (int)Math.Round(height * scale / 2) * 2);
            if (w > 8192 || h > 8192) throw new ArgumentException("This media is too wide or tall at this zoom. Choose Fit or reduce zoom.");
            return new Rectangle((480 - w) / 2 + OffsetX, (480 - h) / 2 + OffsetY, w, h);
        }
        public string Filter(int width, int height) {
            Rectangle r = GetBounds(width, height);
            // Padding allows positive and negative offsets without negative crop coordinates.
            return string.Format(CultureInfo.InvariantCulture,
                "scale={0}:{1},pad=iw+1920:ih+1920:960:960:black,crop=480:480:{2}:{3},setsar=1,fps={4}",
                r.Width, r.Height, 960 - r.X, 960 - r.Y, Fps);
        }
    }
}
