using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OmenLCDControl {
    public sealed class DashboardForm : Form {
        private readonly DashboardRuntime runtime;
        private DashboardConfig config;
        private readonly Func<DashboardConfig,Task> activate;
        private readonly PictureBox picture;
        private readonly DataGridView grid;
        private string selectedLayout;
        private readonly List<RadioButton> layoutChoices=new List<RadioButton>();
        private readonly NumericUpDown slide;
        private readonly Label information;
        private readonly ListBox readings;
        private readonly Timer timer=new Timer{Interval=100};
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private bool applying;
        private int ticks;
        public DashboardForm(DashboardRuntime service,DashboardConfig settings,Func<DashboardConfig,Task> apply){
            runtime=service;activate=apply;var json=new JavaScriptSerializer();config=json.Deserialize<DashboardConfig>(json.Serialize(settings));
            Text="Omen LCD control - Dashboard designer";Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(14,18,26);ForeColor=Color.White;
            ClientSize=new Size(1190,790);MinimumSize=new Size(1100,820);StartPosition=FormStartPosition.CenterParent;
            var header=new Panel{Dock=DockStyle.Top,Height=96,Padding=new Padding(18)};Controls.Add(header);
            header.Controls.Add(new Label{Text="STATS STUDIO",Left=20,Top=12,Width=220,Height=35,Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Color.Turquoise});
            selectedLayout=config.Layout;
            var layoutPanel=new FlowLayoutPanel{Left=254,Top=19,Width=300,Height=32};header.Controls.Add(layoutPanel);
            foreach(string name in new[]{"Rings","Bars","Spotlight"}){var choice=new RadioButton{Text=name,AutoSize=true,Checked=name==selectedLayout,ForeColor=Color.White};choice.CheckedChanged+=(s,e)=>{if(choice.Checked)selectedLayout=choice.Text;};layoutChoices.Add(choice);layoutPanel.Controls.Add(choice);}
            header.Controls.Add(new Label{Text="Seconds per slide",Left=565,Top=22,Width=140,Height=25});
            slide=new NumericUpDown{Left=705,Top=19,Width=75,DecimalPlaces=2,Increment=.05m,Minimum=.5m,Maximum=1,Value=(decimal)config.SlideSeconds};header.Controls.Add(slide);
            header.Controls.Add(new Label{Text="Assign your metrics below. Slots 1-4 draw graphs; slots 5-6 sit in the center / footer. Spotlight cycles all enabled slots.",Left=22,Top=62,Width=1140,Height=27,ForeColor=Color.LightSteelBlue});
            var footer=new Panel{Dock=DockStyle.Bottom,Height=66};Controls.Add(footer);
            var save=Button("Export design",20,14,138,footer);var load=Button("Import design",170,14,138,footer);
            var activateButton=Button("Apply dashboard",957,14,205,footer);activateButton.Anchor=AnchorStyles.Right|AnchorStyles.Top;activateButton.BackColor=Color.Turquoise;activateButton.ForeColor=Color.Black;
            var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16,0,16,0)};Controls.Add(body);body.BringToFront();
            var left=new Panel{Dock=DockStyle.Left,Width=490};body.Controls.Add(left);
            picture=new PictureBox{Left=5,Top=4,Width=480,Height=480,SizeMode=PictureBoxSizeMode.Zoom};left.Controls.Add(picture);
            information=new Label{Left=12,Top=490,Width=465,Height=112,ForeColor=Color.LightSteelBlue};left.Controls.Add(information);
            var right=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12,4,0,0)};body.Controls.Add(right);right.BringToFront();
            grid=new DataGridView{Left=12,Top=4,Width=646,Height=226,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.FromArgb(23,29,40),SelectionMode=DataGridViewSelectionMode.FullRowSelect};
            grid.DefaultCellStyle.ForeColor=Color.Black;grid.RowTemplate.Height=28;right.Controls.Add(grid);
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Enabled",HeaderText="On",FillWeight=30});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Slot",HeaderText="Slot",ReadOnly=true,FillWeight=48});
            grid.Columns.Add(new DataGridViewComboBoxColumn{Name="Metric",HeaderText="Metric",FillWeight=165,FlatStyle=FlatStyle.Flat});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Label",HeaderText="Label",FillWeight=90});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Min",HeaderText="Min",FillWeight=42});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Max",HeaderText="Max",FillWeight=42});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Color",HeaderText="Color",FillWeight=69});
            grid.DataError+=(s,e)=>{e.ThrowException=false;information.Text="Choose a valid metric ID.";};
            FillGrid();
            var refresh=Button("Refresh metrics",12,244,145,right);var add=Button("Add custom metric",167,244,170,right);var color=Button("Slot color...",347,244,130,right);
            right.Controls.Add(new Label{Text="LIVE SOURCES (missing or stale values display --)",Left=12,Top=298,Width=600,Height=24,ForeColor=Color.Turquoise});
            readings=new ListBox{Left=12,Top=327,Width=646,Height=125,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top,BackColor=Color.FromArgb(23,29,40),ForeColor=Color.LightSteelBlue,HorizontalScrollbar=true};right.Controls.Add(readings);
            right.Controls.Add(new Label{Text="CUSTOM DATA API",Left=12,Top=470,Width=620,Height=24,ForeColor=Color.Turquoise});
            var url=new TextBox{Left=12,Top=500,Width=374,ReadOnly=true,Text=runtime.Server!=null?runtime.Server.Url+"/v1/metrics":runtime.ApiStatus};right.Controls.Add(url);
            var copy=Button("Copy token",399,496,125,right);var help=Button("API guide",532,496,115,right);
            right.Controls.Add(new Label{Text="Send numeric values or text with a local bearer token.\nCustom keys start with custom. (for example custom.fps).",Left=12,Top=540,Width=635,Height=48,ForeColor=Color.LightSteelBlue});
            refresh.Click+=(s,e)=>{ReadGrid();RefreshMetricChoices();};add.Click+=(s,e)=>AddCustom();
            color.Click+=(s,e)=>{if(grid.CurrentRow==null)return;using(var dialog=new ColorDialog()){if(dialog.ShowDialog(this)==DialogResult.OK)grid.CurrentRow.Cells["Color"].Value="#"+dialog.Color.R.ToString("X2")+dialog.Color.G.ToString("X2")+dialog.Color.B.ToString("X2");}};
            copy.Click+=(s,e)=>{if(runtime.Server!=null){Clipboard.SetText(runtime.Server.Token);information.Text="Token copied. Keep it private; it allows local metric updates.";}};
            help.Click+=(s,e)=>{
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"docs","custom-data.md");if(!File.Exists(path))path=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","docs","custom-data.md"));
                if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});else MessageBox.Show(this,"See docs/custom-data.md in the source or portable release.");
            };
            save.Click+=(s,e)=>{try{ReadGrid();using(var dialog=new SaveFileDialog{Filter="Dashboard JSON|*.json",FileName="my-dashboard.json"})if(dialog.ShowDialog(this)==DialogResult.OK)File.WriteAllText(dialog.FileName,json.Serialize(config));}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            load.Click+=(s,e)=>{try{using(var dialog=new OpenFileDialog{Filter="Dashboard JSON|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK){if(new FileInfo(dialog.FileName).Length>65536)throw new ArgumentException("Design file is too large.");var imported=json.Deserialize<DashboardConfig>(File.ReadAllText(dialog.FileName));imported.Validate();config=imported;selectedLayout=config.Layout;foreach(var choice in layoutChoices)choice.Checked=choice.Text==selectedLayout;slide.Value=(decimal)config.SlideSeconds;FillGrid();}}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            activateButton.Click+=async(s,e)=>{if(applying)return;try{ReadGrid();applying=true;activateButton.Enabled=false;await activate(json.Deserialize<DashboardConfig>(json.Serialize(config)));information.Text="Dashboard is live on your cooler. You can close this designer; it keeps running in the tray.";}catch(Exception ex){MessageBox.Show(this,ex.Message,"Dashboard could not apply");}finally{applying=false;activateButton.Enabled=true;}};
            timer.Tick+=(s,e)=>TickPreview();timer.Start();FormClosing+=(s,e)=>{if(applying)e.Cancel=true;};FormClosed+=(s,e)=>{timer.Dispose();if(picture.Image!=null)picture.Image.Dispose();};
        }
        private Button Button(string text,int left,int top,int width,Control parent){var b=new Button{Text=text,Left=left,Top=top,Width=width,Height=36,BackColor=Color.FromArgb(35,46,63),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};parent.Controls.Add(b);return b;}
        private void RefreshMetricChoices(){
            var choices=runtime.Hub.Snapshot().Keys.Concat(config.Slots.Select(s=>s.Metric)).Distinct().OrderBy(s=>s).ToArray();
            var column=(DataGridViewComboBoxColumn)grid.Columns["Metric"];column.Items.Clear();column.Items.AddRange(choices.Cast<object>().ToArray());
        }
        private void FillGrid(){grid.Rows.Clear();RefreshMetricChoices();for(int i=0;i<6;i++){var slot=config.Slots[i];grid.Rows.Add(slot.Enabled,i<4?"Graph "+(i+1):"Text "+(i-3),slot.Metric,slot.Label,slot.Min,slot.Max,slot.Color);}}
        private void ReadGrid(){
            grid.EndEdit();var candidate=new DashboardConfig{Layout=selectedLayout,SlideSeconds=(double)slide.Value,Slots=new List<DashboardSlot>()};
            foreach(DataGridViewRow row in grid.Rows)candidate.Slots.Add(new DashboardSlot{Enabled=Convert.ToBoolean(row.Cells["Enabled"].Value),Metric=Convert.ToString(row.Cells["Metric"].Value),Label=Convert.ToString(row.Cells["Label"].Value),Min=Convert.ToDouble(row.Cells["Min"].Value),Max=Convert.ToDouble(row.Cells["Max"].Value),Color=Convert.ToString(row.Cells["Color"].Value)});
            candidate.Validate();config=candidate;
        }
        private void TickPreview(){
            try{
                if(!grid.IsCurrentCellInEditMode)ReadGrid();
                var image=DashboardRenderer.Render(config,runtime.Hub.Snapshot(),clock.Elapsed.TotalSeconds,DateTime.UtcNow);var previous=picture.Image;picture.Image=image;if(previous!=null)previous.Dispose();
                if(++ticks%10==0){readings.BeginUpdate();readings.Items.Clear();foreach(var pair in runtime.Hub.Snapshot())readings.Items.Add(pair.Key+"  =  "+DashboardRenderer.Display(pair.Value,DateTime.UtcNow)+"  ["+pair.Value.Source+"]");readings.EndUpdate();information.Text=runtime.SensorStatus+"\n\n"+runtime.GpuStatus+"\n"+runtime.ApiStatus;}
            }catch(Exception e){information.Text=e.Message;}
        }
        private void AddCustom(){
            using(var dialog=new Form{Text="Register a custom metric",ClientSize=new Size(410,205),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
                var id=new TextBox{Left=120,Top=20,Width=265,Text="custom."};var label=new TextBox{Left=120,Top=62,Width=265,Text="My metric"};var unit=new TextBox{Left=120,Top=104,Width=265};
                dialog.Controls.Add(new Label{Text="Metric ID",Left=18,Top=23});dialog.Controls.Add(new Label{Text="Label",Left=18,Top=65});dialog.Controls.Add(new Label{Text="Unit (optional)",Left=18,Top=107});dialog.Controls.Add(id);dialog.Controls.Add(label);dialog.Controls.Add(unit);
                var ok=new Button{Text="Add",Left=280,Top=150,Width=105,DialogResult=DialogResult.OK};dialog.Controls.Add(ok);dialog.AcceptButton=ok;
                if(dialog.ShowDialog(this)==DialogResult.OK){try{if(!MetricHub.ValidId(id.Text)||!id.Text.StartsWith("custom.")||label.Text.Length>48||unit.Text.Length>16)throw new ArgumentException("Use a valid custom. ID, label <=48 characters and unit <=16 characters.");runtime.Hub.Register(id.Text,label.Text,unit.Text,"Custom API");ReadGrid();RefreshMetricChoices();}catch(Exception e){MessageBox.Show(this,e.Message);}}
            }
        }
        internal void RenderSmoke(string path){Show();var wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<1800){Application.DoEvents();System.Threading.Thread.Sleep(20);}ticks=9;TickPreview();Application.DoEvents();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();}
    }
}
