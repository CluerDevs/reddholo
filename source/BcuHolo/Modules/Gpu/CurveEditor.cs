using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BcuHolo.Modules.Gpu
{
    /// <summary>Canvas with 5 draggable (or arrow-key nudged) points, grid, glowing polyline and a live marker.</summary>
    internal sealed class CurveEditor : Canvas
    {
        private const double H = 240, L = 44, Tp = 12, Rm = 16, Bm = 30;
        private double PW = 496, PH = 198;
        private readonly Canvas _grid = new() { IsHitTestVisible = false };
        private readonly Ellipse _ring;
        private readonly GpuModel _m;
        private readonly Polyline _line, _glow;
        private readonly Ellipse[] _dots = new Ellipse[GpuModel.PointCount];
        private readonly Ellipse _marker;
        private readonly TextBlock _readout;
        private int _drag = -1, _focus = -1;

        public CurveEditor(GpuModel m)
        {
            _m = m;
            Height = H;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Background = Brushes.Transparent;
            Children.Add(_grid);

            // Glow = blurred copy of the line (a DropShadowEffect colour can't follow theme resources).
            _glow = new Polyline { StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.5, Effect = new BlurEffect { Radius = 8 }, IsHitTestVisible = false };
            _glow.SetResourceReference(Shape.StrokeProperty, "Accent");
            Children.Add(_glow);
            _line = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
            _line.SetResourceReference(Shape.StrokeProperty, "Accent");
            Children.Add(_line);

            _marker = new Ellipse { Width = 10, Height = 10, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _marker.SetResourceReference(Shape.FillProperty, "Good");
            Children.Add(_marker);

            _readout = new TextBlock { FontSize = 11, IsHitTestVisible = false };
            _readout.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            _readout.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            SetLeft(_readout, L + 8); SetTop(_readout, Tp + 2);
            Children.Add(_readout);

            _ring = new Ellipse { Width = 28, Height = 28, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _ring.SetResourceReference(Shape.StrokeProperty, "Accent");
            Children.Add(_ring);

            for (int i = 0; i < _dots.Length; i++)
            {
                int idx = i;
                var d = new Ellipse
                {
                    Width = 18, Height = 18, StrokeThickness = 2, Cursor = Cursors.Hand,
                    Focusable = true, FocusVisualStyle = null, // replaced by the Accent ring (_ring)
                };
                d.SetResourceReference(Shape.StrokeProperty, "Accent");
                d.SetResourceReference(Shape.FillProperty, "Bg");
                System.Windows.Automation.AutomationProperties.SetName(d, $"Fan curve point {i + 1}. Arrow keys adjust.");
                d.MouseLeftButtonDown += (_, e) => { _drag = idx; d.Focus(); d.CaptureMouse(); e.Handled = true; };
                d.MouseMove += (_, e) => { if (_drag == idx && d.IsMouseCaptured) Drag(idx, e.GetPosition(this)); };
                d.MouseLeftButtonUp += (_, _) => { _drag = -1; d.ReleaseMouseCapture(); };
                d.GotKeyboardFocus += (_, _) => { _focus = idx; Redraw(); };
                d.LostKeyboardFocus += (_, _) => { if (_focus == idx) _focus = -1; Redraw(); };
                d.KeyDown += (_, e) =>
                {
                    int dt = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0;
                    int df = e.Key == Key.Up ? 1 : e.Key == Key.Down ? -1 : 0;
                    if (dt == 0 && df == 0) return;
                    _m.SetPoint(idx, _m.T[idx] + dt, _m.F[idx] + df);
                    e.Handled = true;
                };
                _dots[i] = d;
                Children.Add(d);
            }
            m.Changed += Redraw;
            SizeChanged += (_, _) => { Layout(); Redraw(); };
            Unloaded += (_, _) => m.Changed -= Redraw;
            Loaded += (_, _) => { m.Changed -= Redraw; m.Changed += Redraw; Layout(); Redraw(); };
            Layout();
            Redraw();
        }

        /// <summary>Recomputes the plot area from the current size and rebuilds grid lines and labels.</summary>
        private void Layout()
        {
            double w = ActualWidth > 0 ? ActualWidth : 560, h = ActualHeight > 0 ? ActualHeight : H;
            PW = Math.Max(120, w - L - Rm);
            PH = Math.Max(60, h - Tp - Bm);
            _grid.Children.Clear();
            for (int t = GpuModel.TempMin; t <= GpuModel.TempMax; t += 10)
            {
                GridLine(X(t), Tp, X(t), Tp + PH);
                Label(t + "°", X(t) - 10, Tp + PH + 6);
            }
            for (int f = GpuModel.FanMin; f <= GpuModel.FanMax; f += 10)
            {
                GridLine(L, Y(f), L + PW, Y(f));
                Label(f + "%", 2, Y(f) - 8);
            }
        }

        private double X(double t) => L + (t - GpuModel.TempMin) / (GpuModel.TempMax - GpuModel.TempMin) * PW;
        private double Y(double f) => Tp + PH - (f - GpuModel.FanMin) / (GpuModel.FanMax - GpuModel.FanMin) * PH;

        private void GridLine(double x1, double y1, double x2, double y2)
        {
            var ln = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1 };
            ln.SetResourceReference(Shape.StrokeProperty, "Line.12");
            _grid.Children.Add(ln);
        }

        private void Label(string s, double x, double y)
        {
            var tb = new TextBlock { Text = s, FontSize = 10, IsHitTestVisible = false };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            SetLeft(tb, x); SetTop(tb, y);
            _grid.Children.Add(tb);
        }

        private void Drag(int i, Point p)
        {
            double t = GpuModel.TempMin + (p.X - L) / PW * (GpuModel.TempMax - GpuModel.TempMin);
            double f = GpuModel.FanMin + (Tp + PH - p.Y) / PH * (GpuModel.FanMax - GpuModel.FanMin);
            _m.SetPoint(i, t, f);
        }

        private void Redraw()
        {
            var pts = new PointCollection { new Point(X(GpuModel.TempMin), Y(_m.F[0])) };
            for (int i = 0; i < _dots.Length; i++)
            {
                pts.Add(new Point(X(_m.T[i]), Y(_m.F[i])));
                SetLeft(_dots[i], X(_m.T[i]) - 9);
                SetTop(_dots[i], Y(_m.F[i]) - 9);
            }
            pts.Add(new Point(X(GpuModel.TempMax), Y(_m.F[^1])));
            _line.Points = pts;
            _glow.Points = pts;

            int fan = _m.Fans.Length > 0 ? _m.Fans[0] : -1;
            if (_m.Temp >= 0 && fan >= 0)
            {
                _marker.Visibility = Visibility.Visible;
                SetLeft(_marker, X(Math.Clamp(_m.Temp, GpuModel.TempMin, GpuModel.TempMax)) - 5);
                SetTop(_marker, Y(Math.Clamp(fan, GpuModel.FanMin, GpuModel.FanMax)) - 5);
            }
            else _marker.Visibility = Visibility.Collapsed;

            int sel = _drag >= 0 ? _drag : _focus;
            if (_focus >= 0)
            {
                _ring.Visibility = Visibility.Visible;
                SetLeft(_ring, X(_m.T[_focus]) - 14);
                SetTop(_ring, Y(_m.F[_focus]) - 14);
            }
            else _ring.Visibility = Visibility.Collapsed;
            _readout.Text = sel >= 0 ? $"Point {sel + 1}: {_m.T[sel]:0}°C -> {_m.F[sel]:0}%" : "";
        }
    }
}
