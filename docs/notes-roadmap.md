# Plano — Sistema de notas da Dynamic Island (v0.5 → v1.0)

**Revisão 2 — 20/10/2026.** Plano corrigido após auditoria técnica. O escopo é estritamente
local: sem sincronização entre PCs, sem compartilhamento, sem celular/nuvem e sem qualquer
chamada de rede no subsistema de notas. O sistema de notas não usa conta, login, telemetria,
FCM, OneDrive/Dropbox, QR, links públicos, Windows Hello ou IA.

## O que mudou nesta revisão

- Contrato de armazenamento por operação (`Upsert`/`Delete`/lote) com `expectedRevision`, em vez
  de regravar a coleção inteira a cada edição.
- Migração v1 → v2 com máquina de estados e ponto de commit único; a v1 nunca é apagada
  automaticamente.
- Diário de rascunho separado do autosave para sobreviver a encerramento forçado e queda de
  energia.
- Backup com restauração testada (manifest + hashes), não apenas ZIP gerado.
- Lembretes com identidade de ocorrência e reconciliação após suspensão, mudança de relógio e DST.
- Notificações via API moderna (`AppNotificationManager`), com prova de conceito no build
  publicado; nada de AUMID manual sem teste.
- Interação da ilha centralizada em um controlador de estados (clique, hold, drag, release).
- Markdown com fonte única de checklist e formato legado preservado.
- Game Notes permanecem independentes; não há editor nem modelo compartilhado com Quick Notes.
- Removidos do plano: sync entre PCs, pasta OneDrive/Dropbox, FCM/celular, QR/compartilhamento,
  Windows Hello, IA local, site estático e PDF.
- OCR adiado; `Windows.Media.Ocr` não será assumido no ZIP sem identidade de pacote.

## Escopo e não objetivos (decidido)

- **Um usuário, um PC.** As notas ficam em `%LocalAppData%\DynamicIsland`; não existe opção de
  apontar os dados para uma pasta sincronizada.
- **Sem rede no subsistema de notas.** `Island.Core/Notes` e `Island.Windows/Notes` não
  referenciam `HttpClient`, `WebClient`, sockets, SDKs de nuvem ou serviços de conta.
- **Sem compartilhamento.** Exportar é uma cópia local para backup/arquivamento; o app não envia,
  publica, sincroniza nem observa a pasta escolhida para exportação.
- **Sem celular.** Nada de FCM, QR, pareamento, app Android ou transferência de notas.
- **Sem autenticação de usuário.** Nada de Windows Hello, PIN ou bloqueio da janela. Se um dia
  houver criptografia em repouso, será um projeto separado, com KDF, salt, nonce, autenticação e
  teste de restauração — não um toggle de Hello.
- **Sem IA.** Nenhum assistente, resumo, classificação automática ou modelo local nesta série.
- **Sem PDF/site estático.** Exportação em Markdown/ZIP local; PDF e HTML ficam fora desta série.
- **Game Notes não convergem.** Nenhuma unificação de modelo ou de editor entre Game Notes e
  Quick Notes. O máximo é uma refatoração interna do editor de Quick Notes, sem reutilização pelo
  Game Notes.
- O clima da ilha é um recurso existente e separado; este plano não adiciona nenhuma chamada de
  rede nova. Se o clima estiver desligado, a cápsula mostra apenas agenda e nota.

## Princípios (decididos)

- Local-first e privado: as notas ficam no PC e não dependem de serviço externo.
- Não virar Notion: captura rápida + ilha são o produto. Rich text pesado, bancos de dados de
  usuário, plugins e organização estilo wiki ficam fora.
- Dados antes de funcionalidades: primeiro um armazenamento em que se possa confiar, depois a
  integração visual.
- Contrato orientado a operações, não a snapshots da coleção inteira.
- Toda migração tem uma única fonte oficial em cada estado e um ponto de commit atômico.
- Escrita atômica por nota + diário de rascunho + backup testado.
- Nenhuma ação destrutiva sem backup anterior e sem Undo.
- A interface nunca substitui um rascunho local por conteúdo externo sem confirmação.
- Tempo testável: `TimeProvider` e `FakeTimeProvider`, sem relógio real nos testes.
- Limites de tamanho e comportamento com disco cheio documentados.
- Game Notes e Quick Notes são domínios separados.

## Estado atual (baseline verificado)

- `QuickNote` tem Id, Title, Content, Checklist, Tags, Color, IsPinned, IsArchived, CreatedAt,
  UpdatedAt e DeletedAt (`src/Island.Core/Notes/QuickNote.cs`).
- `IQuickNotesStore` expõe apenas `LoadAsync` e `SaveAsync(IReadOnlyList<QuickNote>)`
  (`src/Island.Core/Notes/IQuickNotesStore.cs:3`). `JsonQuickNotesStore.SaveAsync` reescreve o
  `notes.json` inteiro (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:83`).
- `QuickNotesService.MutateAsync()` envia o conjunto completo de notas a cada alteração
  (`src/Island.Core/Notes/QuickNotesService.cs:227`). Não há distinção de criação, edição,
  remoção ou lote no contrato.
- `AtomicJsonFile` grava em arquivo temporário, faz `Flush(flushToDisk: true)` e usa
  `File.Replace`/`File.Move` (`src/Island.Windows/Storage/AtomicJsonFile.cs:8`). Isso protege a
  substituição do arquivo, mas não o texto ainda não persistido.
- `QuickNotesViewModel` usa debounce de 450 ms (`src/Island.App/ViewModels/QuickNotesViewModel.cs:59`),
  faz flush ao fechar normalmente e tem estado de erro; ele atualiza a lista a partir das próprias
  operações e não se inscreve em `QuickNotesService.Changed`.
- `settings.json` já existe e guarda preferências do app; `storage.json` ainda não existe. O plano
  não vai misturar manifesto de armazenamento com estado de janela.
- `SingleInstanceGuard` já impede duas instâncias do app (`src/Island.Windows/Shell/OverlayWindowService.cs:75`,
  adquirido em `src/Island.App/App.xaml.cs:60`); ainda assim, o store valida revisão para não
  sobrescrever alteração externa.
- Game Notes é outro domínio: `GameNote` tem apenas texto, datas, fixação e conclusão; o livro é
  por jogo e `GameNotesService` trabalha com chave de jogo. Não é equivalente a `QuickNote`.
- Lembretes, OCR, toast, histórico de versões e diário de rascunho não existem.
- Calendar existe, mas `CalendarTask` tem `DateOnly DueDate` e conclusão; não tem hora,
  recorrência, snooze nem ocorrências.
- Ilha ociosa: `IdleContentResolver` mostra clima **ou** início agendado de Pomodoro; a largura é
  fixa em `IdleCompactWidth = 170` (`src/Island.App/Animations/IslandTransitions.cs:423`); os
  cliques e o drag são tratados em `IslandWindow.xaml.cs` (`OnIslandPressed/Released/Clicked`).
- `--soak` exercita principalmente mídia/volume simulados; `--smoke-test` verifica inicialização,
  mas não o fluxo criar → salvar → reiniciar → recuperar uma nota.

## Arquitetura alvo

```
src/Island.Core/Notes/
  QuickNote v2, NoteStatus, INotesStore, NotesSnapshot, SaveResult, NoteOperation,
  Reminder, ReminderOccurrence, Template, Revision, TagsCatalog,
  NoteSearchIndex, NaturalDateParser, NoteMigrator, DraftJournal

src/Island.Windows/Notes/
  FolderNotesStore, NotesBackupService, NotesImporterExporter,
  NotesDraftJournal, ToastNoteNotifier, NotesMaintenance

src/Island.Core/Idle/
  IdleContent composto (clima + Pomodoro agendado + nota fixada)

src/Island.App/
  IdlePresenter composto, QuickNotesEditorView (extração interna),
  CommandPalette, FloatingNoteWindow, TodayView, sidebar de tags
```

`QuickNotesService` continua sendo o dono das regras, do snapshot e do evento `Changed`. Tudo novo
(lembretes, busca, histórico, lixeira) consome o snapshot dele. Não existe adaptador de
sincronização nesta arquitetura.

## Contrato de armazenamento v2 (v0.5, decisão central)

O contrato atual não consegue distinguir criação, edição, remoção e lote; comparar snapshots para
descobrir isso seria caro e frágil. O novo contrato é orientado a operações:

```csharp
public interface INotesStore
{
    Task<NotesSnapshot> LoadAsync(CancellationToken ct = default);

    Task<SaveResult> UpsertAsync(
        QuickNote note,
        long expectedRevision,
        CancellationToken ct = default);

    Task<SaveResult> DeleteAsync(
        Guid id,
        long expectedRevision,
        CancellationToken ct = default);

    Task<SaveResult> ApplyBatchAsync(
        IReadOnlyList<NoteOperation> operations,
        CancellationToken ct = default);
}
```

- `expectedRevision` é 0 para nota nova e a revisão lida pelo editor para edição/remoção.
- `QuickNote.Revision` é local: incrementa a cada escrita bem-sucedida. `UpdatedAt` serve para
  exibição, nunca para decidir conflito.
- `SaveResult` distingue `Saved`, `Conflict(current)`, `Invalid` e `IoError`; o chamador não pode
  tratar conflito como sucesso.
- `ApplyBatchAsync` valida todas as revisões antes de escrever. Se alguma divergir, não escreve
  nada. Se a falha acontecer no meio, o diário de operações permite concluir ou reverter de forma
  idempotente.
- Excluir ou arquivar pela UI é `Upsert` com `Status.Trash`/`Status.Archived`; `DeleteAsync` é
  exclusão definitiva, usada só no expurgo da lixeira ou em “Esvaziar agora”, sempre com backup.
- `LoadAsync` devolve notas, revisão do store e avisos de quarentena; arquivo inválido de uma nota
  não derruba as demais.
- Não há `deviceId`, tombstones de sincronização nem resolução por “último arquivo vence”. Esses
  conceitos só fariam sentido com sincronização, que está fora do escopo.
- `QuickNotesService` passa a chamar `Upsert/Delete/ApplyBatch`; a interface antiga
  `SaveAsync(coleção)` é removida depois da migração.

## Armazenamento em disco, migração e recuperação

### Layout

```
%LocalAppData%\DynamicIsland\
  notes\<id>.json                         documento da nota (schema 2)
  notes.staging\                          transitório durante a migração
  drafts\<id>.draft.json                  rascunho ainda não confirmado
  attachments\<noteId>\<attachmentId>.<ext>
  revisions\<noteId>\<revision>.json.gz
  journal\pending.json                    diário de operações em lote
  reminders-state.json                    estado de entrega das ocorrências
  backups\notes-AAAAMMDD-HHMMSS.zip
  quarantine\<id>-<timestamp>.json        arquivo inválido isolado
  tags.json                               catálogo (cor, ordem; contagem deriva)
  templates.json
  notes.json                              legado v1 (só durante a migração)
  notes.v1.json                           notes.json antigo, preservado
  storage.json                            manifesto do armazenamento
  migration.json                          estado transitório da migração
  settings.json                           preferências do app (existente)
```

- `storage.json` contém apenas o manifesto (`schemaVersion`, `createdAt`, formato). Não guarda
  tamanho/posição de janela, coleção selecionada nem busca.
- `settings.json` continua sendo o dono das preferências de interface. Se o volume crescer, o
  estado da janela de notas pode ir para um `notes-ui.json` separado; nunca para `storage.json`.
- O diretório é fixo. Não existe `NotesDataDirectory` nem configuração para pasta sincronizada.
- `notes.json` deixa de ser reescrito após a migração. Cada save grava somente o arquivo da nota
  alterada.

### Regras do `FolderNotesStore`

- Carrega o diretório, valida cada arquivo e isola um arquivo inválido em `quarantine/`, sem
  perder as outras notas.
- Se encontrar dois arquivos com o mesmo Id, carrega o primeiro e quarentena o segundo, com aviso.
- Rejeita schema de nota maior que o suportado em modo somente leitura; não faz downgrade nem
  sobrescreve dado de versão mais nova.
- Ignora campos desconhecidos ao ler, mas preserva os campos conhecidos no roundtrip.
- Grava com `AtomicJsonFile` (temporário + flush + `File.Replace`), uma nota por vez.
- Serializa leitura + verificação de revisão + escrita com um gate assíncrono; se o arquivo
  alvo mudou entre a leitura e o replace, retorna `Conflict` em vez de sobrescrever.
- Em `ApplyBatchAsync`, grava primeiro um diário com as operações e as revisões esperadas; cada
  operação é idempotente; ao terminar, marca o diário como concluído.
- Na inicialização, um diário incompleto é reproduzido. Se uma revisão já avançou, a reprodução
  para e reporta quais operações foram aplicadas.

### Migração v1 → v2 (máquina de estados)

A migração **não** promete “nunca perder nada” por mágica; ela garante que exista sempre uma fonte
íntegra e que o estado interrompido seja reconhecido.

| Estado | O que significa | Fonte oficial na inicialização |
| --- | --- | --- |
| `Prepared` | backup criado e validado; hash/Ids da v1 registrados | v1 (`notes.json`) |
| `V2Staged` | todos os arquivos v2 escritos em `notes.staging/` e validados | v1 |
| `V2Published` | `notes.staging/` renomeado para `notes/` | v1, até o commit |
| `Validated` | v2 relido e comparado campo a campo com a v1 | v1, até o commit |
| `Committed` | `notes.json` renomeado para `notes.v1.json`; v2 assume | v2 (`notes/`) |
| `Failed` | migração abortada; v1 intacta | v1 |

Regras de recuperação:

- Antes de `Committed`, a v1 é a fonte oficial. Staging parcial é descartado e a migração pode ser
  repetida.
- O commit efetivo é o estado `Committed` em `migration.json`. Se o processo cair entre a
  validação e o rename, a inicialização conclui o rename; se cair depois do rename e antes de
  gravar `Committed`, reconhece o commit por `notes.v1.json` + v2 validado contra o hash
  registrado em `migration.json`. Um `notes.v1.json` órfão, sem estado de migração, não é tratado
  como commit.
- v2 inválido nunca é promovido. A v1 íntegra (`notes.json` ou `notes.v1.json`) continua sendo a
  fonte; o backup pré-migração permanece.
- `notes.v1.json` não é apagado automaticamente. O usuário pode removê-lo manualmente depois de
  confirmar que a v2 está boa.
- Um app mais antigo que o schema encontrado não escreve nada: abre em modo somente leitura e
  pede atualização.

### Validação da migração

- Comparar o conjunto exato de Ids, não apenas a contagem.
- Comparar título, conteúdo, checklist, tags, cor, fixação, arquivamento, datas e demais campos.
- Calcular um hash canônico por nota e um hash agregado; comparar antes e depois.
- Rejeitar Ids duplicados, campos obrigatórios ausentes e JSON inválido.
- Fazer o teste com um `notes.json` real de fixture, com 100 notas, acentos, emojis, textos
  longos, checklist e notas na lixeira.
- Injetar falha em cada estado da máquina e verificar a fonte escolhida na reinicialização.

### Evolução do schema

- A v2 base adiciona apenas `Revision` e `Status` (Active/Archived/Trash), mantendo leitura do
  legado.
- `Reminder` entra na v0.7; `Attachments` na v0.8; `ContentFormat` na v0.9. Campos novos são
  opcionais, com default, e só entram quando a funcionalidade existe de verdade.
- Cada campo novo tem teste de roundtrip e de leitura de um v2 antigo sem o campo.
- `ReminderAt`, `Attachments` e `Source` não entram “preventivamente” sem contrato e teste.

## Rascunho, autosave e mudanças externas

- O autosave da nota continua com debounce de 450 ms, mas o texto é protegido antes disso por um
  diário de rascunho.
- `NotesDraftJournal` grava `drafts/<id>.draft.json` com Id, revisão-base, título, conteúdo,
  `ContentFormat`, checklist legado, tags, cor, anexos/lembretes referenciados e horário. A
  gravação é atômica, com throttle curto (200–250 ms) e independente do histórico de versões.
- Depois de uma gravação bem-sucedida da nota, o diário correspondente é removido. Em erro de
  I/O ou disco cheio, o diário permanece e a UI mostra erro; nunca mostra “Salvo”.
- Na inicialização, se existe rascunho mais novo que a nota, a UI oferece “Recuperar rascunho” ou
  “Descartar”. O rascunho nunca é aplicado automaticamente por cima da nota.
- Ao fechar normalmente, o app faz flush da nota e limpa o diário. Em encerramento forçado, o
  diário é a recuperação.
- `QuickNotesViewModel` passa a se inscrever em `QuickNotesService.Changed` para atualizar a lista
  e as contagens, mas não substitui o texto do editor aberto. Em ativação da janela, ele compara a
  revisão do store:
  - sem rascunho pendente: recarrega a nota se a revisão mudou;
  - com rascunho pendente: mostra “Recarregar / Manter meu rascunho”, sem sobrescrever nada.
- O `SingleInstanceGuard` existente continua; a validação por `expectedRevision` é a segunda
  barreira, inclusive contra edição manual de arquivo ou importação.
- Histórico de versões não substitui o diário: um snapshot a cada dois minutos pode perder o que
  foi digitado depois do último snapshot.

## Lixeira, Undo e ações em lote

- `Status.Trash` + `DeletedAt` substituem o estado espalhado; a lixeira é local e mantém 30 dias.
- A exclusão definitiva só acontece no expurgo automático depois de 30 dias ou em “Esvaziar
  agora”, com confirmação e backup anterior.
- Undo imediato de 8 passos em memória para excluir/arquivar/limpar checklist. Restaurar da
  lixeira é uma ação durável separada; o histórico de versões (v0.9) é outra proteção.
- Toda ação em lote passa por `ApplyBatchAsync` e grava backup antes; se o diário falhar, nada é
  aplicado. A lixeira não usa tombstones de sincronização.

## Backup, restauração, importação e exportação

Backup e restauração entram na v0.5; exportação/importação de Markdown/ZIP entram na v1.0.

- `NotesBackupService` gera ZIP com notas, tags, templates, anexos, revisões relevantes e um
  `manifest.json` com contagem, tamanho e SHA-256 por arquivo.
- `settings.json` fica fora do backup (contém token/credenciais protegidas); o backup cobre dados
  de notas, anexos, tags, templates e revisões.
- Rotação: 7 diários + 4 semanais + backup obrigatório antes de migração, importação e ação em
  lote destrutiva. Um backup antigo só é apagado depois que o novo foi criado e validado.
- Restauração real: extrair para diretório temporário, validar manifest e hashes, criar backup
  pré-restauração e só então trocar os dados com diário. ZIP corrompido, truncado ou com arquivo
  faltando aborta sem tocar nos dados atuais.
- Testar restauração interrompida em cada etapa, não apenas o ZIP feliz.
- Exportação: ZIP v2 completo e nota única em Markdown. Importação: `.md`/`.txt` em lote com
  prévia antes de gravar.
- Importação de ZIP: rejeitar caminhos absolutos, `..`, links simbólicos/reparse points, entradas
  fora do diretório de destino, excesso de entradas e excesso de tamanho.
- Ids duplicados na importação são rejeitados por padrão; a opção explícita “criar cópias com
  novos Ids” é oferecida quando fizer sentido. Nunca sobrescrever nota existente sem confirmação.
- Exportação é local. Não há upload, compartilhamento, publicação nem observação da pasta de
  destino.

### Limites e disco cheio

- Texto por nota: 1 MiB.
- Anexo individual: 25 MiB; total de anexos por nota: 100 MiB.
- Texto, anexo ou importação acima do limite: rejeitar com mensagem explícita; nunca truncar ou
  ignorar em silêncio.
- Importação ZIP (v1.0): 250 MiB e 10.000 entradas.
- Imagens: limitar pixels e memória na decodificação, não apenas bytes.
- Backup: limite configurável e aviso antes de crescer demais.
- Disco cheio durante autosave: abortar a escrita, manter o arquivo anterior íntegro, manter o
  diário, mostrar erro e permitir tentar de novo. Nunca truncar a nota.
- Disco cheio durante backup/importação: abortar e remover temporários; não apagar o backup
  anterior.

## Ilha ociosa composta (v0.6)

Objetivo: a cápsula ociosa mostra `[clima] [Pomodoro agendado] [nota fixada]`, cada segmento com
o próprio clique; o resto da cápsula continua abrindo o shelf. Os casos atuais (clima sozinho,
horário sozinho) continuam funcionando.

- `IdleContent` passa a ser um registro composto com segmentos opcionais:
  `IdleContent.Capsule(Weather? weather, ScheduledPomodoro? scheduled, PinnedNote? note)`.
  `None` e os registros atuais continuam existindo para compatibilidade.
- `IdleContentResolver` ganha `PinnedNote` (nota `IsPinned` mais recentemente atualizada, com
  setting) e devolve os segmentos que couberem. No máximo uma nota fixada é exibida.
- `IdlePresenter` expõe por segmento `HasWeather/HasSchedule/HasNote`, texto, glifo, `NoteId`,
  `IdleWidth` e `Description` combinada. Clima e agenda continuam como hoje; a nota vem do
  `QuickNotesService.Changed`.
- Largura: `IslandShapeTable` recebe `idleWidth` calculado, não o bool `idleShown`. O presenter
  mede o texto, soma os segmentos e limita a `IdleCompactMaxWidth = 240` DIPs; a nota é truncada
  com “…” e nunca empurra os outros segmentos para fora. Em modo vertical (docked), mostra um
  segmento por vez, na mesma prioridade (agenda > nota > clima).
- Prioridade padrão: agenda > nota > clima. A ordem pode ser configurada; o clima é o primeiro a
  ceder, a nota é truncada em seguida e a agenda mantém a hora. Em DPI alto ou fonte de
  acessibilidade maior, medir o espaço real e esconder o segmento que não couber, em vez de
  cortar letras.
- Cliques: um controlador de interação centraliza os estados `Idle`, `Pressed`, `Holding`,
  `Dragging` e `ClickSuppressed`. O `Up` de um segmento chama `ResetPress()` antes de executar a
  ação, limpando hold timer, slop, captura de mouse, opacidade e flags de drag. O `Down` continua
  no controle da ilha, então o drag (apertar e arrastar) não é sequestrado pelo segmento. Se o
  `Up` do segmento for marcado como tratado, o controlador usa `PreviewMouseLeftButtonUp` ou
  `AddHandler(..., handledEventsToo: true)` para não perder o release.
- Ações:
  - nota → abre a nota exata (`QuickNotesHost.OpenNote(id, rect do segmento)`); se a nota sumiu,
    cai para `OpenNotes()`.
  - Pomodoro agendado → expande o shelf e navega ao widget (`ReopenCommand` +
    `ShelfLayer.JumpToWidget("pomodoro")`; fallback `PomodoroSessionWindow.ShowFor`).
  - clima → nada; clicar fora de um segmento com ação expande o shelf, como hoje.
  - clique direito → menu da ilha, comportamento atual.
- Settings novas: `IdleShowPinnedNote` (default true), `IdleHideNoteText` (modo discreto,
  default false) e `IdleSegmentOrder` (default `agenda,nota,clima`, validado ao carregar).
  `IdleNoteWhilePomodoroRunning` fica fora da v0.6; se voltar, exige largura ~230 e teste de
  legibilidade.
- Privacidade: com `IdleHideNoteText` ligado, mostrar “Nota fixada” em vez do texto. Esconder o
  texto também quando a sessão do Windows estiver bloqueada e, em melhor esforço, em sessão
  remota. Não prometer detecção de toda captura de tela; o modo discreto é a garantia principal.
- Acessibilidade: `AutomationProperties.Name` por segmento (“Nota fixada: …”, “Pomodoro começa às
  …”, “Clima: …”), contraste e foco visível.
- Nada de exibir nota durante Pomodoro em execução nesta fase.

## Tempo: lembretes e notificações (v0.7)

### Modelo e identidade de ocorrência

- `Reminder` na nota: Id, NoteId, `NextOccurrenceUtc` + `TimeZoneId` e hora local âncora,
  recorrência (nenhuma/diária/dias úteis/semanal/mensal), início, snooze, habilitado e última
  ocorrência entregue.
- Cada ocorrência tem identidade persistida (`reminderId` + instante/local da ocorrência). Isso
  impede disparo duplicado depois de reiniciar, suspender ou mudar o relógio.
- Estado de entrega persistido em `reminders-state.json` (separado da nota), com escrita atômica
  e inclusão no backup. A nota não é reescrita só porque um lembrete disparou.
- `ReminderScheduler` no Core usa `TimeProvider`; não lê o relógio do sistema diretamente e é
  testável com `FakeTimeProvider`.

### Regras de recorrência

- Mensal no dia 31: usa o último dia do mês quando o dia não existe (fevereiro, abril etc.).
- Horário inexistente por DST: dispara no primeiro horário válido seguinte.
- Horário ambíguo por DST: escolhe o offset padrão e registra a decisão.
- Ao acordar depois de dias desligado: não disparar uma rajada de toasts; agrupar em um aviso
  “enquanto você estava fora” com contagem e abrir a visão Hoje.
- Editar a recorrência cancela ocorrências futuras antigas e agenda as novas; a identidade da
  ocorrência evita duplicata.
- Snooze 10 min/1 h/amanhã cria uma ocorrência derivada, não reescreve a regra original.

### Entrega

- Fazer uma prova de conceito com `Microsoft.Windows.AppNotifications.AppNotificationManager` no
  build publicado sem MSIX. Se o registro COM não funcionar no ZIP, usar o notice da ilha como
  fallback e não bloquear a fase.
- Notificação agendada com app fechado é desejável, mas o Windows pode descartá-la se o PC ficar
  desligado mais de cinco minutos após o horário. Por isso, a reconciliação na inicialização é
  obrigatória.
- Notice da ilha sempre; toast do Windows quando disponível; som opcional.
- Durante foco Pomodoro, adiar para a próxima pausa se a setting estiver ligada.

### Hoje e Calendar

- Visão Hoje: vencidos, hoje, próximos 7 dias e feitos; snooze e conclusão.
- Criar `AgendaItem` como representação unificada da agenda, referenciando a nota/lembrete por
  `SourceKind` + `SourceId`, sem copiar o conteúdo para a base do Calendar.
- `CalendarTask` continua com `DateOnly` para tarefas existentes. A agenda de notas é uma camada
  separada e somente leitura no Calendar; nada de escrita bidirecional no Calendar.
- Não há envio para celular, FCM ou notificação remota.

## Captura, anexos e editor (v0.8)

Ordem interna: primeiro extrair o editor de Quick Notes do XAML atual; depois parser/templates;
depois anexos e File Drop; por último multi-seleção. A lista abaixo descreve o escopo, não a ordem
de implementação.

1. Captura inteligente no QuickCapture com parser no Core e testes: `#tag`, `- item`/`- [ ]`,
   `!` para fixar, `@amanhã 15h`, `@sexta`, `@em 2 dias` via `NaturalDateParser` (pt-BR, sem
   dependência).
2. `@sexta` significa a próxima sexta; se hoje for sexta, hoje conta como próxima. Documentar e
   testar a regra.
3. Preservar texto literal: `!`, `#` e `@` só viram comandos em posições definidas; oferecer
   escape (`\!`, `\#`, `\@`) para quem quer o caractere literal. Nada de transformar código ou
   texto comum silenciosamente.
4. Datas: guardar o instante UTC e o fuso/regra de recorrência separadamente; nunca inferir fuso
   na leitura.
5. Chips de prévia antes de salvar. Enter salva, Ctrl+Enter abre o app, Esc cancela.
6. “Salvar clipboard como nota” reaproveita o `ClipboardHistory` existente.
7. Anexos: imagens/arquivos colados ou arrastados no editor e no capture; nome interno
   `<attachmentId>.<ext>`, nome original apenas como metadado de exibição; cópia para
   `attachments\<noteId>\`; miniaturas, abrir/revelar, limite por tamanho.
8. Preparar o anexo em temporário, mover com segurança e só então referenciar na nota. Se a nota
   falhar, o anexo órfão é coletado; se o anexo faltar, a UI mostra placeholder e a nota continua
   abrindo.
9. Arrastar do Explorer: validar arquivo regular, tamanho e permissão; rejeitar links
   simbólicos/reparse points; copiar, nunca executar ou abrir automaticamente.
10. Arrastar imagens/arquivos para a ilha cria nota com anexos (ação nova no menu de setores do
    File Drop; o File Tray continua intocado).
11. Templates: 5 embutidos (em branco, reunião, compras, nota do dia, projeto) + templates do
    usuário em `templates.json`; variáveis `{{data}}`, `{{hora}}`, `{{titulo}}` substituídas como
    texto, sem avaliar expressões.
12. Sidebar de coleções/tags: Ativas, Hoje, Fixadas, Arquivo, Lixeira, Tags.
13. Multi-seleção na lista (Ctrl/Shift) com `ApplyBatchAsync`; só entra depois que o contrato de
    lote e o diário estiverem testados.
14. Editor: refatoração interna do editor de Quick Notes para reduzir o XAML gigante. **Não** criar
    um editor compartilhado com Game Notes e **não** unificar os modelos.
15. OCR fica fora da v0.8. `Windows.Media.Ocr` não será assumido no ZIP sem POC com identidade de
    pacote. Se OCR voltar depois, usar Tesseract offline, com benchmark de tamanho de distribuição,
    memória, latência e qualidade em pt-BR; nada de nuvem. Executar fora da thread WPF, com
    cancelamento e limites de pixels/memória.

Aceite: captura Ctrl+Alt+N até nota salva ≤ 2 s (p95 no hardware-alvo, com anexos medidos
separadamente); parser e checklist idênticos aos criados à mão; anexo ou arquivo importado
malicioso rejeitado; nenhuma mudança de comportamento em Game Notes. OCR, se um dia entrar, é
medido à parte.

## Organização, Markdown e busca (v0.9)

### Markdown e checklist com fonte única

- `ContentFormat` existe só para a transição:
  - `PlainText`: legado somente leitura. O texto é exibido literalmente; o `Checklist` legado é
    lido, mas não escrito de novo.
  - `Markdown`: notas novas e notas legadas depois da conversão. O checklist vive no próprio
    Markdown (`- [ ]`/`- [x]`) e a visão interativa é derivada do documento.
- Ao editar uma nota PlainText, o app oferece a conversão para Markdown com prévia; a partir daí,
  uma única fonte. Nunca manter `Content` Markdown e `Checklist` estruturado como duas fontes para
  a mesma nota.
- Itens de checklist legados viram linhas `- [ ]`/`- [x]`; os IDs só são preservados se houver
  referência a eles (histórico/undo). Caso contrário, texto e estado são a fonte.
- Conversão de PlainText para Markdown é explícita, com prévia, escape de `#`, `-`, `>` etc. para
  preservar a aparência original, e possibilidade de desfazer. Uma nota que começava com `#` não
  vira título por acidente.
- Parser: avaliar Markdig (CommonMark, árvore sintática e posições de origem) em vez de escrever
  um parser completo. A renderização em WPF usa um subconjunto seguro.
- Segurança do renderizador: HTML bruto é escapado/ignorado; nada de script, `javascript:`,
  `file://` ou imagens externas baixadas automaticamente. Links só abrem com clique explícito e
  esquema allowlist; imagens só de `attachments\`.
- Histórico: snapshot se o conteúdo mudou e passaram ≥ 2 min, até 50 por nota/30 dias; manter
  também revisões marcadas, snapshots antes de importação/migração e um limite por espaço.
  Restaurar cria uma nova revisão; nunca destrói o histórico. Histórico ≠ diário de rascunho.
- Busca: índice em memória (`NoteSearchIndex`), normalização sem acento, por palavra e prefixo,
  fallback de substring; operadores `tag:`, `is:fixada`, `tem:lembrete`, `tem:checklist`,
  `antes:`/`depois:`, frase entre aspas; destaque do trecho; ordenação.
- Índice incremental: atualizar por Id + revisão, remover ao excluir/arquivar, reconstruir após
  importação/migração/recarga externa. Benchmark contra SQLite FTS5 só se o alvo estourar.
- Tags de verdade: autocomplete, catálogo com cor/ordem, renomear/mesclar/excluir com atualização
  em lote via `ApplyBatchAsync`, contagem derivada.
- Links internos por Id imutável (`[[note:<guid>|texto]]`); o texto é só exibição e é atualizado
  quando o título muda. Backlinks e “notas relacionadas” por tags + links; sem wiki completa.
- Nota flutuante always-on-top com opacidade e geometria salvas; salva como nota normal.
- Command palette Ctrl+K para notas, tags e ações; atalhos configuráveis.
- SpellCheck do WPF com toggle e aviso quando o pack pt-BR não existir.

Aceite: nota antiga mantém aparência; checklist não duplica; busca p95 < 80 ms com 5k notas;
restaurar histórico não perde a versão atual; links sobrevivem a renomeação.

## v1.0 — Portabilidade local

1. Backup e restauração completos, com teste de restauração interrompida e de backup corrompido.
2. Exportação local em Markdown/ZIP e importação com prévia; sem PDF, site estático, QR ou envio
   ao celular.
3. Desempenho: abertura, autosave e busca sem bloquear a UI; 5k notas; memória estável em soak.
4. Acessibilidade: nomes, contraste, foco, escalonamento de DPI e fonte maior.
5. Instalação estável pelo fluxo de release atual (`release.ps1`, smoke test), sem o subsistema de
   notas participar de chamadas de rede.
6. Formato v2 documentado e congelado; qualquer migração futura segue a mesma máquina de estados.

Fora da v1.0: sync entre PCs, OneDrive/Dropbox, FCM/celular, QR, compartilhamento, Windows Hello,
IA local, PDF, site estático e criptografia de exportação. Se algum desses voltar, entra como
projeto separado, com escopo, ameaças e testes próprios.

## Fases e releases (resumo)

| Versão | Entrega principal | Fica fora desta versão |
| --- | --- | --- |
| v0.5 — Dados seguros | contrato por operação, migração, backup/restore, diário de rascunho, lixeira, Undo, limites | multi-seleção, editor novo, lembretes, import/export |
| v0.6 — Ilha composta | nota fixada, segmentos, controlador de clique/drag, largura medida, privacidade, acessibilidade | nota durante Pomodoro |
| v0.7 — Tempo | lembretes com ocorrências, scheduler, notificações, Hoje, AgendaItem | escrita no Calendar, celular |
| v0.8 — Captura | parser, templates, anexos seguros, editor interno de Quick Notes, multi-seleção, sidebar | OCR, editor compartilhado |
| v0.9 — Organização | Markdown seguro, checklist com fonte única, tags, busca indexada, histórico, links por Id, nota flutuante | wiki, IA |
| v1.0 — Portabilidade local | backup/restore completo, export/import, desempenho, acessibilidade, instalação | sync, compartilhamento, FCM, QR, Hello, PDF/site |

Critérios de aceite por fase:

- v0.5: migração interrompida em cada estado não perde nota; kill durante digitação recupera
  rascunho; um arquivo corrompido não derruba os outros; restore de backup corrompido aborta sem
  tocar nos dados; disco cheio mantém o arquivo anterior e o rascunho.
- v0.6: clique na nota abre a nota exata; drag continua funcionando; clique direito continua
  abrindo o menu; 100/150/200% de DPI; modo discreto esconde o texto.
- v0.7: lembrete dispara com app ocioso; não duplica após reiniciar; ocorrências perdidas são
  agrupadas; mensal/dia 31 e DST têm teste; toast funciona no build publicado ou cai no notice.
- v0.8: captura ≤ 2 s p95; parser respeita escape; anexo ausente não quebra a nota; anexo ou
  arquivo importado malicioso é rejeitado; Game Notes não muda.
- v0.9: Markdown não reinterpreta nota legada; checklist tem uma fonte; busca p95; histórico
  restaura sem destruir; links por Id sobrevivem a rename.
- v1.0: 5k notas com autosave e busca simultâneos; restore roundtrip; memória estável; nenhuma
  chamada de rede no subsistema de notas.

## Ordem técnica (dependências entre fases)

1. v0.5: contrato `INotesStore` + `FolderNotesStore` + migração + diário + backup/restore.
2. v0.6: ilha composta (depende só de `OpenNote`, que já existe, e do controlador de clique).
3. v0.7: lembretes/ocorrências + notificações + Hoje + `AgendaItem`.
4. v0.8: captura + anexos + refatoração interna do editor + multi-seleção.
5. v0.9: Markdown/checklist com fonte única + tags + busca + histórico + links por Id.
6. v1.0: portabilidade local, desempenho e acabamento.

Não há dependência de sincronização entre PCs em nenhuma etapa. O armazenamento por arquivo
existe para escrita incremental, isolamento de corrupção e backup, não para sync.

## Riscos e armadilhas

- Migração v1 → v2 é o ponto de maior risco: testar cada estado, comparar conteúdo e nunca
  promover v2 inválido. `notes.v1.json` é preservado.
- Backup só é útil se a restauração for testada; ZIP gerado não é prova de recuperação.
- Autosave atômico não salva o texto ainda não persistido; sem diário de rascunho, encerramento
  forçado perde a última digitação.
- `expectedRevision` é obrigatório: sem ele, uma edição externa ou importação pode ser
  sobrescrita silenciosamente.
- `IslandSettings.Equals/GetHashCode` são mantidos à mão: toda setting nova precisa entrar nos
  dois, senão a ilha não reage à mudança.
- `ServiceRegistration` esquecido = release quebrada (v0.4.12); o smoke do `release.ps1` pega.
- Notificações: fazer POC com `AppNotificationManager` no build publicado; não assumir AUMID
  manual. Toast agendado pode ser descartado com o PC desligado; reconciliar na inicialização.
- `Windows.Media.Ocr` não é confiável no ZIP sem identidade de pacote; não planejar essa API sem
  POC. Tesseract offline adiciona binários nativos e modelos; medir antes.
- Clique da ilha: tratar `Up` no segmento sem limpar hold/captura/opacidade deixa estado sujo.
  Centralizar no controlador e testar clique, hold, drag e release.
- Largura 240 DIPs é pequena para três segmentos com DPI/acessibilidade; medir, truncar e ter
  prioridade, em vez de cortar letras.
- Markdown + checklist em campos separados criam duas fontes de verdade; usar `ContentFormat` e
  uma fonte por nota.
- Texto legado não é Markdown por garantia; converter com escape e prévia, não reinterpretar.
- Busca por substring em 5k notas pode custar CPU; indexar e medir p95, não só a consulta.
- Disco cheio e arquivo bloqueado precisam de caminho de erro explícito; nunca mostrar “Salvo”
  antes do flush.
- Não colocar dados em pasta sincronizada por padrão; o diretório é fixo em `%LocalAppData%`.
- Nada de segredo no repositório; o subsistema de notas não usa credenciais.

## Testes por fase

### Unidade

- Contrato do store: create/update/delete, `expectedRevision`, conflito, lote com journal,
  replay idempotente, dois writers simulados.
- Migração: v1 real, v1 corrompido, v2 já migrado, reexecução, falha em cada estado, hash/conteúdo,
  Ids duplicados.
- Backup/restore: roundtrip, ZIP truncado, hash errado, arquivo faltando, restauração interrompida.
- Diário de rascunho: kill durante digitação, rascunho mais novo que a nota, rascunho descartado,
  nota alterada externamente.
- Parser de captura: `#tag`, checklist, `!`, `@sexta`, `@amanhã 15h`, escape, texto literal.
- Lembretes: ocorrência única, duplicata após restart, mensal dia 31, DST, relógio alterado,
  suspensão longa, snooze.
- Busca: sem acento, prefixo, substring, operadores, 5k notas, atualização incremental,
  remoção/recarga.
- Markdown/checklist: PlainText preservado, conversão com escape, roundtrip, links por Id,
  renomeação, HTML/script bloqueado.
- Anexos/importação: nome interno, path traversal, symlink, tamanho, anexo ausente, disco cheio.
- Arquitetura: `Island.Core/Notes` e `Island.Windows/Notes` não referenciam rede.

### Integração e smoke

- `--demo` cobre clique nos segmentos da ilha (nota e Pomodoro agendado), drag e clique direito.
- `--soak` precisa exercitar autosave + lembretes + busca, não só mídia/volume; verificar memória
  estável.
- Criar um `--notes-smoke` com diretório temporário isolado: criar nota, salvar, encerrar à força,
  reiniciar e recuperar o rascunho. O `release.ps1` pode rodar esse modo antes de publicar.
- `--smoke-test` atual continua validando a inicialização; o novo modo cobre o fluxo de notas.

### Checklist prioritário antes de cada release

1. Migração interrompida em diferentes etapas.
2. Backup corrompido ou incompleto.
3. Disco cheio durante autosave.
4. Encerramento forçado durante digitação.
5. Dois processos gravando a mesma nota (store com duas instâncias).
6. Notas duplicadas com o mesmo Id.
7. Anexo ausente ou com conteúdo inválido.
8. Importação ZIP com tentativa de path traversal.
9. Lembretes após suspensão e mudança de relógio.
10. Clique, drag e release nos segmentos da ilha.
11. 5.000 notas com busca e autosave simultâneos.
12. Alteração externa enquanto o editor está aberto.
13. Restauração de backup após interrupção.
14. Configuração de UI separada do manifesto de armazenamento.
15. Nenhuma chamada de rede no código de notas.

### Metas de desempenho

- Busca p95 < 80 ms com 5.000 notas.
- Nenhuma leitura de arquivo bloqueando a thread da UI de forma perceptível.
- Memória estável em soak prolongado com autosave e lembretes.
- Captura Ctrl+Alt+N até nota salva ≤ 2 s p95 no hardware-alvo.
- Abertura de nota e autosave medidos separadamente de anexos (e de OCR, se ele existir).

## Decisões fora de escopo (explícitas)

- Sincronização entre computadores, OneDrive, Dropbox, pasta compartilhada ou servidor local.
- Compartilhamento, QR, links públicos, colaboração, contas e login.
- FCM, celular, app Android e notificações remotas.
- Windows Hello, bloqueio por PIN e criptografia em repouso.
- IA local, resumos, classificação automática e assistentes.
- PDF, site estático, impressão e criptografia de exportação.
- Editor ou modelo compartilhado entre Quick Notes e Game Notes.

Se alguma dessas ideias voltar, ela entra como projeto separado, com escopo próprio, análise de
ameaças e testes de restauração — não como item escondido no meio de uma fase de notas.
