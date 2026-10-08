namespace Island.App.Animations;

/// <summary>
/// A scalar driven by a closed-form damped spring. The displacement from the target is
///   underdamped (zeta &lt; 1):  d(t) = e^(-zeta*w*t) * (A cos(wd*t) + B sin(wd*t)),  wd = w*sqrt(1 - zeta^2)
///   critical   (zeta &gt;= 1): d(t) = e^(-w*t) * (A + B*t)
/// Whenever the target or stiffness changes, the coefficients A and B are re-solved from the current
/// position and velocity, so retargeting mid-flight is continuous in both position and velocity.
/// Zeta below 1 gives a small lead overshoot; zeta of 1 (or more) never overshoots.
/// </summary>
public sealed class SpringValue
{
    /// <summary>Within this distance and below <see cref="SettleVelocity"/> the spring counts as at rest.</summary>
    public const double SettleDistance = 0.05;
    public const double SettleVelocity = 0.5;

    private const double CriticalThreshold = 0.999;
    private const double MinOmega = 1e-3;

    private double _omega;
    private double _zeta;
    private double _target;
    private double _current;
    private double _velocity;

    // Coefficients of the displacement since the last re-base, and the time elapsed since it.
    private double _a;
    private double _b;
    private double _t;

    public SpringValue(double omega, double zeta, double initial = 0.0)
    {
        _omega = Math.Max(omega, MinOmega);
        _zeta = Math.Max(zeta, 0.0);
        _target = initial;
        _current = initial;
    }

    public double Current => _current;
    public double Target => _target;
    public double Velocity => _velocity;
    public double Omega => _omega;
    public double Zeta => _zeta;

    public bool IsSettled =>
        Math.Abs(_current - _target) < SettleDistance && Math.Abs(_velocity) < SettleVelocity;

    /// <summary>
    /// Natural frequency whose envelope falls to about 1% after <paramref name="seconds"/>
    /// (perceptual settle time). Uses the underdamped envelope for zeta &lt; 1 and the critical one otherwise.
    /// </summary>
    public static double OmegaForSettleTime(double seconds, double zeta)
    {
        double envelopeConstant = zeta < CriticalThreshold ? 4.6 : 6.6;
        return envelopeConstant / (Math.Max(zeta, 0.05) * Math.Max(seconds, 1e-3));
    }

    /// <summary>Sets a new target, keeping the current position and velocity.</summary>
    public void SetTarget(double target)
    {
        if (target == _target) return;
        _target = target;
        Rebase();
    }

    /// <summary>Changes stiffness without a position or velocity jump.</summary>
    public void Retune(double omega, double zeta)
    {
        _omega = Math.Max(omega, MinOmega);
        _zeta = Math.Max(zeta, 0.0);
        Rebase();
    }

    /// <summary>Jumps to <paramref name="value"/> at rest (no motion).</summary>
    public void Snap(double value)
    {
        _target = value;
        _current = value;
        _velocity = 0.0;
        _a = 0.0;
        _b = 0.0;
        _t = 0.0;
    }

    /// <summary>
    /// Advances the spring by <paramref name="dt"/> seconds.
    /// Returns true once the spring has settled (the value is then snapped exactly onto the target).
    /// </summary>
    public bool Advance(double dt)
    {
        if (dt > 0.0) _t += dt;
        Evaluate(_t, out double displacement, out double velocity);
        _current = _target + displacement;
        _velocity = velocity;

        if (IsSettled)
        {
            Snap(_target);
            return true;
        }
        return false;
    }

    private void Rebase()
    {
        double x0 = _current - _target;
        double v0 = _velocity;
        _t = 0.0;

        if (_zeta < CriticalThreshold)
        {
            double wd = _omega * Math.Sqrt(1.0 - _zeta * _zeta);
            _a = x0;
            _b = (v0 + _zeta * _omega * x0) / wd;
        }
        else
        {
            _a = x0;
            _b = v0 + _omega * x0;
        }
    }

    private void Evaluate(double t, out double displacement, out double velocity)
    {
        if (_zeta < CriticalThreshold)
        {
            double wd = _omega * Math.Sqrt(1.0 - _zeta * _zeta);
            double e = Math.Exp(-_zeta * _omega * t);
            double c = Math.Cos(wd * t);
            double s = Math.Sin(wd * t);
            double osc = _a * c + _b * s;
            displacement = e * osc;
            velocity = e * (-_zeta * _omega * osc + wd * (-_a * s + _b * c));
        }
        else
        {
            double e = Math.Exp(-_omega * t);
            double p = _a + _b * t;
            displacement = e * p;
            velocity = e * (_b - _omega * p);
        }
    }
}
