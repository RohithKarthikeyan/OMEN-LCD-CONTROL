using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OmenLCDControl {
    public sealed class PreviewCanvas : Control {
        private Image media;
        private readonly Timer animation;
        private Point dragStart;
        private int originX, originY;
        public MediaTransform Transform = new MediaTransform();
        public event EventHandler PositionChanged;
        public event EventHandler ZoomIn, ZoomOut;
        public Image Media { get { return media; } }
        public PreviewCanvas() {
            DoubleBuffered = true; BackColor = Color.FromArgb(14, 18, 26); Cursor = Cursors.Hand;
            animation = new Timer { Interval = 33 };
            animation.Tick += (s, e) => { if (media != null && ImageAnimator.CanAnimate(media)) { ImageAnimator.UpdateFrames(media); Invalidate(); } };
            animation.Start();
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && media != null) { dragStart = e.Location; originX = Transform.OffsetX; originY = Transform.OffsetY; Capture = true; } };
            MouseMove += (s, e) => {
                if (Capture && e.Button == MouseButtons.Left) {
                    float scale = PreviewSize / 480f;
                    Transform.OffsetX = Math.Max(-480, Math.Min(480, originX + (int)((e.X - dragStart.X) / scale)));
                    Transform.OffsetY = Math.Max(-480, Math.Min(480, originY + (int)((e.Y - dragStart.Y) / scale)));
                    Invalidate(); if (PositionChanged != null) PositionChanged(this, EventArgs.Empty);
                }
            };
            MouseUp += (s, e) => Capture = false;
            MouseWheel += (s, e) => { var handler = e.Delta > 0 ? ZoomIn : ZoomOut; if (handler != null) handler(this, EventArgs.Empty); };
        }
        public void SetMedia(Image image) {
            if (media != null) { if (ImageAnimator.CanAnimate(media)) ImageAnimator.StopAnimate(media, OnFrame); MediaService.DisposePreview(media); }
            media = image;
            if (media != null && ImageAnimator.CanAnimate(media)) ImageAnimator.Animate(media, OnFrame);
            Invalidate();
        }
        private void OnFrame(object sender, EventArgs args) { /* Timer repaints on the UI thread. */ }
        private float PreviewSize { get { return Math.Max(40, Math.Min(Width, Height) - 64); } }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float size = PreviewSize, left = (Width - size) / 2, top = (Height - size) / 2;
            using (var border = new Pen(Color.FromArgb(42, 54, 70), 1)) g.DrawRectangle(border, left, top, size, size);
            var saved = g.Save();
            using (var circle = new GraphicsPath()) { circle.AddEllipse(left, top, size, size); g.SetClip(circle); }
            g.FillEllipse(Brushes.Black, left, top, size, size);
            if (media != null) {
                try {
                    var rect = Transform.GetBounds(media.Width, media.Height); float k = size / 480;
                    g.DrawImage(media, new RectangleF(left + rect.X * k, top + rect.Y * k, rect.Width * k, rect.Height * k));
                } catch (ArgumentException) { }
            } else {
                using (var brush = new SolidBrush(Color.FromArgb(132, 147, 166)))
                using (var font = new Font("Segoe UI", 13))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("Add a GIF, video, or image\n\nDrag to position. Scroll to zoom.", font, brush, new RectangleF(left + 20, top + 20, size - 40, size - 40), format);
            }
            g.Restore(saved);
            using (var ring = new Pen(Color.FromArgb(53, 214, 204), 3)) g.DrawEllipse(ring, left, top, size, size);
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); SetMedia(null); } base.Dispose(disposing); }
    }
}
