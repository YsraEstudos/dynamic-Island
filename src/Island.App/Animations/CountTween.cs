namespace Island.App.Animations;

/// <summary>
/// Moves a number from one value to another with ease-out on the shared frame pump. It holds no frame callback
/// once it has landed, so a settled number costs nothing.
/// </summary>
public sealed class CountTween : IFrameClient
{
    private const double DurationSeconds = 0.35;

    private readonly Action<double> _apply;
    private double _from;
    private double _to;
    private double _elapsed;
    private bool _running;

    public CountTween(Action<double> apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <summary>Starts the move. Equal values apply at once, with no frames.</summary>
    public void Start(double from, double to)
    {
        if (from == to)
        {
            Stop();
            _apply(to);
            return;
        }

        _from = from;
        _to = to;
        _elapsed = 0;
        _running = true;
        MotionPump.Request(this);
    }

    /// <summary>Drops a move in progress; the value the caller last applied stays.</summary>
    public void Stop() => _running = false;

    public bool OnFrame(double dt)
    {
        if (!_running) return false;

        _elapsed += dt;
        double t = Math.Min(1.0, _elapsed / DurationSeconds);
        if (t >= 1.0)
        {
            _running = false;
            _apply(_to);
            return false;
        }

        double eased = 1.0 - Math.Pow(1.0 - t, 3);
        _apply(_from + (_to - _from) * eased);
        return true;
    }
}
