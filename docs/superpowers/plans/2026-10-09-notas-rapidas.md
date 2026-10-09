# Notas rápidas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar notas rápidas persistentes à Estante da Dynamic Island, com widget compacto, janela de organização, busca e captura por atalho.

**Architecture:** Regras e modelo ficam em `Island.Core/Notes`, com armazenamento atrás de um contrato e implementação JSON atômica em `Island.Windows`. `Island.App` compõe o serviço, janela focável, widget da Estante, configurações e hotkey global; a UI mantém a edição local até confirmar cada salvamento.

**Tech Stack:** C# / .NET 10, WPF, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-09-notas-rapidas-design.md`

## Global Constraints

- `Island.Core` não referencia WPF nem Win32; `Island.Windows` implementa contratos; `Island.App` compõe os serviços.
- Nenhuma dependência de banco de dados é adicionada.
- Os dados ficam em `%LocalAppData%\\DynamicIsland\\notes.json`, com versão de formato e substituição atômica.
- Falha de `Ctrl+Alt+N` não impede a abertura pela Estante; atalhos `Ctrl+N` e `Ctrl+F` continuam locais à janela.
- A UI respeita **Reduce animations**, não mantém animação ligada em repouso e permanece operável por teclado.
- Itens na lixeira só são apagados permanentemente por ação explícita.
- Preservar fim de linha existente nos arquivos modificados e não tocar em arquivos de build (`bin/`, `obj/`).
- Conferir `git status --short` antes das tarefas e preservar todo trabalho local já existente; não usar `git add .` nem `git commit -a`.
- No momento em que este plano foi escrito, `ServiceRegistration.cs`, `ShelfContext.cs` e `WidgetCatalog.cs` já contêm alterações locais do recurso Calendar. Manter os registros `ICalendarStore`/`CalendarAgenda`, a propriedade/argumento `ShelfContext.Calendar` e o descritor Calendar; selecionar só hunks de Notas em commits desses arquivos.

## Review Focus

- Fechar ou trocar a nota durante a janela de debounce não pode perder a última edição — cubra em `QuickNotesViewModelTests` (Task 4).
- Um rascunho vazio não deve ser persistido, mas apagar o conteúdo de uma nota existente não pode apagá-la implicitamente — cubra em `QuickNotesServiceTests` (Task 1).
- Falha de gravação ou JSON corrompido não pode aparecer como salvamento bem-sucedido nem destruir a cópia recuperável — cubra em `JsonQuickNotesStoreTests` e `QuickNotesViewModelTests` (Tasks 2 e 4).
- Fixar, arquivar, restaurar e excluir permanentemente devem produzir listas e ordenação coerentes — cubra em `QuickNotesServiceTests` (Task 1).
- Conflito da hotkey global deve preservar acesso pela Estante e configuração desligada deve liberar o registro — cubra a aplicação de configuração/registro na integração (Task 5).

---

### Task 1: Modelo e regras de notas no Core

**Files:**
- Create: `src/Island.Core/Notes/QuickNote.cs`
- Create: `src/Island.Core/Notes/IQuickNotesStore.cs`
- Create: `src/Island.Core/Notes/QuickNotesService.cs`
- Test: `tests/Island.Core.Tests/QuickNotesServiceTests.cs`

**Interfaces:**
- `QuickNote(Guid Id, string Title, string Content, IReadOnlyList<QuickNoteChecklistItem> Checklist, IReadOnlyList<string> Tags, QuickNoteColor Color, bool IsPinned, bool IsArchived, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt)`.
- `QuickNoteChecklistItem(Guid Id, string Text, bool IsCompleted, int Order)`.
- `QuickNoteDraft(string Title, string Content, IReadOnlyList<QuickNoteChecklistItem> Checklist, IReadOnlyList<string> Tags, QuickNoteColor Color)`.
- `QuickNoteColor` contém `Default`, `Blue`, `Green`, `Yellow`, `Pink` e `Purple`; `QuickNoteCollection` contém `Active`, `Archived` e `Trash`.
- `IQuickNotesStore.LoadAsync(CancellationToken cancellationToken = default) -> Task<IReadOnlyList<QuickNote>>` e `SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default) -> Task`.
- `QuickNotesService(IQuickNotesStore store, TimeProvider? timeProvider = null)` expõe `InitializeAsync(CancellationToken cancellationToken = default)`, `GetNotes(QuickNoteCollection collection = QuickNoteCollection.Active, string? query = null)`, `SaveDraftAsync(Guid? id, QuickNoteDraft draft, CancellationToken cancellationToken = default) -> Task<QuickNote?>`, `SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default)`, `SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)`, `MoveToTrashAsync(Guid id, CancellationToken cancellationToken = default)`, `RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken = default)`, `DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)` e `Changed`.
- Um rascunho novo só tem conteúdo quando título/corpo não são brancos ou há um item de checklist com texto. Etiquetas sozinhas não criam nota. Etiquetas são aparadas e deduplicadas sem distinção de caixa.
- Coleções: `Active`, `Archived`, `Trash`. A lista ativa ordena fixadas primeiro e depois `UpdatedAt` decrescente; busca compara título, conteúdo e etiquetas sem distinção de maiúsculas/minúsculas. As mutações usam `SemaphoreSlim` para serializar gravações concorrentes.
- Cada mutação persiste o snapshot antes de publicá-lo em memória. `SaveDraftAsync(null, vazio)` retorna `null`; uma nota existente permanece existente mesmo se seu rascunho ficar vazio.

- [x] **Step 1: Escrever testes falhando** em `QuickNotesServiceTests` com um `IQuickNotesStore` em memória:

```csharp
[Fact] public async Task Blank_new_draft_is_not_persisted() { var s = await CreateServiceAsync(); Assert.Null(await s.SaveDraftAsync(null, EmptyDraft)); Assert.Empty(s.GetNotes(QuickNoteCollection.Active)); }
[Fact] public async Task Existing_note_with_empty_draft_remains_saved() { var (s, n) = await CreateServiceWithNoteAsync(); await s.SaveDraftAsync(n.Id, EmptyDraft); Assert.Single(s.GetNotes(QuickNoteCollection.Active)); }
[Fact] public async Task Search_matches_content_and_tags_without_case_sensitivity() { var s = await CreateServiceWithNoteAsync("café", ["Leitura"]); Assert.Single(s.GetNotes(QuickNoteCollection.Active, "CAFÉ")); Assert.Single(s.GetNotes(QuickNoteCollection.Active, "leitura")); }
[Fact] public async Task Pinned_notes_sort_before_unpinned_then_by_updated_time() { var (s, newer, older, unpinned) = await CreateOrderingFixtureAsync(); Assert.Equal(new[] { newer.Id, older.Id, unpinned.Id }, s.GetNotes().Select(n => n.Id)); }
[Fact] public async Task Trash_restore_and_permanent_delete_use_separate_collections() { var (s, n) = await CreateServiceWithNoteAsync(); await s.MoveToTrashAsync(n.Id); Assert.Empty(s.GetNotes()); await s.RestoreFromTrashAsync(n.Id); Assert.Single(s.GetNotes()); await s.DeletePermanentlyAsync(n.Id); Assert.Empty(s.GetNotes(QuickNoteCollection.Trash)); }
[Fact] public async Task Archived_note_leaves_active_and_returns_when_unarchived() { var (s, n) = await CreateServiceWithNoteAsync(); await s.SetArchivedAsync(n.Id, true); Assert.Empty(s.GetNotes()); Assert.Single(s.GetNotes(QuickNoteCollection.Archived)); await s.SetArchivedAsync(n.Id, false); Assert.Single(s.GetNotes()); }
[Fact] public async Task Failed_store_write_does_not_publish_the_mutation() { var (s, store) = await CreateServiceAsyncWithStore(); store.ThrowOnSave = true; await Assert.ThrowsAsync<IOException>(() => s.SaveDraftAsync(null, DraftWithContent)); Assert.Empty(s.GetNotes()); }
```

`CreateServiceAsync`, `CreateServiceAsyncWithStore`, `CreateServiceWithNoteAsync(string content = "nota", IReadOnlyList<string>? tags = null) -> (QuickNotesService service, QuickNote note)`, `CreateOrderingFixtureAsync`, `EmptyDraft`, `DraftWithContent`, and the fake store are private test helpers in this file; initialize each service before assertions.

- [x] **Step 2: Confirmar falha** — execute `dotnet test tests/Island.Core.Tests/Island.Core.Tests.csproj --filter FullyQualifiedName~QuickNotesServiceTests`; os tipos e comportamentos ainda não existem.
- [x] **Step 3: Implementar os modelos e o serviço** com snapshots imutáveis, timestamps via `TimeProvider` e alteração em memória somente após a conclusão bem-sucedida do armazenamento.
- [x] **Step 4: Confirmar aprovação** — repita o comando direcionado e confirme que todos os casos de `QuickNotesServiceTests` passam.
- [x] **Step 5: Commit** — `feat: add quick notes core model and service`.

### Task 2: Armazenamento JSON resiliente

**Files:**
- Create: `src/Island.Windows/Notes/JsonQuickNotesStore.cs`
- Test: `tests/Island.Windows.Tests/JsonQuickNotesStoreTests.cs`

**Interfaces:**
- `JsonQuickNotesStore(string? directory = null, ILogger<JsonQuickNotesStore>? log = null)` implementa `IQuickNotesStore`; sem diretório fornecido, usa `%LocalAppData%\\DynamicIsland`.
- O documento JSON guarda `schemaVersion: 1` e `notes`; cada nota preserva os campos do modelo Core.
- Versão maior que `1` lança `UnsupportedNotesSchemaException` antes de qualquer movimento ou gravação.
- Arquivo ausente retorna coleção vazia. JSON inválido é movido para recuperação sem substituir um `.bad` existente. Versão futura não é regravada nem colocada em quarentena como corrupção.
- `SaveAsync` grava em `notes.json.tmp`, faz flush, substitui atomicamente `notes.json` e limpa o temporário; erros de I/O são propagados ao chamador.

- [x] **Step 1: Escrever testes falhando** em `JsonQuickNotesStoreTests`:

```csharp
[Fact] public async Task Round_trip_preserves_checklist_tags_and_flags() { var (s, n) = CreateStoreWithNote(); await s.SaveAsync([n]); var loaded = Assert.Single(await s.LoadAsync()); Assert.Equal(n.Id, loaded.Id); Assert.Equal(n.Checklist, loaded.Checklist); Assert.Equal(n.Tags, loaded.Tags); Assert.Equal(n.IsArchived, loaded.IsArchived); }
[Fact] public async Task Missing_file_returns_empty_collection() { Assert.Empty(await CreateEmptyStore().LoadAsync()); }
[Fact] public async Task Corrupt_file_is_quarantined_without_overwriting_prior_recovery() { var (s, dir, path, bad) = CreateStoreWithPaths(); await File.WriteAllTextAsync(path, "{ invalid"); var corrupt = await File.ReadAllBytesAsync(path); await File.WriteAllBytesAsync(bad, priorRecovery); Assert.Empty(await s.LoadAsync()); Assert.Equal(priorRecovery, await File.ReadAllBytesAsync(bad)); Assert.Contains(Directory.GetFiles(dir, "notes.json.bad-*"), p => File.ReadAllBytes(p).SequenceEqual(corrupt)); }
[Fact] public async Task Unsupported_schema_is_left_untouched() { var (s, path) = CreateEmptyStoreWithPath(); await File.WriteAllTextAsync(path, """{ "schemaVersion": 99, "notes": [] }"""); var original = await File.ReadAllBytesAsync(path); await Assert.ThrowsAsync<UnsupportedNotesSchemaException>(() => s.LoadAsync()); Assert.Equal(original, await File.ReadAllBytesAsync(path)); }
[Fact] public async Task Save_replaces_target_and_cleans_temporary_file() { var (s, temp) = CreateEmptyStoreWithTempPath(); var (first, second) = CreateTwoNotes(); await s.SaveAsync([first]); await s.SaveAsync([second]); Assert.Equal(second.Id, Assert.Single(await s.LoadAsync()).Id); Assert.False(File.Exists(temp)); }
```

The test fixture owns a unique temp directory, `priorRecovery`, and helpers `CreateStoreWithNote`, `CreateEmptyStore`, `CreateStoreWithPaths`, `CreateEmptyStoreWithPath`, `CreateEmptyStoreWithTempPath`, and `CreateTwoNotes`; it deletes the directory after each test. When `notes.json.bad` already exists, the store writes the corrupt source to a unique `notes.json.bad-<timestamp>` path instead.

- [x] **Step 2: Confirmar falha** — execute `dotnet test tests/Island.Windows.Tests/Island.Windows.Tests.csproj --filter FullyQualifiedName~JsonQuickNotesStoreTests`.
- [x] **Step 3: Implementar o repositório** com serialização camelCase, exclusão mútua por instância e gravação temporária/atômica compatível com o comportamento de `JsonSettingsStore`.
- [x] **Step 4: Confirmar aprovação** — repita o comando direcionado; confira também que arquivo de versão futura não foi alterado.
- [x] **Step 5: Commit** — `feat: persist quick notes atomically as versioned json`.

### Task 3: Composição do serviço e opção da hotkey

**Files:**
- Modify: `src/Island.Core/Configuration/IslandSettings.cs`
- Modify: `src/Island.App/Composition/ServiceRegistration.cs`
- Modify: `src/Island.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/Island.App/Shell/SettingsWindow.xaml`
- Test: `tests/Island.Core.Tests/QuickNotesSettingsTests.cs`
- Modify/Test: `tests/Island.Windows.Tests/JsonSettingsStoreTests.cs`

**Interfaces:**
- `IslandSettings.QuickNotesHotkeyEnabled` tem padrão `true` e participa de `Equals`/`GetHashCode`.
- `ServiceRegistration.Build` registra `IQuickNotesStore` como singleton `JsonQuickNotesStore` e `QuickNotesService` como singleton.
- Acrescentar registros de notas sem remover `ICalendarStore` nem `CalendarAgenda` das alterações locais já existentes em `ServiceRegistration.cs`.
- `SettingsViewModel.QuickNotesHotkeyEnabled` atualiza a preferência por `Commit`, seguindo o padrão de outras opções live.

- [x] **Step 1: Escrever `QuickNotesSettingsTests` falhando** com `Assert.True(new IslandSettings().QuickNotesHotkeyEnabled)` e `Assert.NotEqual(enabled, enabled with { QuickNotesHotkeyEnabled = false })`; adicionar em `JsonSettingsStoreTests` o caso `Settings_file_without_notes_hotkey_uses_enabled_default`.
- [x] **Step 2: Confirmar falha** — execute `dotnet test tests/Island.Core.Tests/Island.Core.Tests.csproj --filter FullyQualifiedName~QuickNotesSettingsTests` e `dotnet test tests/Island.Windows.Tests/Island.Windows.Tests.csproj --filter FullyQualifiedName~Settings_file_without_notes_hotkey_uses_enabled_default`.
- [x] **Step 3: Implementar configuração e DI**, acrescentando um toggle acessível em Configurações → Notas rápidas; preservar configurações antigas que não contêm o novo campo. A verificação da ViewModel pertence à Task 4.
- [x] **Step 4: Confirmar aprovação** — repita o comando direcionado e compile `src/Island.App/Island.App.csproj`.
- [x] **Step 5: Commit** — `feat: register quick notes and add hotkey setting`.

### Task 4: ViewModel e janela de notas

**Files:**
- Create: `src/Island.App/ViewModels/QuickNotesViewModel.cs`
- Create: `src/Island.App/Shell/QuickNotesWindow.xaml`
- Create: `src/Island.App/Shell/QuickNotesWindow.xaml.cs`
- Modify: `tests/Island.Windows.Tests/Island.Windows.Tests.csproj` (referenciar `Island.App` para testar a ViewModel)
- Test: `tests/Island.Windows.Tests/QuickNotesViewModelTests.cs`

**Interfaces:**
- `QuickNotesViewModel(QuickNotesService service, TimeProvider? timeProvider = null, TimeSpan? autosaveDelay = null)` expõe `Notes`, `SearchText`, `SelectedNoteId`, `TitleDraft`, `ContentDraft`, `ChecklistDraft`, `TagsDraft`, `ColorDraft`, `SaveState` (`QuickNotesSaveState.Pending`, `Saving`, `Saved`, `Error`), `FlushPendingSaveAsync(CancellationToken cancellationToken = default)`, `BeginNewCommand`, `SelectNoteCommand` (`IAsyncRelayCommand<Guid>`), `TogglePinCommand`, `ArchiveSelectedCommand`, `TrashSelectedCommand`, `RestoreSelectedCommand` e `DeleteSelectedPermanentlyCommand`.
- Edição aguarda 450 ms sem digitação antes de `SaveDraftAsync`; `FlushPendingSaveAsync(CancellationToken)` confirma a gravação antes de trocar nota ou fechar a janela.
- A ViewModel mantém o rascunho local e sinaliza erro se a persistência falhar; nunca marca esse estado como salvo.
- `QuickNotesWindow(QuickNotesViewModel viewModel, Func<bool> reduceAnimations)` é uma janela WPF focável com busca/lista à esquerda e editor à direita. O fechamento aguarda `FlushPendingSaveAsync`; se falhar, permanece aberto com o rascunho e o erro visíveis.

- [x] **Step 1: Escrever testes falhando** em `QuickNotesViewModelTests` com atraso de autosave injetado de 10 ms:

```csharp
[Fact] public async Task New_empty_draft_is_not_saved_until_it_has_content() { var vm = CreateViewModel(TimeSpan.FromMilliseconds(10)); vm.BeginNewCommand.Execute(null); await vm.FlushPendingSaveAsync(); Assert.Empty(vm.Notes); }
[Fact] public async Task Switching_notes_flushes_the_pending_debounced_edit() { var (vm, service, current, other) = await CreateViewModelWithTwoNotesAsync(TimeSpan.FromMilliseconds(10)); vm.ContentDraft = "latest"; await vm.SelectNoteCommand.ExecuteAsync(other.Id); Assert.Equal("latest", service.GetNotes().Single(n => n.Id == current.Id).Content); }
[Fact] public async Task Explicit_flush_persists_before_the_debounce_expires() { var (vm, service, note) = await CreateViewModelWithNoteAsync(autosaveDelay: TimeSpan.FromSeconds(5)); vm.ContentDraft = "closing now"; await vm.FlushPendingSaveAsync(); Assert.Equal("closing now", service.GetNotes().Single(n => n.Id == note.Id).Content); }
[Fact] public async Task Save_failure_keeps_draft_and_sets_error_state() { var (vm, store) = await CreateViewModelWithStoreAsync(TimeSpan.FromMilliseconds(10)); vm.BeginNewCommand.Execute(null); vm.ContentDraft = "draft intacto"; store.ThrowOnSave = true; await vm.FlushPendingSaveAsync(); Assert.Equal("draft intacto", vm.ContentDraft); Assert.Equal(QuickNotesSaveState.Error, vm.SaveState); }
[Fact] public async Task Autosave_persists_after_the_quiet_period() { var (vm, service, note) = await CreateViewModelWithNoteAsync(autosaveDelay: TimeSpan.FromMilliseconds(20)); vm.ContentDraft = "saved after pause"; await Task.Delay(100); Assert.Equal("saved after pause", service.GetNotes().Single(n => n.Id == note.Id).Content); }
[Fact] public async Task Search_includes_tags_and_content() { var (vm, note) = await CreateViewModelWithNoteAsync("receita", ["cozinha"]); vm.SearchText = "cozinha"; Assert.Equal(note.Id, Assert.Single(vm.Notes).Id); vm.SearchText = "receita"; Assert.Equal(note.Id, Assert.Single(vm.Notes).Id); }
[Fact] public void Settings_view_model_commits_global_hotkey_preference_changes() { IslandSettings? applied = null; var s = new SettingsViewModel(new IslandSettings { QuickNotesHotkeyEnabled = false }, ["Primary"], next => applied = next); s.QuickNotesHotkeyEnabled = true; Assert.True(s.Current.QuickNotesHotkeyEnabled); Assert.True(applied!.QuickNotesHotkeyEnabled); }
```

`CreateViewModel(TimeSpan? autosaveDelay = null)`, `CreateViewModelWithTwoNotesAsync(TimeSpan autosaveDelay) -> (QuickNotesViewModel vm, QuickNotesService service, QuickNote current, QuickNote other)`, `CreateViewModelWithStoreAsync(TimeSpan autosaveDelay)`, and `CreateViewModelWithNoteAsync(string content = "texto", IReadOnlyList<string>? tags = null, TimeSpan? autosaveDelay = null)` are private helpers; the fake store can throw on writes.

- [ ] **Step 2: Confirmar falha** — execute `dotnet test tests/Island.Windows.Tests/Island.Windows.Tests.csproj --filter FullyQualifiedName~QuickNotesViewModelTests`.
- [x] **Step 3: Implementar ViewModel e janela** com atalhos `Ctrl+N`/`Ctrl+F`, estados de salvamento visíveis, botões nomeados e operação por teclado.
- [x] **Step 4: Adicionar transições finitas** de opacidade/deslocamento ao abrir/fechar a janela e trocar itens, seguindo o ritmo de saída/entrada já usado no app; quando `reduceAnimations()` retornar `true`, apresentar as alterações sem movimento.
- [x] **Step 5: Confirmar aprovação** — repita os testes direcionados e compile `src/Island.App/Island.App.csproj`.
- [x] **Step 6: Commit** — `feat: add quick notes window and autosave editor`.

### Task 5: Widget, abertura da janela e captura global

**Files:**
- Create: `src/Island.App/Views/Widgets/QuickNotesWidget.xaml`
- Create: `src/Island.App/Views/Widgets/QuickNotesWidget.xaml.cs`
- Create: `src/Island.App/Shell/QuickNotesWindowManager.cs`
- Create: `src/Island.App/Shell/QuickNotesHotkeyController.cs`
- Create: `src/Island.App/Widgets/IQuickNotesWindowHost.cs`
- Create: `src/Island.Windows/Input/IGlobalHotkey.cs`
- Modify: `src/Island.App/Shell/QuickNotesWindow.xaml`
- Modify: `src/Island.App/Shell/QuickNotesWindow.xaml.cs`
- Modify: `src/Island.App/Widgets/ShelfContext.cs`
- Modify: `src/Island.App/Widgets/WidgetCatalog.cs`
- Modify: `src/Island.App/Composition/ServiceRegistration.cs`
- Modify: `src/Island.App/App.xaml.cs`
- Modify: `src/Island.Windows/Input/GlobalHotkey.cs`
- Modify: `src/Island.App/Styles/WidgetIcons.xaml`
- Test: `tests/Island.Windows.Tests/QuickNotesIntegrationTests.cs`

**Interfaces:**
- `IQuickNotesWindowHost` expõe `OpenForCapture()`, `OpenNotes()`, `HotkeyConflict`, `SetHotkeyConflict(bool)` e `event Action<bool> HotkeyConflictChanged`; `QuickNotesWindowManager` implementa o contrato e mantém no máximo uma janela aberta.
- `ShelfContext` expõe o serviço e `IQuickNotesWindowHost QuickNotesHost`; o contrato fica no projeto App e não referencia tipos WPF.
- Acrescentar essas dependências ao construtor atual de `ShelfContext` preservando `CalendarAgenda calendar`/`Calendar`; a fábrica na composição deve continuar injetando ambos os recursos.
- Acrescentar o descritor `quicknotes` e preservar o descritor `calendar` que já está em alteração local.
- O descritor registrado em `WidgetCatalog.All` usa ID `quicknotes`, título `Notas`, altura padrão e largura de 280 DIP.
- `IGlobalHotkey` expõe `Pressed`, `Register()` e `Dispose()`; `GlobalHotkey` implementa esse contrato.
- `QuickNotesHotkeyController(Func<IGlobalHotkey> createHotkey, Action openForCapture, Action<Action> dispatchToUi, Action<bool> setHotkeyConflict)` expõe `SetEnabled(bool)`, `IsEnabled`, `IsAvailable` e `HotkeyConflict` (`IsEnabled && !IsAvailable`); alterar a preferência libera/recria o registro e o evento encaminha a captura pela UI thread.
- `QuickNotesWindowManager.SetHotkeyConflict(bool)` atualiza `QuickNotesWindow.SetHotkeyConflict(bool)` e dispara `HotkeyConflictChanged`; o widget assina ao carregar, desassina ao descarregar e só mostra aviso quando a opção está ligada e o registro falhou.
- Conflito de registro deixa o widget funcional e o estado indisponível é visível na janela/widget e no log.

- [x] **Step 1: Escrever testes falhando** em `QuickNotesIntegrationTests`:

```csharp
[Fact] public void Catalog_contains_one_280_dip_quick_notes_widget() { Assert.Single(WidgetCatalog.All.Where(w => w.Id == "quicknotes")); Assert.Equal(280, WidgetCatalog.Find("quicknotes")!.Width); }
[Fact] public void Disabled_hotkey_is_not_registered() { controller.SetEnabled(false); Assert.Equal(0, fake.RegisterCalls); Assert.False(controller.HotkeyConflict); }
[Fact] public void Registration_conflict_keeps_shelf_access_and_reports_unavailable() { fake.RegisterResult = false; controller.SetEnabled(true); Assert.False(controller.IsAvailable); Assert.True(controller.HotkeyConflict); host.OpenForCapture(); Assert.Equal(1, host.CaptureRequests); }
[Fact] public void Hotkey_press_is_dispatched_to_ui_capture_callback() { controller.SetEnabled(true); fake.RaisePressed(); Assert.NotNull(queuedUiAction); queuedUiAction!(); Assert.Equal(1, captureRequests); }
[Fact] public void Disabling_hotkey_disposes_the_previous_registration() { controller.SetEnabled(true); controller.SetEnabled(false); Assert.True(fake.Disposed); Assert.False(controller.HotkeyConflict); }
```

`controller`, `fake`, `host`, and `queuedUiAction` are initialized by a private fixture with an `IGlobalHotkey` fake and a dispatch callback that stores the queued action.

- [ ] **Step 2: Confirmar falha** — execute `dotnet test tests/Island.Windows.Tests/Island.Windows.Tests.csproj --filter FullyQualifiedName~QuickNotesIntegrationTests`.
- [x] **Step 3: Implementar o widget e o manager**, mostrando estado vazio ou duas notas da ordenação do serviço, prévias curtas, botão `+` e estado de conflito da hotkey; conectar abertura e criar/focar captura pelo `IQuickNotesWindowHost`. O widget assina `QuickNotesService.Changed` por `UiSignal`/Dispatcher e remove a assinatura em `Unloaded`.
- [x] **Step 4: Integrar o ciclo de vida em `App.xaml.cs`**: inicializar notas antes do primeiro uso; criar o manager uma vez por app; registrar `Ctrl+Alt+N` conforme a preferência; encaminhar a abertura pela UI thread; ligar a callback de disponibilidade ao manager/window/widget; atualizar o registro quando `SettingsApplier.Applied` mudar a preferência; e liberar a hotkey ao sair. Falha no registro deve ser logada sem derrubar o app.
- [ ] **Step 5: Confirmar aprovação** — repita os testes direcionados e compile `src/Island.App/Island.App.csproj`; confira visualmente ativação de janela única, captura focada e acesso pela Estante em conflito de hotkey.
- [x] **Step 6: Commit** — `feat: add quick notes shelf widget and global capture`.

### Task 6: Integração e aceitação do app

**Files:**
- Modify apenas os arquivos das Tasks 1–5 que precisarem de correções encontradas durante a integração.
- Test: `tests/Island.Core.Tests/QuickNotesServiceTests.cs`
- Test: `tests/Island.Windows.Tests/JsonQuickNotesStoreTests.cs`
- Test: `tests/Island.Windows.Tests/QuickNotesViewModelTests.cs`
- Test: `tests/Island.Windows.Tests/QuickNotesIntegrationTests.cs`

- [x] **Step 1: Executar todos os testes automatizados** com `dotnet test DynamicIsland.slnx`; resolver falhas de Notas. Se as mudanças de Calendar ainda estiverem em andamento, identificar e relatar falhas atribuíveis a elas sem alterá-las.
- [ ] **Step 2: Conferir o fluxo visual no app**: adicionar Notas na Estante; criar pela janela e pelo `Ctrl+Alt+N`; pesquisar título/conteúdo/etiqueta; fixar, arquivar, excluir, restaurar e apagar permanentemente.
- [ ] **Step 3: Conferir persistência e estados de borda**: reiniciar e reabrir notas; fechar logo após digitar; testar hotkey desligada/conflitante; confirmar rascunho vazio, erro visível de salvamento e recuperação de JSON corrompido sem sobregravar a cópia.
- [ ] **Step 4: Conferir navegação por teclado, foco acessível e preferência **Reduce animations**; verificar estado ocioso sem animação contínua.
- [x] **Step 5: Executar `dotnet build DynamicIsland.slnx` em configuração Release**, revisar `git diff --check` e o escopo final.
- [x] **Step 6: Commit** — `feat: complete integrated quick notes experience`.

## Evidências de aceitação

- Suíte completa no snapshot dos commits de Notas, incluindo o alias WPF corrigido: Core 422/422 e Windows 179/179.
- Build Release da solução no mesmo snapshot: sucesso, zero avisos e zero erros.
- No checkout compartilhado, a suíte completa e o build Release param em `CalendarDayWindow.xaml.cs:232` (`Brush`) e `CalendarWidget.xaml.cs:18` (`UserControl`). Os arquivos do Calendário permaneceram intactos.
- Os testes de aceitação visual e de uso real após reiniciar não foram executados: este ambiente não disponibilizou uma sessão de automação da interface nativa. As etapas 2–4 da Task 6 permanecem abertas para smoke test interativo.

Ruling: validar todos os projetos em um snapshot dos commits de Notas, corrigido com o alias WPF, porque o checkout compartilhado tem dois erros de compilação em arquivos locais do Calendário; custo se errado: o snapshot não cobre alterações não commitadas do Calendário ou de outros arquivos.
