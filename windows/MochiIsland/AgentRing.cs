using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;
using Windows.UI;

namespace MochiIsland;

/// <summary>
/// The macOS status ring, ported 1:1 from AgentMarks.swift: a ring of ten blue dots
/// orbiting while a task runs, and a gapped ring with a check (done) or cross
/// (wait/fail) in the gap afterwards. Geometry is in AgentRow = 44 units.
/// </summary>
internal static class AgentRing
{
    private static readonly Color Thinking = Color.FromArgb(255, 51, 128, 255);
    private static readonly Color Stuck = Color.FromArgb(255, 255, 59, 48);
    private static readonly Color Finished = Color.FromArgb(255, 46, 186, 92);

    public static FrameworkElement? Create(string status, double row)
    {
        if (status is not ("running" or "done" or "wait" or "fail")) return null;
        var unit = row / 44.0;
        if (status == "running") return Dots(row);
        return CornerRing(row, unit, status == "done" ? Finished : Stuck, status == "done");
    }

    private static readonly List<DispatcherQueueTimer> running = new();

    /// <summary>Stop the dots timers before the shell rebuilds its children.</summary>
    public static void Stop()
    {
        foreach (var timer in running) timer.Stop();
        running.Clear();
    }

    private static FrameworkElement Dots(double row)
    {
        var unit = row / 44.0;
        var canvas = new Canvas { Width = row, Height = row, IsHitTestVisible = false };
        var brush = new SolidColorBrush(Thinking);
        var dots = new List<Ellipse>();
        for (var index = 0; index < 10; index++)
        {
            var dot = new Ellipse { Fill = brush, Width = 0, Height = 0 };
            dots.Add(dot);
            canvas.Children.Add(dot);
        }
        var center = row / 2;
        var start = Environment.TickCount64;
        var timer = canvas.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            var t = (Environment.TickCount64 - start) / 1000.0;
            var intro = Math.Clamp(t / 0.45, 0, 1);
            var ease = intro * intro * (3 - 2 * intro); // one-shot 0.45s growth
            var turn = (t / 2.4 % 1.0) * 2 * Math.PI;
            var radius = 19.15 * unit * ease;
            var size = 2.7 * unit * ease;
            for (var index = 0; index < dots.Count; index++)
            {
                var angle = turn + index * 2 * Math.PI / 10.0;
                Canvas.SetLeft(dots[index], center + Math.Cos(angle) * radius - size / 2);
                Canvas.SetTop(dots[index], center + Math.Sin(angle) * radius - size / 2);
                dots[index].Width = size;
                dots[index].Height = size;
            }
        };
        // The ring is long-lived: run while it is in the tree only.
        canvas.Loaded += (_, _) =>
        {
            if (!running.Contains(timer)) running.Add(timer);
            timer.Start();
        };
        canvas.Unloaded += (_, _) =>
        {
            timer.Stop();
            running.Remove(timer);
        };
        return canvas;
    }

    private static FrameworkElement CornerRing(double row, double unit, Color color, bool check)
    {
        var radius = 19.15 * unit;
        var stroke = 1.9 * unit;
        var arm = 2.85 * unit;
        var center = row / 2;
        var gapCenter = Math.PI / 4;
        var gap = 58 * Math.PI / 180;
        var brush = new SolidColorBrush(color);
        var canvas = new Canvas { Width = row, Height = row, IsHitTestVisible = false };

        var ring = new Path
        {
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        var figure = new PathFigure { StartPoint = PointAt(center, radius, gapCenter + gap / 2), IsClosed = false };
        var line = new PolyLineSegment();
        const int steps = 96;
        var sweep = Math.PI * 2 - gap;
        for (var step = 0; step <= steps; step++)
        {
            line.Points.Add(PointAt(center, radius, gapCenter + gap / 2 + sweep * step / steps));
        }
        figure.Segments.Add(line);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ring.Data = geometry;
        canvas.Children.Add(ring);

        var badge = PointAt(center, radius, gapCenter);
        var mark = new Path
        {
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        var markFigure = new PathFigure { IsClosed = false };
        var markLines = new PolyLineSegment();
        if (check)
        {
            markFigure.StartPoint = new Point(badge.X - arm, badge.Y + arm * 0.08);
            markLines.Points.Add(new Point(badge.X - arm * 0.28, badge.Y + arm * 0.78));
            markLines.Points.Add(new Point(badge.X + arm * 1.08, badge.Y - arm * 0.82));
            markFigure.Segments.Add(markLines);
        }
        else
        {
            markFigure.StartPoint = new Point(badge.X - arm, badge.Y - arm);
            markLines.Points.Add(new Point(badge.X + arm, badge.Y + arm));
            markFigure.Segments.Add(markLines);
            var second = new PathFigure { IsClosed = false, StartPoint = new Point(badge.X + arm, badge.Y - arm) };
            var secondLines = new PolyLineSegment();
            secondLines.Points.Add(new Point(badge.X - arm, badge.Y + arm));
            second.Segments.Add(secondLines);
            var markGeometry = new PathGeometry();
            markGeometry.Figures.Add(markFigure);
            markGeometry.Figures.Add(second);
            mark.Data = markGeometry;
        }
        if (check)
        {
            var markGeometry = new PathGeometry();
            markGeometry.Figures.Add(markFigure);
            mark.Data = markGeometry;
        }
        canvas.Children.Add(mark);

        var compositor = ElementCompositionPreview.GetElementVisual(canvas).Compositor;
        var ringVisual = ElementCompositionPreview.GetElementVisual(canvas);
        ringVisual.CenterPoint = new Vector3((float)center, (float)center, 0);
        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0f, new Vector3(0.55f));
        pop.InsertKeyFrame(0.55f, new Vector3(1.08f));
        pop.InsertKeyFrame(1f, Vector3.One);
        pop.Duration = TimeSpan.FromMilliseconds(420);
        ringVisual.StartAnimation("Scale", pop);

        var breathe = compositor.CreateVector3KeyFrameAnimation();
        breathe.InsertKeyFrame(0f, Vector3.One);
        breathe.InsertKeyFrame(0.5f, new Vector3(1.12f));
        breathe.InsertKeyFrame(1f, Vector3.One);
        breathe.Duration = TimeSpan.FromSeconds(1.8);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        var markVisual = ElementCompositionPreview.GetElementVisual(mark);
        markVisual.CenterPoint = new Vector3((float)badge.X, (float)badge.Y, 0);
        markVisual.StartAnimation("Scale", breathe);
        return canvas;
    }

    private static Point PointAt(double center, double radius, double angle)
    {
        return new Point(center + Math.Cos(angle) * radius, center + Math.Sin(angle) * radius);
    }
}
