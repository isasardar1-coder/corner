// Corner draws every control itself; the Fluent theme is only there for the parts Avalonia templates (text boxes,
// scroll viewers, tooltips). These styles take Fluent's own look back off them, so they look as on Windows:
// a text box is bare text and a caret, with no box, underline or highlight of its own.
using Avalonia.Styling;

namespace Corner
{
    static class Look
    {
        public static Styles Plain()
        {
            var s = new Styles();
            s.Add(new Style(x => x.OfType<TextBox>())
            {
                Setters = { new Setter(Layoutable.MinHeightProperty, 0.0), new Setter(Layoutable.MinWidthProperty, 0.0) }
            });
            foreach (var state in new[] { ":pointerover", ":focus", ":disabled" })
            {
                s.Add(new Style(x => x.OfType<TextBox>().Class(state).Template().OfType<Border>().Name("PART_BorderElement"))
                {
                    Setters =
                    {
                        new Setter(Border.BackgroundProperty, Brushes.Transparent),
                        new Setter(Border.BorderBrushProperty, Brushes.Transparent),
                        new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                    }
                });
            }
            s.Add(new Style(x => x.OfType<TextBox>().Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters = { new Setter(Border.MinHeightProperty, 0.0), new Setter(Border.CornerRadiusProperty, new CornerRadius(0)) }
            });
            // Fluent keeps the typed text's colour for itself on hover and focus; the box's own Foreground wins instead
            foreach (var state in new[] { ":pointerover", ":focus" })
                s.Add(new Style(x => x.OfType<TextBox>().Class(state)) { Setters = { } });
            return s;
        }
    }
}
