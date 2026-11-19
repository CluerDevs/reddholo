using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BcuHolo.Modules.Gpu
{
    /// <summary>270-degree arc helpers (same dashed-Ellipse trick as the dashboard gauges: dash units are multiples of StrokeThickness).</summary>
    internal static class Arcs
    {
        public const double Start = 135, Sweep = 270;

        public static Ellipse Make(double size, double thickness, string key, bool round = true)
        {
            var e = new Ellipse
            {
                Width = size, Height = size, StrokeThickness = thickness,
                StrokeDashCap = round ? PenLineCap.Round : PenLineCap.Flat,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(Start),
                IsHitTestVisible = false,
            };
            e.SetResourceReference(Shape.StrokeProperty, key);
            SetFracNow(e, 1);
            return e;
        }

        /// <summary>Glides to the first <paramref name="frac"/> of the 270-degree sweep; NaN or 0 hides the arc.</summary>
        public static void SetFrac(Ellipse e, double frac) => Motion.To(e, frac, f => SetFracNow(e, f));

        public static void SetFracNow(Ellipse e, double frac)
        {
            if (double.IsNaN(frac) || frac <= 0) { e.Visibility = Visibility.Collapsed; return; }
            e.Visibility = Visibility.Visible;
            double c = Math.PI * (e.Width - e.StrokeThickness) / e.StrokeThickness;
            e.StrokeDashArray = new DoubleCollection { Math.Max(0.001, 0.75 * c * Math.Min(1, frac)), c };
        }

        public static void Place(Canvas canvas, FrameworkElement e, double canvasSize)
        {
            Canvas.SetLeft(e, (canvasSize - e.Width) / 2);
            Canvas.SetTop(e, (canvasSize - e.Height) / 2);
            canvas.Children.Add(e);
        }
    }

    /// <summary>A value arc plus a blurred copy underneath for the glow (a DropShadowEffect colour can't follow theme resources).</summary>
    internal sealed class ArcPair
    {
        public readonly Ellipse Main, Glow;
        private string _key;

        public ArcPair(double size, double thickness, string key)
        {
            Main = Arcs.Make(size, thickness, key);
            Glow = Arcs.Make(size + 4, thickness + 4, key); // +4 each so the centre line matches Main
            Glow.Opacity = 0.55;
            Glow.Effect = new BlurEffect { Radius = 8 };
            _key = key;
        }

        public void AddTo(Canvas c, double canvasSize)
        {
            Arcs.Place(c, Glow, canvasSize);
            Arcs.Place(c, Main, canvasSize);
        }

        public void Set(double frac, string key)
        {
            if (key != _key)
            {
                _key = key;
                Main.SetResourceReference(Shape.StrokeProperty, key);
                Glow.SetResourceReference(Shape.StrokeProperty, key);
            }
            Arcs.SetFrac(Main, frac);
            Arcs.SetFrac(Glow, frac);
        }
    }

    /// <summary>Afterburner-style dial: tick rim that lights up with the value, end-cap marker, outer glowing arc for a second metric.</summary>
    internal sealed class Dial : Grid
    {
        private const double S = 260, C = 130;
        private readonly Line[] _ticks;
        private readonly bool[] _lit, _major;
        private readonly ArcPair _outer;
        private readonly Polygon _cap;
        private readonly Canvas _capLayer;
        private readonly RotateTransform _capRot = new(Arcs.Start, C, C);
        private readonly TextBlock _value, _sub, _outerText;
        private readonly double _max;
        private string _tickKey, _capKey, _textKey, _outerTextKey;

        public Dial(string title, string unit, double max, int steps, int minor, Func<double, string> label, string subKey)
        {
            _max = max;
            Width = S; Height = S;
            var c = new Canvas { Width = S, Height = S, IsHitTestVisible = false };

            // Outer track + value arc (second metric).
            Arcs.Place(c, Arcs.Make(246, 6, "Line.24"), S);
            _outer = new ArcPair(246, 6, "Accent");
            _outer.AddTo(c, S);

            // Centre disc and thin inner rings.
            var disc = new Ellipse { Width = 130, Height = 130 };
            disc.SetResourceReference(Shape.FillProperty, "Bg.4D");
            Arcs.Place(c, disc, S);
            var ring = new Ellipse { Width = 134, Height = 134, StrokeThickness = 1 };
            ring.SetResourceReference(Shape.StrokeProperty, "Line.40");
            Arcs.Place(c, ring, S);
            var dots = new Ellipse { Width = 142, Height = 142, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 1, 3 } };
            dots.SetResourceReference(Shape.StrokeProperty, "Line.33");
            Arcs.Place(c, dots, S);

            // Tick rim (major/minor) with scale labels on the major ticks.
            int n = steps * minor;
            _ticks = new Line[n + 1]; _lit = new bool[n + 1]; _major = new bool[n + 1];
            const double rOut = 112;
            for (int i = 0; i <= n; i++)
            {
                double f = (double)i / n;
                double a = (Arcs.Start + Arcs.Sweep * f) * Math.PI / 180;
                bool maj = i % minor == 0;
                double rIn = rOut - (maj ? 12 : 6);
                var ln = new Line
                {
                    X1 = C + rIn * Math.Cos(a), Y1 = C + rIn * Math.Sin(a), X2 = C + rOut * Math.Cos(a), Y2 = C + rOut * Math.Sin(a),
                    StrokeThickness = maj ? 2.5 : 1.5, IsHitTestVisible = false,
                };
                ln.SetResourceReference(Shape.StrokeProperty, maj ? "Line.66" : "Line.33");
                _ticks[i] = ln; _major[i] = maj;
                c.Children.Add(ln);
                if (maj)
                {
                    var tb = new TextBlock { Text = label(max * f), FontSize = 10, Width = 34, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                    tb.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
                    Canvas.SetLeft(tb, C + 91 * Math.Cos(a) - 17);
                    Canvas.SetTop(tb, C + 91 * Math.Sin(a) - 7);
                    c.Children.Add(tb);
                }
            }

            // End-cap marker: a wedge on the rim, rotated to the current value.
            _cap = new Polygon { Points = new PointCollection { new Point(C + 97, C - 5), new Point(C + 118, C), new Point(C + 97, C + 5) }, Visibility = Visibility.Collapsed };
            _cap.SetResourceReference(Shape.FillProperty, "Accent");
            _capKey = "Accent";
            _capLayer = new Canvas { Width = S, Height = S, RenderTransform = _capRot, IsHitTestVisible = false };
            _capLayer.Children.Add(_cap);
            c.Children.Add(_capLayer);
            Children.Add(c);

            // Centre readout.
            _value = Ui.Big(44);
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            var unitTb = Ui.Text(unit, 13, "Muted");
            unitTb.HorizontalAlignment = HorizontalAlignment.Center;
            _sub = Ui.Text("", 12, subKey, mono: true);
            _sub.TextWrapping = TextWrapping.NoWrap;
            _sub.HorizontalAlignment = HorizontalAlignment.Center;
            var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, 0), IsHitTestVisible = false };
            centre.Children.Add(_value); centre.Children.Add(unitTb); centre.Children.Add(_sub);
            Children.Add(centre);

            // Bottom gap of the 270-degree sweep: outer-arc readout and the dial title.
            _outerText = Ui.Text("", 13, "Accent", mono: true);
            _outerText.TextWrapping = TextWrapping.NoWrap;
            _outerText.HorizontalAlignment = HorizontalAlignment.Center;
            _outerTextKey = "Accent";
            var cap = new TextBlock { Text = title, Style = Ui.R<Style>("Caps"), HorizontalAlignment = HorizontalAlignment.Center };
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), IsHitTestVisible = false };
            bottom.Children.Add(_outerText); bottom.Children.Add(cap);
            Children.Add(bottom);
        }

        /// <param name="value">Primary value for ticks and marker; NaN when unavailable.</param>
        /// <summary>Lights ticks up to <paramref name="f"/> and points the end cap there (called per animation frame).</summary>
        private void ApplySweep(double f)
        {
            if (double.IsNaN(f)) f = 0;
            int n = _ticks.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                bool lit = f > 0 && (double)i / n <= f + 1e-9;
                if (lit == _lit[i]) continue;
                _lit[i] = lit;
                _ticks[i].SetResourceReference(Shape.StrokeProperty, lit ? _tickKey : _major[i] ? "Line.66" : "Line.33");
            }
            _capRot.Angle = Arcs.Start + Arcs.Sweep * f;
        }

        public void Set(double value, string text, string valueKey, string textKey, double outerFrac, string outerText, string outerKey, string sub)
        {
            Ui.Ref(_value, TextBlock.ForegroundProperty, textKey, ref _textKey);
            bool ok = !double.IsNaN(value);
            double f = ok ? Math.Clamp(value / _max, 0, 1) : 0;

            if (valueKey != _tickKey)
            {
                _tickKey = valueKey;
                Array.Clear(_lit); // lit ticks get re-referenced with the new colour on the next apply
            }
            // Big number counts toward the new reading alongside the sweep (only when it's a plain integer).
            if (ok && int.TryParse(text, out var shownInt)) Motion.To(_value, shownInt, v => _value.Text = Math.Round(v).ToString("0"), _max * 0.02);
            else Motion.To(_value, double.NaN, _ => _value.Text = text);

            _cap.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            Ui.Ref(_cap, Shape.FillProperty, valueKey, ref _capKey);
            Motion.To(_capRot, ok ? f : double.NaN, ApplySweep);

            _outer.Set(outerFrac, outerKey);
            _outerText.Text = outerText;
            Ui.Ref(_outerText, TextBlock.ForegroundProperty, outerKey, ref _outerTextKey);
            _sub.Text = sub;
        }
    }

    /// <summary>Small secondary gauge tile: mini ring with a percentage, caption, value and a sub line.</summary>
    internal sealed class Stat : Border
    {
        private readonly Ellipse _arc;
        private readonly TextBlock _ring, _value, _sub;
        private string _key;

        public Stat(string caption)
        {
            Style = Ui.R<Style>("Card");
            Padding = new Thickness(14, 12, 14, 12);
            CornerRadius = new CornerRadius(14);

            var g = new Grid { Width = 62, Height = 62, VerticalAlignment = VerticalAlignment.Center };
            var track = Arcs.Make(62, 6, "Line.24");
            _arc = Arcs.Make(62, 6, "Line");
            _key = "Line";
            _ring = Ui.Text("", 12, "Text", mono: true);
            _ring.TextWrapping = TextWrapping.NoWrap;
            _ring.HorizontalAlignment = HorizontalAlignment.Center;
            _ring.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(track); g.Children.Add(_arc); g.Children.Add(_ring);

            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = caption, Style = Ui.R<Style>("Caps") });
            _value = Ui.Text("", 14, "Text", mono: true, weight: FontWeights.SemiBold);
            _sub = Ui.Text("", 11, "Muted", mono: true);
            text.Children.Add(_value); text.Children.Add(_sub);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(text, 1);
            row.Children.Add(g); row.Children.Add(text);
            Child = row;
        }

        public void Set(double frac, string ringText, string key, string value, string sub)
        {
            Arcs.SetFrac(_arc, frac);
            Ui.Ref(_arc, Shape.StrokeProperty, key, ref _key);
            _ring.Text = ringText;
            _value.Text = value;
            _sub.Text = sub;
        }
    }

    /// <summary>Read-only horizontal bar with a label and a value. Fill width is a star-split of two columns, so no layout pass is needed.</summary>
    internal sealed class Bar : StackPanel
    {
        private readonly ColumnDefinition _a = new(), _b = new();
        private readonly Border _fill;
        private readonly TextBlock _value;

        public Bar(string label, string key)
        {
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _value = Ui.Text("", 13, "Text", mono: true);
            _value.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(_value, 1);
            head.Children.Add(Ui.Text(label, 13, "Muted"));
            head.Children.Add(_value);

            var track = new Grid { Height = 8, Margin = new Thickness(0, 5, 0, 0) };
            track.ColumnDefinitions.Add(_a); track.ColumnDefinitions.Add(_b);
            var bg = new Border { CornerRadius = new CornerRadius(4) };
            bg.SetResourceReference(Border.BackgroundProperty, "Line.24");
            Grid.SetColumnSpan(bg, 2);
            _fill = new Border { CornerRadius = new CornerRadius(4) };
            _fill.SetResourceReference(Border.BackgroundProperty, key);
            track.Children.Add(bg); track.Children.Add(_fill);

            Margin = new Thickness(0, 0, 0, 12);
            Children.Add(head); Children.Add(track);
            Set(double.NaN, "--");
        }

        public void Set(double frac, string text)
        {
            Motion.To(_fill, double.IsNaN(frac) ? double.NaN : Math.Clamp(frac, 0, 1), v =>
            {
                double f = double.IsNaN(v) ? 0 : v;
                _a.Width = new GridLength(f, GridUnitType.Star);
                _b.Width = new GridLength(1 - f, GridUnitType.Star);
                _fill.Visibility = f <= 0 ? Visibility.Collapsed : Visibility.Visible;
            });
            _value.Text = text;
        }
    }

    /// <summary>Live history: temp, load, power, fan as polylines over the last 60 samples, each scaled to 0..100 of its own range.</summary>
    internal sealed class HistoryGraph : StackPanel
    {
        private static readonly string[] Names = { "TEMP", "LOAD", "POWER", "FAN" };
        private static readonly string[] Keys = { "Danger", "Accent", "Blue", "Violet" };
        private readonly Canvas _plot = new() { ClipToBounds = true, IsHitTestVisible = false };
        private readonly Polyline[] _lines = new Polyline[4];
        private readonly TextBlock[] _vals = new TextBlock[4];
        private double[][] _data = { new double[0], new double[0], new double[0], new double[0] };
        private double _powerScale = 1;
        private const double PlotH = 170;

        public HistoryGraph()
        {
            var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
            for (int i = 0; i < 4; i++)
            {
                var sw = new Rectangle { Width = 16, Height = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                sw.SetResourceReference(Shape.FillProperty, Keys[i]);
                _vals[i] = Ui.Text("--", 12, "Text", mono: true);
                _vals[i].TextWrapping = TextWrapping.NoWrap;
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
                item.Children.Add(sw);
                item.Children.Add(Ui.Text(Names[i] + " ", 12, "Muted"));
                item.Children.Add(_vals[i]);
                legend.Children.Add(item);

                var pl = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                pl.SetResourceReference(Shape.StrokeProperty, Keys[i]);
                _lines[i] = pl;
                _plot.Children.Add(pl);
            }

            var gridLines = new UniformGrid { Rows = 4, Columns = 1, IsHitTestVisible = false };
            for (int i = 0; i < 4; i++)
            {
                var b = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
                b.SetResourceReference(Border.BorderBrushProperty, "Line.14");
                gridLines.Children.Add(b);
            }
            var frame = new Border { Height = PlotH, BorderThickness = new Thickness(0, 0, 0, 1), Child = new Grid { Children = { gridLines, _plot } } };
            frame.SetResourceReference(Border.BorderBrushProperty, "Line.24");
            _plot.SizeChanged += (_, _) => Redraw();

            Children.Add(legend);
            Children.Add(frame);
            Children.Add(Ui.Text("Each trace is scaled 0–100: °C, load %, power as % of the limit, fan %.", 12, "Muted"));
            ((TextBlock)Children[2]).Margin = new Thickness(0, 8, 0, 0);
        }

        public void Update(GpuModel m)
        {
            _data = new[] { m.HistTemp, m.HistLoad, m.HistPower, m.HistFan };
            double lim = double.IsNaN(m.LimitW) ? 0 : m.LimitW;
            _powerScale = Math.Max(1, Math.Max(lim, m.MaxW));
            _vals[0].Text = m.Temp >= 0 ? m.Temp + "°C" : "--";
            _vals[1].Text = m.Load >= 0 ? m.Load + "%" : "--";
            _vals[2].Text = double.IsNaN(m.PowerW) ? "--" : $"{m.PowerW:0} W";
            _vals[3].Text = m.Fans.Length > 0 ? Ui.FanText(m, false) : "--";
            Redraw();
        }

        private void Redraw()
        {
            double w = _plot.ActualWidth > 0 ? _plot.ActualWidth : 500, h = _plot.ActualHeight > 0 ? _plot.ActualHeight : PlotH;
            double step = w / (GpuModel.HistoryLen - 1);
            for (int s = 0; s < 4; s++)
            {
                var d = _data[s];
                double scale = s == 2 ? _powerScale : 100;
                double x0 = w - (d.Length - 1) * step;
                var pts = new PointCollection();
                for (int i = 0; i < d.Length; i++)
                {
                    if (double.IsNaN(d[i])) continue;
                    pts.Add(new Point(x0 + i * step, h - 2 - (h - 4) * Math.Clamp(d[i] / scale, 0, 1)));
                }
                _lines[s].Points = pts;
            }
        }
    }
}
