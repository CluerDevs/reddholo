using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace BcuHolo
{
    /// <summary>
    /// Instrument-style easing for live values: each key glides from what it currently shows to the new target, so gauges
    /// sweep instead of jumping once a second. One CompositionTarget.Rendering handler drives every active tween and is
    /// detached when nothing is moving. Honours Windows' "Show animations" setting.
    /// </summary>
    internal static class Motion
    {
        private const double Seconds = 0.25;
        // Sub-threshold jitter (idle load 1%->2%) snaps instead of animating every second. Tweens run at the display's
        // frame rate: the cards no longer carry a drop shadow that each frame had to re-blur.

        private sealed class State
        {
            public double From, To, Shown = double.NaN;
            public TimeSpan Start;
            public Action<double> Apply;
            public bool Running;
        }

        private static readonly ConditionalWeakTable<object, State> States = new();
        private static readonly List<State> Running = new();
        private static bool _hooked;

        /// <summary>
        /// Moves <paramref name="key"/>'s shown value toward <paramref name="target"/>, calling <paramref name="apply"/> each frame.
        /// NaN applies immediately (no reading). The first value sweeps up from 0, like an instrument powering on.
        /// Moves smaller than <paramref name="minStep"/> (default 2% of a 0..1 fraction) snap without animating.
        /// </summary>
        public static void To(object key, double target, Action<double> apply, double minStep = 0.02)
        {
            var s = States.GetOrCreateValue(key);
            s.Apply = apply;
            if (double.IsNaN(target) || !SystemParameters.ClientAreaAnimation)
            {
                Stop(s);
                s.Shown = target;
                apply(target);
                return;
            }
            if (s.Running && s.To == target) return;
            if (!s.Running && !double.IsNaN(s.Shown) && Math.Abs(s.Shown - target) < minStep) { s.Shown = target; apply(target); return; }
            s.From = double.IsNaN(s.Shown) ? 0 : s.Shown;
            s.To = target;
            s.Start = TimeSpan.MinValue; // stamped on the next frame, so a long idle gap doesn't skip the tween
            if (!s.Running) { s.Running = true; Running.Add(s); }
            if (!_hooked) { _hooked = true; CompositionTarget.Rendering += OnFrame; }
        }

        /// <summary>Subtle entrance: fade in and rise 12 px over 0.2 s, starting after <paramref name="delayMs"/>.</summary>
        public static void Enter(UIElement e, int delayMs = 0)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var dur = TimeSpan.FromSeconds(0.2);
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var move = new TranslateTransform(0, 12);
            e.RenderTransform = move;
            e.Opacity = 0;
            // Base opacity stays 0 through the start delay (an animation not yet begun doesn't apply its From value);
            // on completion the animation is removed and the real value set, so nothing stays animated.
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = ease };
            fade.Completed += (_, _) => { e.BeginAnimation(UIElement.OpacityProperty, null); e.Opacity = 1; };
            e.BeginAnimation(UIElement.OpacityProperty, fade);
            var rise = new System.Windows.Media.Animation.DoubleAnimation(12, 0, dur) { BeginTime = begin, EasingFunction = ease };
            rise.Completed += (_, _) => { e.RenderTransform = Transform.Identity; };
            move.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        private static void Stop(State s)
        {
            if (!s.Running) return;
            s.Running = false;
            Running.Remove(s);
        }

        private static void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            for (int i = Running.Count - 1; i >= 0; i--)
            {
                var s = Running[i];
                if (s.Start == TimeSpan.MinValue) s.Start = now;
                double t = Math.Clamp((now - s.Start).TotalSeconds / Seconds, 0, 1);
                double k = 1 - Math.Pow(1 - t, 3); // ease-out cubic: fast start, gentle settle
                s.Shown = s.From + (s.To - s.From) * k;
                try { s.Apply(s.Shown); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Motion: " + ex.Message); t = 1; }
                if (t >= 1) { s.Shown = s.To; s.Running = false; Running.RemoveAt(i); }
            }
            if (Running.Count == 0) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
        }
    }
}
