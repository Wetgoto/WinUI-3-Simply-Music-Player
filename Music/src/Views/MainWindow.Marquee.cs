using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Music;

/// <summary>
/// Scrolls clipped titles automatically: each one travels to the end, pauses,
/// jumps back to the start and repeats. It runs on its own timer and does not
/// depend on the pointer being over the text.
/// </summary>
public sealed partial class MainWindow
{
    private const double MarqueeStepPixels = 0.9;
    private const int MarqueeTickMilliseconds = 30;
    private const int MarqueePauseMilliseconds = 1500;

    private DispatcherQueueTimer? _marqueeTimer;
    private int _marqueePauseTicks;

    private void Marquee_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        EnsureMarqueeTimer().Start();
    }

    private DispatcherQueueTimer EnsureMarqueeTimer()
    {
        if (_marqueeTimer is not null)
        {
            return _marqueeTimer;
        }

        _marqueeTimer = DispatcherQueue.CreateTimer();
        _marqueeTimer.Interval = TimeSpan.FromMilliseconds(MarqueeTickMilliseconds);
        _marqueeTimer.IsRepeating = true;
        _marqueeTimer.Tick += (_, _) => MarqueeTick();
        return _marqueeTimer;
    }

    /// <summary>
    /// One step for every marquee on screen. Only the rows that currently have
    /// a clipped label move, so hidden pages cost nothing.
    /// </summary>
    private void MarqueeTick()
    {
        if (_marqueePauseTicks > 0)
        {
            _marqueePauseTicks--;
            return;
        }

        var moved = false;
        var reachedEnd = false;

        foreach (var viewer in FindMarquees(RootGrid))
        {
            var limit = viewer.ScrollableWidth;
            if (limit <= 0.5)
            {
                // Nothing is clipped: keep it parked at the start.
                if (viewer.HorizontalOffset != 0)
                {
                    viewer.ChangeView(0, null, null, disableAnimation: true);
                }

                continue;
            }

            var next = viewer.HorizontalOffset + MarqueeStepPixels;
            if (next >= limit)
            {
                next = limit;
                reachedEnd = true;
            }

            viewer.ChangeView(next, null, null, disableAnimation: true);
            moved = true;
        }

        if (!moved)
        {
            // Nothing needs to scroll right now; stop polling until layout asks again.
            _marqueeTimer?.Stop();
            return;
        }

        if (reachedEnd)
        {
            // Hold the end of the text for a moment, then start over from the
            // left in the same direction.
            _marqueePauseTicks = MarqueePauseMilliseconds / MarqueeTickMilliseconds;
            foreach (var viewer in FindMarquees(RootGrid))
            {
                if (viewer.ScrollableWidth > 0.5)
                {
                    viewer.ChangeView(0, null, null, disableAnimation: true);
                }
            }
        }
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
                if (child is ScrollViewer { Tag: "Marquee" } viewer)
                {
                    // Only walk visible pages.
                    if (viewer.ActualWidth > 0 && viewer.Visibility == Visibility.Visible)
                    {
                        found.Add(viewer);
                    }

                    continue;
                }

                if (child is FrameworkElement { Visibility: Visibility.Collapsed })
                {
                    continue;
                }

                Collect(child, found);
            }
        }
    }
}
