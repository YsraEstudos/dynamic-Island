using Island.Core.Configuration;

namespace Island.Core.Pomodoro;

public sealed record PomodoroProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Meu perfil";
    public int FocusMinutes { get; init; } = 25;
    public int BreakMinutes { get; init; } = 5;
    public PomodoroSoundKind FocusSound { get; init; } = PomodoroSoundKind.SoftBell;
    public PomodoroSoundKind BreakSound { get; init; } = PomodoroSoundKind.Coffee;
    public bool AutoAdvance { get; init; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => $"{Name} · {FocusMinutes}/{BreakMinutes}";
}

public static class PomodoroProfiles
{
    public const int MaxProfiles = 24;
    public static IReadOnlyList<PomodoroProfile> Defaults { get; } = Array.AsReadOnly(new[]
    {
        new PomodoroProfile { Id = "study", Name = "Estudo" },
        new PomodoroProfile { Id = "reading", Name = "Leitura", FocusMinutes = 50, BreakMinutes = 10, FocusSound = PomodoroSoundKind.Piano },
        new PomodoroProfile { Id = "work", Name = "Trabalho", FocusMinutes = 45, BreakMinutes = 15, FocusSound = PomodoroSoundKind.Digital },
    });

    public static string CleanText(string? text, int maxLength)
    {
        string clean = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    public static IslandSettings Normalize(IslandSettings settings)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new List<PomodoroProfile>();
        foreach (var p in (settings.PomodoroProfiles ?? Defaults).Take(MaxProfiles))
        {
            if (p is null) continue;
            string id = CleanText(p.Id, 64);
            if (id.Length == 0 || !ids.Add(id)) { id = Guid.NewGuid().ToString("N"); ids.Add(id); }
            string name = CleanText(p.Name, 40);
            profiles.Add(p with { Id = id, Name = name.Length == 0 ? "Meu perfil" : name,
                FocusMinutes = Math.Clamp(p.FocusMinutes, 1, 120), BreakMinutes = Math.Clamp(p.BreakMinutes, 1, 120),
                FocusSound = PomodoroSoundSelection.Normalize(p.FocusSound, PomodoroSoundKind.SoftBell),
                BreakSound = PomodoroSoundSelection.Normalize(p.BreakSound, PomodoroSoundKind.Coffee) });
        }
        if (profiles.Count == 0) profiles.AddRange(Defaults);
        return settings with { PomodoroGoal = CleanText(settings.PomodoroGoal, 160), PomodoroProfiles = profiles.ToArray(),
            PomodoroProfileId = profiles.Any(p => p.Id == settings.PomodoroProfileId) ? settings.PomodoroProfileId : profiles[0].Id };
    }

    public static IslandSettings Apply(IslandSettings settings, PomodoroProfile profile) => settings with
    {
        PomodoroProfileId = profile.Id, PomodoroFocusMinutes = profile.FocusMinutes, PomodoroBreakMinutes = profile.BreakMinutes,
        PomodoroFocusSound = profile.FocusSound, PomodoroBreakSound = profile.BreakSound, PomodoroAutoAdvance = profile.AutoAdvance,
    };
}
