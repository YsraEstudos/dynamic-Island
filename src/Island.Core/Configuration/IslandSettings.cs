namespace Island.Core.Configuration;

/// <summary>Screen edge the island is docked to. A docked island is vertical and sits flush against that edge.</summary>
public enum DockEdge
{
    None,
    Left,
    Right,
}

public sealed record IslandSettings
{
    /// <summary>Index into the monitor list; 0 = primary.</summary>
    public int MonitorIndex { get; init; } = 0;
    public int CompactWidth { get; init; } = 124;
    public int CompactHeight { get; init; } = 36;
    /// <summary>Distance from the top edge of the monitor, in DIPs.</summary>
    public int TopMargin { get; init; } = 8;
    public double VolumeDisplaySeconds { get; init; } = 1.8;
    public double MediaPreviewSeconds { get; init; } = 3.5;
    public double ExpandedIdleSeconds { get; init; } = 6.0;
    public bool HideInFullscreen { get; init; } = true;

    /// <summary>When true the island sits at <see cref="PositionX"/>/<see cref="PositionY"/> (set by hold-and-drag) instead of top-centre.</summary>
    public bool UseCustomPosition { get; init; } = false;
    /// <summary>Edge the island is docked to (vertical layout). With a dock, <see cref="PositionY"/> is the vertical centre of the island and <see cref="PositionX"/> only picks the monitor.</summary>
    public DockEdge Dock { get; init; } = DockEdge.None;
    /// <summary>The island was left as the Mini pill; it starts as the Mini pill again. Kept in sync by the app from the island state.</summary>
    public bool Minimized { get; init; } = false;
    /// <summary>Horizontal centre of the island in physical pixels of the virtual screen. Only used with <see cref="UseCustomPosition"/>.</summary>
    public int PositionX { get; init; } = 0;
    /// <summary>Top edge of the island in physical pixels of the virtual screen. Only used with <see cref="UseCustomPosition"/>.</summary>
    public int PositionY { get; init; } = 0;
    /// <summary>Process names (no ".exe", case-insensitive) of games that hide the island while they are the foreground app, even when windowed.</summary>
    public IReadOnlyList<string> GameProcesses { get; init; } = Array.Empty<string>();

    public bool ReduceAnimations { get; init; } = false;
    public bool StartWithWindows { get; init; } = false;
    public bool ShowVolume { get; init; } = true;
    public bool ShowMedia { get; init; } = true;

    /// <summary>Ids of the widgets shown in the shelf, row by row. Known ids: nowplaying, pomodoro, calendar, filetray.</summary>
    public IReadOnlyList<string> ShelfWidgets { get; init; } = new[] { "nowplaying", "pomodoro" };
    /// <summary>Row index of each entry in <see cref="ShelfWidgets"/>. Missing entries are row 0 (settings from before rows).</summary>
    public IReadOnlyList<int> ShelfRows { get; init; } = Array.Empty<int>();
    public int PomodoroFocusMinutes { get; init; } = 25;
    public int PomodoroBreakMinutes { get; init; } = 5;
    /// <summary>Number of pomodoros (focus + break) run back to back when Play starts a fresh focus.</summary>
    public int PomodoroCycles { get; init; } = 1;
    public bool PomodoroSound { get; init; } = true;
    public bool ClipboardEnabled { get; init; } = true;
    public int ClipboardMaxItems { get; init; } = 50;
    public double NoticeSeconds { get; init; } = 3.0;

    /// <summary>When true, starting an Angry pomodoro also blocks the phone for the remaining time.</summary>
    public bool PhoneBlockEnabled { get; init; } = false;
    /// <summary>FCM registration token of the phone (copied from the Foco &amp; Bem-Estar app).</summary>
    public string PhoneFcmToken { get; init; } = string.Empty;

    /// <summary>GitHub repository ("owner/name") whose latest release is checked for a newer .zip. Older files without it get the default.</summary>
    public string UpdateRepository { get; init; } = "YsraEstudos/dynamic-Island";

    /// <summary>The shelf as rows (see <see cref="ShelfLayout.ToRows"/>).</summary>
    public IReadOnlyList<IReadOnlyList<string>> GetShelfRows() => ShelfLayout.ToRows(ShelfWidgets, ShelfRows);

    // Records compare collections by reference; settings must compare by value (ShelfWidgets, ShelfRows).
    public bool Equals(IslandSettings? other) => other is not null && MonitorIndex == other.MonitorIndex && CompactWidth == other.CompactWidth && CompactHeight == other.CompactHeight && TopMargin == other.TopMargin && VolumeDisplaySeconds == other.VolumeDisplaySeconds && MediaPreviewSeconds == other.MediaPreviewSeconds && ExpandedIdleSeconds == other.ExpandedIdleSeconds && HideInFullscreen == other.HideInFullscreen && UseCustomPosition == other.UseCustomPosition && Dock == other.Dock && Minimized == other.Minimized && PositionX == other.PositionX && PositionY == other.PositionY && GameProcesses.SequenceEqual(other.GameProcesses) && ReduceAnimations == other.ReduceAnimations && StartWithWindows == other.StartWithWindows && ShowVolume == other.ShowVolume && ShowMedia == other.ShowMedia && ShelfWidgets.SequenceEqual(other.ShelfWidgets) && ShelfRows.SequenceEqual(other.ShelfRows) && PomodoroFocusMinutes == other.PomodoroFocusMinutes && PomodoroBreakMinutes == other.PomodoroBreakMinutes && PomodoroCycles == other.PomodoroCycles && PomodoroSound == other.PomodoroSound && ClipboardEnabled == other.ClipboardEnabled && ClipboardMaxItems == other.ClipboardMaxItems && NoticeSeconds == other.NoticeSeconds && PhoneBlockEnabled == other.PhoneBlockEnabled && PhoneFcmToken == other.PhoneFcmToken && UpdateRepository == other.UpdateRepository;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(MonitorIndex);
        h.Add(CompactWidth);
        h.Add(CompactHeight);
        h.Add(TopMargin);
        h.Add(VolumeDisplaySeconds);
        h.Add(MediaPreviewSeconds);
        h.Add(ExpandedIdleSeconds);
        h.Add(HideInFullscreen);
        h.Add(UseCustomPosition);
        h.Add(Dock);
        h.Add(Minimized);
        h.Add(PositionX);
        h.Add(PositionY);
        foreach (var g in GameProcesses) h.Add(g);
        h.Add(ReduceAnimations);
        h.Add(StartWithWindows);
        h.Add(ShowVolume);
        h.Add(ShowMedia);
        foreach (var w in ShelfWidgets) h.Add(w);
        foreach (var r in ShelfRows) h.Add(r);
        h.Add(PomodoroFocusMinutes);
        h.Add(PomodoroBreakMinutes);
        h.Add(PomodoroCycles);
        h.Add(PomodoroSound);
        h.Add(ClipboardEnabled);
        h.Add(ClipboardMaxItems);
        h.Add(NoticeSeconds);
        h.Add(PhoneBlockEnabled);
        h.Add(PhoneFcmToken);
        h.Add(UpdateRepository);
        return h.ToHashCode();
    }
}
