using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CH_V03.App.ViewModels;

namespace CH_V03.App.Analysis;

/// <summary>测试值及每条记录上下限的轻量级趋势绘图控件。</summary>
public sealed class TestTrendChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<TestTrendSampleViewModel>), typeof(TestTrendChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSamplesChanged));
    public IReadOnlyList<TestTrendSampleViewModel>? Samples { get => (IReadOnlyList<TestTrendSampleViewModel>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (TestTrendChart)d;
        if (e.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, chart.OnCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, chart.OnCollectionChanged);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var plot = new Rect(58, 18, Math.Max(1, ActualWidth - 72), Math.Max(1, ActualHeight - 48));
        var muted = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(224, 242, 254)), 1);
        var samples = Samples?.ToList() ?? [];
        var numericSamples = samples.Where(sample => sample.NumericValue.HasValue).ToList();
        void Label(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        if (numericSamples.Count == 0) { Label("没有可绘制的数值型测试值", plot.Left + 12, plot.Top + plot.Height / 2); return; }

        var allValues = numericSamples.Select(sample => sample.NumericValue!.Value)
            .Concat(samples.Where(sample => sample.NumericLowerLimit.HasValue).Select(sample => sample.NumericLowerLimit!.Value))
            .Concat(samples.Where(sample => sample.NumericUpperLimit.HasValue).Select(sample => sample.NumericUpperLimit!.Value)).ToList();
        var min = allValues.Min();
        var max = allValues.Max();
        var padding = Math.Max(Math.Abs(max - min) * 0.1, Math.Max(Math.Abs(max) * 0.02, 0.01));
        min -= padding;
        max += padding;
        var span = Math.Max(max - min, 0.001);
        double X(int sequence) => samples.Count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * (sequence - 1) / (samples.Count - 1);
        double Y(double value) => plot.Bottom - Math.Clamp((value - min) / span, 0, 1) * plot.Height;
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Label((min + span * i / 4).ToString("0.###"), 2, y - 7);
        }

        DrawSeries(dc, samples.Where(sample => sample.NumericLowerLimit.HasValue).Select(sample => (sample.Sequence, sample.NumericLowerLimit!.Value)), X, Y, new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 1.2) { DashStyle = DashStyles.Dash });
        DrawSeries(dc, samples.Where(sample => sample.NumericUpperLimit.HasValue).Select(sample => (sample.Sequence, sample.NumericUpperLimit!.Value)), X, Y, new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 1.2) { DashStyle = DashStyles.Dash });
        DrawSeries(dc, numericSamples.Select(sample => (sample.Sequence, sample.NumericValue!.Value)), X, Y, new Pen(Brushes.DodgerBlue, 1.8));
        foreach (var sample in numericSamples)
        {
            var point = new Point(X(sample.Sequence), Y(sample.NumericValue!.Value));
            dc.DrawEllipse(sample.IsAbnormal ? Brushes.Red : Brushes.DodgerBlue, null, point, 3.2, 3.2);
            if (sample.Sequence == 1 || sample.Sequence == samples.Count || sample.Sequence % Math.Max(1, samples.Count / 5) == 0) Label(sample.Sequence.ToString(), point.X - 4, plot.Bottom + 4);
        }
    }

    private static void DrawSeries(DrawingContext dc, IEnumerable<(int Sequence, double Value)> values, Func<int, double> x, Func<double, double> y, Pen pen)
    {
        var points = values.ToList();
        if (points.Count == 0) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x(points[0].Sequence), y(points[0].Value)), false, false);
            for (var i = 1; i < points.Count; i++) context.LineTo(new Point(x(points[i].Sequence), y(points[i].Value)), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
