using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ComfyGenReader.Controls;

/// <summary>
/// Paints search matches behind a TextBox's text: every match in MatchBrush, the current one in CurrentBrush. A TextBox can only
/// highlight its one selection, so the TextBox gets a transparent background and sits over this element in the
/// same cell. Only matches on the visible lines are drawn, again on every scroll, resize and wrap change.
/// </summary>
public sealed class MatchHighlighter : FrameworkElement
{
    public static readonly DependencyProperty MatchBrushProperty = DependencyProperty.Register(
        nameof(MatchBrush), typeof(Brush), typeof(MatchHighlighter),
        new FrameworkPropertyMetadata(Brushes.Yellow, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentBrushProperty = DependencyProperty.Register(
        nameof(CurrentBrush), typeof(Brush), typeof(MatchHighlighter),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    private TextBox? target;
    private IReadOnlyList<int> matches = [];
    private int length;
    private int current = -1;

    public Brush MatchBrush
    {
        get => (Brush)GetValue(MatchBrushProperty);
        set => SetValue(MatchBrushProperty, value);
    }

    public Brush CurrentBrush
    {
        get => (Brush)GetValue(CurrentBrushProperty);
        set => SetValue(CurrentBrushProperty, value);
    }

    public void Attach(TextBox textBox)
    {
        target = textBox;
        textBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
        textBox.SizeChanged += (_, _) => InvalidateVisual();
        DependencyPropertyDescriptor.FromProperty(TextBox.TextWrappingProperty, typeof(TextBox))
            .AddValueChanged(textBox, (_, _) => RedrawAfterLayout());
    }

    /// <summary>Highlights <paramref name="matchStarts"/> (each <paramref name="matchLength"/> long); <paramref name="currentIndex"/> indexes them.</summary>
    public void Show(IReadOnlyList<int> matchStarts, int matchLength, int currentIndex)
    {
        matches = matchStarts;
        length = matchLength;
        current = currentIndex;
        RedrawAfterLayout();
    }

    public void Clear() => Show([], 0, -1);

    // Character positions are only valid once the TextBox has laid out its (new) text.
    private void RedrawAfterLayout() => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Loaded);

    protected override void OnRender(DrawingContext dc)
    {
        if (target is null || matches.Count == 0 || length == 0) return;
        if (!target.IsArrangeValid)
        {
            RedrawAfterLayout();
            return;
        }
        if (target.Template?.FindName("PART_ContentHost", target) is not ScrollViewer host) return;
        var first = target.GetFirstVisibleLineIndex();
        var last = target.GetLastVisibleLineIndex();
        if (first < 0 || last < first) return;
        var from = target.GetCharacterIndexFromLineIndex(first);
        var to = target.GetCharacterIndexFromLineIndex(last) + target.GetLineLength(last);

        // Clipped to the text viewport, so partly scrolled-out lines do not paint under the border or padding.
        var offset = (Vector)target.TranslatePoint(default, this);
        dc.PushClip(new RectangleGeometry(new Rect(host.TranslatePoint(default, this), new Size(host.ViewportWidth, host.ViewportHeight))));
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i];
            if (start >= to) break; // matches are in order
            if (start + length <= from) continue;
            var brush = i == current ? CurrentBrush : MatchBrush;
            foreach (var box in Boxes(start)) dc.DrawRectangle(brush, null, Rect.Offset(box, offset));
        }
        dc.Pop();
    }

    /// <summary>One box per visual line the match covers (a wrapped match spans more than one).</summary>
    private IEnumerable<Rect> Boxes(int start)
    {
        var end = start + length - 1;
        var lead = target!.GetRectFromCharacterIndex(start);
        var trail = target.GetRectFromCharacterIndex(end, trailingEdge: true);
        if (lead.IsEmpty || trail.IsEmpty) yield break;
        if (lead.Top == trail.Top)
        {
            yield return new Rect(lead.TopLeft, new Point(trail.Left, lead.Bottom));
            yield break;
        }

        Rect? line = null;
        for (var k = start; k <= end; k++)
        {
            var a = target.GetRectFromCharacterIndex(k);
            var b = target.GetRectFromCharacterIndex(k, trailingEdge: true);
            if (a.IsEmpty || b.IsEmpty) continue;
            var box = new Rect(a.TopLeft, new Point(b.Left, a.Bottom));
            if (line is { } l && l.Top == box.Top) line = Rect.Union(l, box);
            else
            {
                if (line is { } done) yield return done;
                line = box;
            }
        }
        if (line is { } rest) yield return rest;
    }
}
