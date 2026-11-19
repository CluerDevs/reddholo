using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace BcuHolo.Widget
{
    /// <summary>Right-click menu: dark glass styling (theme keys via DynamicResource) and the configuration commands.</summary>
    public sealed partial class WidgetWindow
    {
        // Parsed per menu open (cheap) so each ContextMenu owns its dictionary. Falls back to the stock menu if anything fails.
        private const string MenuXaml = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Style x:Key=""HoloMenu"" TargetType=""ContextMenu"">
    <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
    <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
    <Setter Property=""HasDropShadow"" Value=""False""/>
    <Setter Property=""Foreground"" Value=""{DynamicResource Text}""/>
    <Setter Property=""FontSize"" Value=""13""/>
    <Setter Property=""MinWidth"" Value=""220""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ContextMenu"">
          <Grid Margin=""0,0,14,14"">
            <Border CornerRadius=""12"" Background=""{DynamicResource Bg.CC}"">
              <Border.Effect><DropShadowEffect Color=""#000000"" BlurRadius=""18"" ShadowDepth=""4"" Opacity=""0.55""/></Border.Effect>
            </Border>
            <Border CornerRadius=""12"" Background=""{DynamicResource Bg.EB}"" BorderBrush=""{DynamicResource GlassLine}"" BorderThickness=""1"">
              <Border CornerRadius=""11"" Background=""{DynamicResource GlassFill}"" Padding=""0,6"">
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation=""Cycle"" KeyboardNavigation.TabNavigation=""Cycle""/>
              </Border>
            </Border>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""{x:Static MenuItem.SeparatorStyleKey}"" TargetType=""Separator"">
    <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Separator"">
          <Border Height=""1"" Margin=""14,5"" Background=""{DynamicResource Line.24}""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""MenuItem"">
    <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
    <Setter Property=""Foreground"" Value=""{DynamicResource Text}""/>
    <Setter Property=""FontSize"" Value=""13""/>
    <Setter Property=""FocusVisualStyle"" Value=""{x:Null}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""MenuItem"">
          <Border x:Name=""Bd"" Margin=""6,1"" Padding=""8,6,10,6"" CornerRadius=""8"" BorderThickness=""1""
                  Background=""{DynamicResource Accent.00}"" BorderBrush=""{DynamicResource Accent.00}"">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""24""/>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""Auto""/>
              </Grid.ColumnDefinitions>
              <Border x:Name=""Box"" Width=""14"" Height=""14"" CornerRadius=""4"" BorderThickness=""1"" HorizontalAlignment=""Left"" VerticalAlignment=""Center""
                      BorderBrush=""{DynamicResource Line.40}"" Background=""{DynamicResource Accent.00}"" Visibility=""Collapsed""/>
              <Path x:Name=""Check"" Width=""14"" Height=""14"" Data=""M3,7.5 L6,10.5 L11,4"" Stroke=""{DynamicResource Accent}"" StrokeThickness=""2""
                    StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" StrokeLineJoin=""Round"" HorizontalAlignment=""Left"" VerticalAlignment=""Center"" Visibility=""Collapsed""/>
              <ContentPresenter Grid.Column=""1"" ContentSource=""Header"" RecognizesAccessKey=""True"" VerticalAlignment=""Center""/>
              <Path x:Name=""Arrow"" Grid.Column=""2"" Data=""M0,0 L4,4 L0,8"" Stroke=""{DynamicResource Muted}"" StrokeThickness=""1.5""
                    Margin=""14,0,0,0"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Visibility=""Collapsed""/>
              <Popup x:Name=""PART_Popup"" Placement=""Right"" HorizontalOffset=""6"" VerticalOffset=""-8"" AllowsTransparency=""True"" Focusable=""False""
                     PopupAnimation=""None"" IsOpen=""{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}"">
                <Grid Margin=""0,0,14,14"" MinWidth=""200"">
                  <Border CornerRadius=""12"" Background=""{DynamicResource Bg.CC}"">
                    <Border.Effect><DropShadowEffect Color=""#000000"" BlurRadius=""18"" ShadowDepth=""4"" Opacity=""0.55""/></Border.Effect>
                  </Border>
                  <Border CornerRadius=""12"" Background=""{DynamicResource Bg.EB}"" BorderBrush=""{DynamicResource GlassLine}"" BorderThickness=""1"">
                    <Border CornerRadius=""11"" Background=""{DynamicResource GlassFill}"" Padding=""0,6"">
                      <ItemsPresenter KeyboardNavigation.DirectionalNavigation=""Cycle"" KeyboardNavigation.TabNavigation=""Cycle""/>
                    </Border>
                  </Border>
                </Grid>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsCheckable"" Value=""True"">
              <Setter TargetName=""Box"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""Check"" Property=""Visibility"" Value=""Visible""/>
              <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""{DynamicResource Accent.66}""/>
              <Setter TargetName=""Box"" Property=""Background"" Value=""{DynamicResource Accent.1A}""/>
            </Trigger>
            <Trigger Property=""Role"" Value=""SubmenuHeader"">
              <Setter TargetName=""Arrow"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsSubmenuOpen"" Value=""True"">
              <Setter TargetName=""Bd"" Property=""Background"" Value=""{DynamicResource Accent.14}""/>
            </Trigger>
            <Trigger Property=""IsHighlighted"" Value=""True"">
              <Setter TargetName=""Bd"" Property=""Background"" Value=""{DynamicResource Accent.1F}""/>
              <Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""{DynamicResource Accent.40}""/>
              <Setter TargetName=""Arrow"" Property=""Stroke"" Value=""{DynamicResource Accent}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Foreground"" Value=""{DynamicResource Muted}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        /// <summary>Parsed menu styles, or null (stock menu) if parsing or template instantiation fails.</summary>
        private static ResourceDictionary MenuStyles()
        {
            try
            {
                var d = (ResourceDictionary)XamlReader.Parse(MenuXaml);
                // Templates instantiate lazily; build each once here so a bad value fails now (inside this try) rather than on open.
                foreach (var style in d.Values.OfType<Style>())
                    foreach (var setter in style.Setters.OfType<Setter>())
                        if (setter.Value is ControlTemplate ct) ct.LoadContent();
                return d;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Widget menu style: " + ex.Message);
                return null;
            }
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var styles = MenuStyles();
            if (styles != null)
            {
                menu.Resources.MergedDictionaries.Add(styles);
                menu.Style = (Style)styles["HoloMenu"];
            }

            var preset = new MenuItem { Header = "Preset" };
            foreach (var (name, ids) in WidgetCatalog.Presets)
            {
                bool current = ids.Length == _s.Items.Count && !ids.Except(_s.Items).Any();
                preset.Items.Add(Check(name, current, () => { _s.Items = ids.ToList(); Rebuild(); }));
            }
            menu.Items.Add(preset);

            var show = new MenuItem { Header = "Show" };
            foreach (var section in WidgetCatalog.Items.GroupBy(i => i.Section))
            {
                var sub = new MenuItem { Header = section.Key };
                foreach (var item in section)
                {
                    string id = item.Id;
                    sub.Items.Add(Check(item.Label, _s.Items.Contains(id), () =>
                    {
                        if (!_s.Items.Remove(id)) _s.Items.Add(id);
                        Rebuild();
                    }, staysOpen: true));
                }
                show.Items.Add(sub);
            }
            menu.Items.Add(show);

            var layout = new MenuItem { Header = "Layout" };
            foreach (var (id, label) in new[] { ("Compact", "Compact (two columns)"), ("Stack", "Stack (narrow column)"), ("Strip", "Strip (horizontal row)") })
                layout.Items.Add(Check(label, _s.Layout == id, () => { _s.Layout = id; Rebuild(); }));
            menu.Items.Add(layout);

            var size = new MenuItem { Header = "Size" };
            foreach (var (v, label) in new[] { (0.8, "Small"), (1.0, "Normal"), (1.25, "Large") })
                size.Items.Add(Check(label, Math.Abs(_s.Scale - v) < 0.01, () => { _s.Scale = v; ApplyWindowSettings(); }));
            menu.Items.Add(size);

            var opacity = new MenuItem { Header = "Transparency" };
            foreach (var (v, label) in new[] { (1.0, "Solid"), (0.85, "85%"), (0.7, "70%"), (0.55, "55%") })
                opacity.Items.Add(Check(label, Math.Abs(_s.Opacity - v) < 0.01, () => { _s.Opacity = v; ApplyWindowSettings(); }));
            menu.Items.Add(opacity);

            menu.Items.Add(new Separator());
            menu.Items.Add(Check("Always on top", _s.OnTop, () => { _s.OnTop = true; ApplyWindowSettings(); }));
            menu.Items.Add(Check("Pin to desktop (behind windows)", !_s.OnTop, () => { _s.OnTop = false; ApplyWindowSettings(); }));
            menu.Items.Add(Check("Lock position", _s.Locked, () => { _s.Locked = !_s.Locked; _s.Save(); }));
            menu.Items.Add(Check("Start with Windows", StartupTask.IsRegistered(), ToggleStartup));

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Open BCU Holo", () => (Application.Current as App)?.ShowMain()));
            menu.Items.Add(Item("Close widget", () => { RememberOpen(false); Close(); }));
            return menu;
        }

        private void ToggleStartup()
        {
            var error = StartupTask.IsRegistered() ? StartupTask.Remove() : StartupTask.Register();
            if (error != null) MessageBox.Show(error, "BCU Holo widget", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static MenuItem Item(string header, Action click)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => click();
            return m;
        }

        private MenuItem Check(string header, bool on, Action click, bool staysOpen = false)
        {
            var m = new MenuItem { Header = header, IsCheckable = true, IsChecked = on, StaysOpenOnClick = staysOpen };
            m.Click += (_, _) => { click(); _s.Save(); };
            return m;
        }
    }
}
