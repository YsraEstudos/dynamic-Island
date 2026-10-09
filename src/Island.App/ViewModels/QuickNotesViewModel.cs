using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.Core.Notes;

namespace Island.App.ViewModels;

public enum QuickNotesSaveState
{
    Pending,
    Saving,
    Saved,
    Error
}

public sealed class QuickNoteChecklistDraftItem : ObservableObject
{
    private string _text;
    private bool _isCompleted;

    internal QuickNoteChecklistDraftItem(
        Guid id,
        string text,
        bool isCompleted,
        int order)
    {
        Id = id;
        _text = text;
        _isCompleted = isCompleted;
        Order = order;
    }

    public Guid Id { get; }
    public int Order { get; }

    public string Text
    {
        get => _text;
        set
        {
            SetProperty(ref _text, value ?? string.Empty);
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            SetProperty(ref _isCompleted, value);
        }
    }
}

/// <summary>Editor state for the notes window. Drafts stay local until their debounced write succeeds.</summary>
public sealed class QuickNotesViewModel : ObservableObject
{
    private static readonly TimeSpan DefaultAutosaveDelay = TimeSpan.FromMilliseconds(450);

    private readonly QuickNotesService _service;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _autosaveDelay;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly ObservableCollection<QuickNoteChecklistDraftItem> _checklistDraft = [];
    private CancellationTokenSource? _debounceCancellation;
    private Task? _pendingSaveTask;
    private PendingSave? _pendingSave;
    private long _editRevision;
    private bool _isLoadingDraft;
    private IReadOnlyList<QuickNote> _notes = Array.Empty<QuickNote>();
    private Guid? _selectedNoteId;
    private QuickNoteCollection _selectedCollection = QuickNoteCollection.Active;
    private string _searchText = string.Empty;
    private string _titleDraft = string.Empty;
    private string _contentDraft = string.Empty;
    private string _tagsDraft = string.Empty;
    private QuickNoteColor _colorDraft = QuickNoteColor.Default;
    private QuickNotesSaveState _saveState = QuickNotesSaveState.Saved;
    private string _saveErrorMessage = string.Empty;

    public QuickNotesViewModel(
        QuickNotesService service,
        TimeProvider? timeProvider = null,
        TimeSpan? autosaveDelay = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _autosaveDelay = autosaveDelay ?? DefaultAutosaveDelay;
        if (_autosaveDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(autosaveDelay));

        _checklistDraft.CollectionChanged += OnChecklistCollectionChanged;
        BeginNewCommand = new AsyncRelayCommand(BeginNewAsync);
        SelectNoteCommand = new AsyncRelayCommand<Guid>(SelectNoteAsync);
        SelectCollectionCommand = new AsyncRelayCommand<QuickNoteCollection>(SelectCollectionAsync);
        TogglePinCommand = new AsyncRelayCommand(TogglePinAsync);
        ArchiveSelectedCommand = new AsyncRelayCommand(ArchiveSelectedAsync);
        TrashSelectedCommand = new AsyncRelayCommand(TrashSelectedAsync);
        RestoreSelectedCommand = new AsyncRelayCommand(RestoreSelectedAsync);
        DeleteSelectedPermanentlyCommand = new AsyncRelayCommand(DeleteSelectedPermanentlyAsync);
        AddChecklistItemCommand = new RelayCommand(AddChecklistItem);
        RemoveChecklistItemCommand = new RelayCommand<QuickNoteChecklistDraftItem>(RemoveChecklistItem);
        AvailableColors = Enum.GetValues<QuickNoteColor>();

        RefreshNotes();
        if (_notes.FirstOrDefault() is { } firstNote) LoadNote(firstNote);
    }

    public IReadOnlyList<QuickNote> Notes
    {
        get => _notes;
        private set => SetProperty(ref _notes, value);
    }

    public IReadOnlyList<QuickNoteColor> AvailableColors { get; }

    public ObservableCollection<QuickNoteChecklistDraftItem> ChecklistDraft => _checklistDraft;

    public QuickNote? SelectedNote => _selectedNoteId is { } id
        ? _service.GetNotes(_selectedCollection).FirstOrDefault(note => note.Id == id)
        : null;

    public Guid? SelectedNoteId
    {
        get => _selectedNoteId;
        private set
        {
            if (SetProperty(ref _selectedNoteId, value))
            {
                OnPropertyChanged(nameof(SelectedNote));
                OnPropertyChanged(nameof(HasSelectedNote));
            }
        }
    }

    public QuickNoteCollection SelectedCollection
    {
        get => _selectedCollection;
        private set
        {
            if (SetProperty(ref _selectedCollection, value))
            {
                OnPropertyChanged(nameof(IsTrashView));
                OnPropertyChanged(nameof(IsArchivedView));
                OnPropertyChanged(nameof(IsEditorEnabled));
                OnPropertyChanged(nameof(ArchiveActionText));
                OnPropertyChanged(nameof(SelectedNote));
            }
        }
    }

    public bool IsTrashView => SelectedCollection == QuickNoteCollection.Trash;
    public bool IsArchivedView => SelectedCollection == QuickNoteCollection.Archived;
    public bool IsEditorEnabled => !IsTrashView;
    public string ArchiveActionText => IsArchivedView ? "Desarquivar" : "Arquivar";
    public bool HasSelectedNote => SelectedNote is not null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty)) RefreshNotes();
        }
    }

    public string TitleDraft
    {
        get => _titleDraft;
        set
        {
            if (SetProperty(ref _titleDraft, value ?? string.Empty)) QueueAutosave();
        }
    }

    public string ContentDraft
    {
        get => _contentDraft;
        set
        {
            if (SetProperty(ref _contentDraft, value ?? string.Empty)) QueueAutosave();
        }
    }

    public string TagsDraft
    {
        get => _tagsDraft;
        set
        {
            if (SetProperty(ref _tagsDraft, value ?? string.Empty)) QueueAutosave();
        }
    }

    public QuickNoteColor ColorDraft
    {
        get => _colorDraft;
        set
        {
            if (SetProperty(ref _colorDraft, value)) QueueAutosave();
        }
    }

    public QuickNotesSaveState SaveState
    {
        get => _saveState;
        private set
        {
            if (SetProperty(ref _saveState, value)) OnPropertyChanged(nameof(SaveStateText));
        }
    }

    public string SaveStateText => SaveState switch
    {
        QuickNotesSaveState.Pending => "Alterações pendentes…",
        QuickNotesSaveState.Saving => "Salvando…",
        QuickNotesSaveState.Saved => "Salvo neste dispositivo",
        QuickNotesSaveState.Error => "Não foi possível salvar",
        _ => string.Empty
    };

    public string SaveErrorMessage
    {
        get => _saveErrorMessage;
        private set => SetProperty(ref _saveErrorMessage, value);
    }

    public IAsyncRelayCommand BeginNewCommand { get; }
    public IAsyncRelayCommand<Guid> SelectNoteCommand { get; }
    public IAsyncRelayCommand<QuickNoteCollection> SelectCollectionCommand { get; }
    public IAsyncRelayCommand TogglePinCommand { get; }
    public IAsyncRelayCommand ArchiveSelectedCommand { get; }
    public IAsyncRelayCommand TrashSelectedCommand { get; }
    public IAsyncRelayCommand RestoreSelectedCommand { get; }
    public IAsyncRelayCommand DeleteSelectedPermanentlyCommand { get; }
    public IRelayCommand AddChecklistItemCommand { get; }
    public IRelayCommand<QuickNoteChecklistDraftItem> RemoveChecklistItemCommand { get; }

    public async Task FlushPendingSaveAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingSave;
        if (pending is null)
        {
            if (_pendingSaveTask is { IsCompleted: false } activeSave)
                await activeSave.WaitAsync(cancellationToken);
            return;
        }

        _debounceCancellation?.Cancel();
        await SaveSnapshotAsync(pending, cancellationToken);
    }

    private async Task BeginNewAsync()
    {
        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        SelectedCollection = QuickNoteCollection.Active;
        RefreshNotes();
        SelectedNoteId = null;
        ClearDraft();
        SaveErrorMessage = string.Empty;
        SaveState = QuickNotesSaveState.Saved;
    }

    private async Task SelectNoteAsync(Guid id)
    {
        if (SelectedNoteId == id) return;

        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        var note = _service.GetNotes(SelectedCollection).FirstOrDefault(item => item.Id == id);
        if (note is not null) LoadNote(note);
    }

    private async Task SelectCollectionAsync(QuickNoteCollection collection)
    {
        if (SelectedCollection == collection) return;

        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        SelectedCollection = collection;
        RefreshNotes();
        if (_notes.FirstOrDefault() is { } firstNote) LoadNote(firstNote);
        else ClearSelection();
    }

    private async Task TogglePinAsync()
    {
        var note = SelectedNote;
        if (note is null || IsTrashView) return;

        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        await _service.SetPinnedAsync(note.Id, !note.IsPinned);
        RefreshNotes();
        OnPropertyChanged(nameof(SelectedNote));
    }

    private async Task ArchiveSelectedAsync()
    {
        var note = SelectedNote;
        if (note is null || IsTrashView) return;

        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        await _service.SetArchivedAsync(note.Id, !note.IsArchived);
        RemoveSelectedAfterMutation();
    }

    private async Task TrashSelectedAsync()
    {
        var note = SelectedNote;
        if (note is null || IsTrashView) return;

        await FlushPendingSaveAsync();
        if (SaveState == QuickNotesSaveState.Error) return;

        await _service.MoveToTrashAsync(note.Id);
        RemoveSelectedAfterMutation();
    }

    private async Task RestoreSelectedAsync()
    {
        var note = SelectedNote;
        if (note is null || !IsTrashView) return;

        await _service.RestoreFromTrashAsync(note.Id);
        RemoveSelectedAfterMutation();
    }

    private async Task DeleteSelectedPermanentlyAsync()
    {
        var note = SelectedNote;
        if (note is null || !IsTrashView) return;

        await _service.DeletePermanentlyAsync(note.Id);
        RemoveSelectedAfterMutation();
    }

    private void RemoveSelectedAfterMutation()
    {
        RefreshNotes();
        if (_notes.FirstOrDefault() is { } firstNote) LoadNote(firstNote);
        else ClearSelection();
    }

    private void AddChecklistItem()
    {
        _checklistDraft.Add(new QuickNoteChecklistDraftItem(
            Guid.NewGuid(), string.Empty, false, _checklistDraft.Count));
    }

    private void RemoveChecklistItem(QuickNoteChecklistDraftItem? item)
    {
        if (item is not null) _checklistDraft.Remove(item);
    }

    private void OnChecklistCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (QuickNoteChecklistDraftItem item in e.OldItems)
                item.PropertyChanged -= OnChecklistItemPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (QuickNoteChecklistDraftItem item in e.NewItems)
                item.PropertyChanged += OnChecklistItemPropertyChanged;
        }

        OnPropertyChanged(nameof(ChecklistDraft));
        if (!_isLoadingDraft) QueueAutosave();
    }

    private void OnChecklistItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueAutosave();

    private void QueueAutosave()
    {
        if (_isLoadingDraft || IsTrashView) return;

        var pending = new PendingSave(SelectedNoteId, CaptureDraft(), ++_editRevision);
        _pendingSave = pending;
        SaveErrorMessage = string.Empty;
        SaveState = QuickNotesSaveState.Pending;

        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = new CancellationTokenSource();
        _pendingSaveTask = DebounceAndSaveAsync(pending, _debounceCancellation.Token);
    }

    private async Task DebounceAndSaveAsync(PendingSave pending, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_autosaveDelay, _timeProvider, cancellationToken);
            await SaveSnapshotAsync(pending, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer edit or an explicit flush owns the current snapshot.
        }
    }

    private async Task SaveSnapshotAsync(PendingSave pending, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (pending.Revision == _editRevision) SaveState = QuickNotesSaveState.Saving;

            try
            {
                var saved = await _service.SaveDraftAsync(pending.NoteId, pending.Draft, cancellationToken);
                if (_pendingSave?.Revision == pending.Revision) _pendingSave = null;

                if (pending.Revision == _editRevision)
                {
                    if (pending.NoteId is null && saved is not null && SelectedNoteId is null)
                        SelectedNoteId = saved.Id;

                    RefreshNotes();
                    SaveErrorMessage = string.Empty;
                    SaveState = QuickNotesSaveState.Saved;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                if (pending.Revision == _editRevision)
                {
                    SaveErrorMessage = "Mantenha esta janela aberta e tente salvar novamente.";
                    SaveState = QuickNotesSaveState.Error;
                }
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private QuickNoteDraft CaptureDraft() => new(
        TitleDraft,
        ContentDraft,
        Array.AsReadOnly(_checklistDraft
            .Select((item, order) => new QuickNoteChecklistItem(item.Id, item.Text, item.IsCompleted, order))
            .ToArray()),
        Array.AsReadOnly(TagsDraft.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
        ColorDraft);

    private void RefreshNotes()
    {
        Notes = _service.GetNotes(SelectedCollection, SearchText);
        OnPropertyChanged(nameof(HasSelectedNote));
    }

    private void LoadNote(QuickNote note)
    {
        _isLoadingDraft = true;
        try
        {
            SelectedNoteId = note.Id;
            TitleDraft = note.Title;
            ContentDraft = note.Content;
            TagsDraft = string.Join(", ", note.Tags);
            ColorDraft = note.Color;
            _checklistDraft.Clear();
            foreach (var item in note.Checklist.OrderBy(item => item.Order))
            {
                _checklistDraft.Add(new QuickNoteChecklistDraftItem(
                    item.Id, item.Text, item.IsCompleted, item.Order));
            }
        }
        finally
        {
            _isLoadingDraft = false;
        }

        SaveErrorMessage = string.Empty;
        SaveState = QuickNotesSaveState.Saved;
    }

    private void ClearSelection()
    {
        SelectedNoteId = null;
        ClearDraft();
    }

    private void ClearDraft()
    {
        _isLoadingDraft = true;
        try
        {
            TitleDraft = string.Empty;
            ContentDraft = string.Empty;
            TagsDraft = string.Empty;
            ColorDraft = QuickNoteColor.Default;
            _checklistDraft.Clear();
        }
        finally
        {
            _isLoadingDraft = false;
        }
    }

    private sealed record PendingSave(Guid? NoteId, QuickNoteDraft Draft, long Revision);
}
