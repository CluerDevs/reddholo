using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BcuHolo.Themes
{
    /// <summary>Caption-bar popup: one swatch card per theme. Click applies live and saves.</summary>
    internal static class ThemePicker
    {
        public static Popup Create(UIElement anchor)
        {
            var list = new WrapPanel { Width = 2 * 236 };
            var root = new StackPanel();
            root.Children.Add(new TextBlock { Style = (Style)Application.Current.FindResource("Caps"), Text = "THEME", Margin = new Thickness(6, 0, 0, 8) });
            root.Children.Add(list);

            var panel = new Border { Child = root, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(14) };
            panel.SetResourceReference(Border.BackgroundProperty, "Tile");
            panel.SetResourceReference(Border.BorderBrushProperty, "GlassLine");
            panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 };

            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade, Child = new Border { Padding = new Thickness(0, 6, 24, 24), Child = panel } };

            void Build()
            {
                list.Children.Clear();
                foreach (var t in ThemeManager.All) list.Children.Add(Card(t));
            }
            ThemeManager.Changed += Build; // refresh the "current" ring
            Build();
            return popup;
        }

        private static Button Card(Theme t)
        {
            bool current = t == ThemeManager.Current;
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var hex in new[] { t.Accent, t.Line, t.Good, t.Danger, t.Violet })
                dots.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(ThemeManager.C(hex)) }); // swatches show that theme's own colours, so literal is right here

            var text = new StackPanel();
            text.Children.Add(dots);
            text.Children.Add(new TextBlock { Text = t.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeManager.C(t.Text)) });
            text.Children.Add(new TextBlock { Text = t.Blurb, FontSize = 12, Foreground = new SolidColorBrush(ThemeManager.C(t.Muted)), Margin = new Thickness(0, 2, 0, 0) });

            var face = new Border
            {
                Child = text, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 12),
                BorderThickness = new Thickness(current ? 2 : 1),
                BorderBrush = new SolidColorBrush(current ? ThemeManager.C(t.Accent) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Background = new LinearGradientBrush(ThemeManager.C(t.Tile), ThemeManager.C(t.Bg), 90),
            };
            var b = new Button { Content = face, Width = 224, Margin = new Thickness(6), Cursor = System.Windows.Input.Cursors.Hand,
                Template = BareTemplate(), ToolTip = current ? t.Name + " (current)" : "Switch to " + t.Name };
            System.Windows.Automation.AutomationProperties.SetName(b, t.Name + " theme");
            b.Click += (_, _) => ThemeManager.Apply(t);
            return b;
        }

        // Button chrome off: the swatch card is the whole visual. Focus/hover come from the card's border via opacity.
        private static ControlTemplate BareTemplate()
        {
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = cp };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            tpl.Triggers.Add(hover);
            return tpl;
        }
    }
}
