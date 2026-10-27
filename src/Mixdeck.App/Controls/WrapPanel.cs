using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Mixdeck.App.Controls;

/// <summary>Range les tranches de gauche à droite et passe à la ligne : la console ne défile que verticalement.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(12d, (d, _) => ((WrapPanel)d).InvalidateMeasure()));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            x += size.Width + Spacing;
            width = Math.Max(width, x - Spacing);
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(width, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}
