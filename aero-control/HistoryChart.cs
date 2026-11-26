using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AeroControl {
    public sealed class HistoryChart : FrameworkElement {
        readonly List<Snapshot> history = new List<Snapshot>();
        public int Minutes = 5;
        public bool Fahrenheit;
        readonly Brush muted = new SolidColorBrush(Color.FromRgb(154,170,193));
        public void Add(Snapshot point) {
            history.Add(point);
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-16);
            history.RemoveAll(p => p.Time < cutoff);
            InvalidateVisual();
        }
        double Unit(double c) { return Fahrenheit ? c * 1.8 + 32 : c; }
        void Label(DrawingContext dc, string text, double x, double y, Brush color) {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, color), new Point(x,y));
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            if (width < 90 || height < 65) return;
            Rect plot = new Rect(40,12,width-50,height-43);
            DateTime now = DateTime.UtcNow;
            double low = 40, high = 80;
            foreach (var p in history) if ((now-p.Time).TotalMinutes <= Minutes) {
                foreach (double? v in new double?[]{p.CpuTemp,p.GpuTemp}) if(v.HasValue) {
                    low = Math.Min(low, Math.Floor(v.Value/20)*20);
                    high = Math.Max(high, Math.Ceiling(v.Value/20)*20);
                }
            }
            high = Math.Max(high, low+40);
            Pen grid = new Pen(new SolidColorBrush(Color.FromRgb(49,64,83)),1);
            for(int i=0;i<3;i++) {
                double value=low+(high-low)*i/2, y=plot.Bottom-plot.Height*i/2;
                dc.DrawLine(grid,new Point(plot.Left,y),new Point(plot.Right,y));
                Label(dc,Unit(value).ToString("0")+"°",0,y-7,muted);
            }
            for(int i=0;i<=5;i++) {
                double x=plot.Left+plot.Width*i/5;
                if(i>0&&i<5) dc.DrawLine(grid,new Point(x,plot.Top),new Point(x,plot.Bottom));
                string text=i==5?"Now":"−"+(Minutes*(5-i)/5.0).ToString("0.#")+"m";
                Label(dc,text,x-(i==0?0:14),plot.Bottom+10,muted);
            }
            DrawSeries(dc,plot,now,low,high,true,Color.FromRgb(155,174,255));
            DrawSeries(dc,plot,now,low,high,false,Color.FromRgb(231,182,144));
            bool any=history.Exists(p=>(now-p.Time).TotalMinutes<=Minutes&&(p.CpuTemp.HasValue||p.GpuTemp.HasValue));
            if(!any) Label(dc,"Temperature history appears as readings arrive",plot.Left+12,plot.Top+plot.Height/2,muted);
        }
        void DrawSeries(DrawingContext dc,Rect plot,DateTime now,double low,double high,bool cpu,Color color) {
            Pen pen=new Pen(new SolidColorBrush(color),2); Point? previous=null; DateTime previousTime=DateTime.MinValue;
            dc.PushClip(new RectangleGeometry(plot));
            foreach(var p in history) {
                double age=(now-p.Time).TotalSeconds;
                if(age<0||age>Minutes*60) continue;
                double? value=cpu?p.CpuTemp:p.GpuTemp;
                if(!value.HasValue){previous=null;continue;}
                Point point=new Point(plot.Right-age/(Minutes*60.0)*plot.Width,plot.Bottom-(value.Value-low)/(high-low)*plot.Height);
                if(previous.HasValue&&(p.Time-previousTime).TotalSeconds<=30) dc.DrawLine(pen,previous.Value,point);
                else dc.DrawEllipse(pen.Brush,null,point,2,2);
                previous=point;previousTime=p.Time;
            }
            dc.Pop();
        }
    }
}
