using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Drivers
{
    /// <summary>Dashboard tile. Built in code because the shell expects a DashTile Border as the root.</summary>
    internal static class DriversTile
    {
        public static Border Create(DriversModel model, IHoloHost host)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Style = Res<Style>("Caps"), Text = "DRIVERS" });
            stack.Children.Add(Bound("GpuName", 16, "Display", "Text", new Thickness(0, 4, 0, 0), wrap: true));
            stack.Children.Add(Bound("GpuVersion", 13, "Mono", "Accent", new Thickness(0, 4, 0, 0)));
            stack.Children.Add(Bound("BiosLine", 13, null, "Muted", new Thickness(0, 8, 0, 0)));
            // Update line colour comes from the model (orange when updates exist, green when up to date).
            var updates = Bound("UpdateTileText", 14, null, "Text", new Thickness(0, 12, 0, 0));
            void Tint() => updates.SetResourceReference(TextBlock.ForegroundProperty, model.UpdateBrushKey);
            model.PropertyChanged += (_, _) => Tint();
            Tint();
            stack.Children.Add(updates);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            var check = new Button { Style = Res<Style>("AccentGlass"), Content = "Check now", Padding = new Thickness(14, 0, 14, 0) };
            check.SetBinding(UIElement.IsEnabledProperty, new Binding("CanCheck"));
            check.Click += (_, _) => _ = model.CheckUpdatesAsync();
            var details = new Button { Style = Res<Style>("Ghost"), Content = "Details", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0) };
            details.Click += (_, _) => host.Navigate("drivers");
            buttons.Children.Add(check);
            buttons.Children.Add(details);
            stack.Children.Add(buttons);

            return new GlassCard
            {
                Style = Res<Style>("DashTile"),
                DataContext = model,
                Child = stack,
            };
        }

        private static TextBlock Bound(string path, double size, string fontKey, string brushKey, Thickness margin, bool wrap = false)
        {
            // Single line with ellipsis (full text in the tooltip) keeps the tile the same height as its neighbours.
            var tb = new TextBlock { FontSize = size, Margin = margin,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.SetBinding(FrameworkElement.ToolTipProperty, new Binding(path));
            if (fontKey != null) tb.FontFamily = Res<FontFamily>(fontKey);
            tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            tb.SetBinding(TextBlock.TextProperty, new Binding(path));
            return tb;
        }

        private static T Res<T>(string key) => (T)Application.Current.FindResource(key);
    }
}
