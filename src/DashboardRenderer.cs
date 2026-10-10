using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OmenLCDControl {
    public static class DashboardRenderer {
        private static readonly Color Background = Color.FromArgb(11, 16, 25), Muted = Color.FromArgb(133, 153, 177), Track = Color.FromArgb(35, 46, 63);
        public static string Display(MetricReading reading, DateTime now) {
            if (reading == null || !reading.IsFresh(now) || (!reading.Value.HasValue && reading.Text == null)) return "--";
            if (reading.Text != null) return reading.Text;
            return reading.Value.Value.ToString(reading.Unit == "GHz" ? "0.00" : "0.#", CultureInfo.InvariantCulture) + (reading.Unit == "%" ? "%" : " " + reading.Unit);
        }
        private static MetricReading Read(Dictionary<string, MetricReading> metrics, string id) { MetricReading value; return metrics.TryGetValue(id, out value) ? value : null; }
        public static double Fraction(DashboardSlot slot, MetricReading reading, DateTime now) {
            if (reading == null || !reading.IsFresh(now) || !reading.Value.HasValue) return 0;
            return Math.Max(0, Math.Min(1, (reading.Value.Value - slot.Min) / (slot.Max - slot.Min)));
        }
        private static Color ColorOf(DashboardSlot slot) { return ColorTranslator.FromHtml(slot.Color); }
        private static void Text(Graphics g, string value, RectangleF box, float size, Color color, bool bold=false) {
            using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment=StringAlignment.Center, LineAlignment=StringAlignment.Center, Trimming=StringTrimming.EllipsisCharacter })
                g.DrawString(value, font, brush, box, format);
        }
        public static Bitmap Render(DashboardConfig config, Dictionary<string, MetricReading> metrics, double seconds, DateTime now) {
            config.Validate(); var bitmap = new Bitmap(480, 480);
            using (var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.Clear(Background);
                if (config.Layout == "Rings") {
                    for (int i=0;i<4;i++) {
                        var slot=config.Slots[i]; if (!slot.Enabled) continue;
                        int radius=213-i*25; var rect=new Rectangle(240-radius,240-radius,radius*2,radius*2);
                        using (var pen=new Pen(Track,13)) g.DrawArc(pen,rect,140,260);
                        var reading=Read(metrics,slot.Metric); float sweep=(float)(260*Fraction(slot,reading,now));
                        if(sweep>0) using(var pen=new Pen(ColorOf(slot),13) { StartCap=LineCap.Round, EndCap=LineCap.Round }) g.DrawArc(pen,rect,140,sweep);
                        // Bottom arc gap doubles as a color-coded legend, preserving the center for status.
                        Text(g,slot.Label+"  "+Display(reading,now),new RectangleF(85,370+i*20,310,22),10,ColorOf(slot),true);
                    }
                    for(int i=4;i<6;i++) if(config.Slots[i].Enabled) {
                        var slot=config.Slots[i]; int top=i==4?177:260;
                        Text(g,slot.Label,new RectangleF(120,top-24,240,22),10,Muted,true);
                        Text(g,Display(Read(metrics,slot.Metric),now),new RectangleF(117,top,246,52),i==4?26:17,ColorOf(slot),true);
                    }
                } else if(config.Layout == "Bars") {
                    Text(g,"SYSTEM PULSE",new RectangleF(80,52,320,35),17,Muted,true);
                    for(int i=0;i<4;i++) {
                        var slot=config.Slots[i]; if(!slot.Enabled)continue; int top=104+i*62;
                        Text(g,slot.Label,new RectangleF(82,top,150,24),11,Muted,true);
                        Text(g,Display(Read(metrics,slot.Metric),now),new RectangleF(232,top,166,24),13,ColorOf(slot),true);
                        using(var track=new SolidBrush(Track))g.FillRectangle(track,85,top+29,310,11);
                        using(var brush=new SolidBrush(ColorOf(slot)))g.FillRectangle(brush,85,top+29,(float)(310*Fraction(slot,Read(metrics,slot.Metric),now)),11);
                    }
                    for(int i=4;i<6;i++)if(config.Slots[i].Enabled){var slot=config.Slots[i];Text(g,slot.Label+"  "+Display(Read(metrics,slot.Metric),now),new RectangleF(75,365+(i-4)*35,330,30),14,ColorOf(slot),true);}
                } else {
                    var slots=config.Slots.Where(s=>s.Enabled).ToList();
                    if(slots.Count==0){Text(g,"Enable a metric slot",new RectangleF(70,180,340,120),20,Muted);return bitmap;}
                    int index=(int)(Math.Floor(seconds/config.SlideSeconds)%slots.Count); var slot=slots[index]; var reading=Read(metrics,slot.Metric);
                    double phase=(seconds%config.SlideSeconds)/config.SlideSeconds;
                    using(var pen=new Pen(Track,15))g.DrawEllipse(pen,37,37,406,406);
                    float sweep=(float)(360*Fraction(slot,reading,now)); if(sweep>0)using(var pen=new Pen(ColorOf(slot),15){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawArc(pen,37,37,406,406,-90,sweep);
                    using(var pen=new Pen(Color.FromArgb(100,ColorOf(slot)),3))g.DrawArc(pen,20,20,440,440,-90,(float)(360*phase));
                    Text(g,slot.Label,new RectangleF(70,137,340,45),18,Muted,true);
                    Text(g,Display(reading,now),new RectangleF(60,195,360,110),reading!=null&&reading.Text!=null?28:48,ColorOf(slot),true);
                    Text(g,(index+1)+" / "+slots.Count,new RectangleF(160,340,160,35),12,Muted);
                }
            }
            return bitmap;
        }
        public static void SaveAtomic(Bitmap bitmap,string target) {
            Directory.CreateDirectory(Path.GetDirectoryName(target));string temp=target+".dashboard.tmp";
            try{bitmap.Save(temp,ImageFormat.Jpeg);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
