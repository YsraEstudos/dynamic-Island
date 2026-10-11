namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Xorshift32 generator. It is allocation-free and cheap enough to draw several numbers per sample, and the same seed
/// always gives the same sequence, which keeps the generators testable.
/// </summary>
internal sealed class XorShift
{
    private readonly uint _seed;
    private uint _state;

    public XorShift(uint seed)
    {
        // Xorshift sticks at zero, so a zero seed is replaced by a fixed non-zero one.
        _seed = seed == 0 ? 0x9E3779B9u : seed;
        _state = _seed;
    }

    /// <summary>Uniform value in [0, 1). Uses 24 bits so the float result can never round up to 1.</summary>
    public float NextUnit()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return (_state >> 8) * (1f / 16777216f);
    }

    /// <summary>Uniform value in [-1, 1).</summary>
    public float NextBipolar() => NextUnit() * 2f - 1f;

    public void Reset() => _state = _seed;

    /// <summary>Derives an unrelated seed for a sub-generator, so each part of one sound has its own sequence.</summary>
    public static uint Derive(uint seed, uint salt)
    {
        // The golden-ratio spread keeps nearby salts from producing nearby seeds.
        uint x = seed ^ (salt * 0x9E3779B9u);
        x ^= x >> 16;
        x *= 0x85EBCA6Bu;
        x ^= x >> 13;
        return x == 0 ? 1u : x;
    }
}
