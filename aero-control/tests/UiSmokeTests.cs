using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using AeroControl;
class UiSmokeTests {
    static void Require(bool ok,string message) {if(!ok)throw new Exception(message);}
    static void Call(App app,string name,params object[] args) {typeof(App).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,args);}
    static void Set(App app,string name,object value) {typeof(App).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,value);}
    static T Find<T>(Window window,string name) where T:class {var value=window.FindName(name) as T;Require(value!=null,"Missing control: "+name);return value;}
    static void Render(Window window,string path,int width,int height) {
        var root=(FrameworkElement)window.Content;
            ((Border)root).Background=window.Background;
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(path))encoder.Save(stream);
    }
    [STAThread] static int Main(string[] args) {
        try {
            var app=new App();Window window;
            using(var stream=typeof(App).Assembly.GetManifestResourceStream("AeroControl.MainWindow.xaml"))window=(Window)XamlReader.Load(stream);
            Set(app,"window",window);
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(1280,820));root.Arrange(new Rect(0,0,1280,820));root.UpdateLayout();
            var overview=Find<FrameworkElement>(window,"OverviewPage");
            Require(Find<Button>(window,"BoostOn").IsDescendantOf(overview),"CoolBoost must be inline on Overview");
            Require(Find<Button>(window,"ApplyCustom").IsDescendantOf(overview),"Custom fan sliders must apply on Overview");
            Require(Find<ComboBox>(window,"PowerPlans").IsDescendantOf(overview),"Power plans must be selectable on Overview");
            Guid balanced=new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),acer=new Guid("4fab1064-5cb4-47ab-ba83-bfd87af0e266");
            var snapshot=new Snapshot {PowerPlanId=acer,PowerPlan="Acer",PowerPlans=new[]{new PowerPlan(balanced,"Balanced"),new PowerPlan(acer,"Acer")},CoolBoostStatus="CoolBoost unavailable: unrecognized firmware state (0x6FF00)"};
            Set(app,"current",snapshot);Call(app,"RefreshReadings");Call(app,"UpdateFeatures");Call(app,"Navigate","Overview");
            Require(overview.Visibility==Visibility.Visible,"Overview must remain visible while adjusting controls");
            Require(Find<ComboBox>(window,"PowerPlans").IsEnabled,"Windows controls must work without an Acer connection");
            Require(!Find<Button>(window,"BoostOn").IsEnabled&&!Find<Button>(window,"BoostOff").IsEnabled,"Unknown CoolBoost must disable both buttons");
            var combo=Find<ComboBox>(window,"PowerPlans");combo.SelectedIndex=0;Call(app,"UpdateFreshness");
            Require(Find<Button>(window,"ApplyPower").IsEnabled,"Selecting a different installed plan must enable Apply");
            Set(app,"busy",true);Call(app,"UpdateFreshness");
            Require(!Find<Button>(window,"ApplyPower").IsEnabled,"Apply must disable during polling so a click cannot be silently discarded");
            Set(app,"busy",false);Call(app,"UpdateFreshness");
            Call(app,"RefreshReadings");Require(((PowerPlan)combo.SelectedItem).Id==balanced,"Polling must preserve a pending plan selection");
            snapshot.PowerPlanId=balanced;snapshot.PowerPlan="Balanced";Call(app,"RefreshReadings");
            Require(!Find<Button>(window,"ApplyPower").IsEnabled,"Readback of the chosen active plan must disable redundant Apply");
            snapshot.Time=DateTime.UtcNow.AddSeconds(-20);Call(app,"UpdateFreshness");Require(!combo.IsEnabled,"Stale power data must disable the selector");
            snapshot.Time=DateTime.UtcNow;snapshot.ServiceConnected=true;snapshot.FanControlSupported=true;snapshot.CoolBoost=false;snapshot.CoolBoostStatus="Off · firmware readback";
            Call(app,"RefreshReadings");Require(Find<Button>(window,"BoostOn").IsEnabled&&!Find<Button>(window,"BoostOff").IsEnabled,"Known Off with verification must allow On only");
            snapshot.CoolBoost=ValueRules.CoolBoostState(0x60300UL);snapshot.CoolBoostStatus="On · firmware readback";
            Call(app,"RefreshReadings");Require(!Find<Button>(window,"BoostOn").IsEnabled&&Find<Button>(window,"BoostOff").IsEnabled,"Verified Aspire On readback must allow disabling CoolBoost");
            snapshot.CpuTemp=64;snapshot.GpuTemp=55;snapshot.CpuLoad=12;snapshot.CpuRpm=3200;snapshot.GpuRpm=3500;snapshot.Mode="Auto";snapshot.Status="Test fixture · UI verification";
            Call(app,"RefreshReadings");
            string output=args.Length>0?args[0]:Environment.CurrentDirectory;Directory.CreateDirectory(output);
            Render(window,Path.Combine(output,"overview-1280.png"),1280,820);
            Render(window,Path.Combine(output,"overview-1040.png"),1040,640);
            Require(Find<ScrollViewer>(window,"OverviewPage").ScrollableHeight<1,"Standard Overview controls and readings must fit in the compact window");
            Set(app,"requestedMode","Custom");Call(app,"RefreshReadings");
            Require(Find<Border>(window,"CustomPanel").Visibility==Visibility.Visible,"Custom must expand the sliders directly on Overview");
            Require(Find<Button>(window,"ApplyCustom").IsEnabled,"Verified Custom mode must enable its inline Apply button");
            Require(overview.Visibility==Visibility.Visible,"Custom selection must keep Overview visible");
            Render(window,Path.Combine(output,"overview-custom-1280.png"),1280,820);
            Require(Find<ScrollViewer>(window,"OverviewPage").ScrollableHeight<1,"All controls including Custom must fit at the default window size");
            Render(window,Path.Combine(output,"overview-custom-1040.png"),1040,640);
            var scroll=Find<ScrollViewer>(window,"OverviewPage");scroll.ScrollToBottom();
            Render(window,Path.Combine(output,"overview-custom-1040-bottom.png"),1040,640);
            Require(scroll.ScrollableWidth<1,"Compact Overview must not require horizontal scrolling");
            Call(app,"Navigate","Device");Require(Find<FrameworkElement>(window,"DevicePage").Visibility==Visibility.Visible,"Support navigation must still work");
            Call(app,"Navigate","Overview");Require(overview.Visibility==Visibility.Visible,"Returning to Overview must preserve controls");
            Console.WriteLine("PASS: inline Overview controls, Custom expansion, CoolBoost firmware states, independent power controls, selection/freshness/polling gates; five dashboard renders");
            app.Shutdown();return 0;
        }catch(Exception e){Console.Error.WriteLine("FAIL: "+e);return 1;}
    }
}
