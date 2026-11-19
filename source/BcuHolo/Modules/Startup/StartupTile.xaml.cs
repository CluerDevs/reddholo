using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Startup
{
    /// <summary>Dashboard tile: enabled/total counts plus up to four enabled third-party items.</summary>
    public partial class StartupTile : GlassCard
    {
        private readonly StartupModule _module;
        private readonly IHoloHost _host;

        public StartupTile(StartupModule module, IHoloHost host)
        {
            InitializeComponent();
            _module = module;
            _host = host;

            module.Changed += Redraw;
            Redraw();
        }

        private void Redraw()
        {
            var rows = _module.Rows;

            CountText.Inlines.Clear();
            CountText.Inlines.Add(new Run($"{rows.Count(r => r.Enabled)} on"));
            var total = new Run($" / {rows.Count}") { FontSize = 16, FontWeight = FontWeights.Normal };
            total.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            CountText.Inlines.Add(total);

            ListPanel.Children.Clear();
            foreach (var r in rows.Where(r => r.Enabled && !r.IsMicrosoft).Take(4))
                ListPanel.Children.Add(Line(r));

            StatusText.Text = _module.IsLoading && rows.Count == 0 ? "Loading…"
                : _module.LoadError != null ? "Some startup sources failed to load."
                : ListPanel.Children.Count == 0 ? "No third-party items enabled."
                : "";
        }

        private Border Line(StartupRow r)
        {
            var kind = new TextBlock
            {
                // Short kind labels: the tile is narrow and the name matters more.
                Text = r.Kind switch { "Run key / Startup folder" => "Run key", "Scheduled task" => "Task", "Browser helper" => "Browser", _ => r.Kind },
                FontSize = 12,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            kind.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(kind, Dock.Right);
            var name = new TextBlock
            {
                Text = r.Name,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = r.Name,
            };

            var dock = new DockPanel();
            dock.Children.Add(kind);
            dock.Children.Add(name);
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 8), Child = dock };
            line.SetResourceReference(Border.BorderBrushProperty, "RowLine");
            return line;
        }

        private void Manage_Click(object sender, RoutedEventArgs e) => _host.Navigate("startup");
    }
}
