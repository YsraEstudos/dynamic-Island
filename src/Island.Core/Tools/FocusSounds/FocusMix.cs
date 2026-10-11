namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// The sounds that play together: each one on or off with its own level, summed into one mono signal, soft-clipped,
/// then multiplied by the master fade. <see cref="MixInto"/> runs on the audio thread and reuses its buffer; the lock
/// only guards the settings the UI changes.
/// </summary>
public sealed class FocusMix
{
    /// <summary>Level a sound starts at when it is first switched on.</summary>
    public const float DefaultLevel = 0.6f;

    private const float Knee = 0.8f;
    private static readonly int KindCount = Enum.GetValues<FocusSoundKind>().Length;

    private readonly object _gate = new();
    private readonly ISoundSource[] _sources;
    private readonly bool[] _on;
    private readonly float[] _levels;
    private readonly GainRamp _fade;
    private float[] _scratch = Array.Empty<float>();

    public FocusMix(int sampleRate, uint seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);

        _sources = new ISoundSource[KindCount];
        for (int k = 0; k < KindCount; k++)
        {
            _sources[k] = SoundSourceFactory.Create((FocusSoundKind)k, sampleRate, XorShift.Derive(seed, (uint)k));
        }

        _on = new bool[KindCount];
        _levels = new float[KindCount];
        Array.Fill(_levels, DefaultLevel);
        _fade = new GainRamp(sampleRate);
    }

    public bool AnySoundOn
    {
        get
        {
            lock (_gate) return Array.IndexOf(_on, true) >= 0;
        }
    }

    public bool IsOn(FocusSoundKind kind)
    {
        lock (_gate) return _on[(int)kind];
    }

    public void SetOn(FocusSoundKind kind, bool on)
    {
        lock (_gate) _on[(int)kind] = on;
    }

    public float Level(FocusSoundKind kind)
    {
        lock (_gate) return _levels[(int)kind];
    }

    /// <summary>Sets the sound's own volume, clamped to 0..1.</summary>
    public void SetLevel(FocusSoundKind kind, float level)
    {
        lock (_gate) _levels[(int)kind] = Math.Clamp(level, 0f, 1f);
    }

    /// <summary>Moves the master gain toward <paramref name="target"/> (0 or 1) over <paramref name="seconds"/>.</summary>
    public void FadeTo(float target, double seconds)
    {
        lock (_gate) _fade.SetTarget(target, seconds);
    }

    /// <summary>Writes the next block of mono samples: the sum of the sounds that are on, soft-clipped, times the fade.</summary>
    public void MixInto(Span<float> mono)
    {
        lock (_gate)
        {
            mono.Clear();
            if (_fade.IsSilent) return;

            if (_scratch.Length < mono.Length) _scratch = new float[mono.Length];
            Span<float> scratch = _scratch.AsSpan(0, mono.Length);

            for (int k = 0; k < KindCount; k++)
            {
                if (!_on[k] || _levels[k] <= 0f) continue;

                _sources[k].Fill(scratch);
                float level = _levels[k];
                for (int i = 0; i < mono.Length; i++)
                {
                    mono[i] += scratch[i] * level;
                }
            }

            for (int i = 0; i < mono.Length; i++)
            {
                mono[i] = SoftClip(mono[i]) * _fade.Next();
            }
        }
    }

    /// <summary>Identity below the knee, then a tanh curve that approaches 1 but never reaches it.</summary>
    private static float SoftClip(float sample)
    {
        float magnitude = MathF.Abs(sample);
        if (magnitude <= Knee) return sample;

        float shaped = Knee + (1f - Knee) * MathF.Tanh((magnitude - Knee) / (1f - Knee));
        return MathF.CopySign(shaped, sample);
    }
}
