using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace BcuHolo.Themes
{
    /// <summary>
    /// Overlay scrollbars (App.xaml ScrollViewer template) stay invisible, including while scrolling (user preference);
    /// they appear only while the pointer is on the bar's strip, so it can still be dragged.
    /// </summary>
    internal static class OverlayScrollbars
    {
        private const double Shown = 0.7;

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(ScrollBar), UIElement.MouseEnterEvent, new MouseEventHandler((s, _) => Fade((ScrollBar)s, Shown, 0.15)));
            EventManager.RegisterClassHandler(typeof(ScrollBar), UIElement.MouseLeaveEvent, new MouseEventHandler((s, _) =>
            {
                var bar = (ScrollBar)s;
                if (!bar.IsMouseCaptureWithin) Flash(bar); // still dragging the thumb: keep it until release
            }));
            EventManager.RegisterClassHandler(typeof(ScrollBar), UIElement.LostMouseCaptureEvent, new MouseEventHandler((s, _) =>
            {
                var bar = (ScrollBar)s;
                if (!bar.IsMouseOver) Flash(bar);
            }));
        }

        /// <summary>Show now, hold ~1 s, fade out.</summary>
        private static void Flash(ScrollBar bar)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(Shown, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(Shown, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.1))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            bar.BeginAnimation(UIElement.OpacityProperty, a);
        }

        private static void Fade(ScrollBar bar, double to, double seconds) =>
            bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)));
    }
}
