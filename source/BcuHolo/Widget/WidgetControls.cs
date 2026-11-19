using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BcuHolo.Modules.Gpu;

namespace BcuHolo.Widget
{
    /// <summary>A theme-key reference on one property that only re-binds when the key actually changes (cheap per tick).</summary>
    internal sealed class Paint
    {
        private readonly FrameworkElement _e;
        private readonly DependencyProperty _p;
        private string _key;

        public Paint(FrameworkElement e, DependencyProperty p, string key) { _e = e; _p = p; Set(key); }

        public void Set(string key)
        {
            if (key == null || key == _key) return;
            _key = key;
            _e.SetResourceReference(_p, key);
        }
    }

    /// <summary>A value TextBlock: null text shows "--" in Muted.</summary>
    internal sealed class Readout
    {
        public readonly TextBlock Tb;
        private readonly Paint _paint;
        private readonly string _defKey;

        public Readout(double size, string key = "Text", bool mono = true, bool bold = false, string font = null)
        {
            Tb = new TextBlock { FontSize = size, Text = "--", TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            if (font != null) Tb.SetResourceReference(TextBlock.FontFamilyProperty, font);
            else if (mono) Tb.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            if (bold) Tb.FontWeight = FontWeights.SemiBold;
            _defKey = key;
            _paint = new Paint(Tb, TextBlock.ForegroundProperty, "Muted");
        }

        public void Set(string text, string key = null)
        {
            if (text == null) { text = "--"; key = "Muted"; }
            if (Tb.Text != text) Tb.Text = text;
            _paint.Set(key ?? _defKey);
        }
    }

    internal static class WUi
    {
        /// <summary>
        /// Vertical fade for area fills over a fixed height: neutral black alpha only, so the theme brush underneath supplies the colour.
        /// Absolute mapping, so the fade doesn't stretch with the data's bounding box.
        /// </summary>
        public static Brush FadeMask(double height)
        {
            var b = new LinearGradientBrush(
                new GradientStopCollection { new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1) },
                new Point(0, 0), new Point(0, height)) { MappingMode = BrushMappingMode.Absolute };
            b.Freeze();
            return b;
        }

        public static TextBlock Label(string s, double size = 11, string key = "Muted", bool bold = false)
        {
            var t = new TextBlock { Text = s, FontSize = size, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, key);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        public static string Num(double v, string fmt) => double.IsNaN(v) || double.IsInfinity(v) ? null : v.ToString(fmt);
        public static string Num(double v, string fmt, string suffix) => double.IsNaN(v) || double.IsInfinity(v) ? null : v.ToString(fmt) + suffix;

        /// <summary>Bits per second, network style.</summary>
        public static string Bits(double bps)
        {
            if (double.IsNaN(bps)) return null;
            if (bps >= 1e9) return (bps / 1e9).ToString("0.00") + " Gb/s";
            if (bps >= 1e6) return (bps / 1e6).ToString("0.0") + " Mb/s";
            return (bps / 1e3).ToString("0") + " kb/s";
        }

        /// <summary>Bytes per second, disk style.</summary>
        public static string Bytes(double bps)
        {
            if (double.IsNaN(bps)) return null;
            if (bps >= 1e9) return (bps / 1e9).ToString("0.00") + " GB/s";
            if (bps >= 1e6) return (bps / 1e6).ToString("0.0") + " MB/s";
            return (bps / 1e3).ToString("0") + " kB/s";
        }

        /// <summary>Section panel: caption row (diamond, title, rule, right readout) over the content. Outer width is fixed.</summary>
        public static Border Panel(string title, double width, UIElement content, out Readout right)
        {
            var stack = new StackPanel();
            right = null;
            if (title != null)
            {
                var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var diamond = new Rectangle { Width = 5, Height = 5, Margin = new Thickness(1, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) };
                diamond.SetResourceReference(Shape.FillProperty, "Accent");
                var t = Label(title.ToUpperInvariant(), 11, "Muted", bold: true);
                Grid.SetColumn(t, 1);
                var rule = new Rectangle { Height = 1, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                rule.SetResourceReference(Shape.FillProperty, "Line.24");
                Grid.SetColumn(rule, 2);
                right = new Readout(11, "Muted");
                right.Set("", "Muted");
                Grid.SetColumn(right.Tb, 3);
                head.Children.Add(diamond); head.Children.Add(t); head.Children.Add(rule); head.Children.Add(right.Tb);
                stack.Children.Add(head);
            }
            stack.Children.Add(content);
            var b = new Border { Width = width, Padding = new Thickness(9, 7, 9, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = stack };
            b.SetResourceReference(Border.BackgroundProperty, "GlassFill");
            b.SetResourceReference(Border.BorderBrushProperty, "Line.1F");
            return b;
        }

        /// <summary>Inner content width of a Panel of the given outer width (border 1+1, padding 9+9).</summary>
        public static double Inner(double width) => width - 20;

        /// <summary>Label left, value right, one line.</summary>
        public static Grid Row(string label, Readout value, double topMargin = 2)
        {
            var g = new Grid { Margin = new Thickness(0, topMargin, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Label(label));
            Grid.SetColumn(value.Tb, 1);
            g.Children.Add(value.Tb);
            return g;
        }

        /// <summary>Label, inline bar, value: one 18 px line.</summary>
        public static Grid BarRow(string label, double labelWidth, HBar bar, Readout value, double valueWidth)
        {
            var g = new Grid { Height = 18 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });
            g.Children.Add(Label(label));
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(bar, 1);
            value.Tb.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(value.Tb, 2);
            g.Children.Add(bar); g.Children.Add(value.Tb);
            return g;
        }

        /// <summary>Small square swatch for legends.</summary>
        public static Rectangle Swatch(string key)
        {
            var r = new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            r.SetResourceReference(Shape.FillProperty, key);
            return r;
        }

        public static Run MakeRun(string text, string key)
        {
            var r = new Run(text);
            r.SetResourceReference(TextElement.ForegroundProperty, key);
            return r;
        }

        /// <summary>Faint dot grid with a radial fade (white glass alpha; static, rendered once).</summary>
        public static Rectangle DotGrid()
        {
            var tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 14, 14))));
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null, new EllipseGeometry(new Point(1, 1), 0.8, 0.8)));
            var brush = new DrawingBrush(tile) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 14, 14), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            brush.Freeze();
            var mask = new RadialGradientBrush(Color.FromArgb(0xFF, 0, 0, 0), Color.FromArgb(0x30, 0, 0, 0)) { Center = new Point(0.8, 0.1), GradientOrigin = new Point(0.8, 0.1), RadiusX = 1.0, RadiusY = 0.8 };
            mask.Freeze();
            return new Rectangle { Fill = brush, OpacityMask = mask, IsHitTestVisible = false };
        }
    }

    /// <summary>
    /// Sci-fi HUD frame: chamfered outline (45° cuts) with a raised title plate on the top-left edge, glass fill, GlassLine stroke,
    /// content clipped to the same outline, and corner art (inset accent runs along each cut ending in glowing nodes; top-left and
    /// bottom-right are the long "primary" runs). All geometry is rebuilt on SizeChanged only, never per tick. Decoration lives in
    /// Canvases so it never feeds back into SizeToContent measuring.
    /// </summary>
    internal sealed class HudFrame : Grid
    {
        public const double Cut = 16;          // chamfer leg at Size Normal (the window's LayoutTransform scales it)
        public const double Drop = 6;          // top edge steps down this much right of the title plate
        private const double Inset = 4, PrimaryRun = 40, MinorRun = 14;

        private readonly Path _shadow = new(), _fill = new(), _line = new() { StrokeThickness = 1 };
        private readonly Border _host;
        private readonly Canvas _art = new() { IsHitTestVisible = false };
        private Size _built;

        public HudFrame(UIElement content, Thickness padding)
        {
            _shadow.SetResourceReference(Shape.FillProperty, "Bg.80");
            _shadow.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.6 }; // the only effect
            _fill.SetResourceReference(Shape.FillProperty, "Bg.E6");
            _line.SetResourceReference(Shape.StrokeProperty, "GlassLine");

            var back = new Canvas();
            back.Children.Add(_shadow);
            back.Children.Add(_fill);
            var lineLayer = new Canvas { IsHitTestVisible = false };
            lineLayer.Children.Add(_line);
            _host = new Border { Padding = padding, Child = content };

            Children.Add(back);
            Children.Add(_host);
            Children.Add(lineLayer);
            Children.Add(_art);
            SizeChanged += (_, e) => Build(e.NewSize);
        }

        private void Build(Size size)
        {
            if (size == _built || size.Width < 4 * Cut || size.Height < 4 * Cut) return;
            _built = size;
            double w = size.Width, h = size.Height;
            double plate = Math.Max(Cut + PrimaryRun + 30, Math.Min(w * 0.55, 230));

            var outline = Outline(w, h, plate);
            _shadow.Data = outline; _fill.Data = outline; _line.Data = outline;
            _host.Clip = outline;

            _art.Children.Clear();
            Corner(0, 0, 1, 1, PrimaryRun);        // top-left: primary
            Corner(w, h, -1, -1, PrimaryRun);      // bottom-right: primary
            Corner(w, Drop, -1, 1, MinorRun);      // top-right (below the plate step)
            Corner(0, h, 1, -1, MinorRun);         // bottom-left

            // Title plate: accent the step-down.
            var step = new Line { X1 = plate, Y1 = 0.5, X2 = plate + Drop, Y2 = 0.5 + Drop, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            step.SetResourceReference(Shape.StrokeProperty, "Accent");
            _art.Children.Add(step);
            Dot(new Point(plate + Drop, 0.5 + Drop), 2.5);

            // Tick marks on the bottom edge, left of the bottom-right run.
            double x0 = w - Cut - PrimaryRun - 22;
            for (int i = 0; i < 4; i++)
            {
                double x = Math.Round(x0 - i * 7) + 0.5;
                var t = new Line { X1 = x, Y1 = h - 1.5, X2 = x, Y2 = h - (i == 0 ? 7.5 : 5), StrokeThickness = 1.2 };
                t.SetResourceReference(Shape.StrokeProperty, "Accent.66");
                _art.Children.Add(t);
            }
        }

        /// <summary>Chamfered outline, inset half a pixel so the 1 px stroke stays crisp.</summary>
        private static StreamGeometry Outline(double w, double h, double plate)
        {
            double a = 0.5, r = w - 0.5, b = h - 0.5, c = Cut, t = Drop;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(a, a + c), true, true);
                ctx.PolyLineTo(new List<Point>
                {
                    new Point(a + c, a), new Point(plate, a), new Point(plate + t, a + t), new Point(r - c, a + t), new Point(r, a + t + c),
                    new Point(r, b - c), new Point(r - c, b), new Point(a + c, b), new Point(a, b - c),
                }, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Inset accent stroke parallel to one chamfer, continuing <paramref name="run"/> px along both adjacent edges, with a node at
        /// each end. (x0, y0) is the corner; sx/sy point inward.
        /// </summary>
        private void Corner(double x0, double y0, double sx, double sy, double run)
        {
            double d = Inset, c = Cut + d * (Math.Sqrt(2) - 1); // where the inset chamfer meets the inset edges
            Point P(double u, double v) => new Point(x0 + sx * u, y0 + sy * v);
            var pts = new PointCollection { P(d, c + run), P(d, c), P(c, d), P(c + run, d) };

            var glow = new Polyline { Points = pts, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            glow.SetResourceReference(Shape.StrokeProperty, "Accent.29");
            var line = new Polyline { Points = pts, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Miter };
            line.SetResourceReference(Shape.StrokeProperty, "Accent");
            _art.Children.Add(glow);
            _art.Children.Add(line);
            double node = run >= PrimaryRun ? 3 : 2.5;
            Dot(P(d, c + run), node);
            Dot(P(c + run, d), node);
        }

        /// <summary>Node: solid accent dot over a translucent halo (glow without BlurEffect).</summary>
        private void Dot(Point p, double size)
        {
            var halo = new Ellipse { Width = size + 5, Height = size + 5 };
            halo.SetResourceReference(Shape.FillProperty, "Accent.33");
            Canvas.SetLeft(halo, p.X - (size + 5) / 2); Canvas.SetTop(halo, p.Y - (size + 5) / 2);
            var dot = new Ellipse { Width = size, Height = size };
            dot.SetResourceReference(Shape.FillProperty, "Accent");
            Canvas.SetLeft(dot, p.X - size / 2); Canvas.SetTop(dot, p.Y - size / 2);
            _art.Children.Add(halo);
            _art.Children.Add(dot);
        }
    }

    /// <summary>Horizontal bar driven by a ScaleTransform (no layout pass per tick). Stretches if no width is given.</summary>
    internal sealed class HBar : Grid
    {
        private readonly ScaleTransform _scale = new(0, 1);
        private readonly Paint _fill;

        public HBar(double height, string fillKey, string trackKey = "Line.1F", double width = double.NaN)
        {
            Height = height; Width = width; IsHitTestVisible = false;
            var track = new Rectangle { RadiusX = height / 2, RadiusY = height / 2 };
            track.SetResourceReference(Shape.FillProperty, trackKey);
            var fill = new Rectangle { RadiusX = height / 2, RadiusY = height / 2, RenderTransform = _scale, RenderTransformOrigin = new Point(0, 0.5) };
            _fill = new Paint(fill, Shape.FillProperty, fillKey);
            Children.Add(track); Children.Add(fill);
        }

        public void Set(double frac, string key = null)
        {
            _scale.ScaleX = double.IsNaN(frac) ? 0 : Math.Clamp(frac, 0, 1);
            _fill.Set(key);
        }
    }

    /// <summary>Vertical bar cell (heatmap / core temp): tinted track, fill scaled from the bottom, optional highlight edge.</summary>
    internal sealed class VBar : Border
    {
        private readonly ScaleTransform _scale = new(1, 0);
        private readonly Paint _fill, _track, _edge;

        public VBar(double width, double height)
        {
            Width = width; Height = height; CornerRadius = new CornerRadius(2); BorderThickness = new Thickness(1); IsHitTestVisible = false;
            _track = new Paint(this, BackgroundProperty, "Line.12");
            _edge = new Paint(this, BorderBrushProperty, "Line.00");
            var r = new Rectangle { RenderTransform = _scale, RenderTransformOrigin = new Point(0.5, 1) };
            _fill = new Paint(r, Shape.FillProperty, "Accent");
            Child = r;
        }

        public void Set(double frac, string fillKey, string trackKey, string edgeKey)
        {
            _scale.ScaleY = double.IsNaN(frac) ? 0 : Math.Clamp(frac, 0, 1);
            _fill.Set(fillKey); _track.Set(trackKey); _edge.Set(edgeKey);
        }
    }

    /// <summary>Small 270-degree ring (house Arcs style) with a centred percentage.</summary>
    internal sealed class MiniRing : Grid
    {
        private readonly Ellipse _arc;
        private readonly Paint _paint;
        private readonly Readout _text = new(11, "Text");

        public MiniRing(double d, double thickness, string key)
        {
            Width = d; Height = d; IsHitTestVisible = false;
            Children.Add(Arcs.Make(d, thickness, "Line.24"));
            _arc = Arcs.Make(d, thickness, key);
            _paint = new Paint(_arc, Shape.StrokeProperty, key);
            Children.Add(_arc);
            _text.Tb.HorizontalAlignment = HorizontalAlignment.Center;
            Children.Add(_text.Tb);
        }

        public void Set(double frac, string key)
        {
            Arcs.SetFrac(_arc, frac);
            _paint.Set(key);
            _text.Set(double.IsNaN(frac) ? null : (frac * 100).ToString("0") + "%");
        }
    }

    /// <summary>
    /// Sparkline: dashed grid, gradient-faded area (theme alpha brush + neutral black OpacityMask on a fixed-size host so the fade
    /// doesn't follow the data's bounding box), stroke line, last-point dot, optional second line. Newest sample at the right edge.
    /// </summary>
    internal sealed class Graph : Grid
    {
        private readonly double _w, _h;
        private readonly Canvas _areaHost, _lines;
        private readonly Polygon _area = new();
        private readonly Polyline _line = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        private readonly Polyline _line2;
        private readonly Ellipse _dot = new() { Width = 5, Height = 5, Visibility = Visibility.Collapsed };
        private readonly Paint _areaP, _lineP, _dotP;

        public Graph(double w, double h, string hue, string hue2 = null)
        {
            _w = w; _h = h;
            Width = w; Height = h; ClipToBounds = true; IsHitTestVisible = false;

            var grid = new Canvas();
            for (int i = 1; i <= 2; i++)
            {
                double y = Math.Round(h * i / 3) + 0.5;
                var gl = new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } };
                gl.SetResourceReference(Shape.StrokeProperty, "Line.14");
                grid.Children.Add(gl);
            }
            var baseLine = new Line { X1 = 0, X2 = w, Y1 = h - 0.5, Y2 = h - 0.5, StrokeThickness = 1 };
            baseLine.SetResourceReference(Shape.StrokeProperty, "Line.29");
            grid.Children.Add(baseLine);

            _areaHost = new Canvas { Width = w, Height = h, OpacityMask = WUi.FadeMask(h) };
            _areaHost.Children.Add(_area);
            _areaP = new Paint(_area, Shape.FillProperty, hue + ".66");

            _lines = new Canvas();
            if (hue2 != null)
            {
                _line2 = new Polyline { StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round };
                _line2.SetResourceReference(Shape.StrokeProperty, hue2);
                _lines.Children.Add(_line2);
            }
            _lineP = new Paint(_line, Shape.StrokeProperty, hue);
            _dotP = new Paint(_dot, Shape.FillProperty, hue);
            _lines.Children.Add(_line);
            _lines.Children.Add(_dot);

            Children.Add(grid); Children.Add(_areaHost); Children.Add(_lines);
        }

        public void SetHue(string hue)
        {
            _lineP.Set(hue); _dotP.Set(hue); _areaP.Set(hue + ".66");
        }

        /// <summary>Draws <paramref name="s"/> (area + line) and optionally <paramref name="s2"/> (line only) on a shared lo..hi scale.</summary>
        public void Draw(Series s, double lo, double hi, Series s2 = null)
        {
            if (double.IsNaN(lo) || double.IsNaN(hi) || hi - lo < 1e-9) { lo = 0; hi = 1; }
            var pts = Points(s, lo, hi);
            _line.Points = pts;
            if (pts.Count > 1)
            {
                var area = new PointCollection(pts.Count + 2);
                foreach (var p in pts) area.Add(p);
                area.Add(new Point(pts[pts.Count - 1].X, _h));
                area.Add(new Point(pts[0].X, _h));
                _area.Points = area;
            }
            else _area.Points = new PointCollection();

            if (pts.Count > 0 && !double.IsNaN(s.Last))
            {
                var last = pts[pts.Count - 1];
                Canvas.SetLeft(_dot, last.X - 2.5);
                Canvas.SetTop(_dot, last.Y - 2.5);
                _dot.Visibility = Visibility.Visible;
            }
            else _dot.Visibility = Visibility.Collapsed;

            if (_line2 != null) _line2.Points = s2 == null ? new PointCollection() : Points(s2, lo, hi);
        }

        private PointCollection Points(Series s, double lo, double hi)
        {
            int n = s.Count;
            var pts = new PointCollection(n);
            double step = (_w - 3) / (Series.Len - 1), x0 = _w - 3 - (n - 1) * step, span = hi - lo;
            for (int i = 0; i < n; i++)
            {
                double v = s[i];
                if (double.IsNaN(v)) continue;
                pts.Add(new Point(x0 + i * step, _h - 2 - (_h - 5) * Math.Clamp((v - lo) / span, 0, 1)));
            }
            return pts;
        }
    }

    /// <summary>
    /// Compact copy of the GPU page Dial, sized natively (no Viewbox, so fonts stay at least 11 px) and without BlurEffect glows:
    /// lit tick rim, end-cap wedge, peak marker, outer arc for a second metric with a translucent wider stroke as its glow.
    /// </summary>
    internal sealed class WDial : Grid
    {
        private readonly double _max;
        private readonly Line[] _ticks;
        private readonly bool[] _lit, _major;
        private readonly Ellipse _outer, _outerGlow;
        private string _outerHue = "Accent", _tickKey;
        private readonly Polygon _cap;
        private readonly Paint _capP;
        private readonly RotateTransform _capRot, _peakRot;
        private readonly Canvas _capLayer, _peakLayer;
        private readonly Readout _value, _sub, _outerText;

        public WDial(double size, string caption, string unit, double max, int steps, int minor)
        {
            _max = max;
            double c = size / 2, k = size / 196.0;
            Width = size; Height = size; IsHitTestVisible = false;
            var cv = new Canvas { Width = size, Height = size };

            // Outer track + second-metric arc and its glow (wider translucent stroke; centre lines match).
            Arcs.Place(cv, Arcs.Make(size - 4, 5, "Line.1F"), size);
            _outerGlow = Arcs.Make(size, 9, "Accent.33");
            _outer = Arcs.Make(size - 4, 5, "Accent");
            Arcs.Place(cv, _outerGlow, size);
            Arcs.Place(cv, _outer, size);

            var disc = new Ellipse { Width = 0.49 * size, Height = 0.49 * size };
            disc.SetResourceReference(Shape.FillProperty, "Bg.66");
            Arcs.Place(cv, disc, size);
            var ring = new Ellipse { Width = 0.51 * size, Height = 0.51 * size, StrokeThickness = 1 };
            ring.SetResourceReference(Shape.StrokeProperty, "Line.40");
            Arcs.Place(cv, ring, size);
            var dots = new Ellipse { Width = 0.56 * size, Height = 0.56 * size, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 1, 3 } };
            dots.SetResourceReference(Shape.StrokeProperty, "Line.33");
            Arcs.Place(cv, dots, size);

            int n = steps * minor;
            _ticks = new Line[n + 1]; _lit = new bool[n + 1]; _major = new bool[n + 1];
            double rOut = 0.43 * size;
            bool labels = size >= 170;
            for (int i = 0; i <= n; i++)
            {
                double f = (double)i / n;
                double a = (Arcs.Start + Arcs.Sweep * f) * Math.PI / 180;
                bool maj = i % minor == 0;
                double rIn = rOut - (maj ? 10 : 5) * k;
                var ln = new Line
                {
                    X1 = c + rIn * Math.Cos(a), Y1 = c + rIn * Math.Sin(a), X2 = c + rOut * Math.Cos(a), Y2 = c + rOut * Math.Sin(a),
                    StrokeThickness = maj ? 2.2 : 1.3,
                };
                ln.SetResourceReference(Shape.StrokeProperty, maj ? "Line.66" : "Line.33");
                _ticks[i] = ln; _major[i] = maj;
                cv.Children.Add(ln);
                if (maj && labels)
                {
                    var tb = new TextBlock { Text = (max * f).ToString("0"), FontSize = 11, Width = 30, TextAlignment = TextAlignment.Center };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                    tb.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
                    double rl = 0.335 * size;
                    Canvas.SetLeft(tb, c + rl * Math.Cos(a) - 15);
                    Canvas.SetTop(tb, c + rl * Math.Sin(a) - 8);
                    cv.Children.Add(tb);
                }
            }

            // Peak-since-open marker: thin bright line across the tick band.
            var peak = new Line { X1 = c + 0.37 * size, Y1 = c, X2 = c + 0.455 * size, Y2 = c, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            peak.SetResourceReference(Shape.StrokeProperty, "Text.B3");
            _peakRot = new RotateTransform(Arcs.Start, c, c);
            _peakLayer = new Canvas { Width = size, Height = size, RenderTransform = _peakRot, Visibility = Visibility.Collapsed };
            _peakLayer.Children.Add(peak);
            cv.Children.Add(_peakLayer);

            // End-cap wedge on the rim, rotated to the value.
            _cap = new Polygon { Points = new PointCollection { new Point(c + 0.36 * size, c - 4 * k), new Point(c + 0.455 * size, c), new Point(c + 0.36 * size, c + 4 * k) } };
            _capP = new Paint(_cap, Shape.FillProperty, "Good");
            _capRot = new RotateTransform(Arcs.Start, c, c);
            _capLayer = new Canvas { Width = size, Height = size, RenderTransform = _capRot, Visibility = Visibility.Collapsed };
            _capLayer.Children.Add(_cap);
            cv.Children.Add(_capLayer);
            Children.Add(cv);

            // Centre readout.
            _value = new Readout(Math.Round(0.17 * size), "Text", mono: false, bold: true, font: "Display");
            _value.Tb.FontWeight = FontWeights.Bold;
            _value.Tb.HorizontalAlignment = HorizontalAlignment.Center;
            var unitTb = WUi.Label(unit, 12);
            unitTb.HorizontalAlignment = HorizontalAlignment.Center;
            _sub = new Readout(11, "Muted");
            _sub.Tb.HorizontalAlignment = HorizontalAlignment.Center;
            var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            centre.Children.Add(_value.Tb); centre.Children.Add(unitTb); centre.Children.Add(_sub.Tb);
            Children.Add(centre);

            // Bottom gap of the sweep: outer-arc readout and the dial caption.
            _outerText = new Readout(12, "Accent");
            _outerText.Tb.HorizontalAlignment = HorizontalAlignment.Center;
            var cap = WUi.Label(caption, 11, "Muted", bold: true);
            cap.HorizontalAlignment = HorizontalAlignment.Center;
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            bottom.Children.Add(_outerText.Tb); bottom.Children.Add(cap);
            Children.Add(bottom);
        }

        /// <param name="value">Primary value (NaN = no reading).</param>
        /// <param name="valueHue">Theme hue for the lit ticks and wedge ("Good"/"Danger").</param>
        /// <param name="outerFrac">0..1 for the outer arc, NaN hides it.</param>
        /// <param name="peak">Highest value since the widget opened (NaN hides the marker).</param>
        public void Set(double value, string text, string valueHue, double outerFrac, string outerText, string outerHue, string sub, double peak)
        {
            bool ok = !double.IsNaN(value);
            _value.Set(ok ? text : null, valueHue == "Danger" ? "Danger" : "Text");
            double f = ok ? Math.Clamp(value / _max, 0, 1) : 0;

            bool keyChanged = valueHue != _tickKey;
            _tickKey = valueHue;
            int n = _ticks.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                bool lit = ok && f > 0 && (double)i / n <= f + 1e-9;
                if (lit == _lit[i] && !(lit && keyChanged)) continue;
                _lit[i] = lit;
                _ticks[i].SetResourceReference(Shape.StrokeProperty, lit ? valueHue : _major[i] ? "Line.66" : "Line.33");
            }
            _capLayer.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            _capRot.Angle = Arcs.Start + Arcs.Sweep * f;
            _capP.Set(valueHue);

            bool pk = !double.IsNaN(peak);
            _peakLayer.Visibility = pk ? Visibility.Visible : Visibility.Collapsed;
            if (pk) _peakRot.Angle = Arcs.Start + Arcs.Sweep * Math.Clamp(peak / _max, 0, 1);

            if (outerHue != _outerHue)
            {
                _outerHue = outerHue;
                _outer.SetResourceReference(Shape.StrokeProperty, outerHue);
                _outerGlow.SetResourceReference(Shape.StrokeProperty, outerHue + ".33");
            }
            Arcs.SetFrac(_outer, outerFrac);
            Arcs.SetFrac(_outerGlow, outerFrac);
            _outerText.Set(outerText, outerHue);
            _sub.Set(sub, "Muted");
        }
    }
}
