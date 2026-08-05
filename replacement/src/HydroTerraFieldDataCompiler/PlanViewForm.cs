using System.Drawing.Drawing2D;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public sealed class PlanViewForm : Form
{
    private readonly PlanViewCanvas _canvas;

    public void UpdateResults(IEnumerable<LineCoverageResult> results, bool preserveView = true)
    {
        _canvas.UpdateResults(results.ToList(), preserveView);
    }

    public PlanViewForm(IEnumerable<LineCoverageResult> results)
    {
        Text = "Survey Line Plan View";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 650);
        Size = new Size(1200, 800);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false,
            BackColor = SystemColors.ControlLight
        };
        var zoomExtents = new Button { Text = "Zoom Extents", AutoSize = true };
        var reset = new Button { Text = "Reset View", AutoSize = true };
        var help = new Label
        {
            Text = "Mouse wheel: zoom   |   Left-drag: pan   |   Double-click: zoom extents",
            AutoSize = true,
            Margin = new Padding(18, 7, 0, 0)
        };
        toolbar.Controls.Add(zoomExtents);
        toolbar.Controls.Add(reset);
        toolbar.Controls.Add(help);

        _canvas = new PlanViewCanvas(results.ToList()) { Dock = DockStyle.Fill };
        zoomExtents.Click += (_, _) => _canvas.ZoomExtents();
        reset.Click += (_, _) => _canvas.ZoomExtents();

        Controls.Add(_canvas);
        Controls.Add(toolbar);
        Shown += (_, _) => _canvas.ZoomExtents();
    }
}

internal sealed class PlanViewCanvas : Control
{
    private List<LineCoverageResult> _results;
    private double _scale = 1.0;
    private double _centerX;
    private double _centerY;
    private bool _panning;
    private Point _lastMouse;

    public PlanViewCanvas(List<LineCoverageResult> results)
    {
        _results = results;
        DoubleBuffered = true;
        BackColor = Color.White;
        Cursor = Cursors.Cross;
        TabStop = true;
        MouseWheel += CanvasMouseWheel;
        MouseDown += CanvasMouseDown;
        MouseMove += CanvasMouseMove;
        MouseUp += (_, _) => { _panning = false; Cursor = Cursors.Cross; };
        MouseDoubleClick += (_, _) => ZoomExtents();
        Resize += (_, _) => Invalidate();
    }

    public void UpdateResults(List<LineCoverageResult> results, bool preserveView = true)
    {
        _results = results;
        if (!preserveView) ZoomExtents();
        else Invalidate();
    }

    public void ZoomExtents()
    {
        var points = new List<(double X, double Y)>();
        foreach (var r in _results)
        {
            points.Add((r.StartX, r.StartY));
            points.Add((r.EndX, r.EndY));
            points.AddRange(r.TrackPoints.Select(p => (p.X, p.Y)));
        }
        if (points.Count == 0 || ClientSize.Width < 20 || ClientSize.Height < 20) return;
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        _centerX = (minX + maxX) / 2.0;
        _centerY = (minY + maxY) / 2.0;
        double width = Math.Max(maxX - minX, 1.0);
        double height = Math.Max(maxY - minY, 1.0);
        _scale = Math.Min((ClientSize.Width - 80.0) / width, (ClientSize.Height - 100.0) / height);
        if (!double.IsFinite(_scale) || _scale <= 0) _scale = 1.0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.White);
        DrawGrid(e.Graphics);

        using var plannedPen = new Pen(Color.SteelBlue, 2.0f);
        using var trackPen = new Pen(Color.ForestGreen, 1.5f);
        using var offlinePen = new Pen(Color.Firebrick, 2.5f);
        using var gapPen = new Pen(Color.DarkOrange, 5.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var endpointBrush = new SolidBrush(Color.SteelBlue);
        using var offlineBrush = new SolidBrush(Color.Firebrick);
        using var integrityBrush = new SolidBrush(Color.MediumPurple);
        using var labelBrush = new SolidBrush(Color.Black);
        using var labelFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);

        foreach (var r in _results)
        {
            PointF start = WorldToScreen(r.StartX, r.StartY);
            PointF end = WorldToScreen(r.EndX, r.EndY);
            e.Graphics.DrawLine(plannedPen, start, end);
            e.Graphics.FillEllipse(endpointBrush, start.X - 3, start.Y - 3, 6, 6);
            e.Graphics.FillEllipse(endpointBrush, end.X - 3, end.Y - 3, 6, 6);

            if (r.TrackPoints.Count > 1)
            {
                PointF[] track = r.TrackPoints.Select(p => WorldToScreen(p.X, p.Y)).ToArray();
                e.Graphics.DrawLines(trackPen, track);
            }
            foreach (var p in r.OfflinePoints)
            {
                PointF s = WorldToScreen(p.X, p.Y);
                e.Graphics.FillEllipse(offlineBrush, s.X - 3.5f, s.Y - 3.5f, 7, 7);
            }
            if (r.NavigationIntegrityHasWarning && r.TrackPoints.Count > 0)
            {
                var marker = r.TrackPoints[r.TrackPoints.Count / 2];
                PointF m = WorldToScreen(marker.X, marker.Y);
                e.Graphics.FillRectangle(integrityBrush, m.X - 4, m.Y - 4, 8, 8);
            }
            foreach (var gap in r.Gaps)
            {
                var a = PointAtChainage(r, gap.StartChainage);
                var b = PointAtChainage(r, gap.EndChainage);
                e.Graphics.DrawLine(gapPen, WorldToScreen(a.X, a.Y), WorldToScreen(b.X, b.Y));
            }

            PointF midpoint = new((start.X + end.X) / 2F, (start.Y + end.Y) / 2F);
            e.Graphics.DrawString(r.LineName, labelFont, labelBrush, midpoint.X + 5, midpoint.Y + 5);
        }

        DrawLegend(e.Graphics, plannedPen, trackPen, gapPen, offlineBrush, integrityBrush);
        if (_results.Count == 0)
        {
            using var emptyFont = new Font(Font.FontFamily, 12F, FontStyle.Italic);
            e.Graphics.DrawString("No line-analysis results are available. Run Analyze Lines first.", emptyFont, Brushes.DimGray, 30, 30);
        }
    }

    private void DrawGrid(Graphics g)
    {
        if (_scale <= 0) return;
        double targetWorld = 100.0 / _scale;
        double step = NiceStep(targetWorld);
        if (step <= 0) return;
        var topLeft = ScreenToWorld(new PointF(0, 0));
        var bottomRight = ScreenToWorld(new PointF(ClientSize.Width, ClientSize.Height));
        double minX = Math.Min(topLeft.X, bottomRight.X), maxX = Math.Max(topLeft.X, bottomRight.X);
        double minY = Math.Min(topLeft.Y, bottomRight.Y), maxY = Math.Max(topLeft.Y, bottomRight.Y);
        using var gridPen = new Pen(Color.FromArgb(235, 235, 235), 1);
        for (double x = Math.Floor(minX / step) * step; x <= maxX; x += step)
        {
            PointF a = WorldToScreen(x, minY), b = WorldToScreen(x, maxY);
            g.DrawLine(gridPen, a, b);
        }
        for (double y = Math.Floor(minY / step) * step; y <= maxY; y += step)
        {
            PointF a = WorldToScreen(minX, y), b = WorldToScreen(maxX, y);
            g.DrawLine(gridPen, a, b);
        }
    }

    private static double NiceStep(double value)
    {
        if (value <= 0 || !double.IsFinite(value)) return 1;
        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double scaled = value / power;
        double nice = scaled < 2 ? 1 : scaled < 5 ? 2 : 5;
        return nice * power;
    }

    private void DrawLegend(Graphics g, Pen planned, Pen track, Pen gap, Brush offline, Brush integrity)
    {
        const int x = 16, y = 16, row = 23;
        using var background = new SolidBrush(Color.FromArgb(235, Color.White));
        using var border = new Pen(Color.LightGray);
        g.FillRectangle(background, x - 8, y - 8, 225, 127);
        g.DrawRectangle(border, x - 8, y - 8, 225, 127);
        g.DrawLine(planned, x, y + 5, x + 32, y + 5); g.DrawString("Planned line", Font, Brushes.Black, x + 40, y - 2);
        g.DrawLine(track, x, y + row + 5, x + 32, y + row + 5); g.DrawString("Collected track", Font, Brushes.Black, x + 40, y + row - 2);
        g.DrawLine(gap, x, y + row * 2 + 5, x + 32, y + row * 2 + 5); g.DrawString("Unsurveyed gap", Font, Brushes.Black, x + 40, y + row * 2 - 2);
        g.FillEllipse(offline, x + 12, y + row * 3 + 1, 9, 9); g.DrawString("Offline position", Font, Brushes.Black, x + 40, y + row * 3 - 2);
        g.FillRectangle(integrity, x + 12, y + row * 4 + 1, 9, 9); g.DrawString("Navigation warning", Font, Brushes.Black, x + 40, y + row * 4 - 2);
    }

    private void CanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        Focus();
        var before = ScreenToWorld(e.Location);
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        _scale = Math.Clamp(_scale * factor, 0.000001, 1000000.0);
        var after = ScreenToWorld(e.Location);
        _centerX += before.X - after.X;
        _centerY += before.Y - after.Y;
        Invalidate();
    }

    private void CanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _panning = true;
        _lastMouse = e.Location;
        Cursor = Cursors.Hand;
    }

    private void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_panning || _scale <= 0) return;
        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;
        _centerX -= dx / _scale;
        _centerY += dy / _scale;
        _lastMouse = e.Location;
        Invalidate();
    }

    private PointF WorldToScreen(double x, double y) => new(
        (float)((x - _centerX) * _scale + ClientSize.Width / 2.0),
        (float)((_centerY - y) * _scale + ClientSize.Height / 2.0));

    private (double X, double Y) ScreenToWorld(PointF p) => (
        _centerX + (p.X - ClientSize.Width / 2.0) / _scale,
        _centerY - (p.Y - ClientSize.Height / 2.0) / _scale);

    private static (double X, double Y) PointAtChainage(LineCoverageResult r, double chainage)
    {
        if (r.PlannedLength <= 0) return (r.StartX, r.StartY);
        double t = Math.Clamp(chainage / r.PlannedLength, 0, 1);
        return (r.StartX + (r.EndX - r.StartX) * t, r.StartY + (r.EndY - r.StartY) * t);
    }
}
