using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Dashboard
{
    public sealed class DashboardModule : IHoloModule
    {
        private readonly Func<IEnumerable<IHoloModule>> _modules;

        /// <param name="modules">All loaded modules; their tiles fill the dashboard's second row.</param>
        public DashboardModule(Func<IEnumerable<IHoloModule>> modules) => _modules = modules;

        public string Id => "dashboard";
        public string Title => "Dashboard";
        public string Glyph => "\uE80F";
        public int Order => 0;

        public FrameworkElement CreateView(IHoloHost host) =>
            new DashboardView(host, _modules().Where(m => m != this).OrderBy(m => m.Order));

        public FrameworkElement CreateTile(IHoloHost host) => null;
    }
}
