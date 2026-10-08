using System.Windows.Media;

namespace Island.App.Animations;

/// <summary>Something that needs a frame callback while it is moving.</summary>
public interface IFrameClient
{
    /// <summary>Advances by <paramref name="dt"/> seconds. Returns true while more frames are needed.</summary>
    bool OnFrame(double dt);
}

/// <summary>
/// The single CompositionTarget.Rendering subscription shared by every animated element.
/// It is attached only while at least one client is active and detached as soon as all of them are at rest,
/// so an idle island costs no frames. Must be used from the UI thread.
/// </summary>
public static class MotionPump
{
    private const double MaxStep = 1.0 / 20.0;

    private static readonly List<IFrameClient> Clients = new();
    private static bool _subscribed;
    private static bool _hasLast;
    private static TimeSpan _last;

    /// <summary>Ensures the client receives frames until its OnFrame returns false. Idempotent.</summary>
    public static void Request(IFrameClient client)
    {
        if (!Clients.Contains(client)) Clients.Add(client);
        if (_subscribed) return;

        _subscribed = true;
        _hasLast = false;
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan now = e is System.Windows.Media.RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        double dt = _hasLast ? (now - _last).TotalSeconds : 1.0 / 60.0;
        _last = now;
        _hasLast = true;
        dt = Math.Clamp(dt, 0.0, MaxStep);

        // Iterate over a snapshot: clients may call Request() while stepping.
        foreach (IFrameClient client in Clients.ToArray())
        {
            if (!client.OnFrame(dt)) Clients.Remove(client);
        }

        if (Clients.Count == 0)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
        }
    }
}
