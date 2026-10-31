using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Music;

/// <summary>
/// Scrolls clipped titles automatically in one direction. Each label is laid
/// out twice side by side; the view travels until the first copy has fully
/// left and the second copy sits exactly where the first started, then jumps
/// back by one text length. Because both copies are identical that jump is
/// invisible, so the text loops seamlessly and never snaps.
/// </summary>
public sealed partial class MainWindow
{
    private const double MarqueeStepPixels = 0.9;
    private const int MarqueeTickMilliseconds = 30;
    private const int MarqueeGapPixels = 40;

    private DispatcherQueueTimer? _marqueeTimer;

    private void Marquee_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            EnsureMarqueeRunning();
            SyncMarqueeClone(viewer);
        }
    }

    private void EnsureMarqueeRunning()
    {
        if (_marqueeTimer is not null)
        {
            _marqueeTimer.Start();
            return;
        }

        _marqueeTimer = DispatcherQueue.CreateTimer();
        _marqueeTimer.Interval = TimeSpan.FromMilliseconds(MarqueeTickMilliseconds);
        _marqueeTimer.IsRepeating = true;
        _marqueeTimer.Tick += (_, _) => MarqueeTick();
        _marqueeTimer.Start();
    }

    private void MarqueeTick()
    {
        var moved = false;

        foreach (var viewer in FindMarquees(RootGrid))
        {
            var content = viewer.Content as StackPanel;
            if (content is null || content.Children.Count < 2 ||
                content.Children[0] is not TextBlock source ||
                content.Children[1] is not TextBlock clone)
            {
                continue;
            }

            // Keep the duplicate in step with the label, which the page code
            // rewrites whenever the current track changes.
            if (source.Text != clone.Text)
            {
                clone.Text = source.Text;
            }

            // The text only travels once the window is narrower than a single copy.
            var cycle = source.ActualWidth + MarqueeGapPixels;
            if (cycle <= 1 || viewer.ViewportWidth >= cycle)
            {
                if (viewer.HorizontalOffset != 0)
                {
                    viewer.ChangeView(0, null, null, disableAnimation: true);
                }

                continue;
            }

            var next = viewer.HorizontalOffset + MarqueeStepPixels;
            if (next >= cycle)
            {
                // The second copy is now exactly where the first one began, so
                // wrapping by one cycle keeps the picture identical.
                next -= cycle;
            }

            viewer.ChangeView(next, null, null, disableAnimation: true);
            moved = true;
        }

        if (!moved)
        {
            _marqueeTimer?.Stop();
        }
    }

    /// <summary>Wraps a marquee's label in a two-copy row, once per label.</summary>
    private static void SyncMarqueeClone(ScrollViewer viewer)
    {
        // Keep the text anchored to the left edge; the default centred content
        // alignment made short titles look shifted once scrolling stopped.
        viewer.HorizontalContentAlignment = HorizontalAlignment.Left;
        viewer.VerticalContentAlignment = VerticalAlignment.Center;

        if (viewer.Content is not TextBlock text)
        {
            return;
        }

        text.HorizontalAlignment = HorizontalAlignment.Left;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var clone = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontFamily = text.FontFamily,
            Foreground = text.Foreground,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        viewer.Content = null;
        text.Margin = new Thickness(0, 0, MarqueeGapPixels, 0);
        row.Children.Add(text);
        row.Children.Add(clone);
        viewer.Content = row;
    }

    private static List<ScrollViewer> FindMarquees(DependencyObject root)
    {
        var result = new List<ScrollViewer>();
        Collect(root, result);
        return result;

        static void Collect(DependencyObject element, List<ScrollViewer> found)
        {
            var count = VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(element, index);

                if (child is FrameworkElement { Visibility: Visibility.Collapsed })
                {
                    continue;
                }

                if (child is ScrollViewer { Tag: "Marquee" } viewer)
                {
                    if (viewer.ActualWidth > 0)
                    {
                        found.Add(viewer);
                    }

                    continue;
                }

                Collect(child, found);
            }
        }
    }
}
