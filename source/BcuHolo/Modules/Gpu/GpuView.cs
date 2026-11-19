using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Gpu
{
    /// <summary>Theme-aware UI helpers. Colours are always resource keys (SetResourceReference) so a live theme switch repaints everything.</summary>
    internal static class Ui
    {
        /// <summary>For Styles and fonts only; never use this for brushes.</summary>
        public static T R<T>(string key) => (T)Application.Current.Resources[key];

        /// <summary>SetResourceReference, skipped when the element already points at <paramref name="key"/> (cheap per-tick calls).</summary>
        public static void Ref(FrameworkElement e, DependencyProperty p, string key, ref string current)
        {
            if (current == key) return;
            current = key;
            e.SetResourceReference(p, key);
        }

        public static TextBlock Text(string s = "", double size = 14, string brush = "Text", bool mono = false, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = s, FontSize = size, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (mono) t.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            if (weight != null) t.FontWeight = weight.Value;
            return t;
        }

        public static TextBlock Big(double size, string brush = "Text")
        {
            var t = Text("", size, brush, weight: FontWeights.Bold);
            t.SetResourceReference(TextBlock.FontFamilyProperty, "Display");
            return t;
        }

        public static Border Row(string label, TextBlock value)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Text(label, 14, "Muted");
            value.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(value, 1);
            g.Children.Add(l);
            g.Children.Add(value);
            var b = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 8), Child = g };
            b.SetResourceReference(Border.BorderBrushProperty, "Line.12");
            return b;
        }

        public static string Watts(double w) => double.IsNaN(w) ? "—" : $"{w:0} W";
        public static string TempText(GpuModel m) => m.Temp >= 0 ? $"{m.Temp}°C" : "—";
        public static string FanText(GpuModel m, bool labelled) =>
            m.Fans.Length == 0 ? "—" : string.Join(" · ", m.Fans.Select((f, i) => labelled && m.Fans.Length > 1 ? $"Fan {i + 1} {f}%" : $"{f}%"));
        public static string Mode(GpuModel m) => !m.CurveActive ? "Auto" : m.FixedPct >= 0 ? $"Fixed {m.FixedPct}%" : "Curve";
    }

    internal sealed class GpuView : UserControl
    {
        // Slider look: thin accent track, boxed thumb, focus ring. Parsed once; falls back to the stock slider if parsing fails.
        private const string SliderXaml = @"
<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Slider"">
  <Grid Height=""28"" Background=""Transparent"">
    <Border x:Name=""f"" CornerRadius=""8"" BorderThickness=""1"" BorderBrush=""{DynamicResource Accent}"" Visibility=""Collapsed"" IsHitTestVisible=""False""/>
    <Border Height=""6"" Margin=""7,0"" CornerRadius=""3"" VerticalAlignment=""Center"" Background=""{DynamicResource Line.24}""/>
    <Track x:Name=""PART_Track"">
      <Track.DecreaseRepeatButton>
        <RepeatButton Command=""{x:Static Slider.DecreaseLarge}"" Focusable=""False"" IsTabStop=""False"">
          <RepeatButton.Template>
            <ControlTemplate TargetType=""RepeatButton"">
              <Grid Background=""Transparent""><Border Height=""6"" CornerRadius=""3,0,0,3"" VerticalAlignment=""Center"" Background=""{DynamicResource Accent}""/></Grid>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command=""{x:Static Slider.IncreaseLarge}"" Focusable=""False"" IsTabStop=""False"">
          <RepeatButton.Template>
            <ControlTemplate TargetType=""RepeatButton"">
              <Grid Background=""Transparent""/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width=""14"" Height=""22"" Focusable=""False"" Cursor=""Hand"">
          <Thumb.Template>
            <ControlTemplate TargetType=""Thumb"">
              <Border CornerRadius=""4"" BorderThickness=""2"" BorderBrush=""{DynamicResource Accent}"" Background=""{DynamicResource Bg}""/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsKeyboardFocused"" Value=""True""><Setter TargetName=""f"" Property=""Visibility"" Value=""Visible""/></Trigger>
    <Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Opacity"" Value=""0.45""/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private readonly GpuModel _m;
        private readonly IHoloHost _host;
        private readonly StackPanel _content = new();
        private readonly Border _banner, _msg;
        private readonly TextBlock _msgText = Ui.Text("", 13, "Muted");
        private readonly TextBlock _name = Ui.Text("", 16, weight: FontWeights.SemiBold);
        private readonly TextBlock _mode = Ui.Text("", 13, "Muted", mono: true);
        private readonly TextBlock _curveStatus = Ui.Text("", 13, "Muted");
        private readonly TextBlock _applyNote = Ui.Text("Apply is disabled while MSI Afterburner is running.", 13, "Muted");
        private readonly TextBlock _applyNote2 = Ui.Text("Apply is disabled while MSI Afterburner is running.", 13, "Muted");
        private readonly Button _applyCurve, _auto, _reset, _applyLimit, _defLimit;
        private readonly RadioButton _modeAuto, _modeCurve, _modeFixed;
        private readonly Slider _fanSlider = new() { Margin = new Thickness(0, 6, 0, 0), IsSnapToTickEnabled = true, TickFrequency = 5, SmallChange = 5, LargeChange = 10, Maximum = 100 };
        private readonly TextBlock _fanSliderValue = Ui.Text("", 15, "Violet", mono: true, weight: FontWeights.Bold);
        private readonly Button _setFixed, _fixed100;
        private readonly Slider _slider = new() { Margin = new Thickness(0, 8, 0, 4), IsSnapToTickEnabled = true, TickFrequency = 1 };
        private readonly TextBlock _sliderValue = Ui.Text("", 18, "Blue", mono: true, weight: FontWeights.Bold);
        private readonly TextBlock _limitInfo = Ui.Text("", 12, "Muted", mono: true);
        private readonly Dial _clock, _temp;
        private readonly Bar _loadBar, _memBar, _vramBar;
        private readonly Stat _sVram, _sMem, _sPower;
        private readonly Stat[] _sFans;
        private readonly HistoryGraph _hist = new();
        private bool _sliderInit;

        // ---- Compact view: smaller dials, no explainer/bars, curve editor + history hidden. Remembered across launches. ----
        private static readonly string CompactMarker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BCU-Holo", "gpu-compact");
        private RadioButton _compactToggle;
        private Viewbox[] _dialBoxes;
        private Border _heroCard;
        private Grid _lower;
        private TextBlock _limitHint;
        private Border _barsRule;
        private bool _compact;

        private void ApplyCompact(bool on, bool save)
        {
            _compact = on;
            _compactToggle.IsChecked = on;
            foreach (var v in _dialBoxes) v.MaxWidth = v.MaxHeight = on ? 220 : 300;
            _heroCard.Padding = new Thickness(on ? 10 : 16);
            var vis = on ? Visibility.Collapsed : Visibility.Visible;
            _limitHint.Visibility = _barsRule.Visibility = _loadBar.Visibility = _memBar.Visibility = _vramBar.Visibility = _lower.Visibility = vis;
            if (!save) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CompactMarker));
                if (on) File.WriteAllText(CompactMarker, ""); else File.Delete(CompactMarker);
            }
            catch (Exception) { /* view preference only */ }
        }
        private string _kMode, _kStatus, _kMsgFg, _kMsgBg, _kMsgBd;

        public GpuView(GpuModel m, IHoloHost host)
        {
            _m = m; _host = host;
            SetResourceReference(ForegroundProperty, "Text");

            var unavailable = Ui.Text(m.Error ?? "", 16, "Danger", weight: FontWeights.SemiBold);
            var unavailCard = new Border { Style = Ui.R<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Child = unavailable, Visibility = Visibility.Collapsed };

            _banner = new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed,
                Child = Ui.Text("Close MSI Afterburner first — two fan controllers will fight", 14, "Danger", weight: FontWeights.SemiBold),
            };
            _banner.SetResourceReference(Border.BackgroundProperty, "Danger.12");
            _banner.SetResourceReference(Border.BorderBrushProperty, "Danger");

            _msg = new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed, Child = _msgText,
            };

            // ---- Title row ----
            var title = new Grid { Margin = new Thickness(4, 0, 4, 12) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tl = new StackPanel { Orientation = Orientation.Horizontal };
            tl.Children.Add(new TextBlock { Text = "GPU", Style = Ui.R<Style>("Caps"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            _name.VerticalAlignment = VerticalAlignment.Center;
            tl.Children.Add(_name);
            _mode.VerticalAlignment = VerticalAlignment.Center;
            var tr = new StackPanel { Orientation = Orientation.Horizontal };
            tr.Children.Add(_mode);
            _compactToggle = new RadioButton { Style = Ui.R<Style>("Chip"), GroupName = "gpuCompact", Content = "Compact view", Margin = new Thickness(14, 0, 0, 0),
                MinHeight = 34, ToolTip = "Smaller dials; hides the curve editor and history" };
            // RadioButton can't uncheck itself, so flip it by hand (same pattern as the Sensors page).
            _compactToggle.Click += (_, _) => ApplyCompact(!_compact, save: true);
            tr.Children.Add(_compactToggle);
            Grid.SetColumn(tr, 1);
            title.Children.Add(tl); title.Children.Add(tr);

            // ---- Dials ----
            int mx = m.MaxCoreMHz > 0 ? m.MaxCoreMHz : 3000;
            int clockStep = mx > 4000 ? 1000 : 500;
            int clockMax = (int)Math.Ceiling(mx / (double)clockStep) * clockStep;
            _clock = new Dial("GPU CLOCK", "MHz", clockMax, clockMax / clockStep, 5, v => v.ToString("0"), "Muted");
            _temp = new Dial("GPU TEMP", "°C", 110, 11, 5, v => v.ToString("0"), "Blue");
            Viewbox Boxed(UIElement d) => new Viewbox { Child = d, Stretch = Stretch.Uniform, MaxWidth = 300, MaxHeight = 300, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            // ---- Control stack ----
            _applyLimit = new Button { Style = Ui.R<Style>("Primary"), Content = "Apply limit" };
            _defLimit = new Button { Style = Ui.R<Style>("Ghost"), Content = "Default", Margin = new Thickness(8, 0, 0, 0) };
            _applyLimit.Click += (_, _) => m.ApplyLimit(_slider.Value, false);
            _defLimit.Click += (_, _) =>
            {
                m.ApplyLimit(0, true);
                if (m.PowerSupported && !double.IsNaN(m.LimitW)) _slider.Value = Math.Clamp(m.LimitW, _slider.Minimum, _slider.Maximum);
            };
            _slider.ValueChanged += (_, _) => _sliderValue.Text = $"{_slider.Value:0} W";
            try { _slider.Template = (ControlTemplate)XamlReader.Parse(SliderXaml); }
            catch (Exception) { /* stock slider */ }

            _modeAuto = new RadioButton { Style = Ui.R<Style>("Chip"), GroupName = "gpufanmode", Content = "Auto", Margin = new Thickness(0, 0, 8, 0) };
            _modeCurve = new RadioButton { Style = Ui.R<Style>("Chip"), GroupName = "gpufanmode", Content = "Curve", Margin = new Thickness(0, 0, 8, 0) };
            _modeFixed = new RadioButton { Style = Ui.R<Style>("Chip"), GroupName = "gpufanmode", Content = "Fixed", Margin = new Thickness(0) };
            _fanSlider.Minimum = m.FloorPct;
            _fanSlider.Value = Math.Clamp(m.LastFixedPct, m.FloorPct, 100);
            _fanSliderValue.Text = $"{_fanSlider.Value:0}%";
            _fanSlider.ValueChanged += (_, _) => _fanSliderValue.Text = $"{_fanSlider.Value:0}%";
            try { _fanSlider.Template = (ControlTemplate)XamlReader.Parse(SliderXaml); }
            catch (Exception) { /* stock slider */ }
            _setFixed = new Button { Style = Ui.R<Style>("AccentGlass"), Content = "Set", Padding = new Thickness(16, 0, 16, 0), MinHeight = 36, Margin = new Thickness(10, 0, 0, 0) };
            _fixed100 = new Button { Style = Ui.R<Style>("Ghost"), Content = "100%", Padding = new Thickness(12, 0, 12, 0), MinHeight = 36, Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Max all fans and hold them there" };
            _setFixed.Click += (_, _) => { m.ApplyFixed((int)_fanSlider.Value); Refresh(); };
            _fixed100.Click += (_, _) => { _fanSlider.Value = 100; m.ApplyFixed(100); Refresh(); };
            _modeFixed.Click += (_, _) => { if (m.FixedPct < 0) m.ApplyFixed((int)_fanSlider.Value); Refresh(); };
            // Click, not Checked: it fires only for user input, and Refresh() afterwards re-syncs if the confirmation was cancelled.
            _modeAuto.Click += (_, _) => { if (m.CurveActive) m.BackToAutomatic(); Refresh(); };
            _modeCurve.Click += (_, _) => { if (!m.CurveActive || m.FixedPct >= 0) m.ApplyCurve(); Refresh(); };

            _loadBar = new Bar("GPU load", "Accent");
            _memBar = new Bar("Memory controller", "Accent.99");
            _vramBar = new Bar("VRAM", "Line");

            var limitHead = new Grid();
            limitHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            limitHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_sliderValue, 1);
            limitHead.Children.Add(Ui.Text("Power limit", 13, "Muted"));
            limitHead.Children.Add(_sliderValue);
            var pbtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            pbtns.Children.Add(_applyLimit); pbtns.Children.Add(_defLimit);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            chips.Children.Add(_modeAuto); chips.Children.Add(_modeCurve); chips.Children.Add(_modeFixed);
            var fixedRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            fixedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fixedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            fixedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fixedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _fanSliderValue.VerticalAlignment = VerticalAlignment.Center; _fanSliderValue.TextAlignment = TextAlignment.Right;
            _fanSlider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_fanSliderValue, 1); Grid.SetColumn(_setFixed, 2); Grid.SetColumn(_fixed100, 3);
            fixedRow.Children.Add(_fanSlider); fixedRow.Children.Add(_fanSliderValue); fixedRow.Children.Add(_setFixed); fixedRow.Children.Add(_fixed100);

            Border Rule() { var b = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) }; b.SetResourceReference(Border.BackgroundProperty, "Line.24"); return b; }

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "CONTROL", Style = Ui.R<Style>("Caps"), Margin = new Thickness(0, 0, 0, 10) });
            stack.Children.Add(limitHead);
            stack.Children.Add(_slider);
            stack.Children.Add(_limitInfo);
            _limitHint = Ui.Text("Lowering the limit reduces heat and noise; raising it adds a little performance.", 12, "Muted");
            stack.Children.Add(_limitHint);
            stack.Children.Add(pbtns);
            _applyNote2.Margin = new Thickness(0, 8, 0, 0);
            _applyNote2.Visibility = Visibility.Collapsed;
            stack.Children.Add(_applyNote2);
            stack.Children.Add(Rule());
            stack.Children.Add(Ui.Text("Fan mode", 13, "Muted"));
            stack.Children.Add(chips);
            stack.Children.Add(fixedRow);
            stack.Children.Add(_barsRule = Rule());
            stack.Children.Add(_loadBar);
            stack.Children.Add(_memBar);
            stack.Children.Add(_vramBar);
            var stackPanel = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 4), Margin = new Thickness(8, 0, 8, 0), Child = stack };
            stackPanel.SetResourceReference(Border.BackgroundProperty, "Line.0F");
            stackPanel.SetResourceReference(Border.BorderBrushProperty, "Line.24");

            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lb = Boxed(_clock); var rb = Boxed(_temp);
            _dialBoxes = new[] { lb, rb };
            Grid.SetColumn(stackPanel, 1);
            Grid.SetColumn(rb, 2);
            hero.Children.Add(lb); hero.Children.Add(stackPanel); hero.Children.Add(rb);
            var heroCard = _heroCard = new Border { Style = Ui.R<Style>("Card"), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 16), Child = hero };

            // ---- Stat strip ----
            _sVram = new Stat("VRAM"); _sMem = new Stat("MEM CLOCK"); _sPower = new Stat("POWER");
            int fans = Math.Max(1, m.FanCount);
            _sFans = new Stat[fans];
            var strip = new UniformGrid { Rows = 1, Columns = 3 + fans, Margin = new Thickness(0, 0, 0, 16) };
            var tiles = new List<Stat> { _sVram, _sMem, _sPower };
            for (int i = 0; i < fans; i++) tiles.Add(_sFans[i] = new Stat(m.FanCount > 1 ? $"FAN {i + 1}" : "FAN"));
            for (int i = 0; i < tiles.Count; i++) { tiles[i].Margin = new Thickness(0, 0, i == tiles.Count - 1 ? 0 : 12, 0); strip.Children.Add(tiles[i]); }

            // ---- Fan curve card (editor + safety text) ----
            _applyCurve = new Button { Style = Ui.R<Style>("Primary"), Content = "Apply curve" };
            _auto = new Button { Style = Ui.R<Style>("Ghost"), Content = "Back to automatic", Margin = new Thickness(8, 0, 0, 0) };
            _reset = new Button { Style = Ui.R<Style>("Ghost"), Content = "Reset points", Margin = new Thickness(8, 0, 0, 0) };
            _applyCurve.Click += (_, _) => m.ApplyCurve();
            _auto.Click += (_, _) => m.BackToAutomatic();
            _reset.Click += (_, _) => m.ResetPoints();
            var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            btns.Children.Add(_applyCurve); btns.Children.Add(_auto); btns.Children.Add(_reset);
            var curve = new StackPanel();
            curve.Children.Add(new TextBlock { Text = "FAN CURVE", Style = Ui.R<Style>("Caps") });
            curve.Children.Add(_curveStatus);
            curve.Children.Add(new Border { Margin = new Thickness(0, 12, 0, 0), Child = new CurveEditor(m) });
            curve.Children.Add(Ui.Text($"Minimum {GpuModel.FanMin}% · forced to 100% from {GpuModel.ForceFullAt}°C · only steps down 10% per second, 3°C below where it was raised. Drag points or use arrow keys.", 12, "Muted"));
            curve.Children.Add(btns);
            _applyNote.Margin = new Thickness(0, 10, 0, 0);
            _applyNote.Visibility = Visibility.Collapsed;
            curve.Children.Add(_applyNote);
            var curveCard = new Border { Style = Ui.R<Style>("Card"), Margin = new Thickness(0, 0, 8, 0), Child = curve };

            var histBody = new StackPanel();
            histBody.Children.Add(new TextBlock { Text = "LIVE HISTORY · LAST 60 S", Style = Ui.R<Style>("Caps") });
            histBody.Children.Add(_hist);
            var histCard = new Border { Style = Ui.R<Style>("Card"), Margin = new Thickness(8, 0, 0, 0), Child = histBody };

            var lower = _lower = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(histCard, 1);
            lower.Children.Add(curveCard); lower.Children.Add(histCard);

            _content.Children.Add(title);
            _content.Children.Add(heroCard);
            _content.Children.Add(strip);
            _content.Children.Add(lower);

            var root = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(unavailCard);
            root.Children.Add(_banner);
            root.Children.Add(_msg);
            root.Children.Add(_content);
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 12, 24), Content = root,
            };

            // Subscribe only while on screen so a discarded view isn't kept alive by the model.
            Loaded += (_, _) => { m.Changed -= Refresh; m.Changed += Refresh; Refresh(); };
            Unloaded += (_, _) => m.Changed -= Refresh;
            ApplyCompact(File.Exists(CompactMarker), save: false);
            unavailCard.Visibility = m.Available ? Visibility.Collapsed : Visibility.Visible;
            _content.Visibility = m.Available ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        private static string Mhz(int v) => v >= 0 ? v.ToString() : "--";
        private static string Pct(int v) => v >= 0 ? v + "%" : "--";
        private static string W(double w) => double.IsNaN(w) ? "--" : $"{w:0}";
        private static string Gb(long mb) => mb >= 0 ? (mb / 1024.0).ToString("0.0") : "--";

        private void Refresh()
        {
            var m = _m;
            _name.Text = m.Name;
            _mode.Text = "Fan mode: " + Ui.Mode(m);
            Ui.Ref(_mode, TextBlock.ForegroundProperty, m.CurveActive ? "Good" : "Muted", ref _kMode);

            // Dials
            bool clk = m.CoreMHz >= 0;
            _clock.Set(clk ? m.CoreMHz : double.NaN, Mhz(m.CoreMHz), "Accent", "Text",
                m.Load >= 0 ? m.Load / 100.0 : double.NaN, "LOAD " + Pct(m.Load), "Accent", $"MEM {Mhz(m.MemMHz)} MHz");
            int fan = m.Fans.Length > 0 ? m.Fans.Max() : -1;
            string tk = m.Temp >= 75 ? "Danger" : "Good";
            _temp.Set(m.Temp >= 0 ? m.Temp : double.NaN, m.Temp >= 0 ? m.Temp.ToString() : "--", tk, m.Temp >= 75 ? "Danger" : "Text",
                fan >= 0 ? fan / 100.0 : double.NaN, "FAN " + Pct(fan), "Violet", $"PWR {W(m.PowerW)} / {W(m.LimitW)} W");

            // Bars
            _loadBar.Set(m.Load >= 0 ? m.Load / 100.0 : double.NaN, Pct(m.Load));
            _memBar.Set(m.MemLoad >= 0 ? m.MemLoad / 100.0 : double.NaN, Pct(m.MemLoad));
            double vf = m.VramTotalMB > 0 && m.VramUsedMB >= 0 ? (double)m.VramUsedMB / m.VramTotalMB : double.NaN;
            _vramBar.Set(vf, $"{Gb(m.VramUsedMB)} / {Gb(m.VramTotalMB)} GB");

            // Stat tiles
            string P(double f) => double.IsNaN(f) ? "--" : $"{f * 100:0}%";
            _sVram.Set(vf, P(vf), "Line", $"{Gb(m.VramUsedMB)}/{Gb(m.VramTotalMB)} GB", "MEM CTRL " + Pct(m.MemLoad));
            double mf = m.MemMHz >= 0 && m.MaxMemMHz > 0 ? Math.Min(1.0, (double)m.MemMHz / m.MaxMemMHz) : double.NaN;
            _sMem.Set(mf, P(mf), "Accent", Mhz(m.MemMHz) + " MHz", "max " + Mhz(m.MaxMemMHz));
            double pf = !double.IsNaN(m.PowerW) && !double.IsNaN(m.LimitW) && m.LimitW > 0 ? Math.Min(1.0, m.PowerW / m.LimitW) : double.NaN;
            _sPower.Set(pf, P(pf), "Blue", W(m.PowerW) + " W", "limit " + W(m.LimitW) + " W");
            for (int i = 0; i < _sFans.Length; i++)
            {
                int f = i < m.Fans.Length ? m.Fans[i] : -1;
                _sFans[i].Set(f >= 0 ? f / 100.0 : double.NaN, Pct(f), "Violet", Pct(f), Ui.Mode(m));
            }

            _hist.Update(m);

            // Status / warnings
            _curveStatus.Text = (m.FixedPct >= 0 ? $"Fixed {m.FixedPct}% — holding {m.FanCount} fan(s); this curve is not in use"
                : m.CurveActive ? $"Curve active — driving {m.FanCount} fan(s)"
                : !string.IsNullOrEmpty(m.Status) && m.SavedWasActive && !m.EverApplied ? m.Status
                : "Automatic fan control") + (m.Dirty ? " · edited, not applied" : "");
            Ui.Ref(_curveStatus, TextBlock.ForegroundProperty, m.CurveActive ? "Good" : "Muted", ref _kStatus);

            bool warn = !string.IsNullOrEmpty(m.Warning);
            var msg = warn ? m.Warning : (m.CurveActive || m.Status.StartsWith("Saved") ? "" : m.Status);
            _msgText.Text = msg;
            _msg.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
            Ui.Ref(_msgText, TextBlock.ForegroundProperty, warn ? "Danger" : "Muted", ref _kMsgFg);
            Ui.Ref(_msg, Border.BackgroundProperty, warn ? "Danger.12" : "Line.0F", ref _kMsgBg);
            Ui.Ref(_msg, Border.BorderBrushProperty, warn ? "Danger.66" : "Line.24", ref _kMsgBd);

            var ab = m.Afterburner ? Visibility.Visible : Visibility.Collapsed;
            _banner.Visibility = ab;
            _applyNote.Visibility = ab;
            _applyNote2.Visibility = ab;
            _applyCurve.IsEnabled = m.CanApply;
            _modeCurve.IsEnabled = _modeFixed.IsEnabled = _setFixed.IsEnabled = _fixed100.IsEnabled = m.CanApply;
            _applyLimit.IsEnabled = _defLimit.IsEnabled = m.CanApply && m.PowerSupported;
            if (!m.CurveActive) _modeAuto.IsChecked = true; else if (m.FixedPct >= 0) _modeFixed.IsChecked = true; else _modeCurve.IsChecked = true;

            if (m.PowerSupported)
            {
                _slider.IsEnabled = true;
                if (!_sliderInit)
                {
                    _slider.Minimum = Math.Ceiling(m.MinW);
                    _slider.Maximum = Math.Floor(m.MaxW);
                    _slider.Value = double.IsNaN(m.LimitW) ? m.DefaultW : Math.Clamp(m.LimitW, _slider.Minimum, _slider.Maximum);
                    _sliderInit = true;
                }
                _limitInfo.Text = $"Default {m.DefaultW:0} W · range {m.MinW:0}–{m.MaxW:0} W · current {Ui.Watts(m.LimitW)}";
            }
            else
            {
                _slider.IsEnabled = false;
                _limitInfo.Text = "Power limit is not adjustable on this GPU/driver.";
            }
        }
    }
}
