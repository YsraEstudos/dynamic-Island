using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.Core.Abstractions;
using Island.Core.Configuration;

namespace Island.App.ViewModels;

/// <summary>
/// Backs the Settings window. Every change produces a new immutable <see cref="IslandSettings"/>
/// and is pushed immediately through the apply callback (live preview, no debounce).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const int CompactWidthMin = 80, CompactWidthMax = 240;
    private const int CompactHeightMin = 28, CompactHeightMax = 60;
    private const int TopMarginMin = 0, TopMarginMax = 80;
    private const double VolumeSecondsMin = 0.5, VolumeSecondsMax = 6;
    private const double MediaSecondsMin = 1, MediaSecondsMax = 10;
    private const double IdleSecondsMin = 2, IdleSecondsMax = 30;

    private readonly Action<IslandSettings> _apply;
    private IslandSettings _current;

    private int _selectedMonitorIndex;
    private int _compactWidth;
    private int _compactHeight;
    private int _topMargin;
    private double _volumeDisplaySeconds;
    private double _mediaPreviewSeconds;
    private double _expandedIdleSeconds;
    private bool _hideInFullscreen;
    private bool _reduceAnimations;
    private bool _startWithWindows;
    private bool _showVolume;
    private bool _showMedia;
    private bool _phoneBlockEnabled;
    private string _phoneFcmToken = string.Empty;
    private string _phoneStatus = string.Empty;
    private bool _sendingPhoneTest;

    private readonly Func<TimeSpan, string, Task<PhoneBlockOutcome>>? _sendPhoneBlock;
    private readonly Func<Task<string>>? _checkForUpdates;
    private string _updateStatus = string.Empty;
    private bool _checkingForUpdates;

    public SettingsViewModel(
        IslandSettings initial,
        IReadOnlyList<string> monitorNames,
        Action<IslandSettings> apply,
        Func<TimeSpan, string, Task<PhoneBlockOutcome>>? sendPhoneBlock = null,
        Func<Task<string>>? checkForUpdates = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(monitorNames);
        ArgumentNullException.ThrowIfNull(apply);

        MonitorNames = monitorNames;
        _apply = apply;
        _sendPhoneBlock = sendPhoneBlock;
        _checkForUpdates = checkForUpdates;
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);

        // Construction does not push to apply: the store already holds `initial`.
        _current = Normalize(initial);
        LoadFrom(_current);
    }

    /// <summary>The last settings produced by this view model.</summary>
    public IslandSettings Current => _current;

    /// <summary>Display names for the monitor picker; the selected index maps to <see cref="IslandSettings.MonitorIndex"/>.</summary>
    public IReadOnlyList<string> MonitorNames { get; }

    public IRelayCommand ResetToDefaultsCommand { get; }

    public int SelectedMonitorIndex
    {
        get => _selectedMonitorIndex;
        set
        {
            // Ignore out-of-range values, e.g. the -1 a ComboBox reports while its items are rebuilt.
            if (!IsValidMonitor(value))
            {
                return;
            }

            if (SetProperty(ref _selectedMonitorIndex, value))
            {
                Commit(s => s with { MonitorIndex = value });
            }
        }
    }

    public int CompactWidth
    {
        get => _compactWidth;
        set
        {
            int next = Math.Clamp(value, CompactWidthMin, CompactWidthMax);
            if (SetProperty(ref _compactWidth, next))
            {
                Commit(s => s with { CompactWidth = next });
            }
        }
    }

    public int CompactHeight
    {
        get => _compactHeight;
        set
        {
            int next = Math.Clamp(value, CompactHeightMin, CompactHeightMax);
            if (SetProperty(ref _compactHeight, next))
            {
                Commit(s => s with { CompactHeight = next });
            }
        }
    }

    public int TopMargin
    {
        get => _topMargin;
        set
        {
            int next = Math.Clamp(value, TopMarginMin, TopMarginMax);
            if (SetProperty(ref _topMargin, next))
            {
                Commit(s => s with { TopMargin = next });
            }
        }
    }

    public double VolumeDisplaySeconds
    {
        get => _volumeDisplaySeconds;
        set
        {
            double next = SnapSeconds(value, VolumeSecondsMin, VolumeSecondsMax);
            if (SetProperty(ref _volumeDisplaySeconds, next))
            {
                Commit(s => s with { VolumeDisplaySeconds = next });
            }
        }
    }

    public double MediaPreviewSeconds
    {
        get => _mediaPreviewSeconds;
        set
        {
            double next = SnapSeconds(value, MediaSecondsMin, MediaSecondsMax);
            if (SetProperty(ref _mediaPreviewSeconds, next))
            {
                Commit(s => s with { MediaPreviewSeconds = next });
            }
        }
    }

    public double ExpandedIdleSeconds
    {
        get => _expandedIdleSeconds;
        set
        {
            double next = SnapSeconds(value, IdleSecondsMin, IdleSecondsMax);
            if (SetProperty(ref _expandedIdleSeconds, next))
            {
                Commit(s => s with { ExpandedIdleSeconds = next });
            }
        }
    }

    public bool HideInFullscreen
    {
        get => _hideInFullscreen;
        set
        {
            if (SetProperty(ref _hideInFullscreen, value))
            {
                Commit(s => s with { HideInFullscreen = value });
            }
        }
    }

    public bool ReduceAnimations
    {
        get => _reduceAnimations;
        set
        {
            if (SetProperty(ref _reduceAnimations, value))
            {
                Commit(s => s with { ReduceAnimations = value });
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                Commit(s => s with { StartWithWindows = value });
            }
        }
    }

    public bool ShowVolume
    {
        get => _showVolume;
        set
        {
            if (SetProperty(ref _showVolume, value))
            {
                Commit(s => s with { ShowVolume = value });
            }
        }
    }

    public bool ShowMedia
    {
        get => _showMedia;
        set
        {
            if (SetProperty(ref _showMedia, value))
            {
                Commit(s => s with { ShowMedia = value });
            }
        }
    }

    public bool PhoneBlockEnabled
    {
        get => _phoneBlockEnabled;
        set
        {
            if (SetProperty(ref _phoneBlockEnabled, value))
            {
                Commit(s => s with { PhoneBlockEnabled = value });
            }
        }
    }

    public string PhoneFcmToken
    {
        get => _phoneFcmToken;
        set
        {
            string next = (value ?? string.Empty).Trim();
            if (SetProperty(ref _phoneFcmToken, next))
            {
                Commit(s => s with { PhoneFcmToken = next });
                OnPropertyChanged(nameof(CanSendPhoneTest));
            }
        }
    }

    /// <summary>Result of the last test, shown under the token. Empty until a test is sent.</summary>
    public string PhoneStatus
    {
        get => _phoneStatus;
        private set => SetProperty(ref _phoneStatus, value);
    }

    public bool CanSendPhoneTest => !_sendingPhoneTest && _sendPhoneBlock is not null && _phoneFcmToken.Length > 0;

    /// <summary>Blocks the phone for one minute. The window asks for confirmation before calling this.</summary>
    public async Task SendPhoneTestAsync()
    {
        if (!CanSendPhoneTest) return;

        _sendingPhoneTest = true;
        OnPropertyChanged(nameof(CanSendPhoneTest));
        PhoneStatus = "Enviando…";
        try
        {
            PhoneBlockOutcome outcome = await _sendPhoneBlock!(TimeSpan.FromMinutes(1), _phoneFcmToken);
            PhoneStatus = outcome.Delivered ? $"Teste enviado. {outcome.Detail}" : outcome.Detail;
        }
        finally
        {
            _sendingPhoneTest = false;
            OnPropertyChanged(nameof(CanSendPhoneTest));
        }
    }

    /// <summary>Result of the last update check, shown under the button. Empty until a check is made.</summary>
    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool CanCheckForUpdates => !_checkingForUpdates && _checkForUpdates is not null;

    /// <summary>Asks for the latest release; the status line says whether there is one.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!CanCheckForUpdates) return;

        _checkingForUpdates = true;
        OnPropertyChanged(nameof(CanCheckForUpdates));
        UpdateStatus = "Checking…";
        try
        {
            UpdateStatus = await _checkForUpdates!();
        }
        finally
        {
            _checkingForUpdates = false;
            OnPropertyChanged(nameof(CanCheckForUpdates));
        }
    }

    private void Commit(Func<IslandSettings, IslandSettings> change)
    {
        _current = change(_current);
        _apply(_current);
    }

    private void ResetToDefaults()
    {
        // One apply call for the whole reset, not one per property. The phone link is a credential, not a preference: keep it.
        _current = Normalize(new IslandSettings
        {
            PhoneBlockEnabled = _current.PhoneBlockEnabled,
            PhoneFcmToken = _current.PhoneFcmToken,
        });
        LoadFrom(_current);
        _apply(_current);
    }

    /// <summary>Pushes values into the backing fields and notifies the UI without calling apply.</summary>
    private void LoadFrom(IslandSettings s)
    {
        SetProperty(ref _selectedMonitorIndex, s.MonitorIndex, nameof(SelectedMonitorIndex));
        SetProperty(ref _compactWidth, s.CompactWidth, nameof(CompactWidth));
        SetProperty(ref _compactHeight, s.CompactHeight, nameof(CompactHeight));
        SetProperty(ref _topMargin, s.TopMargin, nameof(TopMargin));
        SetProperty(ref _volumeDisplaySeconds, s.VolumeDisplaySeconds, nameof(VolumeDisplaySeconds));
        SetProperty(ref _mediaPreviewSeconds, s.MediaPreviewSeconds, nameof(MediaPreviewSeconds));
        SetProperty(ref _expandedIdleSeconds, s.ExpandedIdleSeconds, nameof(ExpandedIdleSeconds));
        SetProperty(ref _hideInFullscreen, s.HideInFullscreen, nameof(HideInFullscreen));
        SetProperty(ref _reduceAnimations, s.ReduceAnimations, nameof(ReduceAnimations));
        SetProperty(ref _startWithWindows, s.StartWithWindows, nameof(StartWithWindows));
        SetProperty(ref _showVolume, s.ShowVolume, nameof(ShowVolume));
        SetProperty(ref _showMedia, s.ShowMedia, nameof(ShowMedia));
        SetProperty(ref _phoneBlockEnabled, s.PhoneBlockEnabled, nameof(PhoneBlockEnabled));
        SetProperty(ref _phoneFcmToken, s.PhoneFcmToken ?? string.Empty, nameof(PhoneFcmToken));
        OnPropertyChanged(nameof(CanSendPhoneTest));
    }

    /// <summary>Clamps every numeric field to its allowed range. Unknown monitors fall back to the primary (0), as the overlay does.</summary>
    private IslandSettings Normalize(IslandSettings s) => s with
    {
        MonitorIndex = IsValidMonitor(s.MonitorIndex) ? s.MonitorIndex : 0,
        CompactWidth = Math.Clamp(s.CompactWidth, CompactWidthMin, CompactWidthMax),
        CompactHeight = Math.Clamp(s.CompactHeight, CompactHeightMin, CompactHeightMax),
        TopMargin = Math.Clamp(s.TopMargin, TopMarginMin, TopMarginMax),
        VolumeDisplaySeconds = SnapSeconds(s.VolumeDisplaySeconds, VolumeSecondsMin, VolumeSecondsMax),
        MediaPreviewSeconds = SnapSeconds(s.MediaPreviewSeconds, MediaSecondsMin, MediaSecondsMax),
        ExpandedIdleSeconds = SnapSeconds(s.ExpandedIdleSeconds, IdleSecondsMin, IdleSecondsMax),
    };

    private bool IsValidMonitor(int index) => index >= 0 && index < Math.Max(1, MonitorNames.Count);

    private static double SnapSeconds(double value, double min, double max) =>
        Math.Round(Math.Clamp(value, min, max), 1, MidpointRounding.AwayFromZero);
}
