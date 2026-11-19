using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BcuHolo
{
    /// <summary>
    /// Card border whose glass fill, edge and drop shadow live in a separate bitmap-cached layer under the content.
    /// A DropShadowEffect on the card itself re-blurs the whole card whenever anything inside changes (gauges sweep every
    /// second); here the shadow layer only re-renders on resize or theme change. The shadow comes from the card shape
    /// only, so the faint halo the old effect cast under text and gauges is gone.
    /// </summary>
    public class GlassCard : Border
    {
        private static readonly DropShadowEffect Shadow = Frozen(new DropShadowEffect { Color = Colors.Black, BlurRadius = 32, ShadowDepth = 8, Opacity = 0.45 });

        private readonly Decorator _layer;

        public GlassCard()
        {
            var skin = new Border { Effect = Shadow };
            Bind(skin, BackgroundProperty);
            Bind(skin, BorderBrushProperty);
            Bind(skin, BorderThicknessProperty);
            Bind(skin, CornerRadiusProperty);
            _layer = new Decorator { Child = skin, CacheMode = new BitmapCache(), IsHitTestVisible = false };
            AddVisualChild(_layer);
        }

        private void Bind(Border skin, DependencyProperty p) =>
            skin.SetBinding(p, new Binding(p.Name) { Source = this });

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override int VisualChildrenCount => base.VisualChildrenCount + 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _layer : base.GetVisualChild(index - 1);

        protected override Size MeasureOverride(Size constraint)
        {
            _layer.Measure(constraint);
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _layer.Arrange(new Rect(finalSize));
            return base.ArrangeOverride(finalSize);
        }

        // The layer draws fill and edge; drawing them here too would double the glass.
        protected override void OnRender(DrawingContext dc) { }

        // OnRender draws nothing, so keep the card's own area clickable the way a filled Border is.
        protected override HitTestResult HitTestCore(PointHitTestParameters p) =>
            Background != null && new Rect(RenderSize).Contains(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;
    }
}
