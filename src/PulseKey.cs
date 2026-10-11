// PulseKey - a small Windows desktop input utility.
// Build: csc.exe /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll /out:PulseKey.exe PulseKey.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

[assembly: System.Reflection.AssemblyTitle("PulseKey")]
[assembly: System.Reflection.AssemblyDescription("Portable Windows input studio")]
[assembly: System.Reflection.AssemblyProduct("PulseKey")]
[assembly: System.Reflection.AssemblyCompany("@BraxFFA")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 @BraxFFA")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.2.0")]

namespace PulseKey {
    [Serializable]
    public class MacroStep {
        public string Kind { get; set; }
        public string Value { get; set; }
        public int DelayMs { get; set; }
        public MacroStep() { Kind = "Key"; Value = "Space"; DelayMs = 100; }
    }

    [Serializable]
    public class MacroFile {
        public string Name { get; set; }
        public List<MacroStep> Steps { get; set; }
        public MacroFile() { Name = "Untitled macro"; Steps = new List<MacroStep>(); }
    }

    internal class TargetWindow {
        public IntPtr Handle;
        public string Title;
        public override string ToString() { return Title; }
    }

    internal static class Native {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);

        [StructLayout(LayoutKind.Explicit, Size=40)] public struct INPUT {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] public MOUSEINPUT mi;
        }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
            public ushort vk, scan;
            public uint flags, time;
            public UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
            public int dx, dy;
            public uint data, flags, time;
            public UIntPtr extra;
        }
        public static bool Key(Keys key, bool down) {
            INPUT input = new INPUT(); input.type = 1;
            uint scan=MapVirtualKey((uint)key,4);
            if(scan!=0&&(scan&0xFF00)!=0xE100) {
                input.ki.scan=(ushort)(scan&0xFF);
                input.ki.flags=8u|((scan&0xFF00)==0xE000?1u:0u)|(down?0u:2u);
            } else {input.ki.vk=(ushort)key;input.ki.flags=down?0u:2u;}
            return SendInput(1, new[] { input }, 40) == 1;
        }
        public static bool Click(string name) {
            uint down = 2, up = 4, data = 0;
            if (name == "Right") { down = 8; up = 16; }
            if (name == "Middle") { down = 32; up = 64; }
            if (name == "X1") { down = 128; up = 256; data = 1; }
            if (name == "X2") { down = 128; up = 256; data = 2; }
            INPUT a = new INPUT(), b = new INPUT();
            a.type = b.type = 0; a.mi.flags = down; b.mi.flags = up;
            a.mi.data = b.mi.data = data;
            return SendInput(2, new[] { a, b }, 40) == 2;
        }
    }

    internal static class Shape {
        public static GraphicsPath Round(RectangleF r, float radius) {
            float d=Math.Min(Math.Min(r.Width,r.Height),radius*2f);
            GraphicsPath p=new GraphicsPath();
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        public static Color Blend(Color a,Color b,float t) {return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    }

    internal static class ThemeCanvas {
        public static Color Top=Color.FromArgb(19,40,74), Bottom=Color.FromArgb(4,6,11);
        private static Bitmap canvas;
        private static int canvasHeight;
        private static Color canvasTop,canvasBottom;
        private static readonly byte[] Dither={
            0,48,12,60,3,51,15,63, 32,16,44,28,35,19,47,31,
            8,56,4,52,11,59,7,55, 40,24,36,20,43,27,39,23,
            2,50,14,62,1,49,13,61, 34,18,46,30,33,17,45,29,
            10,58,6,54,9,57,5,53, 42,26,38,22,41,25,37,21
        };
        private static byte Channel(double value,double threshold){return (byte)Math.Max(0,Math.Min(255,(int)Math.Floor(value+threshold)));}
        private static void EnsureCanvas(int height){
            const int width=8;
            height=Math.Max(1,height);
            if(canvas!=null&&canvasHeight==height&&canvasTop==Top&&canvasBottom==Bottom)return;
            Bitmap next=new Bitmap(width,height,PixelFormat.Format32bppArgb);
            BitmapData data=next.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try{
                byte[] pixels=new byte[data.Stride*height];
                for(int y=0;y<height;y++){
                    double t=height==1?0:(double)y/(height-1);
                    double red=Top.R+(Bottom.R-Top.R)*t,green=Top.G+(Bottom.G-Top.G)*t,blue=Top.B+(Bottom.B-Top.B)*t;
                    int row=y*data.Stride,pattern=(y&7)*8;
                    for(int x=0;x<width;x++){
                        double threshold=(Dither[pattern+(x&7)]+.5)/64.0;
                        int pixel=row+x*4;
                        pixels[pixel]=Channel(blue,threshold);pixels[pixel+1]=Channel(green,threshold);pixels[pixel+2]=Channel(red,threshold);pixels[pixel+3]=255;
                    }
                }
                Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
            }finally{next.UnlockBits(data);}
            if(canvas!=null)canvas.Dispose();canvas=next;canvasHeight=height;canvasTop=Top;canvasBottom=Bottom;
        }
        public static void Paint(Graphics g,Control c) {
            int x=0,y=0;Control node=c;
            while(node!=null && !(node is Form)){x+=node.Left;y+=node.Top;node=node.Parent;}
            Form form=node as Form;
            EnsureCanvas(form==null?c.Height:form.ClientSize.Height);
            GraphicsState state=g.Save();
            try{
                g.SetClip(c.ClientRectangle);
                using(TextureBrush brush=new TextureBrush(canvas,WrapMode.Tile)){
                    brush.TranslateTransform(-x,-y);
                    g.FillRectangle(brush,c.ClientRectangle);
                }
            }finally{g.Restore(state);}
        }
        public static void PaintUnder(Graphics g,Control c) {
            int x=c.Left;
            Control node=c.Parent;
            while(node!=null && !(node is RoundedPanel) && node.BackColor.A==0){x+=node.Left;node=node.Parent;}
            RoundedPanel parent=node as RoundedPanel;
            if(parent!=null){
                Color canvasTint=parent.CanvasTint,fillEnd=parent.FillEnd;
                if(parent.PaintCanvas){Paint(g,c);return;}
                if(canvasTint.A>0){Paint(g,c);using(Brush tint=new SolidBrush(canvasTint))g.FillRectangle(tint,c.ClientRectangle);return;}
                if(fillEnd.A>0){using(Brush fill=new LinearGradientBrush(new Rectangle(-x,0,Math.Max(1,parent.Width),Math.Max(1,c.Height)),parent.BackColor,fillEnd,LinearGradientMode.Horizontal))g.FillRectangle(fill,c.ClientRectangle);return;}
                g.Clear(parent.BackColor);return;
            }
            g.Clear(node==null?c.BackColor:node.BackColor);
        }
    }

    internal sealed class ThemeSpec {
        public readonly string Name,Description;
        public readonly Color Top,Bottom,Surface,Surface2,Accent,AccentAlt,Text,Muted,Border;
        public ThemeSpec(string name,string description,string top,string bottom,string surface,string surface2,string accent,string text,string muted,string border,string accentAlt=null) {
            Name=name;Description=description;
            Top=ColorTranslator.FromHtml(top);Bottom=ColorTranslator.FromHtml(bottom);Surface=ColorTranslator.FromHtml(surface);
            Surface2=ColorTranslator.FromHtml(surface2);Accent=ColorTranslator.FromHtml(accent);AccentAlt=ColorTranslator.FromHtml(accentAlt??accent);Text=ColorTranslator.FromHtml(text);
            Muted=ColorTranslator.FromHtml(muted);Border=ColorTranslator.FromHtml(border);
        }
    }

    internal sealed class RoundedPanel : Panel {
        public int Radius=7;
        public Color Outline=Color.Transparent;
        public Color FillEnd=Color.Transparent;
        public Color CanvasTint=Color.Transparent;
        public bool PaintCanvas;
        public RoundedPanel(){DoubleBuffered=true;}
        protected override void OnPaintBackground(PaintEventArgs e){
            if(PaintCanvas){ThemeCanvas.Paint(e.Graphics,this);return;}
            if(CanvasTint.A>0)ThemeCanvas.Paint(e.Graphics,this);
            else ThemeCanvas.PaintUnder(e.Graphics,this);
            if(Radius<=0){using(Brush b=new SolidBrush(BackColor))e.Graphics.FillRectangle(b,ClientRectangle);return;}
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            using(GraphicsPath p=Shape.Round(new RectangleF(.5f,.5f,Width-1f,Height-1f),Radius)){
                if(CanvasTint.A>0)using(Brush b=new SolidBrush(CanvasTint))e.Graphics.FillPath(b,p);
                else if(FillEnd.A>0)using(Brush b=new LinearGradientBrush(new Rectangle(0,0,Math.Max(1,Width),Math.Max(1,Height)),BackColor,FillEnd,LinearGradientMode.Horizontal))e.Graphics.FillPath(b,p);
                else using(Brush b=new SolidBrush(BackColor))e.Graphics.FillPath(b,p);
            }
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Radius<=0)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            if(Outline.A>0)using(GraphicsPath p=Shape.Round(new RectangleF(.5f,.5f,Width-1f,Height-1f),Radius))using(Pen pen=new Pen(Outline,1f))e.Graphics.DrawPath(pen,p);
        }
    }

    internal sealed class CardShadow : Control {
        public CardShadow(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaintBackground(PaintEventArgs e){ThemeCanvas.PaintUnder(e.Graphics,this);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            for(int i=0;i<4;i++){
                float inset=i*1.5f;
                using(GraphicsPath path=Shape.Round(new RectangleF(inset,inset,Width-1f-2f*inset,Height-1f-2f*inset),10))
                using(Brush shade=new SolidBrush(Color.FromArgb(14,0,0,0)))e.Graphics.FillPath(shade,path);
            }
        }
    }

    internal sealed class GradientLabel : Label {
        public Color StartColor=Color.White, EndColor=Color.White;
        public GradientLabel(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;AutoSize=true;Padding=new Padding(0,0,12,4);}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using(Brush brush=new LinearGradientBrush(new Rectangle(0,0,Math.Max(1,Width),Math.Max(1,Height)),StartColor,EndColor,LinearGradientMode.Horizontal))
            using(StringFormat format=(StringFormat)StringFormat.GenericTypographic.Clone()){
                format.FormatFlags|=StringFormatFlags.NoWrap;
                e.Graphics.DrawString(Text,Font,brush,new PointF(0,0),format);
            }
        }
    }

    internal sealed class RoundedButton : Control {
        private bool hover;
        public int Radius=6;
        public Color Outline=Color.Transparent;
        public Color FillEnd=Color.Transparent;
        public bool TransparentFill;
        public ContentAlignment TextAlign=ContentAlignment.MiddleCenter;
        public RoundedButton(){Cursor=Cursors.Hand;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.StandardClick|ControlStyles.Selectable,true);}
        protected override void OnPaintBackground(PaintEventArgs e){ThemeCanvas.PaintUnder(e.Graphics,this);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;e.SuppressKeyPress=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            RectangleF r=new RectangleF(.5f,.5f,Width-1f,Height-1f);
            using(GraphicsPath p=Shape.Round(r,Radius)){
                Color fill=hover&&Enabled?Shape.Blend(BackColor,ForeColor,.07f):BackColor;
                Color end=FillEnd.A>0&&hover&&Enabled?Shape.Blend(FillEnd,ForeColor,.07f):FillEnd;
                if(!Enabled)fill=Shape.Blend(fill,Color.Black,.25f);
                if(!Enabled&&end.A>0)end=Shape.Blend(end,Color.Black,.25f);
                if(!TransparentFill||hover){
                    if(end.A>0)using(Brush b=new LinearGradientBrush(new Rectangle(0,0,Math.Max(1,Width),Math.Max(1,Height)),fill,end,LinearGradientMode.Horizontal))e.Graphics.FillPath(b,p);
                    else using(Brush b=new SolidBrush(fill))e.Graphics.FillPath(b,p);
                }
                if(Outline.A>0){using(Pen pen=new Pen(Outline,1f))e.Graphics.DrawPath(pen,p);}
            }
            Rectangle tr=new Rectangle(Padding.Left+10,0,Width-Padding.Horizontal-20,Height);
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|((TextAlign==ContentAlignment.MiddleLeft)?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(e.Graphics,Text,Font,tr,Enabled?ForeColor:Color.Gray,flags);
        }
    }

    internal sealed class RoundedSelect : Control {
        public readonly List<object> Items=new List<object>();
        public event EventHandler SelectedIndexChanged;
        public Color Outline=Color.FromArgb(55,75,112);
        public Color Hint=Color.FromArgb(149,164,205);
        public Color ArrowColor=Color.FromArgb(149,164,205);
        public Color FillEnd=Color.Transparent;
        public string Placeholder="Choose an option";
        private int selected=-1;
        private bool hover;
        private ContextMenuStrip menu;
        public int SelectedIndex {get{return selected;}set{int v=value>=0&&value<Items.Count?value:-1;if(v==selected)return;selected=v;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
        public object SelectedItem {get{return selected>=0?Items[selected]:null;}set{SelectedIndex=Items.IndexOf(value);}}
        public new string Text {get{return SelectedItem==null?"":SelectedItem.ToString();}set{int i=Items.FindIndex(x=>x.ToString()==value);SelectedIndex=i;}}
        public RoundedSelect(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Height=38;Cursor=Cursors.Hand;TabStop=true;}
        protected override void OnPaintBackground(PaintEventArgs e){ThemeCanvas.PaintUnder(e.Graphics,this);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnClick(EventArgs e){base.OnClick(e);if(Items.Count==0)return;
            if(menu!=null){if(menu.Visible){menu.Close();return;}menu.Dispose();}
            menu=new ContextMenuStrip();menu.ShowImageMargin=false;menu.BackColor=BackColor;menu.ForeColor=ForeColor;menu.Font=Font;menu.MaximumSize=new Size(Math.Max(Width,430),400);
            for(int i=0;i<Items.Count;i++){int index=i;ToolStripMenuItem item=new ToolStripMenuItem(Items[i].ToString());item.Padding=new Padding(8,5,8,5);item.BackColor=BackColor;item.ForeColor=ForeColor;item.Click+=(s,x)=>SelectedIndex=index;menu.Items.Add(item);}
            menu.Show(this,new Point(0,Height+3));
        }
        protected override void Dispose(bool disposing){if(disposing&&menu!=null)menu.Dispose();base.Dispose(disposing);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space||e.KeyCode==Keys.Down){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;RectangleF r=new RectangleF(.5f,.5f,Width-1f,Height-1f);
            using(GraphicsPath p=Shape.Round(r,6)){
                if(FillEnd==Color.Transparent)using(Brush b=new SolidBrush(BackColor))e.Graphics.FillPath(b,p);
                else using(Brush b=new LinearGradientBrush(new Rectangle(0,0,Math.Max(1,Width),Math.Max(1,Height)),BackColor,FillEnd,LinearGradientMode.Horizontal))e.Graphics.FillPath(b,p);
            }
            using(GraphicsPath p=Shape.Round(r,6))using(Pen pen=new Pen(hover||Focused?ForeColor:Outline,1f))e.Graphics.DrawPath(pen,p);
            string value=SelectedItem==null?Placeholder:Text;Color color=SelectedItem==null?Hint:ForeColor;
            TextRenderer.DrawText(e.Graphics,value,Font,new Rectangle(14,0,Width-45,Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
            using(Pen pen=new Pen(ArrowColor,1.8f)){int cx=Width-21,cy=Height/2;e.Graphics.DrawLines(pen,new[]{new Point(cx-5,cy-2),new Point(cx,cy+3),new Point(cx+5,cy-2)});}
        }
    }

    internal sealed class AppForm : Form {
        private sealed class UiMetrics {
            public Rectangle Bounds;
            public Padding Padding;
            public float FontSize;
            public FontStyle FontStyle;
            public string FontFamily;
            public Font ScaledFont;
            public int Radius;
        }
        private static readonly ThemeSpec[] Themes={
            new ThemeSpec("Blue Eclipse","Midnight blue fades smoothly into black with lavender and icy-blue accents.","#152D51","#04060B","#15233B","#20324E","#A8B9FF","#DFE6FA","#A6B5CE","#334866","#79D5FF"),
            new ThemeSpec("Obsidian","Graphite and black with steel-blue and mist-violet accents.","#232B38","#05070A","#1D2633","#2A3748","#A7BCE0","#E2EAF5","#A7B6C9","#405167","#D9B7FF"),
            new ThemeSpec("Aurora","Deep teal fades to black with mint and twilight-violet accents.","#123B3B","#03090B","#173236","#20454A","#83E3D0","#DDF6F1","#A1C6C2","#356166","#B6A3FF"),
            new ThemeSpec("Amethyst","Ink-purple and black with violet and orchid-pink highlights.","#312047","#07050C","#2A213B","#3B2F51","#C6ACFF","#EFE8FA","#B8A9D0","#56466D","#F3A9E8"),
            new ThemeSpec("Deep Sea","Ocean blue fades into near-black with cyan and seafoam.","#12384B","#03090E","#17313F","#214558","#84D7EF","#E0F0F7","#A5C4D0","#386176","#75F1C7"),
            new ThemeSpec("Ember","Dark charcoal-red with coral and warm-gold accents.","#402523","#0A0607","#342427","#4A3131","#F2AC91","#F7E8E1","#C9ADA9","#674846","#FFD383"),
            new ThemeSpec("Forest","Evergreen fades to black with sage and soft-gold accents.","#203B2E","#050A07","#20362C","#2D4939","#A7DDB5","#E4F1E7","#B0C8B6","#46634E","#D8CA86"),
            new ThemeSpec("Crimson","Burgundy-black with rose and warm-peach highlights.","#3D1F31","#09060B","#342330","#4B3041","#EBA5C2","#F6E6ED","#C7A9B7","#654758","#FFCB9D"),
            new ThemeSpec("Slate","Cool slate fades to black with ice-blue and soft violet.","#2B3944","#080B0F","#293641","#3A4A56","#AFCCDE","#E6EFF5","#B0C1CB","#4E626F","#B6ABF3"),
            new ThemeSpec("Copper Night","Smoky brown-black with brushed copper and verdigris.","#3D2B21","#0A0806","#342A23","#4B392C","#EAC18D","#F4EADD","#C5B39D","#68513D","#93D1C0"),
            new ThemeSpec("Blackout","True black canvas with graphite controls and soft gray type.","#000000","#000000","#0D0D10","#17171C","#AEB2BC","#A5A8AF","#787C85","#303039","#909BA8"),
            new ThemeSpec("Neon Orchid","Deep raspberry pink fades to black with vivid pink and turquoise accents.","#4A172E","#090407","#321A2B","#482A3B","#FF8FC4","#FFEAF4","#D8B4C7","#704658","#72E8DF"),
            new ThemeSpec("Royal Gold","Velvet violet and black paired with warm gold and lilac.","#2E1F4C","#08060C","#291F3B","#3D2E54","#FFD276","#F5EDF8","#C4B4D1","#5F4B75","#C8A5FF"),
            new ThemeSpec("Coral Reef","Deep tropical teal with vivid coral and sea-glass highlights.","#104147","#030B0C","#17363A","#245057","#FF9E8F","#EAF6F4","#B0D0CE","#3C6970","#83E5D1"),
            new ThemeSpec("Electric Cobalt","Saturated cobalt slips into black with bold tangerine and ice-blue.","#1D3474","#05070E","#1A2C52","#284071","#FFB46B","#E7EDFA","#ABBCDC","#435D91","#9FD6FF")
        };
        private Color bg, side, surface, surface2, accent, accentAlt, secondaryText, secondaryLine, textColor, muted, border;
        private readonly RoundedPanel root = new RoundedPanel{Radius=0,PaintCanvas=true};
        private readonly RoundedPanel sidebar = new RoundedPanel{Radius=0,PaintCanvas=true}, content = new RoundedPanel{Radius=0,PaintCanvas=true};
        private readonly RoundedPanel header = new RoundedPanel{Radius=0,PaintCanvas=true};
        private RoundedPanel hero;
        private Label status = new Label();
        private Label madeBy;
        private LinkLabel author;
        private RoundedButton startButton = new RoundedButton();
        private RoundedButton fullscreenButton = new RoundedButton();
        private RoundedSelect targetBox, mouseBox;
        private RoundedButton keyButton, holdButton, repeatButton;
        private TextBox intervalBox;
        private Label cpsLabel, targetNote;
        private Panel stepsHost;
        private TextBox macroName;
        private readonly System.Windows.Forms.Timer actionTimer = new System.Windows.Forms.Timer();
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseKey", "settings.txt");
        private Keys selectedKey = Keys.Space;
        private string selectedMouse = "Left", mode = "Hold", inputKind = "Keyboard", theme = "Blue Eclipse";
        private int interval = 100;
        private IntPtr target = IntPtr.Zero;
        private bool armed, holding, macroRunning, active;
        private Keys heldKey;
        private string heldMouse, heldKind;
        private CancellationTokenSource macroCancel;
        private MacroFile macro = new MacroFile();
        private string page = "Quick action";
        private bool fullscreen, previousTopMost;
        private Rectangle previousBounds;
        private Size previousMinimumSize;
        private FormWindowState previousWindowState;
        private FormBorderStyle previousBorderStyle;
        private readonly Dictionary<Control,UiMetrics> uiMetrics=new Dictionary<Control,UiMetrics>();
        private Size baseClientSize;
        private float uiScale=1f;
        private bool scaleQueued,buildingPage;

        public AppForm() {
            LoadSettings(); ApplyColors();
            Text = "PulseKey"; Size = new Size(1100,810); MinimumSize = new Size(1100,780);
            baseClientSize=ClientSize;
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI",10);
            AutoScaleMode = AutoScaleMode.Dpi; Icon = MakeIcon();
            root.Dock = DockStyle.Fill; root.BackColor = bg; Controls.Add(root);
            sidebar.Dock = DockStyle.Left; sidebar.Width=220; sidebar.BackColor=side; root.Controls.Add(sidebar);
            header.Dock = DockStyle.Top; header.Height=68; header.BackColor=bg; root.Controls.Add(header);
            content.Dock=DockStyle.Fill; content.Padding=new Padding(28,8,28,22); content.BackColor=bg; root.Controls.Add(content);
            root.Controls.SetChildIndex(content,0); root.Controls.SetChildIndex(header,1); root.Controls.SetChildIndex(sidebar,2);
            BuildShell(); BuildPage();
            actionTimer.Tick += ActionTick;
            header.Resize += (s,e)=>LayoutHeader();
            content.Resize += (s,e)=>LayoutWorkspace();
            sidebar.Resize += (s,e)=>LayoutSidebarFooter();
            Resize += (s,e)=>QueueUiScale();
            Shown += (s,e) => { TintTitleBar();
                bool startHotkey=Native.RegisterHotKey(Handle,1,0,(uint)Keys.F8), stopHotkey=Native.RegisterHotKey(Handle,2,0,(uint)Keys.F9);
                RefreshTargets(); if(!startHotkey||!stopHotkey)MessageBox.Show(this,"F8 or F9 is already in use. Close the conflicting app and restart PulseKey. The on-screen buttons still work.","Hotkey unavailable",MessageBoxButtons.OK,MessageBoxIcon.Warning); };
            FormClosing += OnAppClosing;
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        private void TintTitleBar(){int dark=1,caption=ColorTranslator.ToWin32(side),captionText=ColorTranslator.ToWin32(textColor);DwmSetWindowAttribute(Handle,20,ref dark,4);DwmSetWindowAttribute(Handle,35,ref caption,4);DwmSetWindowAttribute(Handle,36,ref captionText,4);}

        private void ApplyColors() {
            ThemeSpec selected=Themes.FirstOrDefault(t=>t.Name==theme)??Themes[0];
            theme=selected.Name;ThemeCanvas.Top=selected.Top;ThemeCanvas.Bottom=selected.Bottom;
            bg=selected.Bottom;side=selected.Top;surface=selected.Surface;surface2=selected.Surface2;
            accent=selected.Accent;accentAlt=selected.AccentAlt;textColor=selected.Text;muted=selected.Muted;border=selected.Border;
            secondaryText=accentAlt==accent?textColor:accentAlt;
            secondaryLine=accentAlt==accent?border:Shape.Blend(border,accentAlt,.50f);
            root.BackColor=bg;sidebar.BackColor=bg;content.BackColor=bg;header.BackColor=bg;
        }
        private Icon MakeIcon() {
            Bitmap bmp=new Bitmap(32,32); using(Graphics g=Graphics.FromImage(bmp)) {
                g.Clear(Color.Transparent); using(Brush b=new SolidBrush(accent)) g.FillEllipse(b,2,2,28,28);
                using(Pen p=new Pen(bg,3)) g.DrawLines(p,new[]{new Point(7,17),new Point(13,17),new Point(16,8),new Point(19,23),new Point(22,16),new Point(26,16)});
                using(Pen ring=new Pen(accentAlt,2.5f))g.DrawArc(ring,3,3,26,26,210,118);
            }
            IntPtr h=bmp.GetHicon(); Icon icon=(Icon)Icon.FromHandle(h).Clone(); DestroyIcon(h); bmp.Dispose(); return icon;
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private Label Label(string value, int size=10, bool bold=false) { return new Label { Text=value, AutoSize=true, ForeColor=textColor, BackColor=Color.Transparent, Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular) }; }
        private GradientLabel TitleLabel(string value,int size=18) { return new GradientLabel { Text=value, StartColor=accent, EndColor=accentAlt, Font=new Font("Segoe UI",size,FontStyle.Bold) }; }
        private Label SectionLabel(string value,int size=8) {Label label=Label(value,size,true);label.ForeColor=secondaryText;return label;}
        private RoundedButton Button(string value, EventHandler click, bool primary=false) {
            RoundedButton b=new RoundedButton { Text=value, BackColor=primary?accent:Shape.Blend(surface2,accent,.10f), FillEnd=primary?accentAlt:Shape.Blend(surface2,accentAlt,.20f), ForeColor=primary?bg:secondaryText, Outline=primary?Color.Transparent:secondaryLine, Font=new Font("Segoe UI",10,FontStyle.Bold), Height=40, Width=130, TabStop=true };
            b.Click+=click; return b;
        }
        private void ClearControls(Control parent) {
            while(parent.Controls.Count>0) { Control child=parent.Controls[0];UnregisterUiTree(child);parent.Controls.RemoveAt(0);child.Dispose(); }
        }
        private int Ui(int pixels){return (int)Math.Round(pixels*uiScale);}
        private static int Scaled(int pixels,float scale){return (int)Math.Round(pixels*scale);}
        private static Padding Scaled(Padding value,float scale){return new Padding(Scaled(value.Left,scale),Scaled(value.Top,scale),Scaled(value.Right,scale),Scaled(value.Bottom,scale));}
        private void RegisterUiTree(Control parent){
            foreach(Control child in parent.Controls){
                if(!uiMetrics.ContainsKey(child))uiMetrics.Add(child,new UiMetrics{
                    Bounds=child.Bounds,Padding=child.Padding,FontSize=child.Font.SizeInPoints,
                    FontStyle=child.Font.Style,FontFamily=child.Font.FontFamily.Name,
                    Radius=child is RoundedPanel?((RoundedPanel)child).Radius:child is RoundedButton?((RoundedButton)child).Radius:0
                });
                RegisterUiTree(child);
            }
        }
        private void UnregisterUiTree(Control control){
            foreach(Control child in control.Controls)UnregisterUiTree(child);
            UiMetrics metrics;
            if(uiMetrics.TryGetValue(control,out metrics)){
                if(metrics.ScaledFont!=null)metrics.ScaledFont.Dispose();
                uiMetrics.Remove(control);
            }
        }
        private void ScaleUiTree(Control parent){
            foreach(Control child in parent.Controls){
                UiMetrics metrics;
                if(!uiMetrics.TryGetValue(child,out metrics))continue;
                if(child is Label||child is TextBox||child is RoundedButton||child is RoundedSelect){
                    Font previous=metrics.ScaledFont;
                    metrics.ScaledFont=new Font(metrics.FontFamily,metrics.FontSize*uiScale,metrics.FontStyle);
                    child.Font=metrics.ScaledFont;
                    if(previous!=null)previous.Dispose();
                }
                child.Padding=Scaled(metrics.Padding,uiScale);
                if(child is RoundedPanel)((RoundedPanel)child).Radius=Ui(metrics.Radius);
                else if(child is RoundedButton)((RoundedButton)child).Radius=Ui(metrics.Radius);
                Rectangle b=metrics.Bounds;
                if(parent==content&&child.Tag is Point)child.Height=Ui(b.Height);
                else child.SetBounds(Ui(b.Left),Ui(b.Top),Ui(b.Width),Ui(b.Height));
                ScaleUiTree(child);
            }
        }
        private void QueueUiScale(){
            if(!IsHandleCreated||scaleQueued)return;
            scaleQueued=true;
            BeginInvoke((Action)(()=>{scaleQueued=false;if(!IsDisposed)UpdateUiScale();}));
        }
        private void UpdateUiScale(){
            if(baseClientSize.Width<1||baseClientSize.Height<1)return;
            float minimum=fullscreen?.55f:1f;
            float next=Math.Max(minimum,Math.Min(1.9f,Math.Min((float)ClientSize.Width/Math.Max(baseClientSize.Width,1116),(float)ClientSize.Height/baseClientSize.Height)));
            next=(float)Math.Round(next*20f)/20f;
            if(Math.Abs(next-uiScale)<.01f){LayoutHeader();LayoutSidebarFooter();LayoutWorkspace();return;}
            uiScale=next;
            sidebar.Width=Ui(220);header.Height=Ui(68);
            ScaleUiTree(sidebar);ScaleUiTree(header);ScaleUiTree(content);
            LayoutHeader();LayoutSidebarFooter();LayoutWorkspace();
        }
        private void AddShadow(Control parent,int left,int top,int width,int height) {
            CardShadow shadow=new CardShadow { Width=width+5,Height=height+6,Anchor=AnchorStyles.Top|AnchorStyles.Left };
            parent.Controls.Add(shadow);
            PlaceWorkspace(shadow,left+2,top+4);
        }
        private int WorkspaceWidth(){return Math.Max(Ui(500),Math.Min(Ui(840),content.ClientSize.Width-Ui(56)));}
        private int WorkspaceLeft(){return Math.Max(0,(content.ClientSize.Width-WorkspaceWidth())/2);}
        private int WorkspaceTop(){return Math.Max(0,(content.ClientSize.Height-Ui(700))/2);}
        private void PlaceWorkspace(Control control,int x,int y){control.Tag=new Point(x,y);control.Left=WorkspaceLeft()+Ui(x);control.Top=WorkspaceTop()+Ui(y);}
        private void LayoutWorkspace(){
            int width=WorkspaceWidth();
            foreach(Control control in content.Controls){
                if(!(control.Tag is Point))continue;
                Point position=(Point)control.Tag;
                control.Left=WorkspaceLeft()+Ui(position.X);
                control.Top=WorkspaceTop()+Ui(position.Y);
                if(control is CardShadow)control.Width=width+Ui(5);
                else if(control is RoundedPanel)control.Width=width;
            }
            content.Invalidate();
        }
        private Panel Card(Control parent, int top, int height) {
            int width=WorkspaceWidth();
            AddShadow(parent,0,top,width,height);
            Panel p=new RoundedPanel { Width=width, Height=height, Radius=8, BackColor=surface, PaintCanvas=true, Outline=secondaryLine, Anchor=AnchorStyles.Top|AnchorStyles.Left };
            PlaceWorkspace(p,0,top);
            parent.Controls.Add(p);p.BringToFront();
            return p;
        }
        private void Put(Control parent, Control c, int x, int y, int width=0) { c.Location=new Point(x,y); if(width>0)c.Width=width; parent.Controls.Add(c); }
        private RoundedSelect Combo() { return new RoundedSelect { BackColor=Shape.Blend(surface2,accent,.06f), FillEnd=Shape.Blend(surface2,accentAlt,.15f), ForeColor=textColor, Outline=secondaryLine, ArrowColor=accentAlt, Hint=muted, Font=new Font("Segoe UI",10), Height=38 }; }
        private TextBox NumberInput(Control parent,int initial,int width) {
            TextBox box=new TextBox { Text=Math.Max(50,Math.Min(10000,initial)).ToString(), BackColor=surface2, ForeColor=secondaryText, BorderStyle=BorderStyle.None, Font=new Font("Segoe UI",10), Width=width };
            box.KeyPress+=(s,e)=>{if(!Char.IsControl(e.KeyChar)&&!Char.IsDigit(e.KeyChar))e.Handled=true;};
            Put(parent,box,12,10);return box;
        }
        private int ReadMilliseconds(TextBox box,int fallback) { int value;return int.TryParse(box.Text,out value)?Math.Max(50,Math.Min(10000,value)):fallback; }
        private static string DisplayKey(string value){return value=="ShiftKey"?"Shift":value=="ControlKey"?"Ctrl":value=="Menu"?"Alt":value;}

        private void BuildShell() {
            ClearControls(sidebar); ClearControls(header);
            Label brand=Label("PULSEKEY",16,true); brand.ForeColor=accentAlt; Put(sidebar,brand,24,27);
            Label sub=Label("DESKTOP INPUT STUDIO",8,true); sub.ForeColor=muted; Put(sidebar,sub,27,61);
            string[] pages={"Quick action","Macro studio","Appearance"};
            for(int i=0;i<pages.Length;i++) { string name=pages[i]; RoundedButton b=Button(name,(s,e)=>{page=name;BuildShell();BuildPage();}); b.TextAlign=ContentAlignment.MiddleLeft; b.Width=188; b.Height=46; b.Left=16; b.Top=138+i*56; b.Padding=new Padding(10,0,0,0); b.BackColor=page==name?Shape.Blend(surface2,accent,.22f):surface2; b.FillEnd=page==name?Shape.Blend(surface2,accentAlt,.30f):Color.Transparent; b.ForeColor=page==name?accent:muted;b.Outline=page==name?secondaryLine:Color.Transparent;b.TransparentFill=page!=name; sidebar.Controls.Add(b); }
            madeBy=Label("Made by",8);madeBy.ForeColor=muted;Put(sidebar,madeBy,27,0);madeBy.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            author=new LinkLabel{Text="@BraxFFA",AutoSize=true,BackColor=Color.Transparent,Font=new Font("Segoe UI",8,FontStyle.Bold),LinkColor=accentAlt,ActiveLinkColor=accent,VisitedLinkColor=accentAlt,LinkBehavior=LinkBehavior.HoverUnderline,TabStop=true};
            author.LinkArea=new LinkArea(0,author.Text.Length);
            author.LinkClicked+=(s,e)=>{try{Process.Start(new ProcessStartInfo("https://linktr.ee/BraxFFA"){UseShellExecute=true});}catch{MessageBox.Show(this,"Could not open the link in your default browser.","Open link",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
            Put(sidebar,author,80,0);author.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            status=Label("●  Idle",9,true); status.ForeColor=muted; status.AutoSize=false; status.TextAlign=ContentAlignment.MiddleRight; status.Width=145; status.Height=40; status.Top=13; status.Anchor=AnchorStyles.Top|AnchorStyles.Right; header.Controls.Add(status);
            startButton=Button("Start  F8",(s,e)=>Toggle(),true); startButton.Width=120; startButton.Top=14; startButton.Anchor=AnchorStyles.Top|AnchorStyles.Right; header.Controls.Add(startButton);
            fullscreenButton=Button(fullscreen?"Exit full screen  F11":"Full screen  F11",(s,e)=>ToggleFullscreen()); fullscreenButton.Width=145;fullscreenButton.Top=14;fullscreenButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(fullscreenButton);
            RegisterUiTree(sidebar);RegisterUiTree(header);
            ScaleUiTree(sidebar);ScaleUiTree(header);
            LayoutHeader();LayoutSidebarFooter();
            UpdateStatus();
        }
        private void LayoutHeader(){
            if(startButton==null||startButton.IsDisposed||fullscreenButton==null||fullscreenButton.IsDisposed||status==null||status.IsDisposed)return;
            startButton.Left=header.ClientSize.Width-Ui(28)-startButton.Width;
            fullscreenButton.Left=startButton.Left-Ui(12)-fullscreenButton.Width;
            status.Left=fullscreenButton.Left-Ui(12)-status.Width;
        }
        private void LayoutSidebarFooter(){
            if(madeBy==null||author==null||madeBy.IsDisposed||author.IsDisposed)return;
            madeBy.Top=sidebar.ClientSize.Height-Ui(52);
            author.Top=madeBy.Top;
        }
        private void ToggleFullscreen(){
            if(!fullscreen){
                previousWindowState=WindowState;previousBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
                previousBorderStyle=FormBorderStyle;previousTopMost=TopMost;previousMinimumSize=MinimumSize;
                Rectangle screen=Screen.FromControl(this).Bounds;
                WindowState=FormWindowState.Normal;MinimumSize=Size.Empty;FormBorderStyle=FormBorderStyle.None;Bounds=screen;TopMost=true;fullscreen=true;
            }else{
                TopMost=previousTopMost;WindowState=FormWindowState.Normal;FormBorderStyle=previousBorderStyle;
                MinimumSize=previousMinimumSize;Bounds=previousBounds;WindowState=previousWindowState;fullscreen=false;
            }
            if(fullscreenButton!=null&&!fullscreenButton.IsDisposed)fullscreenButton.Text=fullscreen?"Exit full screen  F11":"Full screen  F11";
            UpdateUiScale();
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
            if(keyData==Keys.F11){ToggleFullscreen();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        private void BuildPage() {
            ClearControls(content);
            buildingPage=true;
            if(page=="Quick action") BuildQuick(); else if(page=="Macro studio") BuildMacro(); else BuildAppearance();
            buildingPage=false;
            RegisterUiTree(content);ScaleUiTree(content);LayoutWorkspace();
        }
        private void BuildQuick() {
            hero=Hero(0,108);
            Label overline=Label("QUICK ACTION",8,true);overline.ForeColor=accentAlt;Put(hero,overline,24,15);
            Put(hero,TitleLabel("One input. Your rhythm."),22,36);
            Label heroSub=Label("Choose an app, set the behavior, then press Start.",9);heroSub.ForeColor=muted;Put(hero,heroSub,24,78);
            Panel targetCard=Card(content,122,126); Put(targetCard,SectionLabel("TARGET WINDOW",9),22,15);
            targetBox=Combo(); targetBox.Placeholder="Choose a desktop app";targetBox.Width=540; Put(targetCard,targetBox,22,45); targetBox.SelectedIndexChanged+=(s,e)=>{StopAll();TargetWindow tw=targetBox.SelectedItem as TargetWindow; target=tw==null?IntPtr.Zero:tw.Handle;};
            RoundedButton refresh=Button("Refresh list",(s,e)=>RefreshTargets()); Put(targetCard,refresh,580,44,138);
            targetNote=Label("Select a desktop window, press Start, then bring that window forward.",9); targetNote.ForeColor=muted; Put(targetCard,targetNote,22,96);
            Panel inputCard=Card(content,262,174); Put(inputCard,SectionLabel("INPUT",9),22,16);
            RoundedButton keyboard=Button("Keyboard",(s,e)=>{inputKind="Keyboard";UpdateInputMode();}); keyboard.Name="keyboard"; Put(inputCard,keyboard,22,48,130);
            RoundedButton mouse=Button("Mouse",(s,e)=>{inputKind="Mouse";UpdateInputMode();}); mouse.Name="mouse"; Put(inputCard,mouse,164,48,130);
            Put(inputCard,SectionLabel("Selected key or button",9),324,54);
            keyButton=Button(DisplayKey(selectedKey.ToString()),(s,e)=>CaptureQuickKey()); Put(inputCard,keyButton,324,86,220);
            mouseBox=Combo(); mouseBox.Items.AddRange(new object[]{"Left","Right","Middle","X1","X2"}); mouseBox.SelectedItem=selectedMouse; mouseBox.SelectedIndexChanged+=(s,e)=>{StopAll();selectedMouse=mouseBox.Text;SaveSettings();}; Put(inputCard,mouseBox,324,87,220);
            Label captureHint=Label("Click the key field to capture any key.",9);captureHint.ForeColor=muted;Put(inputCard,captureHint,22,130);
            UpdateInputMode(false);
            Panel modeCard=Card(content,450,142); Put(modeCard,SectionLabel("BEHAVIOR",9),22,15);
            holdButton=Button("Hold",(s,e)=>{mode="Hold";UpdateMode();}); Put(modeCard,holdButton,22,46,130);
            repeatButton=Button("Repeat",(s,e)=>{mode="Repeat";UpdateMode();}); Put(modeCard,repeatButton,164,46,130);
            Put(modeCard,SectionLabel("INTERVAL"),340,27);
            RoundedPanel numberWrap=new RoundedPanel{BackColor=surface2,Outline=secondaryLine,Radius=7,Width=160,Height=40};Put(modeCard,numberWrap,340,49);
            intervalBox=NumberInput(numberWrap,interval,105);
            Put(numberWrap,SectionLabel("ms",9),124,9);
            cpsLabel=Label("",9); cpsLabel.ForeColor=muted; Put(modeCard,cpsLabel,22,108);
            intervalBox.Leave+=(s,e)=>{interval=ReadMilliseconds(intervalBox,interval);intervalBox.Text=interval.ToString();UpdateMode();SaveSettings();};
            UpdateMode();
            Label tip=Label("F8 starts or pauses   ·   F9 releases and stops   ·   Output runs only while your target is in front.",9); tip.ForeColor=muted; PlaceWorkspace(tip,4,608);content.Controls.Add(tip);
            RefreshTargets();
        }
        private RoundedPanel Hero(int top,int height){int width=WorkspaceWidth();AddShadow(content,0,top,width,height);hero=new RoundedPanel{Radius=8,Width=width,Height=height,BackColor=surface,PaintCanvas=true,Outline=secondaryLine,Anchor=AnchorStyles.Top|AnchorStyles.Left};PlaceWorkspace(hero,0,top);content.Controls.Add(hero);hero.BringToFront();return hero;}
        private void UpdateInputMode(bool userChange=true) {
            if(keyButton==null||mouseBox==null)return;
            keyButton.Visible=inputKind=="Keyboard"; mouseBox.Visible=inputKind=="Mouse";
            Control parent=keyButton.Parent; if(parent!=null)foreach(Control c in parent.Controls) if(c.Name=="keyboard"||c.Name=="mouse") { bool selected=(c.Name=="keyboard")== (inputKind=="Keyboard");c.BackColor=selected?Shape.Blend(surface2,accent,.23f):Shape.Blend(surface2,accent,.10f);c.ForeColor=selected?accent:secondaryText;RoundedButton button=c as RoundedButton;if(button!=null)button.FillEnd=Shape.Blend(surface2,accentAlt,selected?0.30f:0.20f);c.Invalidate(); }
            if(userChange)StopAll();SaveSettings();
        }
        private void UpdateMode() {
            if(holdButton==null||repeatButton==null||intervalBox==null)return;
            holdButton.BackColor=mode=="Hold"?Shape.Blend(surface2,accent,.23f):Shape.Blend(surface2,accent,.10f); holdButton.FillEnd=Shape.Blend(surface2,accentAlt,mode=="Hold"?0.30f:0.20f); holdButton.ForeColor=mode=="Hold"?accent:secondaryText;
            repeatButton.BackColor=mode=="Repeat"?Shape.Blend(surface2,accent,.23f):Shape.Blend(surface2,accent,.10f); repeatButton.FillEnd=Shape.Blend(surface2,accentAlt,mode=="Repeat"?0.30f:0.20f); repeatButton.ForeColor=mode=="Repeat"?accent:secondaryText;
            intervalBox.Enabled=mode=="Repeat";
            cpsLabel.Text=mode=="Repeat"?String.Format("≈ {0:0.##} actions per second",1000.0/interval):"Holds until stopped or the target loses focus.";
            if(armed) StopAll(); SaveSettings();
        }
        private void CaptureQuickKey() {
            Keys? result=CaptureKey(); if(result.HasValue) { StopAll();selectedKey=result.Value; keyButton.Text=DisplayKey(selectedKey.ToString()); SaveSettings(); }
        }
        private Keys? CaptureKey(IWin32Window owner=null) {
            using(Form f=new Form()) {
                f.Text="Choose a key";f.Size=new Size(360,170);f.StartPosition=FormStartPosition.CenterParent;f.FormBorderStyle=FormBorderStyle.FixedDialog;f.MaximizeBox=false;f.MinimizeBox=false;f.KeyPreview=true;f.BackColor=surface;
                Label l=Label("Press the key you want to use\nShift is supported. F8 and F9 are reserved.",11);l.Location=new Point(28,28);f.Controls.Add(l);
                Keys? result=null;
                f.KeyDown+=(s,e)=>{ Keys k=e.KeyCode; if(k!=Keys.F8&&k!=Keys.F9&&k!=Keys.None) {result=k;f.DialogResult=DialogResult.OK;f.Close();} e.SuppressKeyPress=true;};
                return f.ShowDialog(owner??this)==DialogResult.OK?result:null;
            }
        }

        private void BuildMacro() {
            Hero(0,102);
            Label overline=Label("MACRO STUDIO",8,true);overline.ForeColor=accentAlt;Put(hero,overline,24,14);
            Put(hero,TitleLabel("Build your sequence."),22,35);
            Label guide=Label("Choose a target, add steps, then run the sequence.",9);guide.ForeColor=muted;Put(hero,guide,24,76);
            Panel card=Card(content,116,482);
            Put(card,SectionLabel("MACRO NAME"),20,16);
            RoundedPanel nameWrap=new RoundedPanel{BackColor=surface2,Outline=secondaryLine,Radius=7,Width=425,Height=40};Put(card,nameWrap,20,46);
            macroName=new TextBox { Text=macro.Name, BackColor=surface2, ForeColor=secondaryText, BorderStyle=BorderStyle.None, Font=new Font("Segoe UI",11), Width=392 }; Put(nameWrap,macroName,13,10);
            macroName.TextChanged+=(s,e)=>macro.Name=macroName.Text;
            Put(card,SectionLabel("TIMELINE"),20,103);
            stepsHost=new Panel{Left=20,Top=130,Width=490,Height=282,BackColor=Color.Transparent,AutoScroll=true};card.Controls.Add(stepsHost);RefreshSteps();
            RoundedPanel palette=new RoundedPanel{BackColor=Shape.Blend(surface2,accent,.08f),FillEnd=Shape.Blend(surface2,accentAlt,.16f),Outline=secondaryLine,Radius=7,Width=240,Height=283};Put(card,palette,530,130);
            Label add=SectionLabel("ADD A STEP");Put(palette,add,17,16);
            RoundedButton addKey=Button("Press a key",(s,e)=>AddKeyStep());addKey.BackColor=surface;addKey.TextAlign=ContentAlignment.MiddleLeft;Put(palette,addKey,15,48,210);
            Label keyHelp=Label("Choose one keyboard key",8);keyHelp.ForeColor=muted;Put(palette,keyHelp,21,91);
            RoundedButton addMouse=Button("Click mouse",(s,e)=>AddMouseStep());addMouse.BackColor=surface;addMouse.TextAlign=ContentAlignment.MiddleLeft;Put(palette,addMouse,15,119,210);
            Label mouseHelp=Label("Left, right, middle, X1 or X2",8);mouseHelp.ForeColor=muted;Put(palette,mouseHelp,21,162);
            RoundedButton addDelay=Button("Wait",(s,e)=>AddDelayStep());addDelay.BackColor=surface;addDelay.TextAlign=ContentAlignment.MiddleLeft;Put(palette,addDelay,15,190,210);
            Label waitHelp=Label("Pause between actions",8);waitHelp.ForeColor=muted;Put(palette,waitHelp,21,233);
            RoundedButton save=Button("Save",(s,e)=>SaveMacro());Put(card,save,20,427,112);
            RoundedButton load=Button("Load",(s,e)=>LoadMacro());Put(card,load,144,427,112);
            Label note=Label("Runs once when the target is in front.",9);note.ForeColor=muted;Put(card,note,285,439);
            RoundedButton run=Button("Run once",(s,e)=>RunMacro(),true);Put(card,run,620,427,150);
            Label tip=Label("Edit changes a step's input and timing. Move steps with Up and Down. F9 stops playback.",9);tip.ForeColor=muted;PlaceWorkspace(tip,4,614);content.Controls.Add(tip);
        }
        private void RefreshSteps() {
            if(stepsHost==null||stepsHost.IsDisposed)return;
            ClearControls(stepsHost);
            if(macro.Steps.Count==0){
                Label emptyTitle=Label("Your timeline is empty",15,true);emptyTitle.ForeColor=textColor;Put(stepsHost,emptyTitle,22,68);
                Label emptyBody=Label("Start with Press a key, Click mouse, or Wait.\nSteps appear here in playback order.",10);emptyBody.ForeColor=muted;Put(stepsHost,emptyBody,23,105);
                if(!buildingPage){RegisterUiTree(stepsHost);ScaleUiTree(stepsHost);}
                return;
            }
            for(int i=0;i<macro.Steps.Count;i++){
                int index=i;MacroStep step=macro.Steps[i];
                RoundedPanel row=new RoundedPanel{Left=0,Top=i*70,Width=472,Height=62,Radius=7,BackColor=Shape.Blend(surface2,accent,.04f),FillEnd=Shape.Blend(surface2,accentAlt,.08f),Outline=secondaryLine};stepsHost.Controls.Add(row);
                Label number=Label((i+1).ToString("00"),10,true);number.ForeColor=accentAlt;Put(row,number,13,20);
                string action=step.Kind=="Click"?"Click "+step.Value:step.Kind=="Delay"?"Wait "+step.DelayMs+" ms":"Press "+DisplayKey(step.Value);
                Put(row,Label(action,11,true),53,10);
                Label after=Label(step.Kind=="Delay"?"Pause in sequence":"then wait "+step.DelayMs+" ms",8);after.ForeColor=muted;Put(row,after,54,36);
                RoundedButton edit=Button("Edit",(s,e)=>EditStep(index));Put(row,edit,226,15,53);edit.Height=32;
                RoundedButton up=Button("Up",(s,e)=>MoveStep(index,-1));Put(row,up,283,15,48);up.Height=32;
                RoundedButton down=Button("Down",(s,e)=>MoveStep(index,1));Put(row,down,335,15,55);down.Height=32;
                RoundedButton remove=Button("Remove",(s,e)=>RemoveStep(index));Put(row,remove,394,15,72);remove.Height=32;
            }
            if(!buildingPage){RegisterUiTree(stepsHost);ScaleUiTree(stepsHost);}
        }
        private void AddKeyStep() { Keys? k=CaptureKey();if(k.HasValue){macro.Steps.Add(new MacroStep{Kind="Key",Value=k.Value.ToString(),DelayMs=100});RefreshSteps();} }
        private void AddMouseStep() {
            using(Form f=Dialog("Add mouse click")) {
                RoundedSelect box=Combo();box.Items.AddRange(new object[]{"Left","Right","Middle","X1","X2"});box.SelectedIndex=0;Put(f,box,24,30,200);
                RoundedButton ok=Button("Add",(s,e)=>{f.DialogResult=DialogResult.OK;f.Close();},true);Put(f,ok,24,88,100);
                if(f.ShowDialog(this)==DialogResult.OK){macro.Steps.Add(new MacroStep{Kind="Click",Value=box.Text,DelayMs=100});RefreshSteps();}
            }
        }
        private void AddDelayStep() { int? ms=AskMilliseconds("Add delay",500);if(ms.HasValue){macro.Steps.Add(new MacroStep{Kind="Delay",Value="Wait",DelayMs=ms.Value});RefreshSteps();} }
        private Form Dialog(string title) { return new Form { Text=title, Size=new Size(300,180), StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, BackColor=surface }; }
        private int? AskMilliseconds(string title,int initial) {
            using(Form f=Dialog(title)) {
                Label l=Label("Milliseconds (50–10000)",9);Put(f,l,24,20);
                RoundedPanel wrap=new RoundedPanel{BackColor=surface2,Outline=border,Radius=7,Width=200,Height=38};Put(f,wrap,24,48);
                TextBox n=NumberInput(wrap,initial,170);
                RoundedButton ok=Button("Apply",(s,e)=>{f.DialogResult=DialogResult.OK;f.Close();},true);Put(f,ok,24,91,110);
                return f.ShowDialog(this)==DialogResult.OK?(int?)ReadMilliseconds(n,initial):null;
            }
        }
        private void RemoveStep(int i) { if(i<0||i>=macro.Steps.Count)return;macro.Steps.RemoveAt(i);RefreshSteps(); }
        private void MoveStep(int i,int delta) { int j=i+delta;if(i<0||j<0||j>=macro.Steps.Count)return;MacroStep s=macro.Steps[i];macro.Steps.RemoveAt(i);macro.Steps.Insert(j,s);RefreshSteps(); }
        private void EditStep(int i) {
            if(i<0||i>=macro.Steps.Count)return;MacroStep step=macro.Steps[i];string value=step.Value;
            using(Form f=Dialog("Edit step "+(i+1))) {
                f.Size=new Size(410,280);
                Label title=SectionLabel(step.Kind=="Delay"?"WAIT DURATION":step.Kind=="Click"?"MOUSE BUTTON":"KEY");Put(f,title,24,22);
                RoundedSelect mouse=null;RoundedButton keyChoice=null;
                if(step.Kind=="Key") {keyChoice=Button(DisplayKey(value),(s,e)=>{Keys? k=CaptureKey(f);if(k.HasValue){value=k.Value.ToString();keyChoice.Text=DisplayKey(value);}},true);Put(f,keyChoice,24,52,230);}
                else if(step.Kind=="Click") {mouse=Combo();mouse.Items.AddRange(new object[]{"Left","Right","Middle","X1","X2"});mouse.SelectedItem=value;Put(f,mouse,24,52,230);}
                else {Label wait=Label("Pause playback for this many milliseconds.",9);wait.ForeColor=muted;Put(f,wait,24,54);}
                Label timing=SectionLabel(step.Kind=="Delay"?"MILLISECONDS":"WAIT AFTER THIS STEP (MS)");Put(f,timing,24,103);
                RoundedPanel wrap=new RoundedPanel{BackColor=surface2,Outline=border,Radius=7,Width=174,Height=38};Put(f,wrap,24,132);
                TextBox num=NumberInput(wrap,step.DelayMs,145);
                RoundedButton save=Button("Save step",(s,e)=>{f.DialogResult=DialogResult.OK;f.Close();},true);Put(f,save,260,176,110);
                if(f.ShowDialog(this)==DialogResult.OK){if(mouse!=null)value=mouse.Text;step.Value=value;step.DelayMs=ReadMilliseconds(num,step.DelayMs);RefreshSteps();}
            }
        }
        private void SaveMacro() {
            using(SaveFileDialog d=new SaveFileDialog{Filter="PulseKey macro (*.pulse.xml)|*.pulse.xml",FileName=Sanitize(macro.Name)+".pulse.xml"}) if(d.ShowDialog(this)==DialogResult.OK) {
                try { using(FileStream fs=File.Create(d.FileName)) new XmlSerializer(typeof(MacroFile)).Serialize(fs,macro); }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not save",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
        private string Sanitize(string s) { string v=String.Join("",s.Split(Path.GetInvalidFileNameChars()));return String.IsNullOrWhiteSpace(v)?"Macro":v; }
        private void LoadMacro() {
            using(OpenFileDialog d=new OpenFileDialog{Filter="PulseKey macro (*.pulse.xml)|*.pulse.xml"}) if(d.ShowDialog(this)==DialogResult.OK) {
                try { using(FileStream fs=File.OpenRead(d.FileName)) { if(fs.Length>262144)throw new InvalidDataException("Macro file is too large.");
                    XmlReaderSettings settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=262144 };
                    using(XmlReader reader=XmlReader.Create(fs,settings)) macro=(MacroFile)new XmlSerializer(typeof(MacroFile)).Deserialize(reader);
                }
                    if(macro.Steps==null||macro.Steps.Count>500)throw new InvalidDataException("Invalid macro length.");
                    if(String.IsNullOrWhiteSpace(macro.Name))macro.Name="Untitled macro";
                    macroName.Text=macro.Name;RefreshSteps(); }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not load",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }

        private void BuildAppearance() {
            Hero(0,120);
            Label over=Label("APPEARANCE",8,true);over.ForeColor=accentAlt;Put(hero,over,24,17);
            Put(hero,TitleLabel("Make it yours.",19),22,43);
            Label heronote=Label("Fifteen dark palettes, including a true black canvas.",9);heronote.ForeColor=muted;Put(hero,heronote,24,88);
            Panel card=Card(content,136,345);Put(card,SectionLabel("SELECTED THEME"),22,18);
            Label p=Label(theme,16,true);p.ForeColor=accent;Put(card,p,22,49);
            string description=(Themes.FirstOrDefault(t=>t.Name==theme)??Themes[0]).Description;
            Label desc=Label(description,10);desc.ForeColor=muted;Put(card,desc,22,87);
            Label gallery=SectionLabel("PALETTES");Put(card,gallery,22,127);
            for(int i=0;i<Themes.Length;i++){
                ThemeSpec spec=Themes[i];RoundedButton tile=Button(spec.Name,(s,e)=>SelectTheme(spec.Name));
                tile.BackColor=Shape.Blend(spec.Surface2,spec.Accent,.15f);tile.FillEnd=Shape.Blend(spec.Surface2,spec.AccentAlt,.30f);tile.ForeColor=spec.Accent;tile.Outline=spec.Name==theme?spec.Accent:spec.Border;
                tile.Font=new Font("Segoe UI",9,FontStyle.Bold);tile.Width=143;tile.Height=43;
                Put(card,tile,22+(i%5)*153,155+(i/5)*56);
            }
            Panel info=Card(content,495,98);Put(info,SectionLabel("DESIGNED TO STAY LIGHT"),22,18);
            Label detail=Label("Native Windows controls, no browser runtime, and no network.",10);detail.ForeColor=muted;Put(info,detail,22,50);
        }
        private void SelectTheme(string chosen) {
            if(theme==chosen||!Themes.Any(t=>t.Name==chosen))return;
            BeginInvoke((Action)(()=>{theme=chosen;ApplyColors();Icon previous=Icon;Icon=MakeIcon();if(previous!=null)previous.Dispose();SaveSettings();BuildShell();BuildPage();TintTitleBar();root.Invalidate(true);}));
        }

        private void RefreshTargets() {
            if(targetBox==null)return;
            IntPtr old=target;targetBox.SelectedIndex=-1;targetBox.Items.Clear();
            Native.EnumWindows((h,p)=>{ if(h==Handle||!Native.IsWindowVisible(h))return true;int n=Native.GetWindowTextLength(h);if(n<2||n>250)return true;
                uint pid;Native.GetWindowThreadProcessId(h,out pid);if(pid==0)return true;
                var sb=new System.Text.StringBuilder(n+1);Native.GetWindowText(h,sb,sb.Capacity);string title=CleanWindowTitle(sb.ToString());if(title.Length>1&&!title.Equals("Program Manager",StringComparison.OrdinalIgnoreCase))targetBox.Items.Add(new TargetWindow{Handle=h,Title=title});return true;},IntPtr.Zero);
            for(int i=0;i<targetBox.Items.Count;i++)if(((TargetWindow)targetBox.Items[i]).Handle==old){targetBox.SelectedIndex=i;break;}
            if(targetBox.SelectedIndex<0) target=IntPtr.Zero;
            if(targetNote!=null)targetNote.Text=targetBox.Items.Count==0?"No visible window is open. Open an app and click Refresh list.":"Select a desktop window, press Start, then bring that window forward.";
            targetBox.Invalidate();
        }
        private static string CleanWindowTitle(string title) {
            var chars=title.Where(c=>Char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).ToArray();
            return new string(chars).Trim();
        }
        private bool TargetReady() {return target!=IntPtr.Zero&&Native.IsWindow(target)&&Native.GetForegroundWindow()==target;}
        private bool IsReserved(Keys key){return key==Keys.F8||key==Keys.F9;}
        private void Toggle() {
            if(macroRunning){StopAll();return;}
            if(armed){StopAll();return;}
            if(target==IntPtr.Zero){MessageBox.Show(this,"Choose a target window on Quick action first.","Target required",MessageBoxButtons.OK,MessageBoxIcon.Information);page="Quick action";BuildShell();BuildPage();return;}
            if(inputKind=="Keyboard"&&IsReserved(selectedKey)){MessageBox.Show(this,"F8 and F9 are reserved.","Choose another key");return;}
            if(intervalBox!=null&&!intervalBox.IsDisposed){interval=ReadMilliseconds(intervalBox,interval);intervalBox.Text=interval.ToString();SaveSettings();}
            armed=true;actionTimer.Interval=mode=="Hold"?100:interval;actionTimer.Start();UpdateStatus();
        }
        private void ActionTick(object sender,EventArgs e) {
            if(!armed)return;
            bool ready=TargetReady();
            if(!ready) { if(!Native.IsWindow(target))StopAll();else {ReleaseHold();SetActive(false);}return; }
            if(mode=="Hold") { if(!holding) { heldKind=inputKind;heldKey=selectedKey;heldMouse=selectedMouse;if(inputKind=="Keyboard")holding=Native.Key(selectedKey,true); else holding=MouseHold(selectedMouse,true); } SetActive(holding); }
            else { bool sent;if(inputKind=="Keyboard") {sent=Native.Key(selectedKey,true);sent=Native.Key(selectedKey,false)&&sent;} else sent=Native.Click(selectedMouse);SetActive(sent); }
        }
        private bool MouseHold(string button,bool down) {
            uint flag=button=="Right"?(down?8u:16u):button=="Middle"?(down?32u:64u):button=="X1"||button=="X2"?(down?128u:256u):(down?2u:4u);
            Native.INPUT i=new Native.INPUT();i.type=0;i.mi.flags=flag;i.mi.data=button=="X1"?1u:button=="X2"?2u:0u;
            return Native.SendInput(1,new[]{i},40)==1;
        }
        private void ReleaseHold() {if(!holding)return;if(heldKind=="Keyboard")Native.Key(heldKey,false);else MouseHold(heldMouse,false);holding=false;}
        private void SetActive(bool value) {if(active==value)return;active=value;UpdateStatus();}
        private void StopAll() {armed=false;active=false;actionTimer.Stop();ReleaseHold();if(macroCancel!=null)macroCancel.Cancel();macroRunning=false;UpdateStatus();}
        private void UpdateStatus() {if(status==null||startButton==null)return;bool ready=armed||macroRunning;status.Text=active?"●  Active":ready?"●  Armed":"●  Idle";status.ForeColor=active?Shape.Blend(accentAlt,Color.White,.10f):ready?Shape.Blend(accent,Color.White,.08f):Shape.Blend(muted,bg,.40f);startButton.Text=ready?"Stop  F9":"Start  F8";}
        private async void RunMacro() {
            if(macroRunning)return;
            if(macro.Steps.Count==0){MessageBox.Show(this,"Add at least one macro step first.","Empty macro",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            if(target==IntPtr.Zero){MessageBox.Show(this,"Choose a target window on Quick action first.","Target required");return;}
            StopAll();macroRunning=true;macroCancel=new CancellationTokenSource();CancellationToken token=macroCancel.Token;UpdateStatus();
            try {
                foreach(MacroStep step in macro.Steps.ToList()) {
                    token.ThrowIfCancellationRequested();
                    SetActive(false);
                    while(!TargetReady()) {if(!Native.IsWindow(target))return;await System.Threading.Tasks.Task.Delay(80,token);}
                    SetActive(true);
                    if(step.Kind=="Key") {Keys key;if(Enum.TryParse(step.Value,out key)&&!IsReserved(key)){Native.Key(key,true);Native.Key(key,false);}}
                    else if(step.Kind=="Click" && new[]{"Left","Right","Middle","X1","X2"}.Contains(step.Value)) Native.Click(step.Value);
                    await System.Threading.Tasks.Task.Delay(Math.Max(50,Math.Min(10000,step.DelayMs)),token);
                }
            } catch(OperationCanceledException) {} finally {active=false;macroRunning=false;UpdateStatus();}
        }
        protected override void WndProc(ref Message m) {if(m.Msg==0x0312){if(m.WParam.ToInt32()==1)Toggle();else if(m.WParam.ToInt32()==2)StopAll();}base.WndProc(ref m);}
        private void OnAppClosing(object sender,FormClosingEventArgs e){ StopAll();SaveSettings();Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2); }
        private void LoadSettings(){try{if(!File.Exists(settingsPath))return;foreach(string line in File.ReadAllLines(settingsPath)){int pos=line.IndexOf('=');if(pos<0)continue;string k=line.Substring(0,pos),v=line.Substring(pos+1);if(k=="Theme"&&(v=="Starry Night"||Themes.Any(t=>t.Name==v)))theme=v=="Starry Night"?"Blue Eclipse":v;else if(k=="Mode"&&new[]{"Hold","Repeat"}.Contains(v))mode=v;else if(k=="Kind"&&new[]{"Keyboard","Mouse"}.Contains(v))inputKind=v;else if(k=="Key")Enum.TryParse(v,out selectedKey);else if(k=="Mouse")selectedMouse=v;else if(k=="Interval")int.TryParse(v,out interval);}}catch{}if(interval<50||interval>10000)interval=100;}
        private void SaveSettings(){try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));File.WriteAllLines(settingsPath,new[]{"Theme="+theme,"Mode="+mode,"Kind="+inputKind,"Key="+selectedKey,"Mouse="+selectedMouse,"Interval="+interval});}catch{}}
    }

    internal static class Program {
        [STAThread] private static void Main() { bool first;using(Mutex mutex=new Mutex(true,"Local\\PulseKey_9C7A0E01",out first)){
            if(!first){MessageBox.Show("PulseKey is already open.","PulseKey",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new AppForm());
        }}
    }
}
