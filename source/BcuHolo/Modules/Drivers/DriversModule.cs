using System.Windows;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Drivers
{
    public sealed class DriversModule : IHoloModule
    {
        // One model for the page and the tile, so a check from either one updates both.
        private readonly DriversModel _model = new();

        public string Id => "drivers";
        public string Title => "Drivers";
        public string Glyph => "\uE7F8";
        public int Order => 50;

        public FrameworkElement CreateView(IHoloHost host)
        {
            _model.Host = host;
            return new DriversView(_model);
        }

        public FrameworkElement CreateTile(IHoloHost host)
        {
            _model.Host = host;
            _ = _model.LoadLocalAsync(); // local WMI only; the tile shows GPU and BIOS
            return DriversTile.Create(_model, host);
        }
    }
}
