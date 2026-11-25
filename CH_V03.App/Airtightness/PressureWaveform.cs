using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CH_V03.Core.Airtightness;

namespace CH_V03.App.Airtightness;

public sealed class PressureWaveform : FrameworkElement
{
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(nameof(Channel), typeof(int), typeof(PressureWaveform), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender), value => value is 1 or 2);
    public int Channel { get => (int)GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(ObservableCollection<PressureSample>), typeof(PressureWaveform), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSamplesChanged));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(35d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillSecondsProperty = DependencyProperty.Register(nameof(FillSeconds), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BalanceSecondsProperty = DependencyProperty.Register(nameof(BalanceSeconds), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TestSecondsProperty = DependencyProperty.Register(nameof(TestSeconds), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ExhaustSecondsProperty = DependencyProperty.Register(nameof(ExhaustSeconds), typeof(double), typeof(PressureWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public ObservableCollection<PressureSample>? Samples { get => (ObservableCollection<PressureSample>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double FillSeconds { get => (double)GetValue(FillSecondsProperty); set => SetValue(FillSecondsProperty, value); }
    public double BalanceSeconds { get => (double)GetValue(BalanceSecondsProperty); set => SetValue(BalanceSecondsProperty, value); }
    public double TestSeconds { get => (double)GetValue(TestSecondsProperty); set => SetValue(TestSecondsProperty, value); }
    public double ExhaustSeconds { get => (double)GetValue(ExhaustSecondsProperty); set => SetValue(ExhaustSecondsProperty, value); }
    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (PressureWaveform)d;
        if (e.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, chart.OnCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, chart.OnCollectionChanged);
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var plot = new Rect(58, 38, Math.Max(1, ActualWidth - 78), Math.Max(1, ActualHeight - 68));
        var max = double.IsFinite(Maximum) && Maximum > 0 ? Maximum : 35;
        var duration = double.IsFinite(Duration) && Duration > 0 ? Duration : 12;
        var muted = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(224, 242, 254)), 1);
        void Label(string value, double x, double y) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 11, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        Label("压力 / kPa", 0, 0);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Label((max * i / 4).ToString("0.#"), 4, y - 7);
        }
        var stages = new[]
        {
            (Name: "充气", Seconds: Math.Max(0, FillSeconds)),
            (Name: "平衡", Seconds: Math.Max(0, BalanceSeconds)),
            (Name: "测试", Seconds: Math.Max(0, TestSeconds)),
            (Name: "泄气", Seconds: Math.Max(0, ExhaustSeconds))
        };
        var stageTotal = stages.Sum(stage => stage.Seconds);
        if (stageTotal > 0 && Math.Abs(stageTotal - duration) < 0.01)
        {
            var stageLine = new Pen(new SolidColorBrush(Color.FromRgb(125, 211, 252)), 1) { DashStyle = DashStyles.Dash };
            var seconds = 0d;
            foreach (var stage in stages)
            {
                var start = seconds;
                seconds += stage.Seconds;
                var startX = plot.Left + plot.Width * start / duration;
                var endX = plot.Left + plot.Width * seconds / duration;
                if (start > 0) dc.DrawLine(stageLine, new Point(startX, plot.Top), new Point(startX, plot.Bottom));
                Label($"{stage.Name} {stage.Seconds:0.#} s", Math.Max(plot.Left, (startX + endX) / 2 - 23), 18);
                Label(start.ToString("0.#"), startX - 8, plot.Bottom + 5);
            }
            Label(duration.ToString("0.#"), plot.Right - 8, plot.Bottom + 5);
        }
        else
        {
            for (var i = 0; i <= 6; i++)
            {
                var x = plot.Left + plot.Width * i / 6;
                dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
                Label((duration * i / 6).ToString("0.#"), x - 8, plot.Bottom + 5);
            }
        }
        Label("时间 / s", Math.Max(plot.Left, plot.Right - 50), 0);
        if (Samples is not { Count: > 0 }) { Label("等待测试数据 · 点击“模拟测试”预览波形", plot.Left + 12, plot.Top + plot.Height / 2); return; }
        dc.PushClip(new RectangleGeometry(plot));
        {
            var channel = Channel;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                foreach (var sample in Samples)
                {
                    var pressure = channel == 1 ? sample.Channel1 : sample.Channel2;
                    var point = new Point(plot.Left + Math.Clamp(sample.Seconds / duration, 0, 1) * plot.Width, plot.Bottom - Math.Clamp(pressure / max, 0, 1) * plot.Height);
                    if (first) { context.BeginFigure(point, false, false); first = false; }
                    else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            var pen = new Pen(channel == 1 ? Brushes.DodgerBlue : Brushes.DarkOrange, 2);
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.Pop();
    }
}
