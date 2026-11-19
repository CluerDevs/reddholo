using System.Collections.Generic;
using System.Windows;

namespace BcuHolo.Plugins
{
    /// <summary>
    /// One entry in the rail: a full-page view plus an optional dashboard tile.
    /// Built-in modules and third-party plugins implement the same interface.
    /// </summary>
    public interface IHoloModule
    {
        /// <summary>Stable lowercase id, e.g. "apps". Used by <see cref="IHoloHost.Navigate"/>.</summary>
        string Id { get; }
        string Title { get; }
        /// <summary>Segoe Fluent Icons glyph, e.g. "\uE71D".</summary>
        string Glyph { get; }
        /// <summary>Rail position; lower comes first.</summary>
        int Order { get; }

        /// <summary>Called once; the host caches the view and re-shows it on navigation.</summary>
        FrameworkElement CreateView(IHoloHost host);

        /// <summary>Called once for the dashboard; return null for no tile.</summary>
        FrameworkElement CreateTile(IHoloHost host);
    }

    /// <summary>What the shell offers to modules.</summary>
    public interface IHoloHost
    {
        void Navigate(string moduleId);

        /// <summary>Yes/No dialog, defaulting to No. Every destructive action goes through this.</summary>
        bool Confirm(string title, string message);

        /// <summary>Small text badge next to the module in the rail (e.g. "75 GB"); null clears it.</summary>
        void SetBadge(string moduleId, string text);
    }

    /// <summary>A source of hardware or environment readings (temperatures, loads, a smart plug's watts…).</summary>
    public interface ISensorSource
    {
        string Name { get; }

        /// <summary>Polled about once a second from a background thread; must not block for long.</summary>
        IEnumerable<SensorReading> Read();
    }

    /// <param name="Group">e.g. "CPU", "GPU", "Drive C:"</param>
    /// <param name="Unit">e.g. "°C", "%", "W", "V", "RPM"</param>
    public sealed record SensorReading(string Source, string Group, string Name, double Value, string Unit);
}
