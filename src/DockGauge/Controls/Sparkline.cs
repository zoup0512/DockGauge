using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace DockGauge.Controls;

/// <summary>
/// 轻量面积折线图：直接在 OnRender 里画，1~2 条序列，Y 轴按窗口内最大值自适应，
/// 用中点二次贝塞尔平滑，带渐变填充和底部基线。
/// </summary>
public class Sparkline : FrameworkElement
{
    private static readonly Color Blue = Color.FromRgb(0x2E, 0x6B, 0xF0);
    private static readonly Color Green = Color.FromRgb(0x10, 0xB9, 0x81);

    private static readonly Pen BluePen = CreatePen(Blue);
    private static readonly Pen GreenPen = CreatePen(Green);
    private static readonly Brush BlueFill = CreateFill(Blue);
    private static readonly Brush GreenFill = CreateFill(Green);
    private static readonly Pen BaselinePen = new(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF1)), 1);

    public IList<double> Series1 { get; set; } = new List<double>();
    public IList<double> Series2 { get; set; } = new List<double>();

    static Sparkline()
    {
        BaselinePen.Freeze();
    }

    private static Pen CreatePen(Color c)
    {
        var p = new Pen(new SolidColorBrush(c), 1.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        p.Freeze();
        return p;
    }

    private static Brush CreateFill(Color c)
    {
        var b = new LinearGradientBrush(
            Color.FromArgb(0x3C, c.R, c.G, c.B),
            Color.FromArgb(0x00, c.R, c.G, c.B),
            new Point(0, 0), new Point(0, 1));
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        var max = 1024.0;
        foreach (var v in Series1) max = Math.Max(max, v);
        foreach (var v in Series2) max = Math.Max(max, v);
        max *= 1.15;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        DrawSeries(dc, Series1, BluePen, BlueFill, w, h - 2, max);
        DrawSeries(dc, Series2, GreenPen, GreenFill, w, h - 2, max);
        dc.DrawLine(BaselinePen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        dc.Pop();
    }

    private static void DrawSeries(DrawingContext dc, IList<double> series, Pen pen, Brush fill, double w, double h, double max)
    {
        int n = series.Count;
        if (n == 0) return;

        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            var x = n == 1 ? 0 : w * i / (n - 1);
            var y = h - Math.Clamp(series[i] / max, 0, 1) * (h - 5);
            pts[i] = new Point(x, y);
        }

        var stroke = new StreamGeometry();
        var area = new StreamGeometry();
        using (var sg = stroke.Open())
        using (var ag = area.Open())
        {
            sg.BeginFigure(pts[0], false, false);
            ag.BeginFigure(new Point(pts[0].X, h), true, false);
            ag.LineTo(pts[0], true, false);

            for (int i = 1; i < n; i++)
            {
                if (i < n - 1)
                {
                    var mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                    sg.QuadraticBezierTo(pts[i], mid, true, false);
                    ag.QuadraticBezierTo(pts[i], mid, true, false);
                }
                else
                {
                    sg.QuadraticBezierTo(pts[i - 1], pts[i], true, false);
                    ag.QuadraticBezierTo(pts[i - 1], pts[i], true, false);
                }
            }

            ag.LineTo(new Point(pts[n - 1].X, h), true, false);
            ag.LineTo(new Point(pts[0].X, h), true, false);
        }

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, stroke);
    }
}
