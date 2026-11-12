namespace SysGuard;

/// <summary>Resultado de uma tentativa de senha.</summary>
internal enum AttemptDecision
{
    /// <summary>Pode tentar.</summary>
    Allowed,

    /// <summary>Bloqueado por excesso de tentativas; aguarde o tempo indicado.</summary>
    LockedOut,
}

/// <summary>
/// Limite de tentativas de senha: 5 erros seguidos bloqueiam por 60 s. Fica só na memória do serviço —
/// reiniciar o Windows zera o contador, o que é aceitável para o uso pretendido.
/// </summary>
internal sealed class AttemptLimiter
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(60);

    private readonly Func<DateTimeOffset> _clock;
    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public AttemptLimiter(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public AttemptDecision Check(out TimeSpan remaining)
    {
        remaining = default;
        var now = _clock();
        if (now < _lockedUntil)
        {
            remaining = _lockedUntil - now;
            return AttemptDecision.LockedOut;
        }

        return AttemptDecision.Allowed;
    }

    /// <summary>Conta um erro; devolve quantas tentativas restam antes do bloqueio (0 quando bloqueia).</summary>
    public int RecordFailure()
    {
        var now = _clock();
        if (now < _lockedUntil) return 0;

        _failures++;
        if (_failures < MaxAttempts) return MaxAttempts - _failures;

        _failures = 0;
        _lockedUntil = now + LockoutDuration;
        return 0;
    }

    public void RecordSuccess() => _failures = 0;
}
