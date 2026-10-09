# Agenda do calendário — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Acrescentar criação e consulta local de eventos, aniversários e tarefas ao calendário, com um símbolo persistente na Dynamic Island enquanto houver tarefa não concluída.

**Architecture:** `Island.Core` define os itens, a agenda e o contrato de armazenamento. `Island.Windows` persiste a agenda num JSON separado. `Island.App` conecta o serviço ao widget, às janelas de agenda e às cápsulas compactas horizontal e vertical.

**Tech Stack:** C# / .NET 10, WPF, `System.Text.Json`, CommunityToolkit.Mvvm já existente.

**Spec:** [2026-10-09-calendar-agenda-design.md](../specs/2026-10-09-calendar-agenda-design.md)

## Global Constraints

- Não adicionar dependências externas.
- Manter `CalendarWidget` com 280 × 152 DIPs e a geometria fixa das outras cartas.
- Manter tarefas, eventos e aniversários fora de `settings.json`, em `%LocalAppData%\DynamicIsland\calendar.json`.
- A cápsula Mini continua vazia; o símbolo aparece nos modos compactos horizontal e vertical.
- O símbolo indica qualquer tarefa incompleta, mesmo futura; não mostrar contador.
- Aniversários de 29 de fevereiro só aparecem em anos bissextos; outras recorrências não deslocam a data.
- Respeitar `ReduceAnimations` e `SystemParameters.ClientAreaAnimation`; estado compacto continua sem animação própria nos glifos.
- Não criar nem executar testes ou build nesta execução, pois isso não foi solicitado.
- Preservar o arquivo não rastreado `docs/superpowers/specs/2026-10-09-notas-rapidas-design.md`, fora do escopo.

## Review Focus

- 29 de fevereiro: conferir que a recorrência aparece apenas em anos bissextos.
- Dia com eventos de dia inteiro e com horário: conferir que todos aparecem e que eventos com horário ficam em ordem crescente.
- Tarefa futura incompleta: conferir que mantém o símbolo até ser concluída.
- Falha ao gravar: conferir que a agenda não anuncia nem publica a mudança como salva.
- Pomodoro e equalizador simultâneos ao símbolo, em ambas as orientações e com movimento reduzido: conferir reserva de espaço e ausência de sobreposição.

---

### Task 1: Modelos e regras da agenda no Core

**Files:**
- Create: `src/Island.Core/Calendar/CalendarTask.cs`
- Create: `src/Island.Core/Calendar/CalendarEvent.cs`
- Create: `src/Island.Core/Calendar/CalendarBirthday.cs`
- Create: `src/Island.Core/Calendar/CalendarData.cs`
- Create: `src/Island.Core/Calendar/CalendarDayItems.cs`
- Create: `src/Island.Core/Calendar/CalendarEntryKind.cs`
- Create: `src/Island.Core/Calendar/CalendarAgenda.cs`
- Create: `src/Island.Core/Abstractions/ICalendarStore.cs`

**Interfaces:**
- `CalendarTask(Guid Id, string Title, DateOnly DueDate, bool IsCompleted)`
- `CalendarEvent(Guid Id, string Title, DateOnly Date, TimeOnly? Time)`
- `CalendarBirthday(Guid Id, string Name, int Month, int Day)`
- `CalendarData` contém listas tipadas de tarefas, eventos e aniversários e um estado vazio padrão.
- `CalendarDayItems` contém as três listas já filtradas para uma data local.
- `ICalendarStore.Load() -> CalendarData` e `ICalendarStore.Save(CalendarData data) -> void`.
- `CalendarAgenda(ICalendarStore store)` expõe `event Action? Changed`, `bool HasPendingTasks`, `CalendarDayItems GetDay(DateOnly date)`, `IReadOnlySet<DateOnly> GetMarkedDates(int year, int month)`, `AddEvent(string title, DateOnly date, TimeOnly? time) -> CalendarEvent`, `AddBirthday(string name, int month, int day) -> CalendarBirthday`, `AddTask(string title, DateOnly dueDate) -> CalendarTask` e `SetTaskCompleted(Guid id, bool isCompleted) -> bool`.

- [x] **Step 1: Criar os modelos tipados e o snapshot `CalendarData`**, com `Guid` estável por item e listas não nulas.
- [x] **Step 2: Criar o contrato `ICalendarStore`** no namespace de abstrações do Core.
- [x] **Step 3: Implementar `CalendarAgenda`** para validação de nomes, filtro por data, aniversários anuais, marcadores mensais e ordenação: tarefas incompletas antes das concluídas; eventos de dia inteiro antes dos eventos por horário; aniversários por nome.
- [x] **Step 4: Publicar mudanças só após persistência bem-sucedida**: criar ou concluir constrói o novo `CalendarData`, chama `Save`, troca o snapshot atual e então emite `Changed`; falhas de `Save` propagam ao chamador sem alterar o snapshot.
- [x] **Step 5: Revisar as dependências do Core** e confirmar que nenhum modelo ou serviço referencia WPF ou `Island.Windows`.

### Task 2: Persistência local e composição

**Files:**
- Create: `src/Island.Windows/Calendar/JsonCalendarStore.cs`
- Modify: `src/Island.App/Composition/ServiceRegistration.cs`
- Modify: `src/Island.App/Widgets/ShelfContext.cs`

**Interfaces:**
- `JsonCalendarStore(string? directory = null)` usa `%LocalAppData%\DynamicIsland` por padrão e implementa `ICalendarStore`.
- `ShelfContext` expõe uma instância singleton `CalendarAgenda` por `CalendarAgenda Calendar`.
- O descritor `calendar` será atualizado na Task 3 para construir o widget com o contexto registrado.

- [x] **Step 1: Implementar carga JSON**: arquivo ausente retorna `CalendarData` vazio; JSON inválido é movido para `calendar.json.bad` antes de retornar agenda vazia; falhas de I/O que impedem preservar os dados propagam erro.
- [x] **Step 2: Implementar gravação atômica** em `calendar.json.tmp`, substituindo ou movendo o destino só após serialização e flush bem-sucedidos; remover temporário em `finally`.
- [x] **Step 3: Registrar `ICalendarStore` e `CalendarAgenda` como singletons** em `ServiceRegistration` sem alterar a carga ou o formato de `settings.json`.
- [x] **Step 4: Adicionar `CalendarAgenda` ao `ShelfContext`** para disponibilizar a instância compartilhada aos widgets.
- [x] **Step 5: Revisar os caminhos de arquivo e a composição** para confirmar que nenhum consumidor recebe uma instância paralela da agenda.

### Task 3: Interação mensal e agenda do dia

**Files:**
- Modify: `src/Island.App/Views/Widgets/CalendarWidget.xaml`
- Modify: `src/Island.App/Views/Widgets/CalendarWidget.xaml.cs`
- Create: `src/Island.App/Shell/CalendarDayWindow.xaml`
- Create: `src/Island.App/Shell/CalendarDayWindow.xaml.cs`
- Create: `src/Island.App/Shell/CalendarEntryWindow.xaml`
- Create: `src/Island.App/Shell/CalendarEntryWindow.xaml.cs`
- Modify: `src/Island.App/Widgets/WidgetCatalog.cs`

**Interfaces:**
- `CalendarWidget(ShelfContext context)` consulta `context.Calendar` e `context.Settings()`.
- `CalendarDayWindow.ShowFor(CalendarAgenda agenda, DateOnly date, bool reduceAnimations)` abre uma única janela para o dia escolhido.
- `CalendarEntryWindow.ShowFor(CalendarAgenda agenda, DateOnly date, CalendarEntryKind kind, bool reduceAnimations)` oferece um formulário de criação para o tipo e data escolhidos.

- [x] **Step 1: Renderizar cada célula de dia como controle acessível e clicável**, mantendo título, navegação, cores e medidas do widget; nome acessível inclui data completa e presença de itens.
- [x] **Step 2: Mostrar marcadores de dias com itens** usando `GetMarkedDates` e atualizar a grade quando `CalendarAgenda.Changed` ocorrer; remover a inscrição ao descarregar o controle.
- [x] **Step 3: Criar a janela da agenda diária** com seções de tarefas, aniversários e eventos; incluir tarefas concluídas, alternância de conclusão, estado vazio e ações para abrir cada tipo de formulário.
- [x] **Step 4: Criar a janela de entrada** com título/nome obrigatório; evento aceita horário `HH:mm` opcional (vazio significa dia inteiro), aniversário usa mês/dia da data selecionada e tarefa usa essa data como vencimento.
- [x] **Step 5: Tratar falhas de gravação na interface** mantendo o formulário aberto, exibindo que não foi salvo e deixando a agenda atual inalterada.
- [x] **Step 6: Animar navegação e janelas com fade e deslocamento curto**, derivando redução de movimento de `Settings.ReduceAnimations || !SystemParameters.ClientAreaAnimation`; evitar animações quando qualquer preferência estiver ativa.
- [x] **Step 7: Revisar fechamento das janelas e inscrições de eventos** para evitar listeners duplicados e referências retidas.

### Task 4: Símbolo persistente de tarefa na Dynamic Island

**Files:**
- Modify: `src/Island.App/ViewModels/IslandViewModel.cs`
- Modify: `src/Island.App/Animations/IslandTransitions.cs`
- Modify: `src/Island.App/Shell/IslandWindow.xaml.cs`
- Modify: `src/Island.App/Views/CompactView.xaml`
- Modify: `src/Island.App/Views/VerticalCompactView.xaml`
- Modify: `src/Island.App/Styles/Controls.xaml`

**Interfaces:**
- `IslandViewModel.HasPendingTasks` reflete `CalendarAgenda.HasPendingTasks`, é inicializado na construção e atualizado por `CalendarAgenda.Changed`; a inscrição é removida em `Dispose`.
- `IslandShapeTable.For(IslandMode mode, IslandSettings settings, bool pomodoroRunning, bool hasPendingTasks, IEnumerable<string> shelfIds, bool vertical = false)` reserva comprimento mínimo de 140 DIPs para o símbolo e preserva o mínimo de 150 DIPs do Pomodoro.

- [x] **Step 1: Conectar o estado da agenda ao `IslandViewModel`**, sem polling, e atualizar a propriedade após criação/conclusão bem-sucedida.
- [x] **Step 2: Redimensionar a forma compacta horizontal/vertical quando a propriedade muda**, usando `IslandShapeTable` e recalculando apenas em modo compacto; não alterar Mini nem outras formas.
- [x] **Step 3: Acrescentar um glifo estático de tarefa em `Controls.xaml`** com cor de destaque e nome acessível; mostrá-lo na cápsula compacta horizontal e vertical apenas quando `HasPendingTasks` for verdadeiro.
- [x] **Step 4: Agrupar o glifo com o equalizador** sem sobreposição; na cápsula vertical, empilhar os glifos na área inferior e manter o contador do Pomodoro na área superior.
- [x] **Step 5: Fazer revisão manual do fluxo de estado e layout** para os cinco cenários listados em `Review Focus`; não executar build ou testes neste plano.

## Revisão do plano

- Todos os requisitos da especificação estão cobertos por uma tarefa: dados e recorrência em Task 1; persistência em Task 2; criação, visualização e movimento em Task 3; indicador horizontal/vertical em Task 4.
- A interface de `CalendarAgenda` é a dependência compartilhada definida antes das tarefas de armazenamento, UI e ilha.
- Os casos de falha e as combinações visuais mais sensíveis estão listados uma vez em `Review Focus` e ligados à revisão das tarefas responsáveis.
- Este plano não inclui testes automatizados nem comandos de build, pois o usuário ainda não pediu teste ou verificação da implementação.

## Handoff de execução

Plano pronto para revisão. Recomendo execução nativa nesta conversa: as quatro tarefas compartilham interfaces em sequência e alteram camadas que precisam ser integradas no mesmo checkout. A execução por subagentes continua disponível se for sua preferência.
