using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Windows.Media.Animation;

namespace StudyCat {
 public static class CalicoAssets {
  public static string AssetDirectory = Directory.GetCurrentDirectory();
  static BitmapSource[] frames;
  public static BitmapSource Frame(int index) {
   if (frames == null) Load();
   if (index<0 || index>=12) throw new ArgumentOutOfRangeException("index");
   return frames[index];
  }
  static void Load() {
   Stream stream = typeof(CalicoAssets).Assembly.GetManifestResourceStream("StudyCat.CalicoAtlas");
   if (stream == null) stream = File.OpenRead(Path.Combine(AssetDirectory,"assets","calico-atlas.png"));
   BitmapImage atlas = new BitmapImage();
   using (stream) { atlas.BeginInit(); atlas.CacheOption = BitmapCacheOption.OnLoad; atlas.StreamSource = stream; atlas.EndInit(); atlas.Freeze(); }
   // The illustrated sheet has taller sitting/stretching rows and a shorter sleeping row.
   int[] rows = {0,(int)(atlas.PixelHeight*0.38),(int)(atlas.PixelHeight*0.652),atlas.PixelHeight};
   frames = new BitmapSource[12];
   for(int i=0;i<12;i++) {
    int col=i%4,row=i/4,x=col*atlas.PixelWidth/4,right=(col+1)*atlas.PixelWidth/4;
    int top=rows[row],bottom=rows[row+1];
    if(row==1 && col==0) bottom=(int)(atlas.PixelHeight*0.625);
    if(row==2 && col==0) top=(int)(atlas.PixelHeight*0.625);
    var tile = new CroppedBitmap(atlas,new Int32Rect(x,top,right-x,bottom-top));
    var rgba = new FormatConvertedBitmap(tile,PixelFormats.Bgra32,null,0);
    int width=rgba.PixelWidth,height=rgba.PixelHeight,stride=width*4; var pixels=new byte[stride*height]; rgba.CopyPixels(pixels,stride,0);
    int minX=width,minY=height,maxX=0,maxY=0;
    for(int py=0;py<height;py++) for(int px=0;px<width;px++) if(pixels[py*stride+px*4+3]>24) {minX=Math.Min(minX,px); minY=Math.Min(minY,py); maxX=Math.Max(maxX,px); maxY=Math.Max(maxY,py);}
    if(minX>maxX) throw new InvalidDataException("Calico pose is empty: "+i);
    minX=Math.Max(0,minX-4);minY=Math.Max(0,minY-4);maxX=Math.Min(width-1,maxX+4);maxY=Math.Min(height-1,maxY+4);
    var frame = new CroppedBitmap(tile,new Int32Rect(minX,minY,maxX-minX+1,maxY-minY+1)); frame.Freeze(); frames[i]=frame;
   }
  }
 }
 public static class CatBrand {
  public static ImageSource Image() {
   return CalicoAssets.Frame(0);
  }
  public static void ExportIcon(string path) {
   var sizes = new int[] {32,64,256}; var frames = new List<byte[]>();
   foreach(int size in sizes) {
    var visual = new DrawingVisual(); using(var drawing = visual.RenderOpen()) drawing.DrawImage(Image(),new Rect(0,0,size,size));
    var bitmap = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using(var stream = new MemoryStream()) { encoder.Save(stream); frames.Add(stream.ToArray()); }
   }
   using(var writer = new BinaryWriter(File.Create(path))) {
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    int offset = 6 + 16*sizes.Length;
    for(int i=0;i<sizes.Length;i++) { writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length; }
    foreach(var frame in frames) writer.Write(frame);
   }
  }
 }
 public class DayRecord { public string Date; public double Reading; public double Video; }
 public class SessionRecord { public string Started; public string Ended; public double Seconds; }
 public class Journal {
  public List<DayRecord> Days = new List<DayRecord>();
  public List<SessionRecord> Sessions = new List<SessionRecord>();
  public int IdleSeconds = 90;
  public double CatLeft = Double.NaN;
  public double CatTop = Double.NaN;
 }
 public class Counter {
  public Journal Data;
  public bool Running, Video, DisplayOn = true, Locked;
  public double SessionSeconds;
  public double SessionDeducted;
  bool idleDeducted;
  class Credit { public DayRecord Day; public bool Video; public double Seconds; }
  List<Credit> credits = new List<Credit>();
  public DateTime SessionStarted;
  public string State = "准备好就出发吧";
  public Counter(Journal data) { Data = data; }
  public void Start(DateTime now) { if (SessionStarted == default(DateTime)) { SessionStarted = now; idleDeducted = false; } Running = true; }
  public void Finish(DateTime now) {
   Running = false;
   if (SessionStarted != default(DateTime) && SessionSeconds > 0) {
    Data.Sessions.Insert(0, new SessionRecord { Started = SessionStarted.ToString("yyyy-MM-dd HH:mm:ss"), Ended = now.ToString("yyyy-MM-dd HH:mm:ss"), Seconds = SessionSeconds });
    if (Data.Sessions.Count > 200) Data.Sessions.RemoveRange(200, Data.Sessions.Count - 200);
   }
   SessionStarted = default(DateTime); SessionSeconds = 0; SessionDeducted = 0; idleDeducted = false; credits.Clear();
  }
  public void Advance(DateTime from, DateTime to, double idleSeconds, bool inputOk) {
   double elapsed = (to - from).TotalSeconds;
   if (inputOk && idleSeconds < Data.IdleSeconds) idleDeducted = false;
   if (!Running) { State = SessionStarted == default(DateTime) ? "准备好就出发吧" : "休息一下，等你回来"; return; }
   if (Locked) { State = "锁屏了，小猫也休息"; return; }
   if (!DisplayOn) { State = "屏幕关了，暂停计时"; return; }
   if (!inputOk) { State = "暂时读不到活动状态，已暂停"; return; }
   if (elapsed <= 0 || elapsed > 5) { State = "刚刚恢复，继续陪你学习"; return; }
   double seconds = elapsed;
   if (!Video) {
    // Count only the part before the idle threshold, including a grace period.
    seconds = Math.Min(elapsed, Math.Max(0, Data.IdleSeconds - idleSeconds + elapsed));
    State = idleSeconds >= Data.IdleSeconds ? "等你动动鼠标，再一起学习" : "正在陪你读书";
   } else State = "正在陪你看课程";
   DateTime cursor = from;
   double left = seconds;
   while (left > 0.000001) {
    double part = Math.Min(left, (cursor.Date.AddDays(1) - cursor).TotalSeconds);
    string date = cursor.ToString("yyyy-MM-dd");
    DayRecord day = Data.Days.Find(d => d.Date == date);
    if (day == null) { day = new DayRecord { Date = date }; Data.Days.Add(day); }
    if (Video) day.Video += part; else day.Reading += part;
    Credit latest = credits.LastOrDefault();
    if (latest != null && latest.Day == day && latest.Video == Video) latest.Seconds += part;
    else credits.Add(new Credit { Day = day, Video = Video, Seconds = part });
    SessionSeconds += part; left -= part; cursor = cursor.AddSeconds(part);
   }
   if (!Video && idleSeconds >= Data.IdleSeconds) {
    if (!idleDeducted) { DeductIdle(); idleDeducted = true; }
    State = "暂时离开了，已扣除最多 5 分钟";
   }
  }
  void DeductIdle() {
   double remaining = Math.Min(300,SessionSeconds), deducted = 0;
   for (int i=credits.Count-1; i>=0 && remaining>0; i--) {
    Credit credit = credits[i]; double part = Math.Min(remaining,credit.Seconds);
    if (credit.Video) credit.Day.Video = Math.Max(0,credit.Day.Video-part);
    else credit.Day.Reading = Math.Max(0,credit.Day.Reading-part);
    credit.Seconds -= part; remaining -= part; deducted += part;
    if (credit.Seconds <= 0.000001) credits.RemoveAt(i);
   }
   SessionSeconds = Math.Max(0,SessionSeconds-deducted); SessionDeducted += deducted;
  }
 }
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size; public uint Tick; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput value);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr RegisterPowerSettingNotification(IntPtr hwnd, ref Guid guid, uint flags);
  [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
  [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
  [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
  public static bool Idle(out double seconds) {
   LastInput input = new LastInput(); input.Size = (uint)Marshal.SizeOf(input);
   if (!GetLastInputInfo(ref input)) { seconds = 0; return false; }
   seconds = unchecked((uint)Environment.TickCount - input.Tick) / 1000.0; return true;
  }
 }
 public class AppWindow : Window {
  public Counter Counter;
  public FloatingCatWindow Companion { get; private set; }
  string folder, dataPath;
  DispatcherTimer timer;
  DateTime previous;
  IntPtr powerHandle, hwnd;
  HwndSource source;
  bool registeredSession, dirty;
  string saveError;
  public bool NativeReady { get { return powerHandle != IntPtr.Zero && registeredSession; } }
  int ticks;
  TextBlock status, today, session, split, note;
  Button start, pin;
  RadioButton readMode, videoMode;
  System.Windows.Controls.Primitives.UniformGrid thresholdPanel;
  ListBox history;
  CatAnimation catAnimation;
  DateTime nextRoll;
  Random animationRandom = new Random();
  internal const string Layout = @"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='28,22'>
   <Grid.Resources>
    <Style TargetType='Button'><Setter Property='Background' Value='#F1E5DC'/><Setter Property='Foreground' Value='#5B493F'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='14,10'/><Setter Property='Margin' Value='4'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='14' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='RadioButton'><Setter Property='Background' Value='#F3ECE3'/><Setter Property='Foreground' Value='#857263'/><Setter Property='Margin' Value='3'/><Setter Property='Padding' Value='8,10'/><Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'><Border x:Name='Pill' Background='{TemplateBinding Background}' BorderBrush='Transparent' BorderThickness='1' CornerRadius='13' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Pill' Property='Background' Value='#DFE8D6'/><Setter TargetName='Pill' Property='BorderBrush' Value='#BDCAA9'/><Setter Property='Foreground' Value='#506344'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Pill' Property='BorderBrush' Value='#8EAA77'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
   </Grid.Resources>
   <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
   <Grid x:Name='TitleBar' Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><StackPanel Orientation='Horizontal' VerticalAlignment='Center'><Image x:Name='TitleIcon' Width='36' Height='36' Margin='0,0,9,0'/><StackPanel><TextBlock Text='小猫陪学' FontSize='20' FontWeight='Bold'/><TextBlock Text='陪你慢慢积累每一点专注' FontSize='10' Foreground='#A08B7D' Margin='0,3,0,0'/></StackPanel></StackPanel><StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'><Button x:Name='Pin' Content='置顶' Padding='9,8' FontSize='11'/><Button x:Name='Minimize' Content='—' Padding='9,5' FontSize='15' ToolTip='最小化'/><Button x:Name='Maximize' Content='□' Padding='9,5' FontSize='15' ToolTip='最大化或还原'/><Button x:Name='CloseWindow' Content='×' Padding='10,3' FontSize='19' Background='#F5DFD7' ToolTip='关闭并结束学习'/></StackPanel></Grid>
   <Border Grid.Row='1' Background='#F5EDE4' CornerRadius='26' Margin='0,20,0,14' Padding='8,8,8,16'>
    <StackPanel>
     <Viewbox Width='260' Height='165'><Grid Width='300' Height='190'><Image x:Name='CatSprite' Width='285' Height='180' Stretch='Uniform' HorizontalAlignment='Center' VerticalAlignment='Bottom' RenderTransformOrigin='0.5,0.9'/><TextBlock x:Name='SleepZ' Text='z z' FontSize='17' Foreground='#B5A38F' HorizontalAlignment='Right' VerticalAlignment='Top' Margin='0,4,8,0' Visibility='Collapsed'/></Grid></Viewbox>
     <TextBlock x:Name='Status' Text='准备好就出发吧' HorizontalAlignment='Center' FontSize='15'/>
    </StackPanel>
   </Border>
   <StackPanel Grid.Row='2'><TextBlock Text='今天的学习时间' Foreground='#A08B7D' HorizontalAlignment='Center'/><TextBlock x:Name='Today' Text='00:00:00' FontSize='42' FontWeight='Bold' HorizontalAlignment='Center' Margin='0,2'/><TextBlock x:Name='Split' HorizontalAlignment='Center' Foreground='#A08B7D'/><TextBlock x:Name='Session' HorizontalAlignment='Center' Margin='0,8' FontSize='14'/></StackPanel>
   <StackPanel Grid.Row='3'>
    <TextBlock Text='学习方式' Margin='5,8,0,6' Foreground='#A08B7D'/>
    <UniformGrid Columns='2'><RadioButton x:Name='ReadMode' GroupName='Mode' IsChecked='True'><StackPanel><TextBlock Text='阅读' FontSize='15' FontWeight='SemiBold' HorizontalAlignment='Center'/><TextBlock Text='跟着鼠标，自动计时' FontSize='11' Margin='0,4,0,0' HorizontalAlignment='Center'/></StackPanel></RadioButton><RadioButton x:Name='VideoMode' GroupName='Mode'><StackPanel><TextBlock Text='视频' FontSize='15' FontWeight='SemiBold' HorizontalAlignment='Center'/><TextBlock Text='安心看课，持续计时' FontSize='11' Margin='0,4,0,0' HorizontalAlignment='Center'/></StackPanel></RadioButton></UniformGrid>
    <TextBlock Text='没有操作多久后休息' Margin='5,10,0,6' Foreground='#A08B7D'/>
    <UniformGrid x:Name='ThresholdPanel' Columns='4'><RadioButton x:Name='Idle60' GroupName='Idle' Content='60 秒' Tag='60'/><RadioButton x:Name='Idle90' GroupName='Idle' Content='90 秒' Tag='90'/><RadioButton x:Name='Idle120' GroupName='Idle' Content='120 秒' Tag='120'/><RadioButton x:Name='Idle180' GroupName='Idle' Content='180 秒' Tag='180'/></UniformGrid>
    <Grid Margin='0,6'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions><Button x:Name='Start' Content='开始学习' Background='#D9E4CE'/><Button Grid.Column='1' x:Name='Finish' Content='结束本次'/></Grid>
    <TextBlock x:Name='Note' TextWrapping='Wrap' Foreground='#A08B7D' FontSize='11' Margin='4,6'/>
   </StackPanel>
   <Grid Grid.Row='4' Margin='0,14,0,0'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><TextBlock Text='最近的学习记录' FontWeight='SemiBold' Margin='4,0,0,8'/><ListBox x:Name='History' Grid.Row='1' BorderThickness='0' Background='Transparent' Foreground='#806D5D' FontSize='12' ScrollViewer.HorizontalScrollBarVisibility='Disabled'/></Grid>
   <TextBlock Grid.Row='5' Text='只记录活动时间 · 数据留在这台电脑' HorizontalAlignment='Center' Foreground='#B2A296' FontSize='11' Margin='0,12,0,0'/>
  </Grid>";
  public AppWindow(string directory) {
   folder = directory; Directory.CreateDirectory(folder); dataPath = Path.Combine(folder, "study-data.xml");
   Journal data = Load(); Counter = new Counter(data);
   Title = "小猫陪学"; Width = 480; Height = Math.Min(850, SystemParameters.WorkArea.Height - 30); MinWidth = 440; MinHeight = 740; ShowInTaskbar = true; ShowActivated = true;
   WindowStyle = WindowStyle.None;
   WindowChrome.SetWindowChrome(this,new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(18), UseAeroCaptionButtons = false });
   Background = new SolidColorBrush(Color.FromRgb(255,250,243)); Foreground = new SolidColorBrush(Color.FromRgb(91,73,63));
   FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterScreen;
   Grid grid = (Grid)System.Windows.Markup.XamlReader.Parse(Layout); Content = grid;
   Icon = CatBrand.Image(); ((Image)grid.FindName("TitleIcon")).Source = Icon;
   ((Grid)grid.FindName("TitleBar")).MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if(e.ClickCount==2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
   ((Button)grid.FindName("Minimize")).Click += delegate { WindowState = WindowState.Minimized; };
   ((Button)grid.FindName("Maximize")).Click += delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
   ((Button)grid.FindName("CloseWindow")).Click += delegate { Close(); };
   status = (TextBlock)grid.FindName("Status"); today = (TextBlock)grid.FindName("Today"); split = (TextBlock)grid.FindName("Split"); session = (TextBlock)grid.FindName("Session"); note = (TextBlock)grid.FindName("Note");
   start = (Button)grid.FindName("Start"); pin = (Button)grid.FindName("Pin"); readMode = (RadioButton)grid.FindName("ReadMode"); videoMode = (RadioButton)grid.FindName("VideoMode"); thresholdPanel = (System.Windows.Controls.Primitives.UniformGrid)grid.FindName("ThresholdPanel"); history = (ListBox)grid.FindName("History");
   catAnimation = new CatAnimation(grid);
   if (!new int[] {60,90,120,180}.Contains(data.IdleSeconds)) data.IdleSeconds = 90;
   ((RadioButton)grid.FindName("Idle" + data.IdleSeconds)).IsChecked = true;
   start.Click += delegate { ToggleLearning(); };
   ((Button)grid.FindName("Finish")).Click += delegate { FinishLearning(); };
   pin.Click += delegate { Topmost = !Topmost; pin.Content = Topmost ? "取消置顶" : "置顶"; };
   RoutedEventHandler modeChanged = delegate { Tick(); Counter.Video = videoMode.IsChecked == true; thresholdPanel.IsEnabled = !Counter.Video; Tick(); };
   readMode.Checked += modeChanged; videoMode.Checked += modeChanged;
   foreach (RadioButton option in thresholdPanel.Children) option.Checked += delegate(object sender, RoutedEventArgs e) { Tick(); Counter.Data.IdleSeconds = Int32.Parse(((RadioButton)sender).Tag.ToString()); dirty = true; Save(); Tick(); };
   SourceInitialized += OnSource;
   Closing += delegate { timer.Stop(); Tick(); Counter.Finish(DateTime.Now); dirty = true; Save(); if (Companion != null) Companion.Shutdown(); if (powerHandle != IntPtr.Zero) Native.UnregisterPowerSettingNotification(powerHandle); if (registeredSession) Native.WTSUnRegisterSessionNotification(hwnd); if (source != null) source.RemoveHook(Hook); };
   previous = DateTime.Now; timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; timer.Tick += delegate { Tick(); }; timer.Start(); RefreshHistory(); Tick();
  }
  public void ToggleLearning() {
   Tick();
   if (Counter.Running) Counter.Running = false;
   else {
    Counter.Start(DateTime.Now);
    if (Companion == null) Companion = new FloatingCatWindow(this);
    Companion.Show();
   }
   dirty = true; Tick(); Save();
  }
  public void FinishLearning() {
   Tick(); Counter.Finish(DateTime.Now);
   if (Companion != null) Companion.Hide();
   dirty = true; Tick(); Save(); RefreshHistory();
  }
  public void RestoreMainWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
  public void RememberCatPosition(double left, double top) {
   Counter.Data.CatLeft = left; Counter.Data.CatTop = top; dirty = true; Save();
  }
  Journal Load() {
   if (!File.Exists(dataPath)) return new Journal();
   try { using (FileStream file = File.OpenRead(dataPath)) return (Journal)new XmlSerializer(typeof(Journal)).Deserialize(file); }
   catch { MessageBox.Show("学习记录暂时无法读取。为保护原记录，本次将停止启动；请保留 study-data.xml 和 .bak 文件。", "小猫陪学"); throw; }
  }
  void Save() {
   if (!dirty) return;
   try {
    string temp = dataPath + ".tmp";
    using (FileStream file = File.Create(temp)) { new XmlSerializer(typeof(Journal)).Serialize(file, Counter.Data); file.Flush(true); }
    if (File.Exists(dataPath)) File.Replace(temp, dataPath, dataPath + ".bak"); else File.Move(temp, dataPath);
    dirty = false; saveError = null;
   } catch (Exception e) { saveError = "保存失败：" + e.Message + "。请暂时保持窗口打开。"; note.Text = saveError; }
  }
  void OnSource(object sender, EventArgs e) {
   hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source.AddHook(Hook);
   Guid guid = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47"); powerHandle = Native.RegisterPowerSettingNotification(hwnd, ref guid, 0);
   registeredSession = Native.WTSRegisterSessionNotification(hwnd, 0);
  }
  IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) {
   if (msg == 0x218 && w.ToInt32() == 0x8013 && l != IntPtr.Zero) {
    Guid guid = (Guid)Marshal.PtrToStructure(l, typeof(Guid));
    if (guid == new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47") && Marshal.ReadInt32(l,16) >= 4) { Tick(); Counter.DisplayOn = Marshal.ReadInt32(l,20) != 0; Tick(); }
   }
   if (msg == 0x2B1) { int code = w.ToInt32(); if (code == 7 || code == 8) { Tick(); Counter.Locked = code == 7; Tick(); } }
   if (msg == 0x218 && w.ToInt32() == 4) { Tick(); previous = DateTime.Now; }
   if (msg == 0x218 && (w.ToInt32() == 18 || w.ToInt32() == 7)) previous = DateTime.Now;
   return IntPtr.Zero;
  }
  public static string Duration(double seconds) { int s = Math.Max(0, (int)seconds); return String.Format("{0:00}:{1:00}:{2:00}",s/3600,(s/60)%60,s%60); }
  void RefreshHistory() {
   history.Items.Clear();
   foreach (DayRecord d in Counter.Data.Days.OrderByDescending(d => d.Date).Take(7)) history.Items.Add(d.Date + "    累计 " + Duration(d.Reading+d.Video));
   if (Counter.Data.Sessions.Count == 0) history.Items.Add("第一段学习，等你和小猫一起开始。");
   foreach (SessionRecord s in Counter.Data.Sessions.Take(8)) history.Items.Add(s.Started + "    " + Duration(s.Seconds));
  }
  public void Tick() {
   DateTime now = DateTime.Now; double idle; bool ok = Native.Idle(out idle); double before = Counter.SessionSeconds;
   Counter.Advance(previous, now, idle, ok); previous = now; if (Counter.SessionSeconds != before) dirty = true;
   DayRecord d = Counter.Data.Days.Find(day => day.Date == now.ToString("yyyy-MM-dd")); double r = d == null ? 0 : d.Reading, v = d == null ? 0 : d.Video;
   today.Text = Duration(r+v); split.Text = "阅读 " + Duration(r) + "   ·   视频 " + Duration(v); session.Text = "本次学习  " + Duration(Counter.SessionSeconds); status.Text = Counter.State;
   start.Content = Counter.Running ? "暂停一下" : Counter.SessionStarted == default(DateTime) ? "开始学习" : "继续学习";
   bool resting = !Counter.Running || Counter.Locked || !Counter.DisplayOn || (!Counter.Video && idle >= Counter.Data.IdleSeconds) || !ok;
   bool blink = ticks % 14 == 0;
   bool focused = !resting;
   if (focused && !catAnimation.IsSleeping) nextRoll = DateTime.UtcNow.AddSeconds(animationRandom.Next(45,91));
   catAnimation.SetSleeping(focused); catAnimation.Advance();
   if (Companion != null) Companion.UpdateDisplay(Counter.SessionSeconds, resting, blink, Counter.Running);
   if (focused && DateTime.UtcNow >= nextRoll) { catAnimation.RollOver(); if(Companion != null) Companion.RollOver(); nextRoll = DateTime.UtcNow.AddSeconds(animationRandom.Next(45,91)); }
   if (saveError == null) note.Text = Counter.Video ? "视频模式持续计时；去玩手机时记得点暂停。关屏、锁屏会自动暂停。" : "无操作满 " + Counter.Data.IdleSeconds + " 秒后暂停，并扣除本次最多 5 分钟；每次离开只扣一次。";
   if (powerHandle == IntPtr.Zero && source != null) note.Text += " 关屏检测注册失败。";
   if (!registeredSession && source != null) note.Text += " 锁屏检测注册失败。";
   ticks++; if (ticks % 20 == 0) { Save(); RefreshHistory(); }
  }
  public void Snapshot(string path) {
   UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(this);
   var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file);
  }
 }
 public class CatAnimation {
  Image sprite;
  FrameworkElement z;
  ScaleTransform breath = new ScaleTransform(1,1);
  RotateTransform rotation = new RotateTransform(0);
  Random random = new Random();
  DateTime actionStarted, actionEnds, nextAction, blinkEnds, nextBlink;
  bool sleepRight, initialized;
  int idlePose;
  bool rolling;
  public bool IsSleeping { get; private set; }
  public int RollCount { get; private set; }
  public int FrameIndex { get; private set; }
  public CatAnimation(FrameworkElement root) {
   sprite = (Image)root.FindName("CatSprite"); z = (FrameworkElement)root.FindName("SleepZ");
   var transforms = new TransformGroup(); transforms.Children.Add(breath); transforms.Children.Add(rotation); sprite.RenderTransform = transforms;
   SetSleeping(false);
  }
  public void SetSleeping(bool value) {
   if (initialized && value == IsSleeping) return;
   initialized = true; IsSleeping = value; rolling=false; actionEnds=DateTime.MinValue; sleepRight=false;
   nextAction = DateTime.UtcNow.AddSeconds(random.Next(9,18)); nextBlink=DateTime.UtcNow.AddSeconds(5);
   z.Visibility=value?Visibility.Visible:Visibility.Collapsed; SetFrame(value?4:0);
   if (value) {
    var breathing = new DoubleAnimation(1,1.025,TimeSpan.FromSeconds(2.8)) {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};
    breath.BeginAnimation(ScaleTransform.ScaleYProperty,breathing);
    var floating = new DoubleAnimation(0.25,0.85,TimeSpan.FromSeconds(2.4)) {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}; z.BeginAnimation(UIElement.OpacityProperty,floating);
   } else {
    breath.BeginAnimation(ScaleTransform.ScaleYProperty,null); z.BeginAnimation(UIElement.OpacityProperty,null); rotation.BeginAnimation(RotateTransform.AngleProperty,null);
    breath.ScaleY=1; rotation.Angle=0;
    // Wake with a small yawn and a stretch before settling down.
    actionStarted=DateTime.UtcNow; actionEnds=actionStarted.AddSeconds(4); idlePose=9;
   }
  }
  void SetFrame(int index) { if(sprite.Source!=null && FrameIndex==index) return; FrameIndex=index; sprite.Source=CalicoAssets.Frame(index); }
  public void PreviewPose(int index) { SetFrame(index); }
  public void ReactToPetting() {
   DateTime now=DateTime.UtcNow; actionStarted=now; actionEnds=now.AddSeconds(2.5);
   if(IsSleeping) SetFrame(10);
   else {idlePose=2;SetFrame(2);}
  }
  public void Advance() {
   AdvanceAt(DateTime.UtcNow);
  }
  public void AdvanceAt(DateTime now) {
   if(rolling) {
    double age=(now-actionStarted).TotalSeconds;
    if(age<0.7) SetFrame(sleepRight?7:4);
    else if(age<2.7) SetFrame(6);
    else { sleepRight=!sleepRight; rolling=false; SetFrame(sleepRight?7:4); nextAction=now.AddSeconds(random.Next(12,23)); }
    return;
   }
   if(IsSleeping) {
    if(now<actionEnds) { SetFrame(10); return; }
    if(now>=nextAction) { actionEnds=now.AddSeconds(1.6); nextAction=now.AddSeconds(random.Next(14,28)); SetFrame(10); return; }
    SetFrame(sleepRight?7:(now.Second%10<5?4:5)); return;
   }
   if(now<actionEnds) {
    if(idlePose==9) SetFrame((now-actionStarted).TotalSeconds<1.5?9:8);
    else if(idlePose==3) SetFrame(((int)((now-actionStarted).TotalSeconds*2))%2==0?3:0);
    else SetFrame(idlePose);
    return;
   }
   if(now>=nextAction) { int[] poses={2,3,8,9,11}; idlePose=poses[random.Next(poses.Length)]; actionStarted=now; actionEnds=now.AddSeconds(idlePose==11?5:3.5); nextAction=now.AddSeconds(random.Next(10,20)); Advance(); return; }
   if(now>=nextBlink) {blinkEnds=now.AddSeconds(0.65);nextBlink=now.AddSeconds(random.Next(4,8));}
   SetFrame(now<blinkEnds?1:0);
  }
  public void RollOver() {
   if (!IsSleeping) return;
   RollCount++; rolling=true; actionStarted=DateTime.UtcNow;
   rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(-4,4,TimeSpan.FromSeconds(1.2)) {AutoReverse=true,FillBehavior=FillBehavior.Stop,EasingFunction=new SineEase()});
   Advance();
  }
 }
 public class FloatingCatWindow : Window {
  AppWindow main;
  TextBlock time, label;
  MenuItem pause;
  bool shuttingDown;
  CatAnimation catAnimation;
  public bool IsSleeping { get { return catAnimation.IsSleeping; } }
  public int RollCount { get { return catAnimation.RollCount; } }
  public int PoseIndex { get { return catAnimation.FrameIndex; } }
  public void PreviewPose(int index) { catAnimation.PreviewPose(index); }
  public void AdvanceAnimationAt(DateTime now) { catAnimation.AdvanceAt(now); }
  public string DisplayedTime { get { return time.Text; } }
  public FloatingCatWindow(AppWindow owner) {
   main = owner;
   Title = "小猫陪学 · 桌面小猫";
   Width = 240; Height = 228; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
   AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
   FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = new SolidColorBrush(Color.FromRgb(91,73,63));
   // Keep this window unowned: minimizing the dashboard must not hide the cat.
   string cat = AppWindow.Layout.Substring(AppWindow.Layout.IndexOf("<Viewbox"));
   cat = cat.Substring(0,cat.IndexOf("</Viewbox>") + "</Viewbox>".Length);
   cat = cat.Replace("Width='260' Height='165'", "Width='235' Height='148'");
   string markup = "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent'>" + cat +
    "<Border Background='#F9FFF9F0' BorderBrush='#E9DDCE' BorderThickness='1' CornerRadius='17' Padding='10,8' Margin='21,1,21,0'><StackPanel><TextBlock x:Name='Label' Text='本次专注' HorizontalAlignment='Center' FontSize='11' Foreground='#9B8675'/><TextBlock x:Name='Time' Text='00:00:00' HorizontalAlignment='Center' FontSize='23' FontWeight='SemiBold' Margin='0,2,0,0'/></StackPanel></Border></StackPanel>";
   var panel = (StackPanel)System.Windows.Markup.XamlReader.Parse(markup); Content = panel;
   time = (TextBlock)panel.FindName("Time"); label = (TextBlock)panel.FindName("Label");
   catAnimation = new CatAnimation(panel);
   ToolTip = "拖动小猫调整位置 · 双击打开主界面 · 右键暂停或结束";
   var menu = new ContextMenu();
   pause = new MenuItem { Header = "暂停学习" }; pause.Click += delegate { main.ToggleLearning(); };
   var dashboard = new MenuItem { Header = "打开主界面" }; dashboard.Click += delegate { main.RestoreMainWindow(); };
   var pet = new MenuItem { Header = "摸摸小花" }; pet.Click += delegate { catAnimation.ReactToPetting(); };
   var finish = new MenuItem { Header = "结束本次学习" }; finish.Click += delegate { main.FinishLearning(); };
   menu.Items.Add(pause); menu.Items.Add(pet); menu.Items.Add(dashboard); menu.Items.Add(new Separator()); menu.Items.Add(finish); ContextMenu = menu;
   MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) {
    if (e.ClickCount == 2) { main.RestoreMainWindow(); e.Handled = true; return; }
    if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) {
     DragMove(); MoveTo(Left,Top); e.Handled = true;
    }
   };
   Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!shuttingDown) { e.Cancel = true; Hide(); } };
   Rect work = SystemParameters.WorkArea;
   double left = main.Counter.Data.CatLeft, top = main.Counter.Data.CatTop;
   if (Double.IsNaN(left) || Double.IsInfinity(left)) left = work.Right - Width - 24;
   if (Double.IsNaN(top) || Double.IsInfinity(top)) top = work.Bottom - Height - 24;
   WindowStartupLocation = WindowStartupLocation.Manual; PlaceWithinDesktop(left,top);
  }
  void PlaceWithinDesktop(double left, double top) {
   Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(left,SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
   Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(top,SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
  }
  public void MoveTo(double left, double top) { PlaceWithinDesktop(left,top); main.RememberCatPosition(Left,Top); }
  public void UpdateDisplay(double seconds, bool resting, bool blink, bool running) {
   time.Text = AppWindow.Duration(seconds); label.Text = resting ? "本次专注 · 已暂停" : "本次专注";
   catAnimation.SetSleeping(!resting); catAnimation.Advance();
   pause.Header = running ? "暂停学习" : "继续学习";
  }
  public void RollOver() { catAnimation.RollOver(); }
  public void Shutdown() { shuttingDown = true; Close(); }
  public void Snapshot(string path) {
   UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32); bitmap.Render(this);
   var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file);
  }
 }
 public static class Checks {
  static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
  public static string Run() {
   DateTime t = new DateTime(2026,10,4,10,0,0); var c = new Counter(new Journal()); c.Start(t);
   c.Advance(t,t.AddSeconds(1),0,true); Assert(c.SessionSeconds==1,"active input");
   c.Advance(t.AddSeconds(1),t.AddSeconds(2),91,true); Assert(c.SessionSeconds==0,"idle pause and zero floor");
   c.Advance(t.AddSeconds(2),t.AddSeconds(3),0,true); Assert(c.SessionSeconds==1,"auto resume");
   c.Video=true; c.Advance(t.AddSeconds(3),t.AddSeconds(4),900,true); Assert(c.SessionSeconds==2,"video idle");
   c.DisplayOn=false; c.Advance(t.AddSeconds(4),t.AddSeconds(5),0,true); Assert(c.SessionSeconds==2,"screen off");
   c.DisplayOn=true; c.Locked=true; c.Advance(t.AddSeconds(5),t.AddSeconds(6),0,true); Assert(c.SessionSeconds==2,"locked");
   c.Locked=false; c.Advance(t.AddSeconds(6),t.AddSeconds(600),0,true); Assert(c.SessionSeconds==2,"sleep gap");
   c.Advance(t,t.AddSeconds(1),0,false); Assert(c.SessionSeconds==2,"input failure");
   c.Running=false; c.Advance(t,t.AddSeconds(1),0,true); Assert(c.SessionSeconds==2,"manual pause");
   c.Finish(t.AddSeconds(600)); Assert(c.Data.Sessions.Count==1 && c.Data.Sessions[0].Seconds==2 && c.SessionSeconds==0,"finish");
   var m = new Counter(new Journal()); var midnight = new DateTime(2026,10,4,23,59,59); m.Start(midnight); m.Advance(midnight,midnight.AddSeconds(2),0,true);
   Assert(m.Data.Days.Count==2 && m.Data.Days[0].Reading==1 && m.Data.Days[1].Reading==1,"midnight split");
   var edge = new Counter(new Journal()); edge.Start(t); edge.Advance(t,t.AddSeconds(1),90.5,true); Assert(edge.SessionSeconds==0 && edge.SessionDeducted==0.5,"threshold boundary");
   var serializer = new XmlSerializer(typeof(Journal)); using(var s = new MemoryStream()) { serializer.Serialize(s,c.Data); s.Position=0; var read=(Journal)serializer.Deserialize(s); Assert(read.Sessions[0].Seconds==2,"persistence"); }
   Assert(Entry.MutexName("WinSta0.Default") != Entry.MutexName("WinSta0.CodexSandboxDesktop"),"separate desktop instance locks");
   var penalty = new Counter(new Journal()); penalty.Start(t);
   for(int i=0;i<150;i++) penalty.Advance(t.AddSeconds(i*4),t.AddSeconds(i*4+4),0,true);
   penalty.Advance(t.AddSeconds(600),t.AddSeconds(601),91,true);
   Assert(penalty.SessionSeconds==300 && penalty.Data.Days[0].Reading==300,"five minute deduction sync");
   penalty.Advance(t.AddSeconds(601),t.AddSeconds(602),92,true);
   Assert(penalty.SessionSeconds==300,"deduct once per idle episode");
   penalty.Advance(t.AddSeconds(602),t.AddSeconds(603),0,true);
   penalty.Advance(t.AddSeconds(603),t.AddSeconds(604),91,true);
   Assert(penalty.SessionSeconds==1 && penalty.SessionDeducted==600,"deduct again after activity");
   var historical = new Journal(); historical.Days.Add(new DayRecord {Date=t.ToString("yyyy-MM-dd"),Reading=1000});
   var shortSession = new Counter(historical); shortSession.Start(t); shortSession.Advance(t,t.AddSeconds(4),0,true); shortSession.Advance(t.AddSeconds(4),t.AddSeconds(5),91,true);
   Assert(shortSession.SessionSeconds==0 && historical.Days[0].Reading==1000,"preserve earlier sessions");
   var midnightPenalty = new Counter(new Journal()); var evening=t.Date.AddHours(23).AddMinutes(56); midnightPenalty.Start(evening);
   for(int i=0;i<150;i++) midnightPenalty.Advance(evening.AddSeconds(i*4),evening.AddSeconds(i*4+4),0,true);
   midnightPenalty.Advance(evening.AddSeconds(600),evening.AddSeconds(601),91,true);
   Assert(midnightPenalty.Data.Days[0].Reading==240 && midnightPenalty.Data.Days[1].Reading==60,"deduct most recent time across midnight");
   var mixed = new Counter(new Journal()); mixed.Start(t); mixed.Video=true;
   for(int i=0;i<150;i++) mixed.Advance(t.AddSeconds(i*4),t.AddSeconds(i*4+4),900,true);
   Assert(mixed.SessionSeconds==600 && mixed.SessionDeducted==0,"video has no idle penalty");
   mixed.Video=false; mixed.Advance(t.AddSeconds(600),t.AddSeconds(604),0,true); mixed.Advance(t.AddSeconds(604),t.AddSeconds(605),91,true);
   Assert(mixed.SessionSeconds==304 && mixed.Data.Days[0].Video==304 && mixed.Data.Days[0].Reading==0,"deduct latest credits across modes");
   return "PASS: 21 timing, idle deduction, persistence and desktop isolation checks";
  }
 }
}

namespace StudyCat {
 public static class Entry {
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder name, int bytes, out int needed);
  static string ObjectName(IntPtr handle) {
   var name = new System.Text.StringBuilder(1024); int needed;
   if (!GetUserObjectInformation(handle,2,name,name.Capacity*2,out needed)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   return name.ToString();
  }
  public static string DesktopScope() {
   return ObjectName(GetProcessWindowStation()) + "." + ObjectName(GetThreadDesktop(GetCurrentThreadId()));
  }
  public static string MutexName(string desktop) {
   return "Local\\StudyCatDesktopV3." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(desktop)).Replace('/','_');
  }
  [STAThread] public static void Main() {
   string directory = AppDomain.CurrentDomain.BaseDirectory;
   bool owns = false;
   using (var mutex = new System.Threading.Mutex(false,MutexName(DesktopScope()))) {
    try {
     try { owns = mutex.WaitOne(0); } catch(System.Threading.AbandonedMutexException) { owns = true; }
     if (!owns) {
      IntPtr window = IntPtr.Zero;
      for (int attempt=0; attempt<12 && window==IntPtr.Zero; attempt++) {
       window = FindWindow(null,"小猫陪学");
       if(window==IntPtr.Zero) System.Threading.Thread.Sleep(250);
      }
      if(window != IntPtr.Zero) { ShowWindow(window,9); SetForegroundWindow(window); }
      else MessageBox.Show("当前桌面已有小猫进程，但窗口未响应。请在任务管理器中结束小猫陪学程序后重新打开。","小猫陪学");
      return;
     }
     var app = new Application();
     var windowNew = new AppWindow(Path.Combine(directory,"data"));
     windowNew.Loaded += delegate { windowNew.WindowState = WindowState.Normal; windowNew.Activate(); };
     app.Run(windowNew);
    } catch(Exception e) {
     try { File.WriteAllText(Path.Combine(directory,"startup-error.log"),e.ToString()); } catch { }
     MessageBox.Show("启动失败："+e.Message+"\n详细信息保存在 startup-error.log。","小猫陪学");
    } finally { if(owns) mutex.ReleaseMutex(); }
   }
  }
}
}
