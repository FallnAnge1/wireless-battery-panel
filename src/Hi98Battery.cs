using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using System.Collections.Generic;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly:System.Reflection.AssemblyTitle("无线设备电量")]
[assembly:System.Reflection.AssemblyProduct("无线设备电量")]
[assembly:System.Reflection.AssemblyDescription("键盘、鼠标及蓝牙设备电量总览")]
[assembly:System.Reflection.AssemblyVersion("2.12.0.0")]
[assembly:System.Reflection.AssemblyFileVersion("2.12.0.0")]

static class AppStorage {
    public static string Root {
        get {
            foreach(string arg in Environment.GetCommandLineArgs())if(arg=="--ui-test"||arg=="--devices-test"||arg=="--animation-test"||arg=="--snapshot")return AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WirelessBatteryPanel");
        }
    }
    public static string Settings {get{return Path.Combine(Root,"settings.xml");}}
    public static string Position {get{return Path.Combine(Root,"window-position.txt");}}
}
public class Preferences {
    public string SelectedDeviceId="hi98";
    public int X=-1,Y=-1,Scale=100,Opacity=100,Threshold=20,TrayStyle=3,TrayDesignVersion=0;
    public bool Locked,TrayOnly,LowAlerted;
    static string PathName {get{return AppStorage.Settings;}}
    public static Preferences Load() {
        try {string path=File.Exists(PathName)?PathName:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.xml");using(var f=File.OpenRead(path))return (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(f);}catch{return new Preferences();}
    }
    public void Save() {
        Directory.CreateDirectory(AppStorage.Root);
        string temporary=PathName+".tmp";
        using(var f=File.Create(temporary))new XmlSerializer(typeof(Preferences)).Serialize(f,this);
        if(File.Exists(PathName))File.Replace(temporary,PathName,null);else File.Move(temporary,PathName);
    }
    public bool ShouldNotify(int percent) {
        if(percent>Threshold+5)LowAlerted=false;
        if(Threshold>0 && percent<=Threshold && !LowAlerted){LowAlerted=true;return true;}
        return false;
    }
}

static class DrawingStyle {
    public static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF box,float radius) {
        var path=new System.Drawing.Drawing2D.GraphicsPath();float diameter=Math.Min(radius*2,Math.Min(box.Width,box.Height));
        path.AddArc(box.X,box.Y,diameter,diameter,180,90);path.AddArc(box.Right-diameter,box.Y,diameter,diameter,270,90);path.AddArc(box.Right-diameter,box.Bottom-diameter,diameter,diameter,0,90);path.AddArc(box.X,box.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
}
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
    public DarkMenuRenderer():base(new MenuColors()){RoundedEdges=false;}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Color.FromArgb(226,233,243):Color.FromArgb(115,127,145);base.OnRenderItemText(e);}
    sealed class MenuColors : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground{get{return Color.FromArgb(28,36,49);}}
        public override Color ImageMarginGradientBegin{get{return ToolStripDropDownBackground;}}
        public override Color ImageMarginGradientMiddle{get{return ToolStripDropDownBackground;}}
        public override Color ImageMarginGradientEnd{get{return ToolStripDropDownBackground;}}
        public override Color MenuBorder{get{return Color.FromArgb(56,69,88);}}
        public override Color MenuItemSelected{get{return Color.FromArgb(45,64,85);}}
        public override Color MenuItemBorder{get{return MenuItemSelected;}}
        public override Color MenuItemSelectedGradientBegin{get{return MenuItemSelected;}}
        public override Color MenuItemSelectedGradientEnd{get{return MenuItemSelected;}}
        public override Color CheckBackground{get{return Color.FromArgb(48,104,91);}}
        public override Color CheckSelectedBackground{get{return CheckBackground;}}
        public override Color SeparatorDark{get{return MenuBorder;}}
        public override Color SeparatorLight{get{return ToolStripDropDownBackground;}}
    }
}

static class TrayArtwork {
    static readonly string[] PixelDigits={"111101101101111","010110010010111","111001111100111","111001111001111","101101111001001","111100111001111","111100111101111","111001001001001","111101111101111","111101111001111"};
    static void PixelNumber(Graphics g,int size,int row,int? number,Color ink) {
        string text=number.HasValue?number.Value.ToString():"--";
        int cell=Math.Max(1,Math.Min((int)(size*.72)/(text.Length*4-1),(int)(size*.38)/5)),width=(text.Length*4-1)*cell;
        int x=(int)Math.Round(size*.57-width/2.0),y=(int)Math.Round(size*(row==0?.25:.75)-2.5*cell);
        var smoothing=g.SmoothingMode;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.None;
        using(var brush=new SolidBrush(ink))for(int n=0;n<text.Length;n++)for(int py=0;py<5;py++)for(int px=0;px<3;px++){
            bool on=text[n]=='-'?py==2:PixelDigits[text[n]-'0'][py*3+px]=='1';
            if(on)g.FillRectangle(brush,x+(n*4+px)*cell,y+py*cell,cell,cell);
        }
        g.SmoothingMode=smoothing;
    }
    public static bool LightTaskbar() {
        try{using(var k=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return k!=null&&Convert.ToInt32(k.GetValue("SystemUsesLightTheme",0))==1;}catch{return false;}
    }
    public static Bitmap Render(int size,int? percent,int threshold,int style,bool light,int? second=null,bool dual=false) {
        var bitmap=new Bitmap(size,size);float unit=size/16f;
        if(dual){using(var g=Graphics.FromImage(bitmap)){
            g.Clear(Color.Transparent);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var outline=DrawingStyle.Round(new RectangleF(.3f*unit,.3f*unit,size-.6f*unit,size-.6f*unit),2.5f*unit)){
            using(var background=new SolidBrush(Color.FromArgb(247,250,253)))g.FillPath(background,outline);
            for(int row=0;row<2;row++){
                int? number=row==0?percent:second;
                Color ink=!number.HasValue?Color.FromArgb(134,146,158):number<=10?Color.FromArgb(190,45,62):threshold>0&&number<=threshold?Color.FromArgb(151,91,0):Color.FromArgb(32,43,55);
                Color marker=!number.HasValue?Color.FromArgb(168,180,192):number<=10?Color.FromArgb(229,78,96):threshold>0&&number<=threshold?Color.FromArgb(225,160,42):row==0?Color.FromArgb(24,169,137):Color.FromArgb(61,127,225);
                using(var mark=DrawingStyle.Round(new RectangleF(1.3f*unit,(row*7.5f+2.1f)*unit,1.25f*unit,4.3f*unit),.6f*unit))using(var fill=new SolidBrush(marker))g.FillPath(fill,mark);
                PixelNumber(g,size,row,number,ink);
            }
            using(var border=new Pen(light?Color.FromArgb(130,149,167):Color.FromArgb(215,226,237),.45f*unit))g.DrawPath(border,outline);
            }
        }return bitmap;}
        using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color ink=percent.HasValue?(percent<=10?Color.FromArgb(244,84,84):threshold>0&&percent<=threshold?Color.FromArgb(240,160,32):light?Color.FromArgb(25,35,45):Color.White):light?Color.FromArgb(95,105,115):Color.FromArgb(175,185,195);
            if(style==3) {
                Color badge=percent.HasValue?(percent<=10?Color.FromArgb(244,84,84):threshold>0&&percent<=threshold?Color.FromArgb(245,182,65):Color.FromArgb(109,231,194)):Color.FromArgb(162,177,195);
                using(var shape=DrawingStyle.Round(new RectangleF(0,0,size,size),3*unit))using(var fill=new SolidBrush(badge))g.FillPath(fill,shape);
                ink=Color.FromArgb(18,37,36);
            }
            using(var brush=new SolidBrush(ink)) {
                if(style==2) {
                    using(var pen=new Pen(ink,Math.Max(1,unit)))g.DrawRectangle(pen,unit,4*unit,12*unit,8*unit);
                    g.FillRectangle(brush,14*unit,6*unit,unit,4*unit);
                    if(percent.HasValue)g.FillRectangle(brush,3*unit,6*unit,9*unit*percent.Value/100f,4*unit);
                    else g.FillRectangle(brush,6*unit,7*unit,3*unit,2*unit);
                } else {
                    // Fit actual glyph outlines: avoids the large font padding that made v1.4 tiny.
                    using(var path=new System.Drawing.Drawing2D.GraphicsPath())using(var family=new FontFamily("Arial"))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
                        path.AddString(percent.HasValue?percent.Value.ToString():"--",family,(int)FontStyle.Bold,32,PointF.Empty,format);
                        var bounds=path.GetBounds();float availableHeight=(style==1?11:14)*unit;
                        float scale=Math.Min((style==3?13:14)*unit/bounds.Width,availableHeight/bounds.Height);
                        using(var transform=new System.Drawing.Drawing2D.Matrix(scale,0,0,scale,(size-bounds.Width*scale)/2-bounds.X*scale,(availableHeight-bounds.Height*scale)/2+unit-bounds.Y*scale)){path.Transform(transform);}
                        g.FillPath(brush,path);
                    }
                    if(style==1) {
                        using(var muted=new SolidBrush(light?Color.FromArgb(160,165,170):Color.FromArgb(80,90,100)))g.FillRectangle(muted,unit,14*unit,14*unit,unit);
                        if(percent.HasValue)g.FillRectangle(brush,unit,14*unit,14*unit*percent.Value/100f,unit);
                    }
                }
            }
        }
        return bitmap;
    }
    public static Icon Create(int? percent,int threshold,int style,bool light,int? second=null,bool dual=false) {
        int[] sizes={16,20,24,32,40,48,64};var frames=new List<byte[]>();
        foreach(int size in sizes)using(var bitmap=Render(size,percent,threshold,style,light,second,dual))using(var png=new MemoryStream()){bitmap.Save(png,System.Drawing.Imaging.ImageFormat.Png);frames.Add(png.ToArray());}
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)sizes[i]);writer.Write((byte)sizes[i]);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}
            foreach(var frame in frames)writer.Write(frame);writer.Flush();stream.Position=0;using(var icon=new Icon(stream,SystemInformation.SmallIconSize))return (Icon)icon.Clone();
        }
    }
}

sealed class DevicePicker : Control {
    public readonly List<DeviceEntry> Items=new List<DeviceEntry>();
    int selected=-1;bool hover;
    public event EventHandler SelectedIndexChanged;
    public DevicePicker(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.ComboBox;}
    public int SelectedIndex {get{return selected;}set{selected=value;Invalidate();var handler=SelectedIndexChanged;if(handler!=null)handler(this,EventArgs.Empty);}}
    public DeviceEntry SelectedItem {get{return selected>=0&&selected<Items.Count?Items[selected]:null;}}
    public void BeginUpdate(){SuspendLayout();}
    public void EndUpdate(){ResumeLayout();Invalidate();}
    protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){hover=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Left)ShowChoices();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space||e.KeyCode==Keys.Down){e.Handled=true;ShowChoices();}}
    void ShowChoices() {
        var popup=new ContextMenuStrip {Renderer=new DarkMenuRenderer(),Font=Font,ShowImageMargin=true};
        for(int i=0;i<Items.Count;i++){int index=i;var option=new ToolStripMenuItem(Items[i].ToString()){Checked=selected==i};option.Click+=(s,e)=>SelectedIndex=index;popup.Items.Add(option);}
        popup.Closed+=(s,e)=>popup.Dispose();popup.Show(this,new Point(0,Height+3));
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent==null?Color.FromArgb(20,28,40):Parent.BackColor);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var round=DrawingStyle.Round(new RectangleF(0,0,Width,Height),6))using(var fill=new SolidBrush(hover?Color.FromArgb(44,64,82):Color.FromArgb(29,43,59)))g.FillPath(fill,round);
        TextRenderer.DrawText(g,SelectedItem==null?"选择设备":SelectedItem.ToString(),Font,new Rectangle(7,0,Width-29,Height),Color.FromArgb(224,237,247),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        using(var pen=new Pen(Color.FromArgb(143,185,193),1.4f)){g.DrawLines(pen,new[]{new PointF(Width-17,Height*.43f),new PointF(Width-13,Height*.6f),new PointF(Width-9,Height*.43f)});}
    }
}

sealed class DeviceEntry {
    public string Id,Name,Transport,Note;
    public string ContainerId,BatterySource="Windows 上报";
    public bool IsG3m,ChargeViaUsb;
    public string ChargeText;
    public int? WindowsChargeState;
    public string ReaderPath;
    public DateTime SampledAt;
    public bool? Connected;
    public int? Percent;
    public override string ToString(){return Name+" · "+Transport;}
}
sealed class InventorySnapshot {
    public readonly List<DeviceEntry> Devices=new List<DeviceEntry>();
    public DateTime Scanned;
    public bool BluetoothOk;
    public bool UsbOk;
    public string Warning="";
}
static class KnownReceivers {
    public static string Diagnostic="";
    [StructLayout(LayoutKind.Sequential)] struct Info {public int Size;public Guid Class;public uint DevInst;public IntPtr Reserved;}
    [StructLayout(LayoutKind.Sequential)] struct Key {public Guid Format;public uint Id;}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(IntPtr guid,string enumerator,IntPtr window,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr list,uint index,ref Info info);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr list,ref Info info,System.Text.StringBuilder buffer,uint size,out uint needed);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceProperty(IntPtr list,ref Info info,ref Key key,out uint type,byte[] buffer,uint size,out uint needed,uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Locate_DevNode(out uint node,string id,uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_ID(uint node,System.Text.StringBuilder id,int length,uint flags);
    public static string UsbRoot(string instanceId) {
        if(String.IsNullOrEmpty(instanceId))return null;uint node;
        if(CM_Locate_DevNode(out node,instanceId,0)!=0)return null;
        for(int depth=0;depth<10;depth++){
            var id=new System.Text.StringBuilder(1024);
            if(CM_Get_Device_ID(node,id,1024,0)!=0)return null;
            string text=id.ToString();if(text.StartsWith("USB\\VID_",StringComparison.OrdinalIgnoreCase)&&text.IndexOf("&MI_",StringComparison.OrdinalIgnoreCase)<0)return text.ToUpperInvariant();
            uint parent;if(CM_Get_Parent(out parent,node,0)!=0)return null;node=parent;
        }return null;
    }
    public static void Scan(Dictionary<string,DeviceEntry> entries) {
        Diagnostic="";int count=0;
        var list=SetupDiGetClassDevs(IntPtr.Zero,"USB",IntPtr.Zero,6);
        if(list==new IntPtr(-1))throw new IOException("无法枚举当前 USB 接收器");
        try{for(uint index=0;;index++){
            var info=new Info {Size=Marshal.SizeOf(typeof(Info))};
            if(!SetupDiEnumDeviceInfo(list,index,ref info)){if(Marshal.GetLastWin32Error()!=259)throw new IOException("USB 接收器枚举未完成");break;}
            var id=new System.Text.StringBuilder(1024);uint needed;
            if(!SetupDiGetDeviceInstanceId(list,ref info,id,1024,out needed))continue;
            count++;if(id.ToString().IndexOf("1D57",StringComparison.OrdinalIgnoreCase)>=0)Diagnostic+="candidate="+id.ToString()+"; ";
            if(!id.ToString().StartsWith("USB\\VID_1D57&PID_130F\\",StringComparison.OrdinalIgnoreCase))continue;
            var key=new Key {Format=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),Id=2};uint type;var bytes=new byte[16];string container=null;
            if(SetupDiGetDeviceProperty(list,ref info,ref key,out type,bytes,16,out needed,0)&&needed==16&&type==13)container=new Guid(bytes).ToString("D");
            Observe(entries,id.ToString(),container,true);
        }}finally{Diagnostic+="scanned="+count;SetupDiDestroyDeviceInfoList(list);}
    }
    public static bool Observe(Dictionary<string,DeviceEntry> entries,string instanceId,string containerId,bool present) {
        if(!present||instanceId==null||!instanceId.StartsWith("USB\\VID_1D57&PID_130F\\",StringComparison.OrdinalIgnoreCase))return false;
        string id="usb:"+instanceId.ToUpperInvariant();
        if(!entries.ContainsKey(id))entries[id]=new DeviceEntry {Id=id,Name="2.4G 无线设备",Transport="2.4G",Note="接收器已插入 · 电量暂不支持"};
        return true;
    }
    public static void Verify(){
        var entries=new Dictionary<string,DeviceEntry>(StringComparer.OrdinalIgnoreCase);string container="8a9c915d-65b0-4f20-81f5-748cfa6a938b";
        if(Observe(entries,"USB\\VID_1D57&PID_130F\\TEST",container,false)||entries.Count!=0)throw new Exception("Absent receiver discovered");
        if(!Observe(entries,"USB\\VID_1D57&PID_130F\\TEST",container,true)||entries.Count!=1)throw new Exception("Present receiver missing");
        Observe(entries,"USB\\VID_1D57&PID_130F\\TEST",container,true);
        if(entries.Count!=1||entries["usb:USB\\VID_1D57&PID_130F\\TEST"].Percent.HasValue)throw new Exception("Receiver duplicated or battery guessed");
        Observe(entries,"USB\\VID_1D57&PID_130F\\SECOND",container,true);if(entries.Count!=2)throw new Exception("Different receivers merged by shared container");
        if(Observe(entries,"USB\\VID_1D57&PID_130F&MI_00\\TEST",container,true)||Observe(entries,"USB\\VID_1234&PID_5678\\TEST",container,true))throw new Exception("Wrong receiver matched");
    }
}
sealed class BluetoothNode {
    public string ContainerId;
    public int? Percent;
    public int? ChargeState;
}
static class ChargeStatus {
    public static int? WindowsValue(object value) {var n=DeviceInventory.Battery(value);return n==0||n==1?n:null;}
    public static string WindowsText(int? value){return value==1?"正在充电":null;}
    public static string Keyboard(Receiver.Reading value){return value!=null&&value.Flags==0x10?"正在充电":null;}
    public static void Verify(){
        if(WindowsValue((byte)1)!=1||WindowsValue(2)!=null||WindowsValue("1")!=null||WindowsText(0)!=null)throw new Exception("Windows charge state misread");
        if(Keyboard(new Receiver.Reading {Percent=95,Flags=1})!=null||Keyboard(new Receiver.Reading {Percent=100,Flags=0x10})!="正在充电")throw new Exception("Keyboard charging or full state guessed");
        var node=new BluetoothNode {ContainerId="C",ChargeState=1};var nodes=new List<BluetoothNode>{node};
        var device=new DeviceEntry {ContainerId="C",Transport="蓝牙",Connected=true,Percent=88};BluetoothSupplement.Apply(device,nodes);
        if(device.ChargeText!="正在充电"||device.Percent!=88)throw new Exception("Bluetooth charging supplement missing");
        device.WindowsChargeState=0;device.ChargeText=null;BluetoothSupplement.Apply(device,nodes);
        if(device.ChargeText!=null)throw new Exception("Explicit not-charging state overwritten");
        device.WindowsChargeState=null;nodes.Add(new BluetoothNode {ContainerId="C",ChargeState=0});BluetoothSupplement.Apply(device,nodes);
        if(device.ChargeText!=null)throw new Exception("Conflicting charge states accepted");
    }
}
static class BluetoothSupplement {
    public static string Container(object value) {
        Guid id;if(value==null||!Guid.TryParse(value.ToString(),out id)||id==Guid.Empty)return null;
        string text=id.ToString("D");return text=="00000000-0000-0000-ffff-ffffffffffff"||text=="ffffffff-ffff-ffff-ffff-ffffffffffff"?null:text;
    }
    public static void Apply(DeviceEntry entry,List<BluetoothNode> nodes) {
        if(entry.Transport!="蓝牙"||entry.Connected!=true||entry.ContainerId==null)return;
        if(!entry.WindowsChargeState.HasValue){
            int? state=null;bool conflict=false;
            foreach(var node in nodes)if(node.ContainerId==entry.ContainerId&&node.ChargeState.HasValue){if(state.HasValue&&state!=node.ChargeState)conflict=true;state=node.ChargeState;}
            if(!conflict){entry.WindowsChargeState=state;entry.ChargeText=ChargeStatus.WindowsText(state);}
        }
        if(entry.Percent.HasValue)return;
        int? value=null;
        foreach(var node in nodes){
            if(node.ContainerId!=entry.ContainerId||!node.Percent.HasValue||node.Percent<=0||node.Percent>100)continue;
            if(value.HasValue&&value!=node.Percent)return; // Conflicting child reports: do not guess.
            value=node.Percent;
        }
        if(value.HasValue){entry.Percent=value;entry.BatterySource="Windows 子节点上报";entry.Note="同一设备的蓝牙子节点电量，可能存在系统缓存";}
    }
    public static void Verify() {
        var entry=new DeviceEntry {Id="bt:test",ContainerId="A",Transport="蓝牙",Connected=true};
        var nodes=new List<BluetoothNode> {new BluetoothNode {ContainerId="B",Percent=91}};
        Apply(entry,nodes);if(entry.Percent.HasValue)throw new Exception("Bluetooth cross-device value leaked");
        nodes.Add(new BluetoothNode {ContainerId="A",Percent=0});nodes.Add(new BluetoothNode {ContainerId="A",Percent=101});
        Apply(entry,nodes);if(entry.Percent.HasValue)throw new Exception("Ambiguous child value accepted");
        nodes.Add(new BluetoothNode {ContainerId="A",Percent=74});entry.Connected=false;
        Apply(entry,nodes);if(entry.Percent.HasValue)throw new Exception("Disconnected child battery leaked");
        entry.Connected=true;Apply(entry,nodes);if(entry.Percent!=74)throw new Exception("Bluetooth supplement missing");
        entry.Percent=82;Apply(entry,nodes);if(entry.Percent!=82)throw new Exception("Primary battery overwritten");
        entry.Percent=null;nodes.Add(new BluetoothNode {ContainerId="A",Percent=73});
        Apply(entry,nodes);if(entry.Percent.HasValue)throw new Exception("Conflicting child values accepted");
    }
}
static class DeviceInventory {
    public static string LastBluetoothError="";
    public static string LastChargingDiagnostic="";
    const string BatteryKey="{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string ChargeKey="System.Devices.ChargingState";
    static InventorySnapshot cached;
    sealed class MouseSample {public Receiver.Reading Value;public DateTime Sampled,Next;public string Note;}
    static readonly Dictionary<string,MouseSample> mouseSamples=new Dictionary<string,MouseSample>();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sa,uint c,uint flags,IntPtr t);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h,byte[] text,int length);
    public static int? Battery(object value) {
        if(!(value is byte||value is short||value is ushort||value is int||value is uint||value is long))return null;
        long n=Convert.ToInt64(value);return n>=0&&n<=100?(int?)n:null;
    }
    static object Property(DeviceInformation d,string key){object v;return d.Properties.TryGetValue(key,out v)?v:null;}
    static bool? Boolean(DeviceInformation d,string key){var v=Property(d,key);return v is bool?(bool?)v:null;}
    static DeviceInformationCollection Await(Windows.Foundation.IAsyncOperation<DeviceInformationCollection> op) {
        try{var until=DateTime.UtcNow.AddSeconds(2);while(op.Status==Windows.Foundation.AsyncStatus.Started){if(DateTime.UtcNow>until){op.Cancel();throw new IOException("设备扫描超时");}Thread.Sleep(20);}return op.GetResults();}finally{op.Close();}
    }
    static string Identity(DeviceInformation d,string key,string prefix) {
        if(prefix=="usb:"){
            string instance=Property(d,"System.Devices.DeviceInstanceId") as string;
            if(instance==null){var parts=d.Id.Split('#');if(parts.Length>=3)instance="HID\\"+parts[1]+"\\"+parts[2];}
            string root=KnownReceivers.UsbRoot(instance);if(root!=null)return prefix+root;
        }
        string container=BluetoothSupplement.Container(Property(d,key));return container!=null?prefix+container:d.Id;
    }
    public static List<BluetoothNode> ReadBluetoothNodes() {
        var children=Await(DeviceInformation.FindAllAsync("System.Devices.Present:=System.StructuredQueryType.Boolean#True",new[]{"System.Devices.ContainerId",BatteryKey,ChargeKey},DeviceInformationKind.Device));
        var nodes=new List<BluetoothNode>();
        foreach(var node in children)if(node.Id.StartsWith("BTH",StringComparison.OrdinalIgnoreCase)){
            var container=BluetoothSupplement.Container(Property(node,"System.Devices.ContainerId"));
            if(container!=null)nodes.Add(new BluetoothNode {ContainerId=container,Percent=Battery(Property(node,BatteryKey)),ChargeState=ChargeStatus.WindowsValue(Property(node,ChargeKey))});
        }
        return nodes;
    }
    public static InventorySnapshot Get(bool force=false) {
        if(!force&&cached!=null&&(DateTime.UtcNow-cached.Scanned.ToUniversalTime()).TotalSeconds<10)return cached;
        var result=new InventorySnapshot {Scanned=DateTime.Now};var entries=new Dictionary<string,DeviceEntry>(StringComparer.OrdinalIgnoreCase);
        try {
            var bluetooth=Await(DeviceInformation.FindAllAsync("System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"",new[]{"System.Devices.Aep.IsConnected","System.Devices.Aep.IsPaired","System.Devices.Aep.ContainerId","System.Devices.BatteryLife",BatteryKey,ChargeKey},DeviceInformationKind.AssociationEndpoint));
            foreach(var d in bluetooth) {
                bool? connected=Boolean(d,"System.Devices.Aep.IsConnected");if(connected!=true&&Boolean(d,"System.Devices.Aep.IsPaired")!=true&&!d.Pairing.IsPaired)continue;
                string id=Identity(d,"System.Devices.Aep.ContainerId","bt:");
                if(ConnectionMonitor.IsKeyboardName(d.Name))id="hi98";
                int? battery=Battery(Property(d,BatteryKey))??Battery(Property(d,"System.Devices.BatteryLife"));if(connected!=true)battery=null;
                var item=new DeviceEntry {Id=id,ContainerId=BluetoothSupplement.Container(Property(d,"System.Devices.Aep.ContainerId")),Name=String.IsNullOrWhiteSpace(d.Name)?"蓝牙设备":d.Name,Transport="蓝牙",Connected=connected,Percent=battery,Note=connected==true?(battery.HasValue?"Windows 上报电量":"Windows 暂未提供电量"):"已配对 · 未连接"};
                DeviceEntry prior;if(entries.TryGetValue(id,out prior)){if(prior.Connected==true&&connected!=true)continue;if(prior.Percent.HasValue&&!item.Percent.HasValue)continue;}
                item.WindowsChargeState=connected==true?ChargeStatus.WindowsValue(Property(d,ChargeKey)):null;item.ChargeText=ChargeStatus.WindowsText(item.WindowsChargeState);entries[id]=item;
            }
            result.BluetoothOk=true;LastBluetoothError="";
        } catch(Exception e) {LastBluetoothError=e.GetType().Name+": "+e.Message;result.Warning="蓝牙扫描暂不可用";}
        bool needChildren=false;foreach(var entry in entries.Values)if(entry.Connected==true&&(!entry.Percent.HasValue||!entry.WindowsChargeState.HasValue)&&entry.ContainerId!=null)needChildren=true;
        if(needChildren)try {
            var nodes=ReadBluetoothNodes();
            foreach(var entry in entries.Values)BluetoothSupplement.Apply(entry,nodes);
        } catch {result.Warning+=(result.Warning.Length==0?"":"；")+"蓝牙子节点电量补充读取暂不可用";}
        try {
            var hid=Await(DeviceInformation.FindAllAsync("System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"",new[]{"System.Devices.ContainerId","System.Devices.DeviceInstanceId",ChargeKey},DeviceInformationKind.DeviceInterface));
            foreach(var d in hid) {
                if(d.Id.IndexOf("vid_",StringComparison.OrdinalIgnoreCase)<0)continue;
                string name=null;using(var h=CreateFile(d.Id,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){if(h.IsInvalid)continue;var bytes=new byte[512];if(HidD_GetProductString(h,bytes,bytes.Length))name=System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();}
                bool primary=d.Id.IndexOf("vid_3554&pid_fa09",StringComparison.OrdinalIgnoreCase)>=0;
                bool g3m=d.Id.IndexOf("vid_320f&pid_706e",StringComparison.OrdinalIgnoreCase)>=0||d.Id.IndexOf("vid_320f&pid_706b",StringComparison.OrdinalIgnoreCase)>=0;
                // Previously identified on this PC; some collections return an empty product string.
                if(String.IsNullOrEmpty(name)&&d.Id.IndexOf("vid_1d57&pid_130f",StringComparison.OrdinalIgnoreCase)>=0)name="2.4G 无线鼠标";
                if(String.IsNullOrEmpty(name)&&primary)name="2.4G Wireless Receiver";
                if(String.IsNullOrEmpty(name)&&g3m)name="G3M PRO";
                if(String.IsNullOrEmpty(name))continue;
                string lower=name.ToLowerInvariant();
                bool wireless=primary||g3m||lower.Contains("wireless")||lower.Contains("receiver")||lower.Contains("dongle")||lower.Contains("2.4")||lower.Contains("lightspeed")||lower.Contains("unifying")||lower.Contains("无线")||lower.Contains("接收器");
                if(!wireless)continue;
                string id=primary?"hi98":Identity(d,"System.Devices.ContainerId","usb:");
                string readerPath=g3m&&Receiver.IsG3mInterface(d.Id)?d.Id:null;
                DeviceEntry prior;if(entries.TryGetValue(id,out prior)){if(readerPath!=null)prior.ReaderPath=readerPath;if(!primary||prior.Connected==true)continue;}
                entries[id]=new DeviceEntry {Id=id,IsG3m=g3m,WindowsChargeState=ChargeStatus.WindowsValue(Property(d,ChargeKey)),ChargeText=ChargeStatus.WindowsText(ChargeStatus.WindowsValue(Property(d,ChargeKey))),Name=primary?"HI98 Pro":name,Transport=g3m?(d.Id.IndexOf("pid_706b",StringComparison.OrdinalIgnoreCase)>=0?"USB 有线":"2.4G"):primary||lower.Contains("2.4")?"2.4G":"USB 无线接收器",Connected=null,Note=primary||g3m?"已适配电量读取":"接收器已插入 · 电量暂不支持",ReaderPath=readerPath};
            }
            result.UsbOk=true;
        }catch {result.Warning+=(result.Warning.Length>0?"；":"")+"USB 接收器扫描暂不可用";}
        try {
            KnownReceivers.Scan(entries);
        }catch {result.UsbOk=false;result.Warning+=(result.Warning.Length>0?"；":"")+"USB 接收器补充扫描暂不可用";}
        if(!entries.ContainsKey("hi98"))entries["hi98"]=new DeviceEntry {Id="hi98",Name="HI98 Pro",Transport="2.4G",Connected=false,Note="未检测到接收器"};
        result.Devices.AddRange(entries.Values);result.Devices.Sort((a,b)=>a.Id==b.Id?0:a.Id=="hi98"?-1:b.Id=="hi98"?1:String.Compare(a.Name,b.Name,StringComparison.CurrentCulture));cached=result;return result;
    }
    public static ConnectionMonitor.Status Read(bool force) {
        LastChargingDiagnostic="";
        var discovered=Get(force);var inventory=new InventorySnapshot {Scanned=discovered.Scanned,BluetoothOk=discovered.BluetoothOk,UsbOk=discovered.UsbOk,Warning=discovered.Warning};
        foreach(var original in discovered.Devices) {
            var d=new DeviceEntry {Id=original.Id,IsG3m=original.IsG3m,ChargeText=original.ChargeText,WindowsChargeState=original.WindowsChargeState,ContainerId=original.ContainerId,BatterySource=original.BatterySource,Name=original.Name,Transport=original.Transport,Note=original.Note,Connected=original.Connected,Percent=original.Percent,ReaderPath=original.ReaderPath};
            if(d.ReaderPath!=null) {
                MouseSample sample;if(!mouseSamples.TryGetValue(d.Id,out sample))mouseSamples[d.Id]=sample=new MouseSample();
                if(force||DateTime.UtcNow>=sample.Next) {
                    try{sample.Value=Receiver.ReadG3m(d.ReaderPath);sample.Sampled=DateTime.Now;sample.Note="设备上报电量";sample.Next=DateTime.UtcNow.AddSeconds(G3mPresentation.Charge(sample.Value,d.Transport=="USB 有线")=="正在充电"?10:30);}
                    catch(IOException){sample.Value=null;sample.Note="设备暂未响应，请唤醒后刷新";sample.Next=DateTime.UtcNow.AddSeconds(10);}
                }
                d.Percent=sample.Value==null?(int?)null:sample.Value.Percent;d.SampledAt=sample.Sampled;d.Connected=sample.Value==null?(bool?)null:true;d.Note=sample.Note;
                d.ChargeText=G3mPresentation.Charge(sample.Value,d.Transport=="USB 有线");
                LastChargingDiagnostic+=d.Transport+": "+(sample.Value==null?"无响应":sample.Value.Percent+"%, flags="+sample.Value.Flags+", "+(d.ChargeText??"无充电标记"))+"; ";
            }
            inventory.Devices.Add(d);
        }
        var visible=G3mPresentation.Visible(inventory.Devices);inventory.Devices.Clear();inventory.Devices.AddRange(visible);
        var primary=ConnectionMonitor.Read(force);primary.ChargeText=ChargeStatus.Keyboard(primary.Battery);primary.Inventory=inventory;return primary;
    }
}
static class G3mPresentation {
    public static string Charge(Receiver.Reading value,bool wired) {
        if(value==null)return null;
        if(wired&&value.Flags==1)return value.Percent>=100?"已充满":"正在充电";
        return value.Flags==2?"正在充电":null;
    }
    public static List<DeviceEntry> Visible(List<DeviceEntry> devices) {
        var wired=devices.FindAll(d=>d.IsG3m&&d.Transport=="USB 有线");
        var radio=devices.FindAll(d=>d.IsG3m&&d.Transport=="2.4G");
        if(wired.Count==1&&radio.Count==1&&wired[0].Connected==true){
            var cable=wired[0];var target=radio[0];target.ChargeText=cable.ChargeText;
            if(cable.ChargeText!=null){target.ChargeViaUsb=true;target.Percent=cable.Percent;target.SampledAt=cable.SampledAt;target.Connected=true;target.Note="USB 电量查询确认 · "+cable.ChargeText;}
        }
        return devices.FindAll(d=>d.Transport!="USB 有线");
    }
    public static void Verify() {
        var r=new Receiver.Reading {Percent=57,Flags=1};if(Charge(r,false)!=null||Charge(r,true)!="正在充电")throw new Exception("Charging flags mapped incorrectly");
        r.Percent=100;if(Charge(r,true)!="已充满")throw new Exception("Full state missing");r.Flags=0;if(Charge(r,true)!=null)throw new Exception("Wired connection mislabelled as charging");
        var devices=new List<DeviceEntry>{new DeviceEntry {Id="radio",IsG3m=true,Transport="2.4G"},new DeviceEntry {Id="wire",IsG3m=true,Transport="USB 有线",Connected=true,Percent=57,ChargeText="正在充电"}};
        var result=Visible(devices);if(result.Count!=1||result[0].Id!="radio"||result[0].ChargeText!="正在充电"||result[0].Percent!=57)throw new Exception("Charging card duplicated or missing");
        var ambiguous=new List<DeviceEntry>{new DeviceEntry {IsG3m=true,Transport="2.4G"},new DeviceEntry {IsG3m=true,Transport="2.4G"},devices[1]};
        foreach(var d in Visible(ambiguous))if(d.ChargeText!=null)throw new Exception("Ambiguous mice charging mixed");
    }
}

static class ConnectionMonitor {
    // Display the device-reported percentage; it is not a calibrated capacity measurement.
    public static string DisplayPercent(Receiver.Reading reading) {return reading==null?"--":reading.Percent+"%";}
    public static string LastRaw="尚无报文";
    public sealed class Status { public string Connection, Detail, ChargeText; public Receiver.Reading Battery; public int Tone; public bool Fresh; public InventorySnapshot Inventory;public DateTime SampleTime;public string Source="设备上报",DeviceName="HI98 Pro"; }
    static Receiver.Reading cached;
    static DateTime nextRead, sampled;
    public static DateTime SampledAt {get{return sampled;}}
    static int failures;
    public static bool IsKeyboardName(string name) {
        if(String.IsNullOrEmpty(name))return false;
        string normalized=name.Replace(" ","").Replace("-","").Replace("_","");
        return normalized.IndexOf("HI98",StringComparison.OrdinalIgnoreCase)>=0;
    }
    public static string ConnectedBluetooth() {
        var inventory=DeviceInventory.Get();if(!inventory.BluetoothOk)throw new IOException("蓝牙扫描失败");
        foreach(var d in inventory.Devices)if(d.Id=="hi98"&&d.Transport=="蓝牙"&&d.Connected==true)return d.Name;
        return null;
    }
    public static Status Read(bool force=false) {
        bool bluetoothChecked=true;
        try {
            string name=ConnectedBluetooth();
            if(name!=null){cached=null;nextRead=DateTime.MinValue;return new Status {Connection="蓝牙 · 已连接",Detail="蓝牙电量暂未适配",Tone=1};}
        } catch {bluetoothChecked=false;}
        try {
            if(!Receiver.IsPresent()) {cached=null;nextRead=DateTime.MinValue;return new Status {Connection=bluetoothChecked?"未检测到连接":"连接状态暂不可用",Detail="检查键盘电源或蓝牙连接",Tone=bluetoothChecked?0:2};}
            if(!force && DateTime.UtcNow<nextRead) {
                return new Status {Connection=cached==null?"2.4G · 连接未确认":"2.4G · 最近读取正常",Detail=cached==null?"休眠或暂未响应 · 右键可刷新":"设备上报 · "+sampled.ToString("HH:mm:ss"),Battery=cached,Tone=cached==null?2:1};
            }
            cached=Receiver.Read();LastRaw=cached.Raw;sampled=DateTime.Now;failures=0;nextRead=DateTime.UtcNow.AddSeconds(ChargeStatus.Keyboard(cached)!=null?10:30);
            return new Status {Connection="2.4G · 接收器已响应",Detail="设备上报 · "+sampled.ToString("HH:mm:ss"),Battery=cached,Tone=1,Fresh=true};
        } catch(IOException e) {
            cached=null;failures++;nextRead=DateTime.UtcNow.AddSeconds(10);
            bool missing=e.Message=="未找到 2.4G 接收器";
            return new Status {Connection=missing?(bluetoothChecked?"未检测到连接":"连接状态暂不可用"):"2.4G · 连接未确认",Detail=missing?(bluetoothChecked?"检查键盘电源或蓝牙连接":"蓝牙检测失败，请稍后刷新"):"休眠或暂未响应 · 右键可刷新",Tone=missing?0:2};
        }
    }
}

static class Receiver {
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint n, ref Iface i);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref Iface i, IntPtr detail, uint len, out uint need, IntPtr d);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string p, uint access, uint share, IntPtr sa, uint create, uint flags, IntPtr t);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps c);
    [DllImport("hid.dll")] static extern bool HidD_FlushQueue(SafeFileHandle h);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);
    [StructLayout(LayoutKind.Sequential)] struct Iface { public int Size; public Guid G; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, Page, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Links, InButtons, InValues, InIndices, OutButtons, OutValues, OutIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    public sealed class Reading { public int Percent; public byte Flags; public string Raw; }
    public static bool IsG3mInterface(string path) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid)return false;IntPtr pp;if(!HidD_GetPreparsedData(h,out pp))return false;
            try{Caps c;return HidP_GetCaps(pp,out c)==0x110000&&c.Page==0xff1c&&c.Usage==0x92&&c.Input==64&&c.Output==64;}finally{HidD_FreePreparsedData(pp);}
        }
    }
    public static Reading ParseG3m(byte[] b,int count) {
        if(b==null||count!=64||b.Length<64||b[0]!=4||b[3]!=0x1a||b[4]!=6||b[5]!=0||b[6]!=0||b[7]!=0||b[8]>100)return null;
        int sum=0;for(int i=3;i<64;i++)sum+=b[i];int checksum=b[1]|(b[2]<<8);
        // This receiver echoes the request checksum (0x20), unlike its updated payload.
        if(checksum!=0x20&&checksum!=(sum&0xffff))return null;
        return new Reading {Percent=b[8],Flags=b[9],Raw=BitConverter.ToString(b,0,64)};
    }
    public static Reading ReadG3m(string path) {
        bool model=path.IndexOf("vid_320f&pid_706e",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("vid_320f&pid_706b",StringComparison.OrdinalIgnoreCase)>=0;
        if(!model||!IsG3mInterface(path))throw new IOException("G3M PRO 接口不匹配");
        using(var handle=CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
            if(handle.IsInvalid)throw new IOException("G3M PRO 接口无法打开");
            using(var stream=new FileStream(handle,FileAccess.ReadWrite,64,true)) {
                HidD_FlushQueue(handle);byte[] query=new byte[64];query[0]=4;query[1]=0x20;query[3]=0x1a;query[4]=6;
                // Battery read only. No profile session, DPI, key mapping or firmware commands.
                try {
                    var write=stream.BeginWrite(query,0,64,null,null);
                    using(var wait=write.AsyncWaitHandle){if(!wait.WaitOne(750)){CancelIoEx(handle,IntPtr.Zero);try{stream.EndWrite(write);}catch(IOException){}catch(OperationCanceledException){}throw new IOException("鼠标查询发送超时");}stream.EndWrite(write);}
                    DateTime end=DateTime.UtcNow.AddMilliseconds(1200);
                    while(DateTime.UtcNow<end) {
                        byte[] reply=new byte[64];var read=stream.BeginRead(reply,0,64,null,null);
                        using(var wait=read.AsyncWaitHandle){if(!wait.WaitOne(Math.Max(1,(int)(end-DateTime.UtcNow).TotalMilliseconds))){CancelIoEx(handle,IntPtr.Zero);try{stream.EndRead(read);}catch(IOException){}catch(OperationCanceledException){}break;}
                            var result=ParseG3m(reply,stream.EndRead(read));if(result!=null)return result;}
                    }
                }catch(OperationCanceledException){throw new IOException("鼠标读取已中断");}
            }
        }
        throw new IOException("鼠标暂未返回有效电量");
    }
    public static bool IsPresent() {
        Guid g;HidD_GetHidGuid(out g);IntPtr set=SetupDiGetClassDevs(ref g,IntPtr.Zero,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1))throw new IOException("无法枚举接收器");
        try {for(uint n=0;;n++) {
            var item=new Iface();item.Size=Marshal.SizeOf(item);
            if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref g,n,ref item))break;
            uint need;SetupDiGetDeviceInterfaceDetail(set,ref item,IntPtr.Zero,0,out need,IntPtr.Zero);
            IntPtr detail=Marshal.AllocHGlobal((int)need);
            try {
                Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                if(!SetupDiGetDeviceInterfaceDetail(set,ref item,detail,need,out need,IntPtr.Zero))continue;
                string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                if(path!=null&&path.IndexOf("vid_3554&pid_fa09",StringComparison.OrdinalIgnoreCase)>=0)return true;
            } finally {Marshal.FreeHGlobal(detail);}
        }} finally {SetupDiDestroyDeviceInfoList(set);}
        return false;
    }
    public static Reading Parse(byte[] b, int count) {
        if(count!=20 || b[0]!=0x13 || b[1]!=0x4a || b[2]!=1 || b[3]!=0 || b[4]!=2 || b[5]>100) return null;
        int sum=0; for(int i=0;i<19;i++) sum+=b[i]; if((byte)sum!=b[19]) return null;
        return new Reading {Percent=b[5], Flags=b[6], Raw=BitConverter.ToString(b,0,20)};
    }
    public static Reading Read() {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set=SetupDiGetClassDevs(ref g,IntPtr.Zero,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1)) throw new IOException("无法枚举接收器");
        bool found=false;
        try {
            for(uint n=0;;n++) {
                var item=new Iface(); item.Size=Marshal.SizeOf(item);
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref g,n,ref item)) break;
                uint need; SetupDiGetDeviceInterfaceDetail(set,ref item,IntPtr.Zero,0,out need,IntPtr.Zero);
                IntPtr detail=Marshal.AllocHGlobal((int)need);
                string path;
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref item,detail,need,out need,IntPtr.Zero)) continue;
                    path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                } finally {Marshal.FreeHGlobal(detail);}
                if(path==null || path.IndexOf("vid_3554&pid_fa09",StringComparison.OrdinalIgnoreCase)<0) continue;
                using(var handle=CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
                    if(handle.IsInvalid) continue;
                    IntPtr pp; if(!HidD_GetPreparsedData(handle,out pp)) continue;
                    Caps caps;
                    try { if(HidP_GetCaps(pp,out caps)!=0x110000) continue; }
                    finally { HidD_FreePreparsedData(pp); }
                    if(caps.Page!=0xff02 || caps.Usage!=2 || caps.Input!=20 || caps.Output!=20) continue;
                    found=true;
                    using(var stream=new FileStream(handle,FileAccess.ReadWrite,20,true)) {
                        byte[] query=new byte[20]; query[0]=0x13; query[1]=0x4a; query[2]=1; query[19]=0x5e;
                        if(!HidD_FlushQueue(handle))throw new IOException("无法清空旧报文，请稍后重试");
                        // Only the documented battery read request. No configuration or firmware writes.
                        var sending=stream.BeginWrite(query,0,query.Length,null,null);
                        using(var wait=sending.AsyncWaitHandle) {
                            if(!wait.WaitOne(1600)) {CancelIoEx(handle,IntPtr.Zero);try{stream.EndWrite(sending);}catch(IOException){}catch(OperationCanceledException){}throw new IOException("电量查询超时");}
                            stream.EndWrite(sending);
                        }
                        DateTime deadline=DateTime.UtcNow.AddMilliseconds(1600);
                        while(DateTime.UtcNow<deadline) {
                            byte[] reply=new byte[20];
                            var pending=stream.BeginRead(reply,0,reply.Length,null,null);
                            using(var wait=pending.AsyncWaitHandle) {
                                int ms=Math.Max(1,(int)(deadline-DateTime.UtcNow).TotalMilliseconds);
                                if(!wait.WaitOne(ms)) {
                                    CancelIoEx(handle,IntPtr.Zero);
                                    try {stream.EndRead(pending);} catch(IOException) {}catch(OperationCanceledException){}
                                    break;
                                }
                                int count=stream.EndRead(pending);
                                var reading=Parse(reply,count); if(reading!=null) return reading;
                            }
                        }
                    }
                }
            }
        } catch(OperationCanceledException){throw new IOException("设备读取已中断，请重新刷新");}
        finally {SetupDiDestroyDeviceInfoList(set);}
        throw new IOException(found?"暂未响应，按一下键盘后刷新":"未找到 2.4G 接收器");
    }
}

sealed class ReadCoordinator {
    readonly Func<bool,ConnectionMonitor.Status> reader;
    Task<ConnectionMonitor.Status> pending;
    public ReadCoordinator(Func<bool,ConnectionMonitor.Status> reader){this.reader=reader;}
    public async Task<ConnectionMonitor.Status> Read(bool force,int timeoutMilliseconds=8000) {
        if(pending!=null&&!pending.IsCompleted)throw new IOException("上次读取仍在结束，请稍后再试");
        pending=Task.Run(()=>reader(force));var current=pending;
        // Observe late faults after a timeout without starting another native read on top of it.
        var observation=current.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        if(await Task.WhenAny(current,Task.Delay(timeoutMilliseconds))!=current)throw new TimeoutException("刷新超时，请稍后重试");
        return await current;
    }
}

sealed class RefreshIconButton : Button {
    public Func<bool> IsSpinning=()=>false;
    public Func<float> Rotation=()=>30;
    bool hover,pressed;
    public RefreshIconButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;}
    protected override bool ShowFocusCues {get{return false;}}
    protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;base.OnMouseDown(e);Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        // Do not invoke the native Button painter: it adds a persistent dotted focus box.
        var g=e.Graphics;g.Clear(Parent==null?Color.FromArgb(20,28,40):Parent.BackColor);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float w=Width;Color background=pressed?Color.FromArgb(52,80,95):hover&&Enabled?Color.FromArgb(44,66,83):Color.FromArgb(32,47,63);
        using(var shape=DrawingStyle.Round(new RectangleF(0,0,Width,Height),8*w/28))using(var fill=new SolidBrush(background))g.FillPath(fill,shape);
        using(var pen=new Pen(Color.FromArgb(109,231,194),Math.Max(1.2f,w/18f))){pen.EndCap=System.Drawing.Drawing2D.LineCap.ArrowAnchor;g.DrawArc(pen,w*.27f,w*.27f,w*.46f,w*.46f,IsSpinning()?Rotation():30,IsSpinning()?255:290);}
    }
}

sealed class OverviewItem {
    public string Id;
    public string Name;
    public string Transport;
    public ConnectionMonitor.Status Status;
    public bool? Online;
}
sealed class DevicePresence {
    sealed class Record {public OverviewItem Item;public long? OfflineSince;public bool Hidden;}
    readonly Dictionary<string,Record> records=new Dictionary<string,Record>();
    public List<OverviewItem> Update(List<OverviewItem> observed,long now,bool complete=true,bool? usbComplete=null,bool? bluetoothComplete=null) {
        var seen=new HashSet<string>();
        foreach(var item in observed){
            string id=item.Id??item.Name;seen.Add(id);Record r;
            if(!records.TryGetValue(id,out r)){r=new Record();records.Add(id,r);}
            r.Item=item;
            if(item.Online==true){r.OfflineSince=null;r.Hidden=false;}
            else if(item.Online==false&&!r.OfflineSince.HasValue)r.OfflineSince=now;
        }
        var visible=new List<OverviewItem>();
        foreach(var pair in records){var r=pair.Value;
            bool scanOk=r.Item.Transport=="蓝牙"?(bluetoothComplete??complete):(usbComplete??complete);
            if(!seen.Contains(pair.Key)&&scanOk){
                if(!r.OfflineSince.HasValue)r.OfflineSince=now;
                r.Item=new OverviewItem {Id=pair.Key,Name=r.Item.Name,Transport=r.Item.Transport,Online=false,Status=new ConnectionMonitor.Status {Connection="未连接",Detail="断开满 1 分钟后自动移除"}};
            }
            if(r.OfflineSince.HasValue&&now-r.OfflineSince.Value>=60000)r.Hidden=true;
            if(!r.Hidden)visible.Add(r.Item);
        }
        visible.Sort((a,b)=>{
            if(a.Id==b.Id)return 0;if(a.Id=="hi98")return -1;if(b.Id=="hi98")return 1;
            bool knownA=a.Status!=null&&a.Status.Battery!=null,knownB=b.Status!=null&&b.Status.Battery!=null;
            if(knownA!=knownB)return knownA?-1:1;
            return String.Compare(a.Name,b.Name,StringComparison.CurrentCulture);
        });
        return visible;
    }
    public static long Now {get{return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency);}}
    public static void Verify() {
        var p=new DevicePresence();var input=new List<OverviewItem>();
        input.Add(new OverviewItem {Id="hi98",Name="Keyboard",Online=true,Status=new ConnectionMonitor.Status()});
        input.Add(new OverviewItem {Id="mouse",Name="Mouse",Online=true,Status=new ConnectionMonitor.Status()});
        if(p.Update(input,0).Count!=2)throw new Exception("New devices missing");
        input[0].Online=false;input.RemoveAt(1);
        if(p.Update(input,1000).Count!=2||p.Update(input,60999).Count!=2||p.Update(input,61000).Count!=0)throw new Exception("Disconnect grace incorrect");
        if(p.Update(input,121000).Count!=0)throw new Exception("Offline device reappeared");
        input[0].Online=true;if(p.Update(input,122000).Count!=1)throw new Exception("Reconnect missing");
        input[0].Online=false;p.Update(input,123000);input[0].Online=true;
        if(p.Update(input,150000).Count!=1||p.Update(input,190000).Count!=1)throw new Exception("Reconnect did not reset timer");
        p=new DevicePresence();input=new List<OverviewItem>{new OverviewItem {Id="usb",Name="USB",Transport="2.4G",Online=true},new OverviewItem {Id="bt",Name="BT",Transport="蓝牙",Online=true}};
        p.Update(input,0);input.Clear();p.Update(input,1000,false,true,false);
        var remaining=p.Update(input,61000,false,true,false);
        if(remaining.Count!=1||remaining[0].Id!="bt")throw new Exception("Bluetooth failure blocked USB removal");
    }
}
sealed class DeviceTray : IDisposable {
    public readonly NotifyIcon Notify=new NotifyIcon();
    Icon artwork;int? percent;int threshold=-1,style=-1;bool light;
    public int? Percent {get{return percent;}}
    public void Update(OverviewItem item,int alert,int design) {
        int? next=item.Status.Battery==null?(int?)null:item.Status.Battery.Percent;bool theme=TrayArtwork.LightTaskbar();
        if(artwork==null||percent!=next||threshold!=alert||style!=design||light!=theme){
            var icon=TrayArtwork.Create(next,alert,design,theme);Notify.Icon=icon;if(artwork!=null)artwork.Dispose();artwork=icon;
            percent=next;threshold=alert;style=design;light=theme;
        }
        string text=item.Name+" · "+(next.HasValue?next+"%":"--")+" · "+item.Status.Connection;
        Notify.Text=text.Length>63?text.Substring(0,60)+"…":text;Notify.Visible=true;
    }
    public void Dispose(){Notify.Visible=false;Notify.Dispose();if(artwork!=null)artwork.Dispose();}
}
sealed class DeviceOverview : ScrollableControl {
    public readonly List<OverviewItem> Cards=new List<OverviewItem>();
    public float ScaleFactor=1;
    public int Threshold=20;
    readonly System.Windows.Forms.Timer chargeAnimation=new System.Windows.Forms.Timer();
    public float ChargePhase;public int ChargeTicks;
    public bool ChargeAnimating {get{return chargeAnimation.Enabled;}}
    public DeviceOverview(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AutoScroll=true;BackColor=Color.FromArgb(20,28,40);TabStop=true;chargeAnimation.Interval=40;chargeAnimation.Tick+=(s,e)=>{ChargePhase=(ChargePhase+.025f)%1;ChargeTicks++;Invalidate();};}
    void UpdateAnimation(){if(Visible&&Cards.Exists(c=>c.Status.ChargeText=="正在充电"))chargeAnimation.Start();else chargeAnimation.Stop();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);UpdateAnimation();}
    protected override void Dispose(bool disposing){if(disposing)chargeAnimation.Dispose();base.Dispose(disposing);}
    public void SetCards(List<OverviewItem> cards){Cards.Clear();Cards.AddRange(cards);UpdateScroll();UpdateAnimation();Invalidate();}
    public void UpdateScroll(){AutoScrollMinSize=new Size(0,(int)(Math.Max(1,Cards.Count)*116*ScaleFactor));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();}
    protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float s=ScaleFactor;
        if(Cards.Count==0){using(var font=new Font("Microsoft YaHei UI",10*s))TextRenderer.DrawText(g,"暂无连接设备\n连接后会自动显示",font,ClientRectangle,Color.FromArgb(143,160,181),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
        using(var nameFont=new Font("Microsoft YaHei UI",10*s,FontStyle.Bold))using(var infoFont=new Font("Microsoft YaHei UI",8*s))using(var percentFont=new Font("Segoe UI",24*s,FontStyle.Bold)) {
            for(int i=0;i<Cards.Count;i++) {
                float y=i*116*s+AutoScrollPosition.Y;int width=ClientSize.Width-2;var item=Cards[i];var status=item.Status;int? percent=status.Battery==null?(int?)null:status.Battery.Percent;
                Color color=percent.HasValue?(percent<=10?Color.FromArgb(244,84,84):Threshold>0&&percent<=Threshold?Color.FromArgb(255,181,91):Color.FromArgb(109,231,194)):Color.FromArgb(143,160,181);
                using(var card=DrawingStyle.Round(new RectangleF(0,y,width,106*s),12*s))using(var fill=new SolidBrush(Color.FromArgb(27,38,53)))g.FillPath(fill,card);
                TextRenderer.DrawText(g,item.Name,nameFont,new Rectangle((int)(14*s),(int)(y+12*s),(int)(width-113*s),(int)(25*s)),Color.FromArgb(226,237,246),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                Color statusColor=status.Tone==1?Color.FromArgb(109,231,194):status.Tone==2?Color.FromArgb(255,181,91):Color.FromArgb(145,163,184);
                TextRenderer.DrawText(g,status.Connection,infoFont,new Rectangle((int)(14*s),(int)(y+39*s),(int)(width-113*s),(int)(20*s)),statusColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                bool charging=status.ChargeText=="正在充电";
                string detail=status.ChargeText!=null?"⚡ "+status.ChargeText+" · "+status.Source.Split(new[]{" · ⚡ "},StringSplitOptions.None)[0]:percent.HasValue?status.Source:(status.Detail??"").Contains("暂不支持")?"电量暂不支持":status.Detail;
                float pulse=(float)(.5+.5*Math.Sin(ChargePhase*Math.PI*2));
                Color detailColor=charging?Color.FromArgb(70+(int)(39*pulse),183+(int)(48*pulse),151+(int)(43*pulse)):Color.FromArgb(139,160,184);
                TextRenderer.DrawText(g,detail,infoFont,new Rectangle((int)(14*s),(int)(y+61*s),width-(int)(28*s),(int)(18*s)),detailColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g,percent.HasValue?percent.Value+"%":"--",percentFont,new Rectangle((int)(width-105*s),(int)(y+17*s),(int)(95*s),(int)(43*s)),color,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                using(var track=DrawingStyle.Round(new RectangleF(14*s,y+88*s,width-28*s,4*s),2*s))using(var fill=new SolidBrush(Color.FromArgb(49,64,83)))g.FillPath(fill,track);
                if(percent.HasValue&&percent>0)using(var bar=DrawingStyle.Round(new RectangleF(14*s,y+88*s,Math.Max(4*s,(width-28*s)*percent.Value/100f),4*s),2*s))using(var fill=new SolidBrush(color))g.FillPath(fill,bar);
                if(charging){
                    var saved=g.Save();using(var track=DrawingStyle.Round(new RectangleF(14*s,y+88*s,width-28*s,4*s),2*s))g.SetClip(track);
                    float sweepWidth=65*s,sweepX=14*s-sweepWidth+(width-28*s+sweepWidth*2)*ChargePhase;
                    using(var shine=new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sweepX,y+88*s,sweepWidth,4*s),Color.Transparent,Color.Transparent,0f)){
                        shine.InterpolationColors=new System.Drawing.Drawing2D.ColorBlend {Colors=new[]{Color.FromArgb(0,255,255,255),Color.FromArgb(220,235,255,248),Color.FromArgb(0,255,255,255)},Positions=new[]{0f,.5f,1f}};
                        g.FillRectangle(shine,sweepX,y+88*s,sweepWidth,4*s);
                    }g.Restore(saved);
                }
            }
        }
    }
}

sealed class BatteryWindow : Form {
    readonly NotifyIcon tray=new NotifyIcon();
    readonly Dictionary<string,DeviceTray> deviceTrays=new Dictionary<string,DeviceTray>();
    readonly Label value=new Label(), connection=new Label(), state=new Label(), time=new Label();
    readonly Label batteryHeading=new Label(),connectionHeading=new Label();
    readonly RefreshIconButton refresh=new RefreshIconButton();
    readonly DevicePicker devicePicker=new DevicePicker();
    readonly DeviceOverview overview=new DeviceOverview();
    readonly DevicePresence presence=new DevicePresence();
    ConnectionMonitor.Status lastPrimary;
    bool bindingDevices;
    readonly ToolTip hints=new ToolTip();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
    readonly ReadCoordinator reads;
    float refreshAngle=30;
    int animationTicks;
    int readTimeout=8000;
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly string settings=AppStorage.Position;
    readonly Preferences prefs=Preferences.Load();
    bool busy, exiting; Icon currentIcon; Point drag; DateTime lastSuccess; int? iconValue; int iconThreshold=-1,iconStyle=-1;bool iconLight;
    string iconSignature;
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run",RunName="HI98ProBattery";
    ToolStripMenuItem startup,locked,trayOnly;
    float displayScale=1;
    Color accent=Color.FromArgb(109,231,194);
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ClassStyle|=0x20000;return p;}}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float s=displayScale;
        using(var border=DrawingStyle.Round(new RectangleF(.5f,.5f,ClientSize.Width-1,ClientSize.Height-1),18*s))using(var pen=new Pen(Color.FromArgb(52,65,84)))g.DrawPath(pen,border);
        using(var badge=DrawingStyle.Round(new RectangleF(22*s,22*s,17*s,12*s),3*s))using(var pen=new Pen(accent,1.5f*s))g.DrawPath(pen,badge);
        using(var brush=new SolidBrush(accent)){g.FillRectangle(brush,40*s,26*s,2*s,4*s);g.FillRectangle(brush,26*s,26*s,9*s,4*s);}
    }
    bool StartupEnabled() {using(var k=Registry.CurrentUser.OpenSubKey(RunKey))return k!=null&&String.Equals(k.GetValue(RunName) as string,"\""+Application.ExecutablePath+"\" --tray",StringComparison.OrdinalIgnoreCase);}
    void SavePreferences() {try{prefs.Save();}catch{tray.ShowBalloonTip(3000,"设置未保存","程序目录不可写，请把工具放在可写文件夹。",ToolTipIcon.Warning);}}
    void ApplyAppearance() {
        int scale=prefs.Scale==80?80:prefs.Scale==125?125:100;
        float ratio=scale/100f;displayScale=ratio;
        int rows=Math.Min(3,Math.Max(1,overview.Cards.Count));int bodyHeight=rows*116;
        SuspendLayout();ClientSize=new Size((int)(344*ratio),(int)((56+bodyHeight+38)*ratio));
        int[,] boxes={{49,18,208,25},{304,15,24,28},{24,83,290,65},{30,227,284,24},{188,68,126,20},{22,282,290,20},{269,17,28,28},{30,67,125,22},{30,199,284,22}};
        float[] sizes={10,15,36,10,8,8,10,9,9};
        for(int i=0;i<9;i++) {
            var c=Controls[i];c.SetBounds((int)(boxes[i,0]*ratio),(int)(boxes[i,1]*ratio),(int)(boxes[i,2]*ratio),(int)(boxes[i,3]*ratio));
            var old=c.Font;c.Font=new Font(old.FontFamily,sizes[i]*ratio,old.Style);old.Dispose();
        }
        overview.ScaleFactor=ratio;overview.Threshold=prefs.Threshold;overview.SetBounds((int)(16*ratio),(int)(56*ratio),(int)(312*ratio),(int)(bodyHeight*ratio));overview.UpdateScroll();
        time.SetBounds((int)(22*ratio),(int)((56+bodyHeight+7)*ratio),(int)(300*ratio),(int)(22*ratio));
        Opacity=Math.Max(60,Math.Min(100,prefs.Opacity))/100.0;
        using(var round=DrawingStyle.Round(new RectangleF(0,0,Width,Height),18*ratio)){var oldRegion=Region;Region=new Region(round);if(oldRegion!=null)oldRegion.Dispose();}
        using(var round=DrawingStyle.Round(new RectangleF(0,0,refresh.Width,refresh.Height),8*ratio)){var oldRegion=refresh.Region;refresh.Region=new Region(round);if(oldRegion!=null)oldRegion.Dispose();}
        var area=Screen.FromPoint(Location).WorkingArea;
        Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        ResumeLayout();Invalidate();
    }
    public BatteryWindow(Func<bool,ConnectionMonitor.Status> reader=null) {
        reads=new ReadCoordinator(reader??(force=>DeviceInventory.Read(force)));
        Text="无线设备电量面板"; FormBorderStyle=FormBorderStyle.None;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(344,312); BackColor=Color.FromArgb(20,28,40);DoubleBuffered=true;
        ForeColor=Color.White; TopMost=true; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual;
        Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-364,Screen.PrimaryScreen.WorkingArea.Bottom-332);
        try {var p=File.ReadAllText(settings).Split(','); var point=new Point(int.Parse(p[0]),int.Parse(p[1]));
            foreach(var screen in Screen.AllScreens) if(screen.WorkingArea.IntersectsWith(new Rectangle(point,Size))) {Location=point;break;}
        } catch {}
        if(prefs.X!=-1&&prefs.Y!=-1)foreach(var screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(new Rectangle(prefs.X,prefs.Y,Width,Height))){Location=new Point(prefs.X,prefs.Y);break;}
        var title=new Label {Text="设备总览",Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold),ForeColor=Color.FromArgb(207,220,235)};
        devicePicker.Items.Add(new DeviceEntry {Id="hi98",Name="HI98 Pro",Transport="2.4G"});devicePicker.SelectedIndex=0;
        devicePicker.SelectedIndexChanged+=(s,e)=>{if(!bindingDevices&&lastPrimary!=null)ShowStatus(lastPrimary);};
        var hide=new Label {Text="×",Location=new Point(274,13),Size=new Size(26,28),Font=new Font("Segoe UI",17),Cursor=Cursors.Hand};
        hide.Click+=(s,e)=>Hide();
        hide.AccessibleName="收起到托盘";hints.SetToolTip(hide,"收起到托盘 · 双击托盘可重新显示");
        hide.MouseEnter+=(s,e)=>hide.ForeColor=accent;hide.MouseLeave+=(s,e)=>hide.ForeColor=Color.White;
        value.Text="--"; value.SetBounds(16,44,260,64); value.Font=new Font("Segoe UI",34,FontStyle.Bold); value.ForeColor=Color.FromArgb(96,222,170);
        connection.SetBounds(21,113,282,24);connection.Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold);connection.Text="正在检测连接…";
        state.SetBounds(21,141,282,23); state.Font=new Font("Microsoft YaHei UI",9); state.Text="正在读取电量…";
        time.SetBounds(21,170,282,20); time.Font=new Font("Microsoft YaHei UI",8); time.ForeColor=Color.FromArgb(133,152,177);
        state.ForeColor=Color.FromArgb(133,152,177);
        refresh.Text="";refresh.AccessibleName="刷新全部设备";refresh.Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold);refresh.FlatStyle=FlatStyle.Flat;refresh.FlatAppearance.BorderSize=0;
        refresh.BackColor=Color.FromArgb(32,47,63);refresh.ForeColor=Color.FromArgb(109,231,194);refresh.Cursor=Cursors.Hand;refresh.UseVisualStyleBackColor=false;
        refresh.FlatAppearance.MouseOverBackColor=Color.FromArgb(44,66,83);refresh.FlatAppearance.MouseDownBackColor=Color.FromArgb(52,80,95);
        refresh.IsSpinning=()=>busy;refresh.Rotation=()=>refreshAngle;
        animation.Interval=30;animation.Tick+=(s,e)=>{refreshAngle=(refreshAngle+12)%360;animationTicks++;refresh.Invalidate();};
        refresh.Click+=async(s,e)=>await RefreshBattery(true);hints.SetToolTip(refresh,"立即重新读取设备电量；自动采样间隔为 30 秒");
        batteryHeading.Text="电量";connectionHeading.Text="连接状态";
        foreach(var heading in new[]{batteryHeading,connectionHeading}){heading.Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold);heading.ForeColor=Color.FromArgb(158,179,202);heading.BackColor=Color.FromArgb(27,38,53);}
        foreach(var label in new[]{value,connection,state})label.BackColor=Color.FromArgb(27,38,53);
        state.TextAlign=ContentAlignment.MiddleRight;state.AutoEllipsis=true;connection.AutoEllipsis=true;
        Controls.AddRange(new Control[]{title,hide,value,connection,state,time,refresh,batteryHeading,connectionHeading,overview});
        foreach(var label in new[]{value,connection,state,batteryHeading,connectionHeading})label.Visible=false;
        hints.SetToolTip(overview,"全部设备集中显示 · 设备较多时可滚动查看");
        menu.Renderer=new DarkMenuRenderer();menu.Font=new Font("Microsoft YaHei UI",9);menu.ShowImageMargin=true;
        menu.Items.Add("显示 / 隐藏悬浮窗",null,(s,e)=>{if(Visible)Hide();else {Show();Activate();}});
        menu.Items.Add("立即刷新",null,async(s,e)=>await RefreshBattery(true));
        locked=new ToolStripMenuItem("锁定悬浮窗位置"){Checked=prefs.Locked,CheckOnClick=true};locked.CheckedChanged+=(s,e)=>{prefs.Locked=locked.Checked;SavePreferences();};menu.Items.Add(locked);
        var scaleMenu=new ToolStripMenuItem("悬浮窗大小");
        foreach(int size in new[]{80,100,125}) {int selected=size;var option=new ToolStripMenuItem(size+"%"){Checked=prefs.Scale==size};option.Click+=(s,e)=>{prefs.Scale=selected;foreach(ToolStripMenuItem i in scaleMenu.DropDownItems)i.Checked=i==option;ApplyAppearance();SavePreferences();};scaleMenu.DropDownItems.Add(option);}menu.Items.Add(scaleMenu);
        var opacityMenu=new ToolStripMenuItem("透明度");
        foreach(int level in new[]{60,80,100}) {int selected=level;var option=new ToolStripMenuItem(level+"%"){Checked=prefs.Opacity==level};option.Click+=(s,e)=>{prefs.Opacity=selected;foreach(ToolStripMenuItem i in opacityMenu.DropDownItems)i.Checked=i==option;ApplyAppearance();SavePreferences();};opacityMenu.DropDownItems.Add(option);}menu.Items.Add(opacityMenu);
        var alertMenu=new ToolStripMenuItem("低电量提醒");
        foreach(int level in new[]{0,10,20,30}) {int selected=level;var option=new ToolStripMenuItem(level==0?"关闭提醒":level+"%"){Checked=prefs.Threshold==level};option.Click+=(s,e)=>{prefs.Threshold=selected;prefs.LowAlerted=false;foreach(ToolStripMenuItem i in alertMenu.DropDownItems)i.Checked=i==option;SavePreferences();};alertMenu.DropDownItems.Add(option);}menu.Items.Add(alertMenu);
        trayOnly=new ToolStripMenuItem("启动时仅显示托盘"){Checked=prefs.TrayOnly,CheckOnClick=true};trayOnly.CheckedChanged+=(s,e)=>{prefs.TrayOnly=trayOnly.Checked;SavePreferences();};menu.Items.Add(trayOnly);
        startup=new ToolStripMenuItem("开机启动（登录后）"){Checked=StartupEnabled()};
        startup.Click+=(s,e)=>{try {using(var k=Registry.CurrentUser.CreateSubKey(RunKey)){if(StartupEnabled())k.DeleteValue(RunName,false);else k.SetValue(RunName,"\""+Application.ExecutablePath+"\" --tray");}startup.Checked=StartupEnabled();}catch{MessageBox.Show("无法修改开机启动设置。","HI98 Pro");}};menu.Items.Add(startup);
        menu.Items.Add("查看诊断信息",null,(s,e)=>MessageBox.Show("无线设备电量面板 v2.12\n\n蓝牙：显示 Windows 提供的电量；未提供则显示 --。\nUSB 无线接收器：只有已适配型号可以读取电量。\n2.4G 接收器已插入，不代表外设一定在线。\n\nHI98 Pro 最近原始报文：\n"+ConnectionMonitor.LastRaw,"读取状态"));
        if(prefs.TrayDesignVersion<1){if(prefs.TrayStyle==0)prefs.TrayStyle=3;prefs.TrayDesignVersion=1;}
        var trayStyle=new ToolStripMenuItem("托盘显示");string[] styleNames={"大号数字","数字 + 电量条","电池图标","圆角电量牌（推荐）"};
        for(int i=0;i<styleNames.Length;i++){int selected=i;var option=new ToolStripMenuItem(styleNames[i]){Checked=prefs.TrayStyle==i};option.Click+=(s,e)=>{prefs.TrayStyle=selected;foreach(ToolStripMenuItem item in trayStyle.DropDownItems)item.Checked=item==option;SyncDeviceTrays();SavePreferences();};trayStyle.DropDownItems.Add(option);}menu.Items.Add(trayStyle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出",null,(s,e)=>{exiting=true;Close();});
        ContextMenuStrip=menu; tray.ContextMenuStrip=menu; tray.Text="无线设备：正在扫描";
        SetIcon(null); tray.Visible=true; tray.DoubleClick+=(s,e)=>{Show();Activate();};
        foreach(Control c in Controls) if(c!=hide&&c!=refresh&&c!=overview) AttachDrag(c); AttachDrag(this);
        timer.Interval=10000; timer.Tick+=async(s,e)=>await RefreshBattery();
        ApplyAppearance();
        SavePreferences();
        Shown+=async(s,e)=>{if(prefs.TrayOnly||Array.IndexOf(Environment.GetCommandLineArgs(),"--tray")>=0)Hide();await RefreshBattery();if(!exiting)timer.Start();};
        FormClosing+=(s,e)=>{if(!exiting){e.Cancel=true;Hide();return;} timer.Stop();animation.Stop();foreach(var item in deviceTrays.Values)item.Dispose();deviceTrays.Clear();tray.Visible=false;tray.Dispose();if(currentIcon!=null)currentIcon.Dispose();menu.Dispose();timer.Dispose();animation.Dispose();hints.Dispose();};
    }
    void AttachDrag(Control c) {
        c.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left&&!prefs.Locked)drag=Cursor.Position;};
        c.MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left&&!prefs.Locked){Point next=Cursor.Position;Location=new Point(Left+next.X-drag.X,Top+next.Y-drag.Y);drag=next;}};
        c.MouseUp+=(s,e)=>{if(!prefs.Locked){prefs.X=Left;prefs.Y=Top;SavePreferences();}};
    }
    void SetIcon(int? percentage) {
        iconSignature=null;
        bool light=TrayArtwork.LightTaskbar();
        if(currentIcon!=null&&iconValue==percentage&&iconThreshold==prefs.Threshold&&iconStyle==prefs.TrayStyle&&iconLight==light)return;
        iconValue=percentage;iconThreshold=prefs.Threshold;iconStyle=prefs.TrayStyle;iconLight=light;
        Icon next=TrayArtwork.Create(percentage,prefs.Threshold,prefs.TrayStyle,light);
        tray.Icon=next;if(currentIcon!=null)currentIcon.Dispose();currentIcon=next;
    }
    string SelectedName {get{var d=devicePicker.SelectedItem as DeviceEntry;return d==null?"HI98 Pro":d.Name;}}
    void TrayText(string text){tray.Text=text.Length>63?text.Substring(0,60)+"…":text;}
    void SyncDeviceTrays() {
        foreach(var item in deviceTrays.Values)item.Dispose();deviceTrays.Clear();
        var cards=overview.Cards;int? first=cards.Count==0||cards[0].Status.Battery==null?(int?)null:cards[0].Status.Battery.Percent;
        int? second=cards.Count<2||cards[1].Status.Battery==null?(int?)null:cards[1].Status.Battery.Percent;
        bool light=TrayArtwork.LightTaskbar();
        string signature=first+"/"+second+"/"+(cards.Count>=2)+"/"+prefs.Threshold+"/"+prefs.TrayStyle+"/"+light;
        if(currentIcon==null||iconSignature!=signature){
            Icon next=TrayArtwork.Create(first,prefs.Threshold,prefs.TrayStyle,light,second,cards.Count>=2);
            tray.Icon=next;if(currentIcon!=null)currentIcon.Dispose();currentIcon=next;iconValue=first;iconSignature=signature;
        }
        var labels=new List<string>();for(int i=0;i<cards.Count;i++)labels.Add((i==0?"上：":i==1?"下：":"")+cards[i].Name+" "+(cards[i].Status.Battery==null?"--":cards[i].Status.Battery.Percent+"%")+(cards[i].Status.Source.Contains("充电")||cards[i].Status.Source.Contains("充满")?" "+cards[i].Status.Source.Replace("设备上报 · ",""):""));
        TrayText(cards.Count==0?"无线设备：暂无连接设备":String.Join(" · ",labels.ToArray()));
    }
    void BindDevices(InventorySnapshot inventory) {
        if(inventory==null)return;bindingDevices=true;
        try {
            devicePicker.BeginUpdate();devicePicker.Items.Clear();int selected=0;
            foreach(var d in inventory.Devices){if(d.Id==prefs.SelectedDeviceId)selected=devicePicker.Items.Count;devicePicker.Items.Add(d);}
            if(devicePicker.Items.Count>0)devicePicker.SelectedIndex=selected;
            hints.SetToolTip(devicePicker,"自动发现 "+inventory.Devices.Count+" 个设备 · 点击切换"+(inventory.Warning.Length==0?"":"\n"+inventory.Warning));
        } finally {devicePicker.EndUpdate();bindingDevices=false;}
    }
    ConnectionMonitor.Status StatusForDevice(ConnectionMonitor.Status primary,DeviceEntry entry) {
        var result=DeviceStatusCore(primary,entry);
        result.ChargeText=entry.Connected==false?null:entry.Id=="hi98"&&entry.Transport!="蓝牙"?ChargeStatus.Keyboard(primary.Battery)??entry.ChargeText:entry.ChargeText;
        result.Source=result.Source.Split(new[]{" · ⚡ "},StringSplitOptions.None)[0]+(result.ChargeText==null?"":" · ⚡ "+result.ChargeText);
        return result;
    }
    ConnectionMonitor.Status DeviceStatusCore(ConnectionMonitor.Status primary,DeviceEntry entry) {
        if(entry==null)return primary;
        if(entry.Id=="hi98") {
            if(entry.Transport=="蓝牙"&&entry.Connected==true&&entry.Percent.HasValue)return new ConnectionMonitor.Status {Connection="蓝牙 · 已连接",Detail=entry.Note,Battery=new Receiver.Reading {Percent=entry.Percent.Value},Tone=1,SampleTime=primary.Inventory.Scanned,DeviceName=entry.Name,Source=entry.BatterySource};
            return primary;
        }
        if(entry.Transport=="蓝牙")return new ConnectionMonitor.Status {Connection=entry.Connected==true?"蓝牙 · 已连接":entry.Connected==false?"蓝牙 · 未连接":"蓝牙 · 状态未知",Detail=entry.Note,Battery=entry.Connected==true&&entry.Percent.HasValue?new Receiver.Reading {Percent=entry.Percent.Value}:null,Tone=entry.Connected==true?1:0,SampleTime=primary.Inventory.Scanned,DeviceName=entry.Name,Source=entry.BatterySource};
        if(entry.ReaderPath!=null)return new ConnectionMonitor.Status {Connection=entry.ChargeViaUsb?"2.4G · 接收器已插入":entry.Transport+(entry.Percent.HasValue?" · 设备已响应":" · 连接未确认"),Detail=entry.Note,Battery=entry.Percent.HasValue?new Receiver.Reading {Percent=entry.Percent.Value}:null,Tone=entry.Percent.HasValue?1:2,SampleTime=entry.SampledAt,DeviceName=entry.Name,Source="设备上报"+(entry.ChargeText==null?"":" · ⚡ "+entry.ChargeText)};
        return new ConnectionMonitor.Status {Connection=entry.Transport+" · 接收器已插入",Detail=entry.Note,Tone=2,DeviceName=entry.Name};
    }
    void ShowStatus(ConnectionMonitor.Status primary) {
        var result=primary;
        connection.Text=result.Connection;state.Text=result.Detail;
        hints.SetToolTip(state,result.Detail);hints.SetToolTip(connection,result.Connection);
        connection.ForeColor=result.Tone==1?Color.FromArgb(96,222,170):result.Tone==2?Color.FromArgb(255,181,91):Color.FromArgb(151,164,184);
        time.Text="检测于 "+DateTime.Now.ToString("HH:mm:ss");var r=result.Battery;
        if(r==null){value.Text="--";state.Text=(result.Detail??"").Contains("暂不支持")?"电量暂不支持":"暂不可读取";value.ForeColor=Color.FromArgb(151,164,184);accent=value.ForeColor;RenderOverview(primary);return;}
        lastSuccess=DateTime.Now;value.Text=ConnectionMonitor.DisplayPercent(r);
        time.Text=(result.Source=="Windows 上报"?"系统读取于 ":"采样于 ")+(result.SampleTime==default(DateTime)?ConnectionMonitor.SampledAt:result.SampleTime).ToString("HH:mm:ss");
        value.ForeColor=r.Percent<=10?Color.FromArgb(244,84,84):prefs.Threshold>0&&r.Percent<=prefs.Threshold?Color.FromArgb(255,181,91):Color.FromArgb(109,231,194);
        accent=value.ForeColor;state.Text=result.Source;
        hints.SetToolTip(state,result.Detail);RenderOverview(primary);
        if(result.Fresh){bool old=prefs.LowAlerted;if(prefs.ShouldNotify(r.Percent))tray.ShowBalloonTip(5000,"键盘电量偏低","HI98 Pro 剩余 "+r.Percent+"%，请及时充电。",ToolTipIcon.Warning);if(old!=prefs.LowAlerted)SavePreferences();}
    }
    void RenderOverview(ConnectionMonitor.Status primary) {
        var cards=new List<OverviewItem>();
        if(primary.Inventory!=null)foreach(var entry in primary.Inventory.Devices){var status=StatusForDevice(primary,entry);bool? online=entry.Transport=="蓝牙"?entry.Connected:entry.Id=="hi98"?(bool?)(primary.Tone==1):entry.ReaderPath!=null?(bool?)entry.Percent.HasValue:true;cards.Add(new OverviewItem {Id=entry.Id,Name=entry.Name,Transport=entry.Transport,Status=status,Online=online});}
        if(primary.Inventory==null)cards.Add(new OverviewItem {Id="hi98",Name="HI98 Pro",Status=primary,Online=primary.Tone==1});
        cards=presence.Update(cards,DevicePresence.Now,primary.Inventory==null||String.IsNullOrEmpty(primary.Inventory.Warning),primary.Inventory==null?(bool?)null:primary.Inventory.UsbOk,primary.Inventory==null?(bool?)null:primary.Inventory.BluetoothOk);
        overview.SetCards(cards);ApplyAppearance();
        int? lowest=null;var summaries=new List<string>();
        foreach(var card in cards){var b=card.Status.Battery;summaries.Add(card.Name+" "+(b==null?"--":b.Percent+"%"));if(b!=null&&(!lowest.HasValue||b.Percent<lowest.Value))lowest=b.Percent;}
        SyncDeviceTrays();
        accent=lowest.HasValue?lowest<=10?Color.FromArgb(244,84,84):prefs.Threshold>0&&lowest<=prefs.Threshold?Color.FromArgb(255,181,91):Color.FromArgb(109,231,194):Color.FromArgb(151,164,184);
        time.Text="共 "+cards.Count+" 个设备 · 检测于 "+DateTime.Now.ToString("HH:mm:ss");
        hints.SetToolTip(overview,"单个托盘图标：上行为第一台，下行为第二台 · "+(primary.Inventory==null?"":primary.Inventory.Warning));Invalidate();
    }
    async Task RefreshBattery(bool force=false) {
        if(busy||exiting)return;busy=true;refresh.Enabled=false;refreshAngle=30;animation.Start();refresh.Invalidate();hints.SetToolTip(refresh,"正在刷新…");
        try {
            var result=await reads.Read(force,readTimeout); if(exiting||IsDisposed)return;
            lastPrimary=result;BindDevices(result.Inventory);ShowStatus(result);
        } catch(Exception e) {
            if(exiting||IsDisposed)return;
            value.Text="--";value.ForeColor=Color.FromArgb(151,164,184);state.Text=e is IOException||e is TimeoutException?e.Message:"读取失败，请稍后刷新";
            hints.SetToolTip(state,state.Text);
            connection.Text="连接状态暂不可用";
            time.Text=lastSuccess==default(DateTime)?"右键可刷新 · 键盘休眠时可能无响应":"上次成功读取 "+lastSuccess.ToString("HH:mm:ss");
            var failed=new List<OverviewItem>();foreach(var card in overview.Cards)failed.Add(new OverviewItem {Id=card.Id,Name=card.Name,Status=new ConnectionMonitor.Status {Connection="连接状态暂不可用",Detail=state.Text}});
            if(failed.Count==0)failed.Add(new OverviewItem {Name="设备扫描",Status=new ConnectionMonitor.Status {Connection="扫描未完成",Detail=state.Text}});
            overview.SetCards(failed);ApplyAppearance();time.Text="刷新失败 · 点击右上角重试";
            SyncDeviceTrays();
            accent=Color.FromArgb(151,164,184);Invalidate();
        } finally {busy=false;if(!exiting&&!IsDisposed){animation.Stop();refreshAngle=30;refresh.Enabled=true;refresh.Invalidate();hints.SetToolTip(refresh,"刷新电量和连接状态");}}
    }
    public async Task Snapshot(string path) { while(busy)await Task.Delay(50);await RefreshBattery();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(path);}exiting=true;Close(); }
    public async Task DeviceSelectionCheck(string directory) {
        while(busy)await Task.Delay(20);Directory.CreateDirectory(directory);
        var inventory=new InventorySnapshot {Scanned=DateTime.Now,BluetoothOk=true};
        inventory.Devices.Add(new DeviceEntry {Id="hi98",Name="HI98 Pro",Transport="2.4G"});
        inventory.Devices.Add(new DeviceEntry {Id="fixture:connected",Name="TEST A 蓝牙耳机",Transport="蓝牙",Connected=true,Percent=73,Note="Windows 上报"});
        inventory.Devices.Add(new DeviceEntry {Id="fixture:offline",Name="TEST B 已断开设备",Transport="蓝牙",Connected=false,Percent=88,Note="已配对 · 未连接"});
        inventory.Devices.Add(new DeviceEntry {Id="fixture:usb",Name="TEST C 接收器",Transport="2.4G",Note="接收器已插入 · 电量暂不支持"});
        lastPrimary=new ConnectionMonitor.Status {Connection="2.4G · 接收器已响应",Battery=new Receiver.Reading {Percent=98},Inventory=inventory,Tone=1};BindDevices(inventory);
        ShowStatus(lastPrimary);
        foreach(int size in new[]{16,32,128})using(var preview=TrayArtwork.Render(size,98,20,3,false,73,true))preview.Save(Path.Combine(directory,"combined-tray-"+size+".png"));
        if(overview.Cards.Count!=4||overview.Cards[0].Status.Battery.Percent!=98||overview.Cards[1].Status.Battery.Percent!=73)throw new Exception("Overview values mixed");
        if(overview.Cards[2].Status.Battery!=null||overview.Cards[3].Status.Battery!=null)throw new Exception("Disconnected or unknown battery fabricated");
        if(iconValue!=98||deviceTrays.Count!=0||!tray.Text.Contains("73%")||devicePicker.Parent!=null||!overview.VerticalScroll.Visible)throw new Exception("Independent trays or scrolling failed");
        using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,ClientRectangle);b.Save(Path.Combine(directory,"overview-top.png"));}
        overview.AutoScrollPosition=new Point(0,116);overview.Invalidate();await Task.Delay(50);
        using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,ClientRectangle);b.Save(Path.Combine(directory,"overview-scrolled.png"));}
        inventory.Devices.RemoveAt(3);inventory.Devices.RemoveAt(2);ShowStatus(lastPrimary);
        if(deviceTrays.Count!=0||iconValue!=98||!tray.Text.Contains("73%"))throw new Exception("Removed device tray not disposed");
        inventory.Devices[1].Percent=72;ShowStatus(lastPrimary);
        if(iconValue!=98||!tray.Text.Contains("72%"))throw new Exception("Per-device update mixed");
        prefs.TrayStyle=(prefs.TrayStyle+1)%4;SyncDeviceTrays();
        var cachedIcon=currentIcon;SyncDeviceTrays();if(!Object.ReferenceEquals(cachedIcon,currentIcon))throw new Exception("Unchanged tray artwork recreated");
        inventory.Devices[1].Percent=71;ShowStatus(lastPrimary);if(Object.ReferenceEquals(cachedIcon,currentIcon))throw new Exception("Changed second value did not redraw tray");
        var savedCards=new List<OverviewItem>(overview.Cards);
        var chargingCards=new List<OverviewItem>{new OverviewItem {Id="fixture:keyboard",Name="TEST 键盘",Status=new ConnectionMonitor.Status {Connection="TEST · 已连接",Battery=new Receiver.Reading {Percent=100},ChargeText="正在充电"}},new OverviewItem {Id="fixture:headset",Name="TEST 耳机",Status=new ConnectionMonitor.Status {Connection="TEST · 蓝牙已连接",Battery=new Receiver.Reading {Percent=73},ChargeText="正在充电"}}};
        overview.SetCards(chargingCards);ApplyAppearance();int chargeTicks=overview.ChargeTicks;await Task.Delay(180);
        if(!overview.ChargeAnimating||overview.ChargeTicks-chargeTicks<2)throw new Exception("Shared charging animation did not advance");
        for(int frame=0;frame<2;frame++){overview.ChargePhase=frame==0?.25f:.6f;using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,ClientRectangle);b.Save(Path.Combine(directory,"charge-frame-"+frame+".png"));}}
        overview.Visible=false;if(overview.ChargeAnimating)throw new Exception("Hidden panel kept charging animation running");overview.Visible=true;
        if(!overview.ChargeAnimating)throw new Exception("Charging animation did not resume");
        chargingCards[0].Status.ChargeText="已充满";chargingCards[1].Status.ChargeText=null;overview.SetCards(chargingCards);
        if(overview.ChargeAnimating)throw new Exception("Full or unknown charge state animated");overview.SetCards(savedCards);
        File.WriteAllText(Path.Combine(directory,"verification.txt"),"PASS: single combined tray, top/bottom percentages and tooltip updates; unknown suppression, removed devices and scrolling. Bluetooth simulated.");exiting=true;Close();
    }
    async Task WaitIdle() {
        DateTime deadline=DateTime.UtcNow.AddSeconds(3);
        while(busy){if(DateTime.UtcNow>deadline)throw new Exception("UI remained busy");await Task.Delay(10);}
        if(!refresh.Enabled||animation.Enabled)throw new Exception("Refresh state was not restored");
    }
    public async Task AnimationCheck(string path,Action<int> scenario,Func<int> calls) {
        await WaitIdle();readTimeout=1000;scenario(1);int ticks=animationTicks;
        refresh.PerformClick();await Task.Delay(65);
        using(var frame=new Bitmap(refresh.Width,refresh.Height)){refresh.DrawToBitmap(frame,refresh.ClientRectangle);frame.Save(path+".frame1.png");}
        await Task.Delay(65);
        using(var frame=new Bitmap(refresh.Width,refresh.Height)){refresh.DrawToBitmap(frame,refresh.ClientRectangle);frame.Save(path+".frame2.png");}
        await WaitIdle();
        if(animationTicks-ticks<3)throw new Exception("Animation did not advance while reading");
        scenario(2);readTimeout=80;refresh.PerformClick();await WaitIdle();
        if(state.Text.IndexOf("超时")<0)throw new Exception("Timeout not reported");
        ticks=animationTicks;await Task.Delay(100);
        if(animationTicks!=ticks)throw new Exception("Animation continued after timeout");
        int before=calls();refresh.PerformClick();await WaitIdle();
        if(calls()!=before||state.Text.IndexOf("上次读取")<0)throw new Exception("Overlapping native read was started");
        await Task.Delay(650);scenario(3);readTimeout=1000;refresh.PerformClick();await WaitIdle();
        if(state.Text.IndexOf("测试读取异常")<0)throw new Exception("Exception state not restored");
        scenario(1);refresh.PerformClick();await WaitIdle();
        if(connection.Text!="TEST · completed")throw new Exception("Refresh did not recover after failure");
        refresh.Focus();using(var frame=new Bitmap(refresh.Width,refresh.Height)){refresh.DrawToBitmap(frame,refresh.ClientRectangle);frame.Save(path+".focused.png");}
        File.WriteAllText(path,"PASS: rotating frames during delayed read; overall timeout stops animation and restores button; late native read is not overlapped; exceptions restore state; retry succeeds. Focused custom button rendered without native focus border. Simulated reader, no hardware state changes.");
        exiting=true;Close();
    }
    public async Task VisualCheck(string directory) {
        while(busy)await Task.Delay(50);await RefreshBattery(true);
        Directory.CreateDirectory(directory);
        refresh.PerformClick();
        if(!busy||refresh.Enabled)throw new Exception("Refresh button did not start a request");
        while(busy)await Task.Delay(30);
        if(!refresh.Enabled||refresh.Text!="")throw new Exception("Refresh button did not recover after the request");
        locked.PerformClick();if(Preferences.Load().Locked!=locked.Checked)throw new Exception("Lock preference persistence failed");locked.PerformClick();
        var opacityMenu=(ToolStripMenuItem)menu.Items[4];((ToolStripMenuItem)opacityMenu.DropDownItems[1]).PerformClick();
        if(Preferences.Load().Opacity!=80||Math.Abs(Opacity-0.8)>0.01)throw new Exception("Opacity preference failed");
        ((ToolStripMenuItem)opacityMenu.DropDownItems[2]).PerformClick();
        var alertMenu=(ToolStripMenuItem)menu.Items[5];if(alertMenu.Enabled){((ToolStripMenuItem)alertMenu.DropDownItems[3]).PerformClick();
        if(Preferences.Load().Threshold!=30)throw new Exception("Threshold preference failed");
        ((ToolStripMenuItem)alertMenu.DropDownItems[2]).PerformClick();}
        foreach(int scale in new[]{80,100,125}) {
            var scaleMenu=(ToolStripMenuItem)menu.Items[3];((ToolStripMenuItem)scaleMenu.DropDownItems[scale==80?0:scale==100?1:2]).PerformClick();
            if(Preferences.Load().Scale!=scale)throw new Exception("Scale preference persistence failed");
            using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(Path.Combine(directory,"size-"+scale+".png"));}
        }
        if(lastPrimary.Inventory!=null)foreach(var entry in lastPrimary.Inventory.Devices)if(entry.Connected==true&&!overview.Cards.Exists(card=>card.Id==entry.Id))throw new Exception("Connected device missing");
        if(deviceTrays.Count!=0)throw new Exception("Missing per-device tray icons");
        foreach(var item in deviceTrays.Values)if(!item.Notify.Visible)throw new Exception("Device tray hidden");
        File.WriteAllText(Path.Combine(directory,"verification.txt"),"PASS: refresh button starts a forced request, disables while busy and recovers; lock, opacity, alert threshold and scale menu actions; settings saved and reloaded; 80/100/125 percent rendering. Device result: "+connection.Text+" / "+state.Text+". Startup toggle not activated during testing.");
        prefs.Scale=100;ApplyAppearance();exiting=true;Close();
    }
}

static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length>1&&args[0]=="--bluetooth-node-probe") {
            try{var nodes=DeviceInventory.ReadBluetoothNodes();int valid=0;foreach(var node in nodes)if(node.Percent.HasValue&&node.Percent>0&&node.Percent<=100)valid++;File.WriteAllText(args[1],"PresentBluetoothNodes="+nodes.Count+"; ValidBatteryProperties="+valid);return 0;}
            catch(Exception e){File.WriteAllText(args[1],e.GetType().Name+": "+e.Message);return 1;}
        }
        if(args.Length>1&&args[0]=="--dual-tray-preview") {
            using(var sheet=new Bitmap(420,190))using(var g=Graphics.FromImage(sheet))using(var font=new Font("Segoe UI",9)){
                g.Clear(Color.White);int[] sizes={16,20,24,32};int?[] values={100,98,20,null};
                for(int theme=0;theme<2;theme++)for(int row=0;row<4;row++){
                    int x=theme*210,y=row*46;using(var fill=new SolidBrush(theme==0?Color.FromArgb(28,32,39):Color.FromArgb(244,246,249)))g.FillRectangle(fill,x,y,210,44);
                    g.DrawString((values[row].HasValue?values[row]+" / 58":"-- / 9"),font,theme==0?Brushes.White:Brushes.Black,x+4,y+13);
                    for(int i=0;i<sizes.Length;i++)using(var icon=TrayArtwork.Render(sizes[i],values[row],20,3,theme==1,row==3?(int?)9:58,true))g.DrawImageUnscaled(icon,x+88+i*29,y+(44-sizes[i])/2);
                }
                sheet.Save(args[1]);
            }return 0;
        }
        if(args.Length>1&&args[0]=="--inventory-probe") {
            var inventory=DeviceInventory.Read(true).Inventory;var lines=new List<string>();lines.Add("BluetoothScan="+inventory.BluetoothOk+"; Warning="+inventory.Warning);
            if(DeviceInventory.LastBluetoothError.Length>0)lines.Add("BluetoothDiagnostic="+DeviceInventory.LastBluetoothError);
            lines.Add("ChargingDiagnostic="+DeviceInventory.LastChargingDiagnostic);
            lines.Add("ReceiverDiagnostic="+KnownReceivers.Diagnostic);
            foreach(var d in inventory.Devices)lines.Add(d.Name+" | "+d.Transport+" | Connected="+(d.Connected.HasValue?d.Connected.Value.ToString():"unknown")+" | Battery="+(d.Percent.HasValue?d.Percent.Value+"%":"--")+" | "+d.Note);
            File.WriteAllLines(args[1],lines);return 0;
        }
        if(args.Length>1&&args[0]=="--tray-preview") {
            using(var bitmap=new Bitmap(540,285))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Arial",10)) {
                g.Clear(Color.FromArgb(240,240,240));int?[] values={100,98,20,9,null};
                for(int theme=0;theme<2;theme++)for(int style=0;style<4;style++) {
                    int y=10+theme*136+style*31;g.FillRectangle(theme==0?Brushes.Black:Brushes.White,110,y,420,29);
                    g.DrawString("Style "+style,font,Brushes.Black,5,y+5);
                    for(int n=0;n<values.Length;n++)using(var icon=TrayArtwork.Render(16,values[n],20,style,theme==1))g.DrawImageUnscaled(icon,130+n*80,y+6);
                    using(var test=TrayArtwork.Create(98,20,style,theme==1)){}
                }
                bitmap.Save(args[1]);
            }return 0;
        }
        if(args.Length>0&&args[0]=="--status-probe") {
            try{var s=ConnectionMonitor.Read();File.WriteAllText(args[1],s.Connection+Environment.NewLine+s.Detail+Environment.NewLine+ConnectionMonitor.DisplayPercent(s.Battery));return 0;}
            catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}
        }
        if(args.Length>0&&args[0]=="--probe") {
            try {var r=Receiver.Read();File.WriteAllText(args.Length>1?args[1]:"battery-reading.txt","UnverifiedReportedValue="+r.Percent+Environment.NewLine+"Raw="+r.Raw+Environment.NewLine);return 0;}
            catch(Exception e){File.WriteAllText(args.Length>1?args[1]:"battery-reading.txt","Error="+e.Message);return 1;}
        }
        if(args.Length>0&&args[0]=="--self-test") {
            DevicePresence.Verify();
            BluetoothSupplement.Verify();
            G3mPresentation.Verify();
            ChargeStatus.Verify();KnownReceivers.Verify();
            byte[] b={19,74,1,0,2,100,1,0,0,0,0,0,0,0,0,0,0,0,0,197};
            if(Receiver.Parse(b,20).Percent!=100)return 1;
            if(ConnectionMonitor.DisplayPercent(Receiver.Parse(b,20))!="100%"||ConnectionMonitor.DisplayPercent(null)!="--")return 10;
            if(DeviceInventory.Battery((byte)98)!=98||DeviceInventory.Battery(0)!=0||DeviceInventory.Battery(101)!=null||DeviceInventory.Battery(null)!=null||DeviceInventory.Battery("99")!=null)return 12;
            var mouse=new byte[64];mouse[0]=4;mouse[1]=0x20;mouse[3]=0x1a;mouse[4]=6;mouse[8]=78;
            if(Receiver.ParseG3m(mouse,64).Percent!=78)return 13;mouse[8]=101;if(Receiver.ParseG3m(mouse,64)!=null)return 14;mouse[8]=78;mouse[7]=255;if(Receiver.ParseG3m(mouse,64)!=null)return 15;mouse[7]=0;mouse[1]=0;if(Receiver.ParseG3m(mouse,64)!=null)return 16;
            b[5]=99;b[19]=196;if(ConnectionMonitor.DisplayPercent(Receiver.Parse(b,20))!="99%")return 11;b[5]=100;b[19]=197;
            b[5]=101;if(Receiver.Parse(b,20)!=null)return 2;b[5]=100;b[19]=0;if(Receiver.Parse(b,20)!=null)return 3;
            if(Receiver.Parse(b,5)!=null)return 4;
            if(!ConnectionMonitor.IsKeyboardName("LEOBOG Hi98 Pro") || !ConnectionMonitor.IsKeyboardName("HI98 5.0") || ConnectionMonitor.IsKeyboardName("Mouse"))return 5;
            var pref=new Preferences();
            if(!pref.ShouldNotify(20)||pref.ShouldNotify(19))return 6;
            using(var memory=new MemoryStream()) {
                var serializer=new XmlSerializer(typeof(Preferences));serializer.Serialize(memory,pref);memory.Position=0;
                pref=(Preferences)serializer.Deserialize(memory);
            }
            if(pref.ShouldNotify(18))return 7;
            if(pref.ShouldNotify(26)||!pref.ShouldNotify(10))return 8;
            pref.Threshold=0;if(pref.ShouldNotify(0))return 9;
            return 0;
        }
        bool created;using(var mutex=new Mutex(true,"Local\\HI98ProBattery_Standalone",out created)) {
            if(!created)return 0;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool animationTest=args.Length>1&&args[0]=="--animation-test",deviceTest=args.Length>1&&args[0]=="--devices-test";
            if(args.Length>0&&(args[0]=="--ui-test"||args[0]=="--snapshot"||animationTest||deviceTest))Application.ThreadException+=(s,e)=>{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-error.txt"),e.Exception.ToString());Environment.Exit(1);};
            int scenario=0,readCalls=0;
            Func<bool,ConnectionMonitor.Status> testReader=force=>{Interlocked.Increment(ref readCalls);int mode=Volatile.Read(ref scenario);if(mode==1)Thread.Sleep(240);if(mode==2)Thread.Sleep(600);if(mode==3)throw new IOException("测试读取异常");return new ConnectionMonitor.Status {Connection="TEST · completed",Detail="Simulated reader",Tone=1};};
            var window=animationTest||deviceTest?new BatteryWindow(testReader):new BatteryWindow();
            if(deviceTest)window.Shown+=async(s,e)=>{await window.DeviceSelectionCheck(args[1]);Application.Exit();};
            if(animationTest)window.Shown+=async(s,e)=>{await window.AnimationCheck(args[1],mode=>Volatile.Write(ref scenario,mode),()=>Volatile.Read(ref readCalls));Application.Exit();};
            if(args.Length>1&&args[0]=="--snapshot") window.Shown+=async(s,e)=>{await window.Snapshot(args[1]);Application.Exit();};
            if(args.Length>1&&args[0]=="--ui-test") window.Shown+=async(s,e)=>{await window.VisualCheck(args[1]);Application.Exit();};
            Application.Run(window);return 0;
        }
    }
}
