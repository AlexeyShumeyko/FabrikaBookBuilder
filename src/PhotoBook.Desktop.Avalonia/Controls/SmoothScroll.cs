using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PhotoBook.Shell.Controls;

/// <summary>
/// A wheel notch carries the page to where it points instead of teleporting there.
/// </summary>
/// <remarks>
/// <para>
/// The platform takes a wheel notch, moves the offset by a fixed step and draws the result on the
/// next frame, so a page taller than the window travels in visible jumps. This takes the wheel
/// over, keeps the offset it is heading for, and walks there over a few frames - the same fifty
/// pixels per notch, spread over about a fifth of a second, so consecutive notches add up instead
/// of restarting.
/// </para>
/// <para>
/// Nothing else is taken away: dragging the scrollbar, the keyboard and any programmatic scroll
/// still land where they are asked to. A glide notices that the offset moved on its own and hands
/// the page over rather than pulling it back.
/// </para>
/// <para>
/// Only where it is asked for, by name, on the scroll viewer. A scroller inside a dialog or a card
/// keeps the platform's behaviour: a change nobody asked for is still a change.
/// </para>
/// </remarks>
public sealed class SmoothScroll : AvaloniaObject
{
    /// <summary>One notch, the distance the platform itself uses.</summary>
    private const double PixelsPerNotch = 50;

    /// <summary>How much of the remaining distance a single frame covers.</summary>
    private const double Follow = 0.28;

    /// <summary>Close enough to the target to land on it, in pixels.</summary>
    private const double Settle = 0.5;

    /// <summary>An offset that moved this far by itself was moved by something else.</summary>
    private const double MovedByOther = 0.6;

    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();

    private SmoothScroll()
    {
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, ScrollViewer, bool>("IsEnabled");

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) =>
        viewer.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.GetNewValue<bool>())
        {
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        }
        else
        {
            viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel);

            if (Glides.TryGetValue(viewer, out var glide))
            {
                glide.Stop();
            }
        }
    }

    private static void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || Math.Abs(e.Delta.Y) < 0.001)
        {
            return;
        }

        var reach = viewer.Extent.Height - viewer.Viewport.Height;
        if (reach <= Settle)
        {
            return;
        }

        var glide = Glides.GetValue(viewer, target => new Glide(target));
        var from = glide.IsGliding ? glide.Target : viewer.Offset.Y;

        glide.Target = Math.Clamp(from - (e.Delta.Y * PixelsPerNotch), 0, reach);
        glide.Start();
        e.Handled = true;
    }

    /// <summary>
    /// The glide of one scroll viewer: where it is going, and the frame clock that takes it there.
    /// </summary>
    private sealed class Glide
    {
        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _timer;
        private double _applied;

        public Glide(ScrollViewer viewer)
        {
            _viewer = viewer;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        }

        public double Target { get; set; }

        public bool IsGliding => _timer.IsEnabled;

        public void Start()
        {
            if (_timer.IsEnabled)
            {
                return;
            }

            _applied = _viewer.Offset.Y;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            // The page can shrink under a glide - a filter emptying the list, a window resize - and
            // a target past the end would keep the clock running forever against a clamped offset.
            var reach = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
            Target = Math.Clamp(Target, 0, reach);

            var offset = _viewer.Offset;
            if (Math.Abs(offset.Y - _applied) > MovedByOther)
            {
                Target = offset.Y;
            }

            var distance = Target - offset.Y;
            var next = Math.Abs(distance) <= Settle ? Target : offset.Y + (distance * Follow);

            _viewer.Offset = new Vector(offset.X, next);
            _applied = next;

            if (Math.Abs(Target - next) <= Settle)
            {
                _timer.Stop();
            }
        }
    }
}
