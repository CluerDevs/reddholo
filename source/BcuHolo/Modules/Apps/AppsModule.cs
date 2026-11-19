using System.Windows;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Apps
{
    public sealed class AppsModule : IHoloModule
    {
        public string Id => "apps";
        public string Title => "Apps";
        public string Glyph => "";
        public int Order => 10;
        public FrameworkElement CreateView(IHoloHost host) => new AppsView();
        public FrameworkElement CreateTile(IHoloHost host) => null;
    }
}
