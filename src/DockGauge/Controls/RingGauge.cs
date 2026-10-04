using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace DockGauge.Controls;

/// <summary>环形进度仪表：灰色轨道 + 蓝色圆头弧线，中心两行文字（标签 / 数值）。</summary>
public class RingGauge : FrameworkElement
{
    private const double StrokeW = 8;

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, (o, _) => ((RingGauge)o).InvalidateVisual()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(RingGauge), new FrameworkPropertyMetadata("", (o, _) => ((RingGauge)o).InvalidateVisual()));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(RingGauge), new FrameworkPropertyMetadata("", (o, _) => ((RingGauge)o).InvalidateVisual()));

    /// <summary>0 ~ 1</summary>
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }

    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0xED, 0xF3));
    private static readonly Brush ProgressBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0xF0));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6));
    private static readonly Brush ValueBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x1D));

    static RingGauge()
    {
        TrackBrush.Freeze();
        ProgressBrush.Freeze();
        LabelBrush.Freeze();
        ValueBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        var cx = w / 2;
        var cy = h / 2;
        var r = Math.Min(w, h) / 2 - StrokeW / 2 - 0.5;

        var trackPen = new Pen(TrackBrush, StrokeW);
        dc.DrawEllipse(null, trackPen, new Point(cx, cy), r, r);

        var pct = Math.Clamp(Percent, 0, 1);
        if (pct > 0.0005)
        {
            var sweep = Math.Min(Math.Max(pct * 360, 12), 359.6); // 小值时画成圆点，满值避免接头重叠
            var startRad = -Math.PI / 2;
            var endRad = startRad + sweep * Math.PI / 180;
            var start = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            var end = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            var pen = new Pen(ProgressBrush, StrokeW) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var boldTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        if (!string.IsNullOrEmpty(Label))
        {
            var ft = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12.5, LabelBrush, dpi);
            dc.DrawText(ft, new Point(cx - ft.WidthIncludingTrailingWhitespace / 2, cy - 17.5));
        }
        if (!string.IsNullOrEmpty(ValueText))
        {
            var ft = new FormattedText(ValueText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, boldTypeface, 14.5, ValueBrush, dpi);
            dc.DrawText(ft, new Point(cx - ft.WidthIncludingTrailingWhitespace / 2, cy + 1.5));
        }
    }
}
