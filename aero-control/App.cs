using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Forms = System.Windows.Forms;

[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]

namespace AeroControl {
    public sealed class App : Application {
        Window window; Hardware hardware; Snapshot current;
        TextBlock[] features;
        readonly HistoryChart chart = new HistoryChart();
        DispatcherTimer timer; bool busy, commandBusy, exiting, fahrenheit, trayOnClose;
        int interval=2; DateTime nextPoll=DateTime.MinValue, lastGood=DateTime.MinValue;
        string requestedMode, commandMessage; DateTime commandMessageUntil;
        Guid? displayedPowerPlan;
        Forms.NotifyIcon tray; Mutex instance;
        readonly Brush accent=new SolidColorBrush(Color.FromRgb(155,174,255));
        readonly Brush muted=new SolidColorBrush(Color.FromRgb(168,180,198));
        readonly Brush green=new SolidColorBrush(Color.FromRgb(100,213,160));
        readonly Brush amber=new SolidColorBrush(Color.FromRgb(231,182,144));
        readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AeroControl","settings.xml");

        [STAThread] public static void Main(string[] args) {
            if(args.Length>0 && args[0]=="--diagnose") { Diagnose(args); return; }
            if(args.Length>0 && args[0]=="--probe") { Probe(args); return; }
            if(args.Length>1 && args[0]=="--fanmode") { FanCommand(args[1],null,0); return; }
            if(args.Length>2 && args[0]=="--fanspeed") { int percent; if(!int.TryParse(args[2],out percent))return; FanCommand(null,args[1],percent); return; }
            if(args.Length>1 && (args[0]=="--coolboost" || args[0]=="--powerplan")) { ExtraCommand(args); return; }
            bool created;
            using(var mutex=new Mutex(true,"Local\\AeroControl.Desktop",out created)) {
                if(!created){MessageBox.Show("Aero Control is already running. Open it from the system tray or taskbar.","Aero Control");return;}
                var app=new App(); app.instance=mutex;
                app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"Aero Control");e.Handled=true;};
                app.Run();
            }
        }
        static void Probe(string[] args) {
            string path=args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe.txt");
            try { using(var h=new Hardware()) File.WriteAllText(path,h.Probe()); }
            catch(Exception e){File.WriteAllText(path,"Probe failed: "+e.Message);}
        }
        // Scriptable path for supervised testing of the gated fan writes; outputs to fancommand.txt next to the executable.
        static void FanCommand(string mode,string fan,int percent) {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fancommand.txt");
            try { using(var h=new Hardware()) {
                Snapshot before=h.Read();
                string error=mode!=null?h.SetFanMode(mode):h.SetFanSpeed(fan,percent);
                Snapshot after=h.Read();
                File.WriteAllText(path,(error==null?"OK: ":"FAILED: ")+(mode!=null?"mode "+mode:fan+" fan "+percent+"%")+Environment.NewLine+
                    (error??"Accepted by Acer's service.")+Environment.NewLine+
                    "RPM before: CPU "+before.CpuRpm+", GPU "+before.GpuRpm+Environment.NewLine+
                    "RPM after: CPU "+after.CpuRpm+", GPU "+after.GpuRpm);
            }} catch(Exception e){File.WriteAllText(path,"FAILED: "+e.Message);}
        }
        static void Diagnose(string[] args) {
            string path=args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics.txt");
            try { using(var h=new Hardware()) {
                Snapshot p=h.Read();
                string report="Device: "+h.Device.Manufacturer+" "+h.Device.Model+Environment.NewLine+
                    "Connected: "+p.ServiceConnected+"; Fan control: "+p.FanControlSupported+Environment.NewLine+
                    "CPU temperature: "+p.CpuTemp+"; GPU temperature: "+p.GpuTemp+Environment.NewLine+
                    "CPU RPM: "+p.CpuRpm+"; GPU RPM: "+p.GpuRpm+Environment.NewLine+
                    "CPU load: "+p.CpuLoad+Environment.NewLine+"CoolBoost: "+p.CoolBoostStatus+Environment.NewLine+
                    "Power plan: "+p.PowerPlan+" ("+p.PowerPlanId+")"+Environment.NewLine+
                    "Installed power plans: "+String.Join(", ",Array.ConvertAll(p.PowerPlans,plan=>plan.Name+" ["+plan.Id+"]"))+Environment.NewLine+"Status: "+p.Status;
                File.WriteAllText(path,report);
            }} catch(Exception e){File.WriteAllText(path,"Diagnostic failed: "+e.Message);}
        }
        static void ExtraCommand(string[] args) {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"extracommand.txt");
            string error;
            if(args[0]=="--powerplan") {
                Guid id; error=Guid.TryParse(args[1],out id)?WindowsPower.SetPlan(id):"Use an installed power-plan GUID.";
            } else if(args[1]!="on" && args[1]!="off")error="Use --coolboost on or --coolboost off.";
            else using(var h=new Hardware()) {h.Read();error=h.SetCoolBoost(args[1]=="on");}
            File.WriteAllText(path,(error==null?"OK: Change confirmed by readback.":"FAILED: "+error)+Environment.NewLine);
            Environment.ExitCode=error==null?0:1;
        }
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e); LoadSettings();
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AeroControl.MainWindow.xaml"))
                window=(Window)XamlReader.Load(stream);
            MainWindow=window;
            window.Width=Math.Min(1280,SystemParameters.WorkArea.Width-36);
            window.Height=Math.Min(820,SystemParameters.WorkArea.Height-28);
            ((Grid)window.FindName("ChartHost")).Children.Add(chart); chart.Fahrenheit=fahrenheit;
            ((Border)window.FindName("TitleBar")).MouseLeftButtonDown+=(s,a)=>{if(a.ClickCount==2) ToggleMaximize();else if(a.ButtonState==MouseButtonState.Pressed)window.DragMove();};
            Click("MinimizeButton",()=>window.WindowState=WindowState.Minimized);
            Click("MaximizeButton",ToggleMaximize); Click("CloseButton",()=>window.Close());
            foreach(string name in new[]{"Overview","Device","Settings"}) {
                string page=name;
                ((RadioButton)window.FindName("Nav"+name)).Checked+=(s,a)=>Navigate(page);
            }
            Click("AutoButton",()=>RequestMode("Auto")); Click("MaxButton",()=>RequestMode("Max"));
            Click("CustomButton",()=>RequestMode("Custom"));
            Click("ApplyCustom",ApplyCustom);
            ((Slider)window.FindName("CpuSlider")).ValueChanged+=(s,a)=>Text("CpuSpeedLabel",(int)a.NewValue+"%");
            ((Slider)window.FindName("GpuSlider")).ValueChanged+=(s,a)=>Text("GpuSpeedLabel",(int)a.NewValue+"%");
            Click("BoostOn",()=>RequestCoolBoost(true));Click("BoostOff",()=>RequestCoolBoost(false));
            Click("ApplyPower",RequestPowerPlan);Click("VerifyOverview",VerifyFan);
            Control<ComboBox>("PowerPlans").SelectionChanged+=(s,a)=>UpdateFreshness();
            Click("CheckAgain",()=>{nextPoll=DateTime.MinValue;Poll();}); Click("SummaryButton",ShowSummary); Click("VerifyFan",VerifyFan);
            Click("Range1",()=>Range(1));Click("Range5",()=>Range(5));Click("Range15",()=>Range(15));
            Click("CelsiusButton",()=>SetUnits(false));Click("FahrenheitButton",()=>SetUnits(true));
            Click("Refresh2",()=>SetInterval(2));Click("Refresh5",()=>SetInterval(5));Click("Refresh10",()=>SetInterval(10));
            var check=(CheckBox)window.FindName("TrayCheck");check.IsChecked=trayOnClose;
            check.Checked+=(s,a)=>{trayOnClose=true;SaveSettings();};check.Unchecked+=(s,a)=>{trayOnClose=false;SaveSettings();};
            ConfigureTray();
            window.Closing+=(s,a)=>{if(trayOnClose&&!exiting){a.Cancel=true;window.Hide();tray.Visible=true;}else{exiting=true;if(timer!=null)timer.Stop();if(tray!=null)tray.Dispose();}};
            window.Closed+=(s,a)=>{if(hardware!=null&&!busy&&!commandBusy)hardware.Dispose();Shutdown();};
            window.Loaded+=async(s,a)=>{
                try {
                    hardware=await Task.Run(()=>new Hardware());
                    if(exiting)return;
                    Text("SidebarModel",hardware.Device.Model);Text("DeviceModel",hardware.Device.Model);
                    Text("DeviceDetails",hardware.Device.Manufacturer+" · Windows\n"+hardware.Device.Cpu+"\n"+hardware.Device.Gpu);
                    timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
                    timer.Tick+=(sender,args)=>{UpdateFreshness();if(DateTime.UtcNow>=nextPoll)Poll();};timer.Start();Poll();
                } catch(Exception error){Text("NoticeText","Device check failed: "+error.Message);Text("ConnectionStatus","Device check failed");}
            };
            UpdateSettingsButtons();window.Show();
        }
        T Control<T>(string name) where T:class {return window.FindName(name) as T;}
        void Text(string name,string value){Control<TextBlock>(name).Text=value;}
        void Click(string name,Action action){Control<Button>(name).Click+=(s,e)=>action();}
        void ToggleMaximize(){window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
        void Navigate(string page){
            foreach(string name in new[]{"Overview","Device","Settings"})Control<FrameworkElement>(name+"Page").Visibility=name==page?Visibility.Visible:Visibility.Collapsed;
            Control<RadioButton>("Nav"+page).IsChecked=true;
            Text("PageTitle",page=="Device"?"Device support":page);
            Text("PageSubtitle",page=="Overview"?"Monitor and control your laptop in one place.":page=="Device"?"See what your laptop can do.":"Make Aero Control feel at home.");
        }
        async void Poll(){
            if(hardware==null||busy||commandBusy||exiting)return;
            busy=true;Control<Button>("CheckAgain").IsEnabled=false;UpdateFreshness();
            try {
                Snapshot p=await Task.Run(()=>hardware.Read());if(exiting)return;
                current=p;
                if(p.ServiceConnected&&(p.CpuTemp.HasValue||p.GpuTemp.HasValue||p.CpuRpm.HasValue||p.GpuRpm.HasValue))lastGood=p.Time;
                chart.Add(p);RefreshReadings();UpdateFeatures();
            } catch(Exception error){
                current=new Snapshot{Status="Reading failed: "+error.Message};RefreshReadings();UpdateFeatures();
            } finally{busy=false;nextPoll=DateTime.UtcNow.AddSeconds(interval);if(!exiting){Control<Button>("CheckAgain").IsEnabled=true;UpdateFreshness();}}
        }
        string Temp(double? value){return value.HasValue?(fahrenheit?value.Value*1.8+32:value.Value).ToString("0")+(fahrenheit?"°F":"°C"):"—";}
        string Usage(double? value){return value.HasValue?value.Value.ToString("0")+"% utilization":"Unavailable";}
        string Rpm(double? value){return value.HasValue?value.Value.ToString("N0")+" RPM":"Unavailable";}
        void RefreshReadings(){
            if(current==null)return;
            Text("CpuTemp",Temp(current.CpuTemp));Text("GpuTemp",Temp(current.GpuTemp));
            Text("CpuLoad",Usage(current.CpuLoad));Text("CpuRpm",Rpm(current.CpuRpm));Text("GpuRpm",Rpm(current.GpuRpm));
            Text("PowerPlan",current.PowerPlan??"Unavailable");
            RefreshPowerPlans();
            Text("CoolBoostState",current.CoolBoost.HasValue?current.CoolBoostStatus+(!current.FanControlSupported?" · verify this device to enable controls":""):
                "CoolBoost unavailable. This firmware did not return a recognized On or Off state.");
            Selected("BoostOn",current.CoolBoost==true);Selected("BoostOff",current.CoolBoost==false);
            var power=Forms.SystemInformation.PowerStatus;
            Text("PowerSource",power.PowerLineStatus==Forms.PowerLineStatus.Online?"Plugged in":power.PowerLineStatus==Forms.PowerLineStatus.Offline?"On battery":"Power unavailable");
            Text("ConnectionStatus",current.ServiceConnected?"Service connected":"Service unavailable");
            Control<System.Windows.Shapes.Ellipse>("StatusDot").Fill=current.ServiceConnected?green:amber;
            Text("NoticeText",DateTime.UtcNow<commandMessageUntil?commandMessage:current.Status);
            string mode=requestedMode??current.Mode;
            bool auto=mode=="Auto",max=mode=="Max",custom=mode=="Custom";
            Selected("AutoButton",auto);Selected("MaxButton",max);Selected("CustomButton",custom);
            string description=mode=="Auto"?"Auto adjusts cooling as temperatures change.":mode=="Max"?"Max requests maximum fan speed.":custom?"Set each fan below. Firmware minimums still apply.":"Choose a cooling mode above.";
            if(!current.FanControlSupported)description="Fan control needs device verification. Monitoring is available where supported.";
            Text("ModeDescription",description);
            Control<Border>("CustomPanel").Visibility=custom&&current.FanControlSupported?Visibility.Visible:Visibility.Collapsed;
            Control<Button>("VerifyFan").Visibility=current.FanControlSupported?Visibility.Collapsed:Visibility.Visible;
            Control<Button>("VerifyFan").IsEnabled=current.ServiceConnected&&!commandBusy;
            Control<Button>("VerifyOverview").Visibility=current.FanControlSupported?Visibility.Collapsed:Visibility.Visible;
            Control<Button>("VerifyOverview").IsEnabled=current.ServiceConnected&&!busy&&!commandBusy;
            UpdateFreshness();
        }
        void UpdateFreshness(){
            if(current==null)return;
            bool fresh=ValueRules.CanControl(current.ServiceConnected,current.FanControlSupported,current.Time,DateTime.UtcNow);
            foreach(string name in new[]{"AutoButton","MaxButton","CustomButton"})Control<Button>(name).IsEnabled=fresh&&!busy&&!commandBusy;
            Control<Button>("ApplyCustom").IsEnabled=fresh&&!busy&&!commandBusy&&(requestedMode??current.Mode)=="Custom";
            Control<Button>("VerifyOverview").IsEnabled=current.ServiceConnected&&!busy&&!commandBusy;
            bool boost=ValueRules.CanCoolBoost(current.ServiceConnected,current.FanControlSupported,current.CoolBoost,current.Time,DateTime.UtcNow);
            Control<Button>("BoostOn").IsEnabled=boost&&!busy&&!commandBusy&&current.CoolBoost!=true;
            Control<Button>("BoostOff").IsEnabled=boost&&!busy&&!commandBusy&&current.CoolBoost!=false;
            var chosen=Control<ComboBox>("PowerPlans").SelectedItem as PowerPlan;
            bool powerFresh=(DateTime.UtcNow-current.Time).TotalSeconds>=0&&(DateTime.UtcNow-current.Time).TotalSeconds<=15;
            Control<ComboBox>("PowerPlans").IsEnabled=!commandBusy&&powerFresh&&current.PowerPlans.Length>0;
            Control<Button>("ApplyPower").IsEnabled=!busy&&!commandBusy&&powerFresh&&chosen!=null&&current.PowerPlanId.HasValue&&chosen.Id!=current.PowerPlanId.Value;
            if(lastGood==DateTime.MinValue)Text("LastReading","No sensor readings yet");
            else{int seconds=(int)Math.Max(0,(DateTime.UtcNow-lastGood).TotalSeconds);Text("LastReading",seconds<2?"Readings updated just now":seconds<=15?"Last reading "+seconds+" seconds ago":"Readings stale · "+seconds+" seconds ago");}
        }
        void RefreshPowerPlans() {
            Text("PowerStatus",current.PowerStatus);
            var combo=Control<ComboBox>("PowerPlans");var selected=combo.SelectedItem as PowerPlan;
            bool changed=combo.Items.Count!=current.PowerPlans.Length;
            if(!changed)for(int i=0;i<current.PowerPlans.Length;i++) {
                var old=combo.Items[i] as PowerPlan;var next=current.PowerPlans[i];
                if(old==null||old.Id!=next.Id||old.Name!=next.Name){changed=true;break;}
            }
            Guid? desired=displayedPowerPlan!=current.PowerPlanId||selected==null?current.PowerPlanId:(Guid?)selected.Id;
            if(changed)combo.ItemsSource=current.PowerPlans;
            foreach(PowerPlan plan in combo.Items)if(desired.HasValue&&plan.Id==desired.Value){combo.SelectedItem=plan;break;}
            displayedPowerPlan=current.PowerPlanId;
        }
        async void RequestCoolBoost(bool enabled) {
            if(current==null||busy||commandBusy||!ValueRules.CanCoolBoost(current.ServiceConnected,current.FanControlSupported,current.CoolBoost,current.Time,DateTime.UtcNow))return;
            commandBusy=true;Text("NoticeText","Changing CoolBoost…");UpdateFreshness();
            try {
                string error=await Task.Run(()=>hardware.SetCoolBoost(enabled));
                commandMessage=error==null?"CoolBoost "+(enabled?"On":"Off")+" confirmed by firmware readback.":error;
            }catch(Exception e){commandMessage="CoolBoost request failed: "+e.Message;}
            finally {FinishExtraCommand();}
        }
        async void RequestPowerPlan() {
            if(current==null||busy||commandBusy)return;
            var plan=Control<ComboBox>("PowerPlans").SelectedItem as PowerPlan;if(plan==null)return;
            commandBusy=true;Text("NoticeText","Applying "+plan.Name+"…");UpdateFreshness();
            try {
                string error=await Task.Run(()=>WindowsPower.SetPlan(plan.Id));
                commandMessage=error==null?"Active Windows power plan: "+plan.Name+". Confirmed by Windows.":error;
            }catch(Exception e){commandMessage="Power plan request failed: "+e.Message;}
            finally {FinishExtraCommand();}
        }
        void FinishExtraCommand() {
            commandMessageUntil=DateTime.UtcNow.AddSeconds(12);commandBusy=false;
            if(!exiting){RefreshReadings();nextPoll=DateTime.MinValue;Poll();}
        }
        int CpuSpeed(){return (int)((Slider)window.FindName("CpuSlider")).Value;}
        int GpuSpeed(){return (int)((Slider)window.FindName("GpuSlider")).Value;}
        async void RequestMode(string mode){
            if(current==null||busy||commandBusy||!ValueRules.CanControl(current.ServiceConnected,current.FanControlSupported,current.Time,DateTime.UtcNow))return;
            int cpu=CpuSpeed(),gpu=GpuSpeed();
            commandBusy=true;Text("NoticeText","Requesting "+mode.ToLowerInvariant()+" cooling…");UpdateFreshness();
            try {
                string error=await Task.Run(()=>hardware.SetFanMode(mode));if(exiting)return;
                string speedError=null;
                if(error==null&&mode=="Custom")speedError=await Task.Run(()=>hardware.SetFanSpeed("CPU",cpu)??hardware.SetFanSpeed("GPU",gpu));
                if(error==null){requestedMode=mode;commandMessage=mode+" request accepted by Acer’s service. Watch the fan readings for the response.";}
                else{commandMessage="Fan mode was not changed: "+error;}
                if(speedError!=null)commandMessage="Fan mode set, but speeds were not applied: "+speedError;
                commandMessageUntil=DateTime.UtcNow.AddSeconds(12);Text("NoticeText",commandMessage);
            }catch(Exception error){commandMessage="Fan mode request failed: "+error.Message;commandMessageUntil=DateTime.UtcNow.AddSeconds(12);}
            finally{commandBusy=false;if(!exiting){RefreshReadings();nextPoll=DateTime.MinValue;Poll();}}
        }
        async void ApplyCustom(){
            if(current==null||busy||commandBusy||!ValueRules.CanControl(current.ServiceConnected,current.FanControlSupported,current.Time,DateTime.UtcNow))return;
            int cpu=CpuSpeed(),gpu=GpuSpeed();
            commandBusy=true;Text("NoticeText","Applying custom fan speeds…");UpdateFreshness();
            try {
                string error=await Task.Run(()=>hardware.SetFanSpeed("CPU",cpu)??hardware.SetFanSpeed("GPU",gpu));if(exiting)return;
                commandMessage=error==null?"Custom fan speeds applied. Watch the fan readings for the response.":"Fan speeds were not changed: "+error;
            }catch(Exception error){commandMessage="Fan speed request failed: "+error.Message;}
            commandMessageUntil=DateTime.UtcNow.AddSeconds(12);
            commandBusy=false;if(!exiting){RefreshReadings();nextPoll=DateTime.MinValue;Poll();}
        }
        async void VerifyFan(){
            if(hardware==null||busy||commandBusy)return;
            commandBusy=true;
            try {
                string error=await Task.Run(()=>hardware.Enroll());if(exiting)return;
                commandMessage=error==null?"Fan control verified on this laptop. Auto, Max and Custom are now available.":"Verification failed: "+error;
            }catch(Exception error){commandMessage="Verification failed: "+error.Message;}
            commandMessageUntil=DateTime.UtcNow.AddSeconds(12);Control<Button>("VerifyFan").IsEnabled=false;
            commandBusy=false;if(!exiting){nextPoll=DateTime.MinValue;Poll();}
        }
        void UpdateFeatures(){
            if(current==null)return;
            if(features==null){
                var rows=Control<StackPanel>("FeatureRows");
                features=new[]{
                    Feature(rows,"CPU monitoring","Temperature and utilization"),
                    Feature(rows,"GPU monitoring","Temperature and utilization"),
                    Feature(rows,"Fan speed readings","Reported CPU and GPU fan RPM"),
                    Feature(rows,"Auto / Max fan control","Established device support and a connected service"),
                    Feature(rows,"Custom fan speed","Per-fan percentages, on Overview"),
                    Feature(rows,"CoolBoost","Firmware readback and verified device required"),
                    Feature(rows,"Power profile switching","Installed Windows power plans, on Overview")};
            }
            FeatureStatus(features[0],current.CpuTemp.HasValue,"Reading available","No temperature reading");
            FeatureStatus(features[1],current.GpuTemp.HasValue,"Reading available","No temperature reading");
            FeatureStatus(features[2],ValueRules.FanTelemetrySufficient(current.CpuRpm,current.GpuRpm),"Reading available","No fan readings");
            FeatureStatus(features[3],current.FanControlSupported,"Device verified","Not verified on this device");
            FeatureStatus(features[4],current.FanControlSupported,"Available on this device","Not verified on this device");
            FeatureStatus(features[5],current.CoolBoost.HasValue&&current.FanControlSupported,"Available on this device",current.CoolBoost.HasValue?"Needs device verification":"Unavailable on this firmware");
            FeatureStatus(features[6],current.PowerPlans.Length>0&&current.PowerPlanId.HasValue,"Available in Windows","Unavailable in Windows");
        }
        TextBlock Feature(Panel parent,string title,string detail){
            var border=new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(51,65,85)),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(0,12,0,12)};
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});
            var labels=new StackPanel();labels.Children.Add(new TextBlock{Text=title,FontSize=14});labels.Children.Add(new TextBlock{Text=detail,FontSize=11,Foreground=muted,Margin=new Thickness(0,4,10,0)});
            var status=new TextBlock{VerticalAlignment=VerticalAlignment.Center,FontSize=12};Grid.SetColumn(status,1);grid.Children.Add(labels);grid.Children.Add(status);border.Child=grid;parent.Children.Add(border);
            return status;
        }
        void FeatureStatus(TextBlock status,bool ok,string yes,string no){status.Text=(ok?"●  ":"○  ")+(ok?yes:no);status.Foreground=ok?green:amber;}
        string Summary(){
            if(hardware==null)return "Device check has not completed.";
            return "Aero Control 0.3 support summary\n\nDevice: "+hardware.Device.Manufacturer+" "+hardware.Device.Model+
                "\nWindows: "+Environment.OSVersion.Version+"\nAcer WMI interface: "+hardware.Device.WmiAvailable+
                "\nService connected: "+(current!=null&&current.ServiceConnected)+"\nFan control verified: "+(current!=null&&current.FanControlSupported)+
                "\nCPU temperature available: "+(current!=null&&current.CpuTemp.HasValue)+"\nGPU temperature available: "+(current!=null&&current.GpuTemp.HasValue)+
                "\nCPU fan reading available: "+(current!=null&&current.CpuRpm.HasValue)+"\nGPU fan reading available: "+(current!=null&&current.GpuRpm.HasValue)+
                "\nCustom fan speed (per-fan percentages): implemented, gated on device verification\nCoolBoost: "+(current==null?"Checking":current.CoolBoostStatus)+
                "\nPower plan: "+(current==null?"Checking":current.PowerPlan)+"\n\nStatus: "+(current==null?"Checking":current.Status)+
                "\n\nNo serial number, device fingerprint or account information is included.";
        }
        void ShowSummary(){
            var dialog=new Window{Owner=window,Title="Support summary",Width=600,Height=490,Background=new SolidColorBrush(Color.FromRgb(21,27,38)),Foreground=Brushes.White,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var dock=new DockPanel{Margin=new Thickness(22)};var copy=new Button{Content="Copy summary",Padding=new Thickness(16,9,16,9),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};
            string summary=Summary();copy.Click+=(s,e)=>{try{Clipboard.SetText(summary);copy.Content="Copied";}catch{copy.Content="Clipboard busy — try again";}};DockPanel.SetDock(copy,Dock.Bottom);dock.Children.Add(copy);
            dock.Children.Add(new TextBox{Text=summary,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),FontSize=13});dialog.Content=dock;dialog.ShowDialog();
        }
        void Selected(string name,bool selected){var b=Control<Button>(name);b.Background=selected?new SolidColorBrush(Color.FromRgb(61,79,121)):new SolidColorBrush(Color.FromRgb(33,44,61));b.BorderBrush=selected?accent:new SolidColorBrush(Color.FromRgb(65,79,102));}
        void Range(int minutes){chart.Minutes=minutes;chart.InvalidateVisual();Selected("Range1",minutes==1);Selected("Range5",minutes==5);Selected("Range15",minutes==15);}
        void SetUnits(bool f){fahrenheit=f;chart.Fahrenheit=f;chart.InvalidateVisual();UpdateSettingsButtons();RefreshReadings();SaveSettings();}
        void SetInterval(int seconds){interval=seconds;nextPoll=DateTime.MinValue;UpdateSettingsButtons();SaveSettings();}
        void UpdateSettingsButtons(){Selected("CelsiusButton",!fahrenheit);Selected("FahrenheitButton",fahrenheit);Selected("Refresh2",interval==2);Selected("Refresh5",interval==5);Selected("Refresh10",interval==10);}
        void LoadSettings(){try{if(File.Exists(settingsPath)){var x=XElement.Load(settingsPath);fahrenheit=(string)x.Attribute("units")=="F";int n;if(int.TryParse((string)x.Attribute("interval"),out n)&&(n==2||n==5||n==10))interval=n;trayOnClose=(string)x.Attribute("tray")=="true";}}catch{}}
        void SaveSettings(){try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));new XElement("settings",new XAttribute("units",fahrenheit?"F":"C"),new XAttribute("interval",interval),new XAttribute("tray",trayOnClose?"true":"false")).Save(settingsPath);}catch(Exception e){Text("NoticeText","Could not save preferences: "+e.Message);}}
        void ConfigureTray(){
            tray=new Forms.NotifyIcon{Text="Aero Control",Icon=System.Drawing.SystemIcons.Application,Visible=false};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("Open Aero Control",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{window.Show();window.WindowState=WindowState.Normal;window.Activate();tray.Visible=false;})));
            menu.Items.Add("Exit",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{exiting=true;window.Close();})));tray.ContextMenuStrip=menu;
            tray.DoubleClick+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{window.Show();window.WindowState=WindowState.Normal;window.Activate();tray.Visible=false;}));
        }
    }
}
