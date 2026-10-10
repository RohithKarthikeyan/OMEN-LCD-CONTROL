using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmenLCDControl {
    public sealed class MainForm : Form {
        private readonly HpDisplayBackend backend = new HpDisplayBackend();
        private readonly MediaService mediaService;
        private readonly AppState state = AppState.Load();
        private readonly PreviewCanvas preview = new PreviewCanvas();
        private readonly NotifyIcon tray;
        private readonly Label status, fileLabel, zoomLabel;
        private readonly Button open, apply, cancel, restore, stats, reset;
        private readonly TrackBar zoom;
        private readonly NumericUpDown x, y, fps;
        private readonly ComboBox sizing, recent;
        private readonly CheckBox startInTray;
        private readonly System.Windows.Forms.Timer statsTimer;
        private DashboardRuntime dashboard;
        private readonly Stopwatch dashboardClock = Stopwatch.StartNew();
        private CancellationTokenSource operation;
        private string source;
        private bool busy, syncing, exiting, statsUpdating;
        private Task statsWork = Task.FromResult(0);
        private readonly string livePath = Path.Combine(AppState.Root, "cache", "live.jpg");
        private static readonly Color Accent = Color.FromArgb(53, 214, 204), PanelColor = Color.FromArgb(23, 29, 40);

        public MainForm() {
            mediaService = new MediaService(backend.Ffmpeg);
            if (state.Transform == null) state.Transform = new MediaTransform();
            if (state.Recent == null) state.Recent = new System.Collections.Generic.List<string>();
            if (state.Dashboard == null) state.Dashboard = new DashboardConfig();
            try { state.Dashboard.Validate(); } catch { state.Dashboard = new DashboardConfig(); }
            state.Transform.Zoom = Math.Max(.25, Math.Min(3, state.Transform.Zoom));
            state.Transform.OffsetX = Math.Max(-480, Math.Min(480, state.Transform.OffsetX));
            state.Transform.OffsetY = Math.Max(-480, Math.Min(480, state.Transform.OffsetY));
            state.Transform.Fps = Math.Max(1, Math.Min(30, state.Transform.Fps));
            preview.Transform = state.Transform;
            Text = "Omen LCD control"; Font = new Font("Segoe UI", 10); ForeColor = Color.FromArgb(231, 237, 246);
            BackColor = Color.FromArgb(14, 18, 26); ClientSize = new Size(1030, 700); MinimumSize = new Size(950, 700);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
            Icon = CreateIcon(); AllowDrop = true;

            var header = new Panel { Dock = DockStyle.Top, Height = 98, Padding = new Padding(26, 16, 26, 8) };
            header.Controls.Add(new Label { Text = "Omen LCD control", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Accent, Dock = DockStyle.Top, Height = 43 });
            header.Controls.Add(new Label { Text = "Make the cooler screen yours.", ForeColor = Color.FromArgb(150, 164, 185), Dock = DockStyle.Bottom, Height = 25 });
            Controls.Add(header);
            status = new Label { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(25, 16, 12, 8), BackColor = PanelColor, AutoEllipsis = true };
            Controls.Add(status);
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 6, 20, 16) };
            Controls.Add(content); content.BringToFront();
            var sidebar = new Panel { Dock = DockStyle.Right, Width = 320, BackColor = PanelColor, Padding = new Padding(16) };
            content.Controls.Add(sidebar);
            var canvasArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 18, 0) };
            content.Controls.Add(canvasArea); canvasArea.BringToFront();
            preview.Dock = DockStyle.Fill; canvasArea.Controls.Add(preview);
            canvasArea.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 40, Text = "480 x 480  /  round screen preview\nDrag media to move it. Scroll to zoom.", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(145, 160, 179), Font = new Font("Segoe UI", 9) });

            AddLabel(sidebar, "YOUR MEDIA", 14, true);
            open = AddButton(sidebar, "Choose GIF, video, or image", 42, 288, true);
            fileLabel = AddLabel(sidebar, "Or drop a file anywhere in this window", 91, false); fileLabel.AutoEllipsis = true; fileLabel.Height = 25;
            recent = new ComboBox { Left = 16, Top = 121, Width = 288, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Recent files", BackColor = BackColor, ForeColor = ForeColor };
            recent.Items.Add("Recent files..."); foreach (var file in state.Recent) if (File.Exists(file)) recent.Items.Add(file);
            recent.SelectedIndex = 0; sidebar.Controls.Add(recent);
            StyleCombo(recent);
            AddLabel(sidebar, "COMPOSITION", 166, true);
            sizing = new ComboBox { Left = 16, Top = 194, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Fit or fill", BackColor = BackColor, ForeColor = ForeColor };
            sizing.Items.AddRange(new object[] { "Fit inside screen", "Fill the screen" }); sizing.SelectedIndex = state.Transform.Fill ? 1 : 0; sidebar.Controls.Add(sizing);
            StyleCombo(sizing);
            reset = AddButton(sidebar, "Reset", 191, 100, false); reset.Left = 204; reset.Height = 32;
            zoomLabel = AddLabel(sidebar, "Zoom  " + (int)(state.Transform.Zoom * 100) + "%", 238, false);
            zoom = new TrackBar { Left = 10, Top = 260, Width = 300, Height = 42, Minimum = 25, Maximum = 300, TickStyle = TickStyle.None, Value = (int)(state.Transform.Zoom * 100), SmallChange = 5, LargeChange = 25, AccessibleName = "Zoom percentage" }; sidebar.Controls.Add(zoom);
            AddLabel(sidebar, "Horizontal", 309, false).Width = 130; var yl = AddLabel(sidebar, "Vertical", 309, false); yl.Left = 166; yl.Width = 130;
            x = AddNumber(sidebar, 16, 337, -480, 480, state.Transform.OffsetX); y = AddNumber(sidebar, 166, 337, -480, 480, state.Transform.OffsetY);
            x.AccessibleName = "Horizontal offset in screen pixels"; y.AccessibleName = "Vertical offset in screen pixels";
            AddLabel(sidebar, "Playback FPS", 383, false); fps = AddNumber(sidebar, 166, 379, 1, 30, state.Transform.Fps); fps.AccessibleName = "Playback frame rate";
            apply = AddButton(sidebar, "Apply to cooler screen", 427, 288, true);
            cancel = AddButton(sidebar, "Cancel conversion", 476, 288, false); cancel.Visible = false;

            var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 88, Padding = new Padding(6, 10, 6, 0), BackColor = BackColor };
            stats = new Button { Text = "Stats studio", Width = 150, Height = 36 }; StyleButton(stats, false); tools.Controls.Add(stats);
            restore = new Button { Text = "Restore OMEN", Width = 150, Height = 36 }; StyleButton(restore, false); tools.Controls.Add(restore);
            var hide = new Button { Text = "Hide to tray", Width = 135, Height = 36 }; StyleButton(hide, false); tools.Controls.Add(hide);
            startInTray = new CheckBox { Text = "Open in tray next time", AutoSize = true, Margin = new Padding(12, 10, 3, 3), Checked = state.StartInTray }; tools.Controls.Add(startInTray);
            canvasArea.Controls.Add(tools); preview.BringToFront();

            var menu = new ContextMenuStrip(); menu.Items.Add("Open editor", null, (s, e) => ShowEditor());
            menu.Items.Add("Restore OMEN", null, async (s, e) => await Restore()); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Restore and exit", null, async (s, e) => await ExitApp());
            tray = new NotifyIcon { Icon = Icon, Text = "Omen LCD control", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => ShowEditor();
            statsTimer = new System.Windows.Forms.Timer { Interval = 100 }; statsTimer.Tick += async (s, e) => await UpdateStats();

            open.Click += async (s, e) => { using (var dialog = new OpenFileDialog { Filter = "Media files|*.gif;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.jpg;*.jpeg;*.png;*.bmp", CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) await LoadMedia(dialog.FileName, true); };
            recent.SelectedIndexChanged += async (s, e) => { if (!syncing && recent.SelectedIndex > 0) await LoadMedia((string)recent.SelectedItem, true); };
            zoom.ValueChanged += (s, e) => { if (!syncing) { state.Transform.Zoom = zoom.Value / 100.0; Changed(); } };
            x.ValueChanged += (s, e) => { if (!syncing) { state.Transform.OffsetX = (int)x.Value; Changed(); } };
            y.ValueChanged += (s, e) => { if (!syncing) { state.Transform.OffsetY = (int)y.Value; Changed(); } };
            fps.ValueChanged += (s, e) => { if (!syncing) { state.Transform.Fps = (int)fps.Value; Changed(); } };
            sizing.SelectedIndexChanged += (s, e) => { if (!syncing) { state.Transform.Fill = sizing.SelectedIndex == 1; Changed(); } };
            reset.Click += (s, e) => { state.Transform.Zoom = 1; state.Transform.OffsetX = 0; state.Transform.OffsetY = 0; state.Transform.Fill = false; SyncControls(); Changed(); };
            preview.PositionChanged += (s, e) => { SyncControls(); Changed(); };
            preview.ZoomIn += (s, e) => zoom.Value = Math.Min(zoom.Maximum, zoom.Value + 5);
            preview.ZoomOut += (s, e) => zoom.Value = Math.Max(zoom.Minimum, zoom.Value - 5);
            apply.Click += async (s, e) => await ApplyMedia(); cancel.Click += (s, e) => { if (operation != null) operation.Cancel(); };
            restore.Click += async (s, e) => await Restore(); stats.Click += (s, e) => ShowDashboard(); hide.Click += (s, e) => HideToTray();
            startInTray.CheckedChanged += (s, e) => { state.StartInTray = startInTray.Checked; Save(); };
            DragEnter += (s, e) => { e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += async (s, e) => { if (!busy) { var files = (string[])e.Data.GetData(DataFormats.FileDrop); if (files != null && files.Length > 0) await LoadMedia(files[0], true); } };
            FormClosing += (s, e) => { if (!exiting) { e.Cancel = true; HideToTray(); } };
            FormClosed += (s, e) => { statsTimer.Dispose(); if (dashboard != null) dashboard.Dispose(); tray.Dispose(); Save(); };
            Shown += async (s, e) => {
                if (state.StartInTray || Program.StartHidden) Hide();
                if (state.LastMedia != null && File.Exists(state.LastMedia)) await LoadMedia(state.LastMedia, false);
                if (backend.HasBackup) SetStatus("Saved recovery available. Use Restore OMEN to recover the previous session.");
                if (Program.OpenStudio) ShowDashboard();
            };
            SetStatus(backend.IsAvailable ? "Ready. Choose media to begin. Closing the editor keeps it running in the tray." : "HP LCD helper not detected. Preview works; open OMEN Gaming Hub's LCD page before applying.");
        }
        private static Icon CreateIcon() {
            using (var bitmap = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent); using (var brush = new SolidBrush(Color.FromArgb(14, 18, 26))) g.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new Pen(Accent, 4)) g.DrawEllipse(pen, 4, 4, 24, 24);
                IntPtr handle = bitmap.GetHicon(); try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); } finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        private Label AddLabel(Control parent, string text, int top, bool caption) {
            var label = new Label { Left = 16, Top = top, Width = 288, Height = 24, Text = text, Font = new Font("Segoe UI", caption ? 9 : 10, caption ? FontStyle.Bold : FontStyle.Regular), ForeColor = caption ? Accent : ForeColor }; parent.Controls.Add(label); return label;
        }
        private Button AddButton(Control parent, string text, int top, int width, bool primary) { var button = new Button { Left = 16, Top = top, Width = width, Height = 42, Text = text }; StyleButton(button, primary); parent.Controls.Add(button); return button; }
        private static void StyleButton(Button button, bool primary) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(56, 69, 86); button.BackColor = primary ? Accent : PanelColor; button.ForeColor = primary ? Color.FromArgb(12, 28, 31) : Color.FromArgb(231, 237, 246); button.Cursor = Cursors.Hand; }
        private static void StyleCombo(ComboBox combo) {
            combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (s, e) => {
                using (var brush = new SolidBrush(PanelColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                string text = e.Index >= 0 ? combo.Items[e.Index].ToString() : combo.Text;
                TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, Color.FromArgb(231, 237, 246), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
        }
        private NumericUpDown AddNumber(Control parent, int left, int top, int min, int max, int value) { var number = new NumericUpDown { Left = left, Top = top, Width = 138, Minimum = min, Maximum = max, Value = value, BackColor = BackColor, ForeColor = ForeColor }; parent.Controls.Add(number); return number; }
        private void SyncControls() { syncing = true; zoom.Value = (int)(state.Transform.Zoom * 100); x.Value = state.Transform.OffsetX; y.Value = state.Transform.OffsetY; sizing.SelectedIndex = state.Transform.Fill ? 1 : 0; syncing = false; }
        private void Changed() { zoomLabel.Text = "Zoom  " + (int)(state.Transform.Zoom * 100) + "%"; preview.Invalidate(); SetStatus("Preview updated - click Apply to send this composition to your cooler."); Save(); }
        private void Save() { try { state.Save(); } catch (Exception e) { AppState.Log(e); } }
        private void SetStatus(string text) { status.Text = text; }
        private void Error(Exception e) { AppState.Log(e); SetStatus(e.Message); MessageBox.Show(this, e.Message, "Omen LCD control", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void SetBusy(bool value) { busy = value; open.Enabled = recent.Enabled = apply.Enabled = restore.Enabled = stats.Enabled = reset.Enabled = !value; zoom.Enabled = x.Enabled = y.Enabled = sizing.Enabled = fps.Enabled = !value; preview.Enabled = !value; cancel.Visible = value; cancel.Enabled = value; }
        private async Task LoadMedia(string path, bool reset) {
            if (busy) return;
            SetBusy(true); operation = new CancellationTokenSource(); SetStatus("Loading preview...");
            try {
                Image image = await mediaService.LoadPreview(path, operation.Token);
                source = path; preview.SetMedia(image); fileLabel.Text = Path.GetFileName(path);
                if (reset) { state.Transform.Zoom = 1; state.Transform.OffsetX = 0; state.Transform.OffsetY = 0; SyncControls(); }
                state.LastMedia = path; state.Recent.Remove(path); state.Recent.Insert(0, path); if (state.Recent.Count > 8) state.Recent.RemoveAt(8); Save();
                syncing = true; recent.Items.Clear(); recent.Items.Add("Recent files..."); foreach (var file in state.Recent) if (File.Exists(file)) recent.Items.Add(file); recent.SelectedIndex = 0; syncing = false;
                SetStatus(MediaService.IsGif(path) ? "Animated GIF preview ready. Drag to move, scroll to zoom, then Apply." : MediaService.IsImage(path) ? "Image ready. Adjust the composition, then Apply." : "Video poster preview ready. Full video will loop on the screen after Apply.");
            } catch (OperationCanceledException) { SetStatus("Preview cancelled."); } catch (Exception e) { Error(e); }
            finally { operation.Dispose(); operation = null; SetBusy(false); }
        }
        private async Task ApplyMedia() {
            if (busy) return; if (source == null || preview.Media == null) { SetStatus("Choose a media file first."); return; }
            SetBusy(true); operation = new CancellationTokenSource(); SetStatus("Preparing your composition... You can cancel before it is sent.");
            string rendered = null;
            try {
                int width = preview.Media.Width, height = preview.Media.Height;
                rendered = await mediaService.Render(source, state.Transform, width, height, operation.Token);
                operation.Token.ThrowIfCancellationRequested(); cancel.Enabled = false; SetStatus("Applying to cooler screen...");
                await Task.Run(() => backend.Apply(rendered, state.Transform.Fps));
                statsTimer.Stop(); SetStatus("Playing on your cooler. Close this window to keep playing in the background.");
                tray.Text = "Omen LCD control - custom media active";
            } catch (OperationCanceledException) { SetStatus("Conversion cancelled. The previous screen selection was kept."); }
            catch (Exception e) { Error(e); }
            finally { operation.Dispose(); operation = null; SetBusy(false); }
        }
        private async Task Restore() {
            if (busy) return; SetBusy(true);
            try { await statsWork; await Task.Run(() => backend.Restore()); statsTimer.Stop(); tray.Text = "Omen LCD control"; SetStatus("Original OMEN settings restored."); }
            catch (Exception e) { Error(e); } finally { SetBusy(false); }
        }
        private async Task ExitApp() {
            if (busy) { ShowEditor(); SetStatus("Finish or cancel the current operation before exiting."); return; }
            SetBusy(true);
            try { await statsWork; await Task.Run(() => backend.Restore()); exiting = true; Close(); }
            catch (Exception e) { ShowEditor(); Error(e); SetBusy(false); }
        }
        private void ShowEditor() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void HideToTray() { Hide(); tray.ShowBalloonTip(1800, "Omen LCD control is running", "Double-click the tray icon to edit. Use Restore and exit to return to OMEN.", ToolTipIcon.Info); }
        private void DrawStats() {
            using (var image = DashboardRenderer.Render(state.Dashboard, dashboard.Hub.Snapshot(), dashboardClock.Elapsed.TotalSeconds, DateTime.UtcNow)) DashboardRenderer.SaveAtomic(image, livePath);
        }
        private void ShowDashboard() {
            if (busy) return;
            if (dashboard == null) dashboard = new DashboardRuntime();
            using (var designer = new DashboardForm(dashboard, state.Dashboard, async config => {
                if (busy) throw new InvalidOperationException("Wait for the current operation to finish.");
                var previous = state.Dashboard;
                try { state.Dashboard = config; await StartStats(); Save(); }
                catch { state.Dashboard = previous; throw; }
            })) designer.ShowDialog(this);
        }
        private async Task StartStats() {
            if (busy) return; SetBusy(true);
            try { await statsWork; dashboardClock.Restart(); await Task.Run(() => { DrawStats(); backend.Apply(livePath, 25); }); statsTimer.Start(); tray.Text = "Omen LCD control - dashboard active"; SetStatus("Custom dashboard is on the cooler. Metrics and custom data keep updating in the tray."); }
            finally { SetBusy(false); }
        }
        private async Task UpdateStats() {
            if (busy || statsUpdating) return; statsUpdating = true;
            try { statsWork = Task.Run(() => DrawStats()); await statsWork; } catch (Exception e) { statsTimer.Stop(); AppState.Log(e); SetStatus("Stats stopped: " + e.Message); }
            finally { statsUpdating = false; }
        }
        internal void RenderSmoke(string path, string sample) {
            if (sample != null && File.Exists(sample)) { using (var image = Image.FromFile(sample)) preview.SetMedia((Image)image.Clone()); fileLabel.Text = Path.GetFileName(sample); }
            Show(); Application.DoEvents();
            Close(); if (IsDisposed || Visible) throw new Exception("Close-to-tray smoke test failed.");
            Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path, ImageFormat.Png); }
            exiting = true; Close();
        }
    }
}
