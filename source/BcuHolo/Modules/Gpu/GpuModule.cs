using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Gpu
{
    public sealed class GpuModule : IHoloModule
    {
        private readonly GpuModel _m = new();

        public string Id => "gpu";
        public string Title => "GPU";
        public string Glyph => "";
        public int Order => 35;

        public FrameworkElement CreateView(IHoloHost host)
        {
            _m.Start(host);
            return new GpuView(_m, host);
        }

        public FrameworkElement CreateTile(IHoloHost host)
        {
            _m.Start(host);
            return BuildTile(host);
        }

        /// <summary>Starts the engine without building a view (widget-only launch).</summary>
        public void Start(IHoloHost host) => _m.Start(host);

        /// <summary>Restores automatic fan control (if a curve was applied) and stops the engine. Also runs on app exit.</summary>
        public void Disable() => _m.Shutdown();

        private FrameworkElement BuildTile(IHoloHost host)
        {
            var m = _m;
            var mode = Ui.Big(30);
            var temp = Ui.Text("", 14, mono: true);
            var fan = Ui.Text("", 14, mono: true);
            var power = Ui.Text("", 14, mono: true);
            var err = Ui.Text("", 13, "Danger");
            var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 8) };
            rows.Children.Add(Ui.Row("Temp", temp));
            rows.Children.Add(Ui.Row("Fan", fan));
            rows.Children.Add(Ui.Row("Power / limit", power));

            const double PW = 220, PH = 56;
            var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            line.SetResourceReference(Shape.StrokeProperty, "Accent");
            var preview = new Canvas { Width = PW, Height = PH, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12), IsHitTestVisible = false };
            preview.Children.Add(line);

            var button = new Button { Style = Ui.R<Style>("Ghost"), Content = "Edit curve" };
            button.Click += (_, _) => host.Navigate(Id);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "GPU CONTROL", Style = Ui.R<Style>("Caps") });
            panel.Children.Add(mode);
            panel.Children.Add(err);
            panel.Children.Add(rows);
            panel.Children.Add(preview);
            panel.Children.Add(button);

            double[] shownT = null, shownF = null;
            void Refresh()
            {
                if (!m.Available)
                {
                    mode.Text = "—";
                    err.Text = m.Error ?? "";
                    rows.Visibility = preview.Visibility = button.Visibility = Visibility.Collapsed;
                    return;
                }
                mode.Text = Ui.Mode(m);
                mode.SetResourceReference(TextBlock.ForegroundProperty, m.CurveActive ? "Good" : "Text");
                temp.Text = Ui.TempText(m);
                fan.Text = Ui.FanText(m, false);
                power.Text = Ui.Watts(m.PowerW) + " / " + Ui.Watts(m.LimitW);
                // The curve only changes on edit (arrays are mutated in place); don't rebuild the geometry every tick.
                if (shownT != null && shownT.AsSpan().SequenceEqual(m.T) && shownF.AsSpan().SequenceEqual(m.F)) return;
                shownT = (double[])m.T.Clone();
                shownF = (double[])m.F.Clone();
                var pts = new PointCollection(m.T.Length);
                for (int i = 0; i < m.T.Length; i++)
                    pts.Add(new Point((m.T[i] - GpuModel.TempMin) / (GpuModel.TempMax - GpuModel.TempMin) * PW,
                        PH - (m.F[i] - GpuModel.FanMin) / (GpuModel.FanMax - GpuModel.FanMin) * PH));
                line.Points = pts;
            }
            Refresh();
            var border = new Border { Style = Ui.R<Style>("DashTile"), Child = panel };
            // Listen only while on screen: page left, window hidden or minimized, or tile discarded.
            Sensors.SensorsModule.WhileShown(border, on =>
            {
                m.Changed -= Refresh;
                if (on) { m.Changed += Refresh; Refresh(); }
            });
            return border;
        }
    }
}
