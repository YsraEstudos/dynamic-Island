using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.Core.Configuration;
using Island.Core.Pomodoro;

namespace Island.App.ViewModels;

/// <summary>Edits future sessions. Applying a profile prepares the timer, never starts it.</summary>
public sealed class PomodoroSessionViewModel : ObservableObject
{
    private readonly PomodoroTimer _timer;
    private readonly AngryPomodoro _angry;
    private readonly Func<IslandSettings> _settings;
    private readonly Action<IslandSettings> _apply;
    private PomodoroProfile? _selected;
    private string _goal, _name = "", _focus = "25", _rest = "5", _status = "";
    private PomodoroSoundKind _focusSound, _breakSound;
    private bool _automatic, _newProfile;
    private bool _updatingProfiles;

    public PomodoroSessionViewModel(PomodoroTimer timer, AngryPomodoro angry, Func<IslandSettings> settings, Action<IslandSettings> apply)
    {
        _timer = timer; _angry = angry; _settings = settings; _apply = apply;
        var initial = PomodoroProfiles.Normalize(settings());
        _goal = initial.PomodoroGoal;
        Profiles = new ObservableCollection<PomodoroProfile>(initial.PomodoroProfiles);
        _selected = Profiles.First(p => p.Id == initial.PomodoroProfileId);
        LoadEditor(_selected);
        // Show the current custom durations/options when the user changed them outside the profile editor.
        _focus = initial.PomodoroFocusMinutes.ToString(); _rest = initial.PomodoroBreakMinutes.ToString();
        _focusSound = initial.PomodoroFocusSound; _breakSound = initial.PomodoroBreakSound;
        _automatic = initial.PomodoroAutoAdvance;
        NewProfileCommand = new RelayCommand(NewProfile, () => CanConfigure && Profiles.Count < PomodoroProfiles.MaxProfiles);
        SaveProfileCommand = new RelayCommand(SaveProfile, () => CanConfigure);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => CanConfigure && !_newProfile && Profiles.Count > 1);
    }

    public ObservableCollection<PomodoroProfile> Profiles { get; }
    public IReadOnlyList<PomodoroSoundOption> Sounds { get; } =
    [new(PomodoroSoundKind.SoftBell, "Sino suave"), new(PomodoroSoundKind.Piano, "Piano"),
     new(PomodoroSoundKind.Digital, "Digital"), new(PomodoroSoundKind.Coffee, "Café")];
    public IRelayCommand NewProfileCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand DeleteProfileCommand { get; }
    public bool CanConfigure => !_timer.IsRunning && !_timer.PlanActive && !_timer.IsAwaitingNextPhase && !_angry.IsSessionActive && _timer.Remaining == _timer.PhaseDuration;
    public string LockMessage => CanConfigure ? "Escolher um perfil prepara a sessão. Iniciar começa o foco." : "A sessão está em andamento. Os ajustes ficam disponíveis ao concluí-la ou redefini-la.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string EditorTitle => _newProfile ? "Novo perfil" : "Ajustar perfil";
    public string Goal
    {
        get => _goal;
        set
        {
            if (!CanConfigure) return;
            string clean = PomodoroProfiles.CleanText(value, 160);
            if (SetProperty(ref _goal, clean)) _apply(_settings() with { PomodoroGoal = clean });
        }
    }
    public PomodoroProfile? SelectedProfile
    {
        get => _selected;
        set
        {
            if (_updatingProfiles || value is null || !CanConfigure || !Profiles.Contains(value)) return;
            if (!SetProperty(ref _selected, value) && !_newProfile) return;
            _newProfile = false;
            LoadEditor(value);
            Apply(value);
            Status = $"{value.Name} pronto para iniciar.";
            Refresh();
        }
    }
    public string ProfileName { get => _name; set => SetProperty(ref _name, value); }
    public string FocusMinutes { get => _focus; set => SetProperty(ref _focus, value); }
    public string BreakMinutes { get => _rest; set => SetProperty(ref _rest, value); }
    public PomodoroSoundKind FocusSound { get => _focusSound; set => SetProperty(ref _focusSound, value); }
    public PomodoroSoundKind BreakSound { get => _breakSound; set => SetProperty(ref _breakSound, value); }
    public bool AutoAdvance
    {
        get => _automatic;
        set
        {
            if (!CanConfigure) return;
            if (SetProperty(ref _automatic, value))
            {
                OnPropertyChanged(nameof(AdvanceDescription));
            }
        }
    }
    public string AdvanceDescription => AutoAdvance ? "As próximas etapas começam automaticamente." : "Cada etapa aguarda seu clique em Iniciar.";

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanConfigure)); OnPropertyChanged(nameof(LockMessage)); OnPropertyChanged(nameof(EditorTitle));
        NewProfileCommand.NotifyCanExecuteChanged(); SaveProfileCommand.NotifyCanExecuteChanged(); DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    private void LoadEditor(PomodoroProfile p)
    {
        ProfileName = p.Name; FocusMinutes = p.FocusMinutes.ToString(); BreakMinutes = p.BreakMinutes.ToString();
        FocusSound = p.FocusSound; BreakSound = p.BreakSound;
        SetProperty(ref _automatic, p.AutoAdvance, nameof(AutoAdvance));
        OnPropertyChanged(nameof(AdvanceDescription)); OnPropertyChanged(nameof(EditorTitle));
    }

    private void NewProfile()
    {
        if (!CanConfigure || Profiles.Count >= PomodoroProfiles.MaxProfiles) return;
        _newProfile = true;
        ProfileName = "Meu perfil";
        Status = "Ajuste os tempos e sons; depois salve o novo perfil.";
        Refresh();
    }

    private void SaveProfile()
    {
        if (!CanConfigure) return;
        string name = PomodoroProfiles.CleanText(ProfileName, 40);
        if (name.Length == 0 || !int.TryParse(FocusMinutes, out int focus) || !int.TryParse(BreakMinutes, out int rest)
            || focus is < 1 or > 120 || rest is < 1 or > 120 || !Enum.IsDefined(FocusSound) || !Enum.IsDefined(BreakSound))
        { Status = "Dê um nome ao perfil e use tempos entre 1 e 120 minutos."; return; }
        if (_newProfile && Profiles.Count >= PomodoroProfiles.MaxProfiles) return;
        var profile = new PomodoroProfile { Id = _newProfile || _selected is null ? Guid.NewGuid().ToString("N") : _selected.Id,
            Name = name, FocusMinutes = focus, BreakMinutes = rest, FocusSound = FocusSound, BreakSound = BreakSound, AutoAdvance = AutoAdvance };
        int index = Profiles.ToList().FindIndex(p => p.Id == profile.Id);
        // Publish the selection before collection notifications so the ComboBox cannot select and apply a neighbor.
        _selected = profile;
        _updatingProfiles = true;
        try { if (index < 0) Profiles.Add(profile); else Profiles[index] = profile; }
        finally { _updatingProfiles = false; }
        _newProfile = false;
        Apply(profile);
        LoadEditor(profile);
        OnPropertyChanged(nameof(SelectedProfile));
        Status = "Perfil salvo. Sua próxima sessão está pronta.";
        Refresh();
    }

    private void DeleteProfile()
    {
        if (!CanConfigure || _newProfile || Profiles.Count <= 1 || _selected is null) return;
        var removed = _selected;
        _selected = Profiles.First(p => p.Id != removed.Id);
        _updatingProfiles = true;
        try { Profiles.Remove(removed); }
        finally { _updatingProfiles = false; }
        Apply(_selected);
        LoadEditor(_selected);
        OnPropertyChanged(nameof(SelectedProfile));
        Status = "Perfil excluído.";
        Refresh();
    }

    private void Apply(PomodoroProfile profile)
    {
        _apply(PomodoroProfiles.Apply(_settings() with { PomodoroProfiles = Profiles.ToArray() }, profile));
        _timer.RefreshConfigurationIfIdle();
    }
}
