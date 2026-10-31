using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Music;

/// <summary>
/// Lets long titles scroll instead of being cut off. A ScrollViewer marked
/// with these handlers scrolls smoothly while the pointer is over it, and
/// only when its text is actually clipped.
/// </summary>
public sealed partial class MainWindow
{
    private const double MarqueeStepPixels = 1.5;
    private DispatcherQueueTimer? _marqueeTimer;
    private ScrollViewer? _activeMarquee;
    private double _marqueeDirection = 1;

    private void Marquee_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableWidth <= 0.5)
        {
            return;
        }

        _activeMarquee = viewer;
        _marqueeDirection = 1;
        EnsureMarqueeTimer().Start();
    }

    private void Marquee_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(_activeMarquee, sender))
        {
            StopMarquee();
        }
    }

    private void Marquee_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(_activeMarquee, sender) &&
            sender is ScrollViewer viewer &&
            viewer.ScrollableWidth <= 0.5)
        {
            StopMarquee();
        }
    }

    private DispatcherQueueTimer EnsureMarqueeTimer()
    {
        if (_marqueeTimer is not null)
        {
            return _marqueeTimer;
        }

        _marqueeTimer = DispatcherQueue.CreateTimer();
        _marqueeTimer.Interval = TimeSpan.FromMilliseconds(30);
        _marqueeTimer.IsRepeating = true;
        _marqueeTimer.Tick += (_, _) => MarqueeTick();
        return _marqueeTimer;
    }

    private void MarqueeTick()
    {
        var viewer = _activeMarquee;
        if (viewer is null)
        {
            StopMarquee();
            return;
        }

        var limit = viewer.ScrollableWidth;
        if (limit <= 0.5)
        {
            StopMarquee();
            return;
        }

        var next = viewer.HorizontalOffset + _marqueeDirection * MarqueeStepPixels;
        if (next >= limit)
        {
            next = limit;
            _marqueeDirection = -1;
        }
        else if (next <= 0)
        {
            next = 0;
            _marqueeDirection = 1;
        }

        viewer.ChangeView(next, null, null, disableAnimation: true);
    }

    private void StopMarquee()
    {
        _marqueeTimer?.Stop();

        if (_activeMarquee is not null)
        {
            _activeMarquee.ChangeView(0, null, null, disableAnimation: true);
        }

        _activeMarquee = null;
    }
}
