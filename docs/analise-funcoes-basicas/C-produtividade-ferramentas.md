# Auditoria — Produtividade e ferramentas

Escopo: notas rápidas (captura, lixeira, arquivo, busca, etiquetas), calendário (eventos, tarefas, aniversários), orçamento de IA, tarefas/progresso da Suite, ferramentas da estante (calculadora, contador, timer/cronômetro, analisador de texto, sons de foco), File Tray/órbita de arquivos e histórico do clipboard.
Método: leitura dos arquivos de cada domínio e busca textual pelos termos que provariam a função (`Reminder`, `Export`, `Import`, `Undo`, `Recurring`, `ICS`, `Preset`, `History`, `SleepTimer`, `OCR`, `Pin`, `Backup`, `SpellCheck`...). Cada item cita `arquivo:linha` ou registra a ausência confirmada; docs/README só foram usados depois de conferir o código.
Observação: o README está desatualizado em `README.md:80` ("Não implementado: conversão de arquivos"), mas `src/Island.Core/Files` já executa 16 ações; esta análise segue o código.

## C01 — Lembretes em notas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** marcar data/hora (e recorrência/snooze) em uma nota e ser avisado mesmo com a ilha recolhida.
- **Hoje:** `QuickNote` guarda só título, conteúdo, checklist, tags, cor, fixação, arquivo e datas (`src/Island.Core/Notes/QuickNote.cs:20-31`); não há campo de lembrete, scheduler ou entrega, e o próprio roadmap diz "Lembretes, OCR, toast, histórico de versões e diário de rascunho não existem" (`docs/notes-roadmap.md:86`).
- **Por que é básico:** nota de captura só fecha o ciclo quando vira tarefa com hora; sem lembrete ela morre na lista.
- **Impacto:** o usuário mantém Notas/ToDo/Alexa externos e deixa de confiar no app para o dia a dia.
- **Mínimo viável:** um lembrete por nota (data/hora local, sem recorrência na primeira versão), notice da ilha + toast do Windows e estado entregue persistido.

## C02 — Anexos e imagens nas notas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** colar ou arrastar uma imagem/arquivo para a nota e vê-lo ao reabrir.
- **Hoje:** o record não tem coleção de anexos (`src/Island.Core/Notes/QuickNote.cs:20-31`), o conteúdo é um `TextBox` puro (`src/Island.App/Shell/QuickNotesWindow.xaml:730-735`) e nenhuma janela de notas aceita drop (`AllowDrop`/`Drop=` ausente em `QuickNotesWindow.xaml` e `QuickCaptureWindow.xaml`).
- **Por que é básico:** metade das ideias capturadas é print, foto de quadro ou PDF; sem colar, a nota fica incompleta.
- **Impacto:** o usuário volta para o Explorer/WhatsApp e a nota deixa de ser o lugar da informação.
- **Mínimo viável:** Ctrl+V/arrastar imagem e arquivo, cópia para pasta do app, miniatura, abrir/revelar e limites de tamanho.

## C03 — Exportar/importar notas e imprimir
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** exportar a nota atual em Markdown/TXT, exportar todas em ZIP, importar `.md`/`.txt` e imprimir.
- **Hoje:** os comandos do editor acabam em criar, selecionar, fixar, arquivar, lixeira, restaurar e checklist (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`); não há classe de exportação/importação em `src/Island.Core/Notes` nem `src/Island.Windows/Notes` (busca `Export|Import` vazia); o roadmap só promete isso para a v1.0 (`docs/notes-roadmap.md:306-313,507-511`).
- **Por que é básico:** dado local precisa sair do app (backup manual, outro editor, apresentação); sem isso, o app é uma gaiola.
- **Impacto:** migração e impressão impossíveis; o usuário duplica conteúdo em outro editor.
- **Mínimo viável:** exportar a nota selecionada (.md) e todas (.zip), importar .md/.txt com prévia e imprimir a nota.

## C04 — Backup, restauração e notes.json corrompido
- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** recuperar as notas de um backup e ser avisado quando o arquivo principal está ilegível.
- **Hoje:** todas as notas vivem em um único `notes.json` reescrito inteiro a cada edição (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:83-103`); se o JSON está inválido, o arquivo é movido para `notes.json.bad` e o app segue vazio, sem aviso na interface e sem caminho de restauração (`:66-75`); não há serviço de backup (`Backup|Restore` ausente em `src/Island.Windows/Notes`; plano em `docs/notes-roadmap.md:292-306`).
- **Por que é básico:** é o único dado insubstituível do domínio; qualquer editor pode salvar um JSON inválido por bug e o usuário não percebe a perda.
- **Impacto:** perda silenciosa de todas as notas; quebra de confiança no app como caderno.
- **Mínimo viável:** backup automático (diário + rotação) e tela "Restaurar backup", com aviso e botão "recuperar arquivo .bad" quando a leitura falhar.

## C05 — Diário de rascunho contra encerramento forçado
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** não perder o que digitou se o app for encerrado à força ou o PC cair.
- **Hoje:** o autosave só escreve após debounce de 450 ms (`src/Island.App/ViewModels/QuickNotesViewModel.cs:59,520-546`) e o flush existe apenas no fechamento normal (`src/Island.App/Shell/QuickNotesWindow.xaml.cs:286-315`); não há diário de rascunho (`docs/notes-roadmap.md:259-280` descreve o que é "planejado").
- **Por que é básico:** um editor de texto maduro assume crash; autosave com debounce não substitui uma cópia em disco da digitação pendente.
- **Impacto:** perda da digitação ainda não gravada (debounce + escrita pendente) em queda de energia/kill; erro de I/O mantém o texto só na memória do editor.
- **Mínimo viável:** gravar rascunho a cada ~250 ms, removê-lo após salvar e oferecer "Recuperar rascunho/Descartar" na abertura.

## C06 — Desfazer e histórico de versões
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** Ctrl+Z depois de excluir/arquivar e poder ver/restaurar uma versão anterior da nota.
- **Hoje:** a lixeira permite restaurar e o arquivo é reversível, mas não há pilha de undo nem histórico: os comandos destrutivos são chamados direto (`src/Island.App/ViewModels/QuickNotesViewModel.cs:401-474`), a tecla Delete manda para a lixeira sem confirmação (`src/Island.App/Shell/QuickNotesWindow.xaml.cs:189-195`) e `Undo|History` não aparece no domínio; o roadmap planeja 8 passos de undo e versões (`docs/notes-roadmap.md:282-290,488-490`).
- **Por que é básico:** toda ação em lista é passível de clique errado; o usuário espera Ctrl+Z imediato.
- **Impacto:** restauração dá trabalho (ir à lixeira) e "esvaziar lixeira" é irreversível; edições antigas não podem ser recuperadas.
- **Mínimo viável:** undo de 8 passos para excluir/arquivar/limpar checklist e histórico simples por nota antes de cada importação/migração.

## C07 — Lixeira sem expiração nem prazo visível
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que notas na lixeira sejam apagadas após um prazo (30 dias) e ver quanto tempo falta.
- **Hoje:** mover para a lixeira só grava `DeletedAt` (`src/Island.Core/Notes/QuickNotesService.cs:188-200`) e o único expurgo é o botão manual "Esvaziar lixeira" (`:152-160`); não há job, prazo ou aviso (plano em `docs/notes-roadmap.md:284-286`).
- **Por que é básico:** lixeira sem expiração vira depósito eterno de lixo e esvaziá-la manualmente é a única forma de liberar espaço.
- **Impacto:** lixeira cresce sem limite; "esvaziar" vira uma decisão arriscada em bloco.
- **Mínimo viável:** expurgo automático após 30 dias com backup, contagem de dias por item e texto "será apagada em X dias".

## C08 — Busca escopada, sem checklist e sem busca global
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** procurar em todas as notas, ignorando maiúsculas/acentos, incluindo itens de checklist, e buscar também no calendário.
- **Hoje:** `GetNotes(collection, query)` filtra apenas a aba atual e só título/conteúdo/tags, com `Contains(..., OrdinalIgnoreCase)` (sensível a acento) (`src/Island.Core/Notes/QuickNotesService.cs:41-71`, condição em `:62-68`); o checklist não é pesquisado e a janela chama com `SelectedCollection` (`src/Island.App/ViewModels/QuickNotesViewModel.cs:605-614`). Não há busca global no app (nenhum índice; `Palette`/`Ctrl+K` ausente).
- **Por que é básico:** em 100+ notas, busca é o principal meio de navegação; limitar à aba esconde justamente o que está arquivado.
- **Impacto:** o usuário esquece onde salvou a nota e não encontra itens de checklist; busca no calendário é impossível.
- **Mínimo viável:** busca em todas as coleções, sem acento, com destaque e inclusão do checklist; segundo passo, incluir calendário.

## C09 — Etiquetas sem gerenciamento nem filtro
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** autocomplete ao digitar, renomear/mesclar/excluir etiquetas e clicar em uma para filtrar.
- **Hoje:** a etiqueta é um `TextBox` de vírgulas no editor (`src/Island.App/Shell/QuickNotesWindow.xaml:809-830`; `src/Island.App/ViewModels/QuickNotesViewModel.cs:252-259`) e a lista só exibe o texto (`QuickNotesWindow.xaml:547-550`); não há catálogo, contagem, renomear/mesclar nem filtro (busca `TagsCatalog`/`ITagCatalog` em `src` vazia); só o texto da busca procura dentro das tags.
- **Por que é básico:** tags digitadas à mão viram sinônimos ("trabalho", "Trabalho", "trab") e não servem para organizar.
- **Impacto:** duplicidade de etiquetas e organização manual inútil; a lista por etiqueta não existe.
- **Mínimo viável:** catálogo derivado com autocomplete, renomear/mesclar com atualização em lote e clique na etiqueta para filtrar.

## C10 — Multi-seleção e ações em lote
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** Ctrl/Shift-clicar várias notas e arquivar, etiquetar ou mandar para a lixeira de uma vez.
- **Hoje:** a lista não define `SelectionMode` (fica o padrão de seleção única) (`src/Island.App/Shell/QuickNotesWindow.xaml:502-509`) e todos os comandos operam em `SelectedNote` (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`); o lote está no roadmap para depois do contrato de operações (`docs/notes-roadmap.md:452-453`).
- **Por que é básico:** limpar a lista depois de uma semana exige uma ação por nota; tarefas de organização são naturalmente em lote.
- **Impacto:** trabalho braçal e desistência de organizar; lixeira/arquivo crescem desordenados.
- **Mínimo viável:** seleção múltipla e barra com Arquivar/Lixeira/Etiqueta (com confirmação e undo).

## C11 — Duplicar nota
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** criar uma cópia da nota (checklist/tags/cor) para outro contexto.
- **Hoje:** não existe comando de duplicar no view model (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`) nem atalho na janela; a única criação é `BeginNewCommand`.
- **Por que é básico:** templates e checklists se repetem (mesma lista de compras, mesmo roteiro); copiar e colar manualmente é o contorno comum.
- **Impacto:** retrabalho para repetir checklists e notas recorrentes.
- **Mínimo viável:** "Duplicar" que cria nota com novo Id e título "cópia", mantendo o resto.

## C12 — Templates e captura inteligente
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** começar de um template (reunião, compras, nota do dia, projeto) e digitar `#tag`, `- [ ]` ou `@amanhã` na captura rápida.
- **Hoje:** a captura rápida só tem texto, cor e fixar — `SaveAsync` chama `CaptureAsync(text, color, pinned)` (`src/Island.App/Shell/QuickCaptureWindow.xaml.cs:240-269`; `src/Island.Core/Notes/QuickNotesService.cs:122-149`); não há templates embutidos nem parser (o roadmap descreve o parser e os 5 templates como v0.8, `docs/notes-roadmap.md:426-452`).
- **Por que é básico:** captura rápida sem sintaxe cria sempre nota crua; templates eliminam digitação repetitiva.
- **Impacto:** o usuário digita listas/tags manualmente depois; captura deixa de ser "uma linha".
- **Mínimo viável:** 5 templates fixos + parser de `#tag`, checklist e data relativa no capture, com chips de prévia.

## C13 — Nota fixada na ilha/recolhida
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** ver a nota fixada (ou a próxima) na cápsula recolhida e abri-la com um clique.
- **Hoje:** o conteúdo ocioso só conhece clima e Pomodoro agendado (`src/Island.Core/Idle/IdleContent.cs:11-20`; `src/Island.Core/Idle/IdleContentResolver.cs:38-49`), e o widget mostra apenas as 2 notas mais recentes (`src/Island.App/Views/Widgets/QuickNotesWidget.xaml.cs:24,72-76`); a nota fixada na ilha é plano v0.6 (`docs/notes-roadmap.md:329-373`).
- **Por que é básico:** a proposta do app é presença constante; a nota importante precisa estar visível sem abrir a estante.
- **Impacto:** a ilha não ajuda a manter a tarefa/nota do dia à vista; usuário cola post-it físico no monitor.
- **Mínimo viável:** exibir a nota fixada mais recente na cápsula (com truncamento e opção de esconder o texto) e abrir a nota no clique.

## C14 — Calendário sem editar nem excluir itens
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** corrigir título/hora e apagar um evento, tarefa ou aniversário criado errado.
- **Hoje:** `CalendarAgenda` expõe apenas `AddEvent`, `AddBirthday`, `AddTask` e `SetTaskCompleted` (`src/Island.Core/Calendar/CalendarAgenda.cs:88-137`); busca por `Delete|Remove|Edit|Update` em `src/Island.Core/Calendar` não retorna nada, e a janela do dia só adiciona e alterna conclusão (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:40-42,173-176,238-246`).
- **Por que é básico:** calendário sem edição/apagar garante erro permanente no primeiro typo; é a função mais básica de uma agenda.
- **Impacto:** agenda poluída por itens errados para sempre; usuário abandona a agenda do app.
- **Mínimo viável:** editar qualquer campo, excluir com confirmação e desfazer; mesmo sem recorrência.

## C15 — Evento sem hora de término/duração
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** marcar início e fim (ou duração) e ver "14:00–15:30" na agenda.
- **Hoje:** `CalendarEvent` é `(Id, Title, Date, Time?)` (`src/Island.Core/Calendar/CalendarEvent.cs:3`); a janela aceita um único HH:mm e avisa "Deixe vazio para dia inteiro" (`src/Island.App/Shell/CalendarEntryWindow.xaml:80-90`; `src/Island.App/Shell/CalendarEntryWindow.xaml.cs:118-129`).
- **Por que é básico:** reunião/consulta tem duração; sem fim não há noção de conflito, "ocupado até", nem sequência do dia.
- **Impacto:** a agenda não serve para planejar o dia; usuário mantém Google Agenda ao lado.
- **Mínimo viável:** fim opcional (ou duração) e exibição do intervalo na lista do dia.

## C16 — Sem recorrência de eventos/tarefas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** repetir diariamente, em dias úteis, semanalmente ou anualmente (e editar "esta/estas/todas").
- **Hoje:** nenhum campo de recorrência em evento, tarefa ou aniversário além do aniversário anual implícito (`src/Island.Core/Calendar/CalendarEvent.cs:3`, `CalendarTask.cs:3`, `CalendarBirthday.cs:3`); o roadmap confirma que `CalendarTask` "não tem hora, recorrência, snooze nem ocorrências" (`docs/notes-roadmap.md:87-88`).
- **Por que é básico:** compromissos fixos (aula, reunião semanal, remédio) são maioria; sem recorrência a agenda é só "feriado pessoal".
- **Impacto:** recriar o mesmo evento toda semana; usuário desiste da agenda do app.
- **Mínimo viável:** regras diária/semanal/mensal/anual com ocorrências derivadas e exclusão de uma ocorrência.

## C17 — Eventos sem lembrete/notificação
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso "evento em 10 min" mesmo com o app sem foco.
- **Hoje:** não há scheduler nem remetente de aviso no calendário (`Reminder|Notification` ausente em `src/Island.Core/Calendar` e `src/Island.Windows/Calendar`); existe o canal de `Notice` da ilha, mas nenhum fluxo do calendário o dispara, e não há toast do Windows (`AppNotification` ausente em `src`).
- **Por que é básico:** evento com hora só serve se avisar; sem isso a agenda não compete com a do Windows/telefone.
- **Impacto:** eventos marcados são esquecidos; o app vira registro passivo.
- **Mínimo viável:** antecedência configurável por evento, notice da ilha e toast/repetição opcional.

## C18 — Só visão mês + janela do dia
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** visão de semana, dia/agenda e "próximos 7 dias", com navegação e criação no contexto.
- **Hoje:** só existe a grade de mês (`src/Island.App/Views/Widgets/CalendarWidget.xaml.cs:149-228`) e a janela de uma data (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:102-176`); não há tela de semana/agenda nem lista de próximos (o "Hoje" unificado está no roadmap, `docs/notes-roadmap.md:411-418`).
- **Por que é básico:** planejar a semana é o uso principal de um calendário; a grade de mês não mostra horários nem conflitos.
- **Impacto:** usuário não consegue enxergar a semana; volta para a agenda do celular.
- **Mínimo viável:** visão agenda (próximos 7 dias) e visão semana, reutilizando os mesmos itens.

## C19 — Sem importar/exportar .ics
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** exportar a agenda para .ics e importar um convite/calendário.
- **Hoje:** o calendário persiste em JSON próprio (`src/Island.Windows/Calendar/JsonCalendarStore.cs`) e não há `iCalendar`/`VCALENDAR`/`.ics` em `src` (busca `ICS|VCALENDAR|iCalendar` vazia).
- **Por que é básico:** .ics é o formato universal de troca; sem ele não há integração com Outlook/Google sem digitação manual.
- **Impacto:** retrabalho para levar eventos de/para outro calendário; impossível participar de convites.
- **Mínimo viável:** exportar todos os itens e importar .ics simples (VEVENT/DTSTART) com prévia e deduplicação.

## C20 — Calendário sem busca
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** procurar "dentista" e pular para a data, sem varrer mês por mês.
- **Hoje:** nenhuma das duas telas tem campo de busca (`src/Island.App/Views/Widgets/CalendarWidget.xaml` e `src/Island.App/Shell/CalendarDayWindow.xaml` não têm `TextBox`/`Search`/`Buscar`), e `CalendarAgenda` só consulta por dia/mês (`src/Island.Core/Calendar/CalendarAgenda.cs:45-86`).
- **Por que é básico:** busca é a forma de achar o compromisso futuro ("quando era a consulta?").
- **Impacto:** o usuário navega meses manualmente e desiste.
- **Mínimo viável:** busca por título em todos os itens com lista de resultados e salto para a data.

## C21 — Evento só com título/data/hora
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** cor/categoria, local, descrição/link e talvez participantes.
- **Hoje:** `CalendarEvent` tem apenas título, data e hora (`src/Island.Core/Calendar/CalendarEvent.cs:3`); a janela de criação pede só nome e HH:mm (`src/Island.App/Shell/CalendarEntryWindow.xaml:70-90`).
- **Por que é básico:** evento real carrega contexto (onde, com quem, link da reunião) e cor/categoria para distinguir trabalho de pessoal.
- **Impacto:** informação fica em outro app; cores não distinguem tipos de compromisso.
- **Mínimo viável:** local e descrição opcionais, uma cor/categoria por evento.

## C22 — Tarefa sem hora, prioridade e descrição
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** prazo com hora, prioridade e uma nota/descrição; ordenar por prioridade.
- **Hoje:** `CalendarTask` é `(Id, Title, DueDate, IsCompleted)` (`src/Island.Core/Calendar/CalendarTask.cs:3`); não há hora, prioridade, descrição, etiqueta nem recorrência, e a linha do dia só mostra o título (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:200-248`).
- **Por que é básico:** tarefa de calendário precisa de prazo/urgência; sem prioridade ou até que horas, não dá para planejar o dia.
- **Impacto:** tarefas urgentes se misturam às triviais; usuário mantém lista externa.
- **Mínimo viável:** hora opcional, prioridade (alta/média/baixa) e descrição curta; ordenar por prioridade/hora.

## C23 — Aniversários sem idade e sem antecedência
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** registrar o ano de nascimento, ver "N anos hoje" e ser lembrado alguns dias antes.
- **Hoje:** `CalendarBirthday` é `(Id, Name, Month, Day)` (`src/Island.Core/Calendar/CalendarBirthday.cs:3`); `AddBirthday(name, month, day)` usa só mês/dia da data selecionada (`src/Island.Core/Calendar/CalendarAgenda.cs:95-104`; `src/Island.App/Shell/CalendarEntryWindow.xaml.cs:95-104,141-143`), e a linha do dia mostra apenas "Aniversário" (`CalendarDayWindow.xaml.cs:250-251`); não há aviso/notificação.
- **Por que é básico:** aniversário sem idade e sem aviso é só um marcador no mês; quem organiza presente quer antecedência.
- **Impacto:** a data passa; o usuário usa o calendário do telefone.
- **Mínimo viável:** ano opcional, idade calculada no dia e lembrete X dias antes.

## C24 — Orçamento sem histórico de consumo
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber quando e quanto consumiu em cada dia (e desfazer um lançamento).
- **Hoje:** o consumo é um único `EnteredValue` acumulado (`src/Island.Core/Budgets/Budget.cs:14,25-26`), e `AddUsage` só soma/subtrai no total (`src/Island.Core/Budgets/BudgetBook.cs:88-96`); não existe log por dia/hora, então o "+1%" não deixa rastro.
- **Por que é básico:** orçamento de IA é usado para controlar ritmo; sem histórico não há como saber se o gasto foi ontem ou hoje.
- **Impacto:** impossível auditar o uso; erro de +5% não tem origem para corrigir.
- **Mínimo viável:** lançamentos com data/hora e delta, saldo calculado e extrato no editor.

## C25 — Sem desfazer os +1%/+5%
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** desfazer um toque acidental (ou corrigir digitando o valor).
- **Hoje:** os botões do widget chamam `AddUsage` direto (`src/Island.App/Views/Widgets/BudgetWidget.xaml.cs:25-28,54-57`; `src/Island.Core/Budgets/BudgetBook.cs:88-96`), sem confirmação, undo ou histórico; a única correção é editar o valor total no editor (`src/Island.App/Budgets/ViewModels/BudgetViewModel.cs:68-72`).
- **Por que é básico:** +5% é um toque em botão pequeno na estante; errar é comum e não deveria exigir abrir editor.
- **Impacto:** números do orçamento perdem confiança; usuário recalcula tudo à mão.
- **Mínimo viável:** desfazer o último lançamento por alguns segundos (como já existe no contador).

## C26 — Orçamento sem alerta de esgotamento
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso quando o restante chega a zero ou o consumo do dia passa do previsto.
- **Hoje:** o status do orçamento só trata "Começa em", "Período encerrado" e "Hoje você marcou como dia sem uso" (`src/Island.App/Budgets/ViewModels/BudgetTextFormatter.cs:55-69`); não há notice/alerta e o valor fica limitado ao total (`src/Island.Core/Budgets/Budget.cs:25-26`), então o usuário só descobre olhando o widget.
- **Por que é básico:** um orçamento serve para frear o consumo; sem alerta ele é um medidor passivo.
- **Impacto:** estouro silencioso; o usuário percebe quando já gastou a cota.
- **Mínimo viável:** aviso da ilha ao atingir 90%/100% e ao ultrapassar o valor previsto do dia (uma vez por dia, sem spam).

## C27 — Sem exportar extrato do orçamento
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** exportar CSV (período, total, usado, restante, dias excluídos) para planilha.
- **Hoje:** não há exportação/impressão no domínio (`Export|Csv|Print` ausente em `src/Island.App/Budgets`); os dados só existem no `budgets.json`.
- **Por que é básico:** controle de gasto pede registro externo/relatório mensal; sem exportar, o histórico (que nem existe, C24) fica preso.
- **Impacto:** impossível conciliar com planilha ou apresentar o gasto.
- **Mínimo viável:** exportar CSV do período atual e, com C24, do histórico de lançamentos.

## C28 — Sem novo período/arquivar período anterior
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** iniciar um novo ciclo com um botão, guardando o resultado do período anterior.
- **Hoje:** o editor só ajusta nome, início, fim, hora de reinício, total, modo e consumo (`src/Island.App/Budgets/Views/BudgetEditor.xaml:16-86`; `src/Island.App/Budgets/ViewModels/BudgetViewModel.cs:38-84`); para recomeçar é preciso editar as datas e zerar o consumo manualmente, e o período encerrado vira "Período encerrado" sem arquivo (não há "Novo período").
- **Por que é básico:** o orçamento tem que ser recorrente; toda semana o usuário precisa fazer essa manutenção manual e perde o comparativo.
- **Impacto:** trabalho semanal e perda do histórico de semanas anteriores.
- **Mínimo viável:** "Novo período" que arquiva os números do ciclo encerrado e reinicia o consumo.

## C29 — Sem lista própria de tarefas/visão agregada
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** uma tela com todos os pendentes/atrasados/hoje, independente da data em que foram criados.
- **Hoje:** tarefas do calendário só aparecem na janela de um dia (`src/Island.Core/Calendar/CalendarAgenda.cs:45-63`; `src/Island.App/Shell/CalendarDayWindow.xaml.cs`), e o widget Tarefas mostra apenas as tarefas da Suite (`src/Island.App/Views/Widgets/TasksWidget.xaml.cs:1-17`); não há lista "pendentes/atrasadas" no app.
- **Por que é básico:** lista de tarefas existe para não esquecer o que está aberto; esconder por data é o oposto de um To-Do.
- **Impacto:** tarefas de ontem não concluídas somem da vista; usuário mantém lista paralela.
- **Mínimo viável:** visão com atrasadas/hoje/próximas/concluídas, concluir e adiar (data) direto na lista.

## C30 — Tarefa sem lembrete de prazo
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso no dia (e na hora, se houver) e destaque de atrasadas ao abrir o app.
- **Hoje:** `CalendarTask` só tem `DueDate` (`src/Island.Core/Calendar/CalendarTask.cs:3`); nada no app agenda aviso de vencimento (`Reminder`/`Notification` ausente no calendário; o `Notice` da ilha nunca é disparado por tarefa) e o widget do mês só marca o dia com um ponto (`src/Island.App/Views/Widgets/CalendarWidget.xaml.cs:182-212`).
- **Por que é básico:** tarefa com prazo sem aviso é anotação; o usuário precisa ser lembrado.
- **Impacto:** prazos perdidos; o app não substitui lembrete do celular.
- **Mínimo viável:** notice de tarefas do dia ao iniciar e de atrasadas, com ação "concluir/adiar".

## C31 — Widget Tarefas é passivo (só leitura da Suite)
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** criar uma tarefa local, concluir ou cancelar dentro do próprio widget.
- **Hoje:** `TasksWidget` só define o `DataContext` para `IslandViewModel.TaskProgress` (`src/Island.App/Views/Widgets/TasksWidget.xaml.cs:1-17`), o presenter não expõe comandos (`src/Island.App/ViewModels/ProgressPresenter.cs:19-104`) e a lista mostra no máximo 3 linhas (`:24`); criar/concluir tarefa só pelo calendário, cancelar não existe em lugar nenhum.
- **Por que é básico:** widget de tarefas que não aceita ação obriga a abrir outra janela para o gesto mais frequente (concluir).
- **Impacto:** widget vira painel decorativo; usuário ignora.
- **Mínimo viável:** botão de concluir/cancelar por linha e "+ tarefa" com captura de uma linha.

## C32 — Sem histórico das tarefas da Suite
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver as últimas tarefas concluídas/falhas, com percentual final e horário.
- **Hoje:** `ProgressTracker` mantém tudo em memória, remove a tarefa concluída após 4 s (`src/Island.Core/Progress/ProgressTracker.cs:17,154-161,188-204`) e `Dispose` limpa as entradas (`:93-104`); nada é persistido, então ao reiniciar a ilha não há histórico.
- **Por que é básico:** tarefa longa (corte/exportação) pode durar minutos e o usuário sai da mesa; depois quer conferir se terminou e qual foi o resultado.
- **Impacto:** perde-se a prova de conclusão/erro; usuário refaz trabalho por dúvida.
- **Mínimo viável:** histórico das últimas 20 tarefas com estado final, hora e mensagem, sobrevivendo ao reinício.

## C33 — Calculadora sem histórico
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver as últimas contas e reaproveitar um resultado anterior.
- **Hoje:** o motor guarda só a conta corrente (`src/Island.Core/Tools/Calculator/CalculatorEngine.cs:18-24`) e o widget só tem visor e keypad (`src/Island.App/Views/Widgets/CalculatorWidget.xaml:7-31`); não há lista de histórico.
- **Por que é básico:** histórico é padrão em calculadora de desktop e evita redigitar a mesma soma.
- **Impacto:** contas repetidas do zero; impossível conferir o cálculo anterior.
- **Mínimo viável:** últimas 10 contas com expressão + resultado, clicáveis para reutilizar.

## C34 — Calculadora sem memória (M+/M−/MR/MC)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** guardar um número na memória e reutilizá-lo em outra conta.
- **Hoje:** `CalculatorKey` tem apenas dígitos, operadores, `%`, `±`, backspace e C (`src/Island.Core/Tools/Calculator/CalculatorKey.cs:3-26`); não há variável nem teclas de memória.
- **Por que é básico:** memória é a segunda função mais usada de uma calculadora (somar várias parcelas, aplicar um fator).
- **Impacto:** usuário anota o intermediário no papel/bloco.
- **Mínimo viável:** memória única com indicador e teclas M+/M−/MR/MC.

## C35 — Calculadora sem funções científicas nem precedência
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** parênteses, potência/raiz e operações com precedência correta.
- **Hoje:** o motor é "pocket" e calcula da esquerda para a direita, declaradamente sem precedência (2+3×4 = 20) (`src/Island.Core/Tools/Calculator/CalculatorEngine.cs:5-8`); não há raiz, potência, parênteses nem funções trigonométricas (enum `CalculatorKey.cs:3-26`).
- **Por que é básico:** contas de produtividade envolvem percentual composto, área, raiz; sem precedência, o resultado está errado para expressões comuns.
- **Impacto:** resultado incorreto sem aviso; usuário não confia e abre a calculadora do Windows.
- **Mínimo viável:** parênteses e potenciação/raiz em modo científico, ou explorar precedência correta.

## C36 — Copiar resultado escondido no clique direito
- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** botão/ícone "copiar resultado" (e Ctrl+C quando houver teclado).
- **Hoje:** copiar só funciona com clique direito no visor (`src/Island.App/Views/Widgets/CalculatorWidget.xaml.cs:36-42,77-89`); o XAML só tem os dois `TextBlock` do visor e o keypad (`src/Island.App/Views/Widgets/CalculatorWidget.xaml:7-31`), sem botão, dica ou atalho.
- **Por que é básico:** o motivo de usar a calculadora no app é jogar o número em outro lugar; um gesto invisível faz a função parecer inexistente.
- **Impacto:** usuário redigita o número ou usa a calculadora do Windows.
- **Mínimo viável:** botão "copiar" no visor com feedback ("copiado").

## C37 — Contador único, sem múltiplos contadores
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** vários contadores nomeados (água, repetições, partidas) convivendo e alternando.
- **Hoje:** existe um único `counter.json` e um único `CounterModel` (`src/Island.Windows/Tools/Counter/CounterStore.cs:12-40`; `src/Island.App/Views/Widgets/CounterWidget.xaml.cs:66-70`); "trocar rótulo" só percorre 6 presets (`src/Island.Core/Tools/Counter/CounterModel.cs:15,94-99`), sem criar/selecionar contadores.
- **Por que é básico:** contador de hábito é usado para mais de uma coisa; um contador por vez obriga a zerar e perder o outro.
- **Impacto:** usuário anota no papel ou perde a contagem anterior.
- **Mínimo viável:** lista de contadores nomeados, com criar/renomear/excluir e um valor cada.

## C38 — Contador preso a presets e sem valor digitável
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** definir passo livre, nome próprio, meta livre e digitar/corrigir o valor atual.
- **Hoje:** passo só 1/5/10 (`src/Island.Core/Tools/Counter/CounterModel.cs:13,80-85`), rótulo só 6 presets (`:15,94-99`), meta só 0/10/20/50/100 (`:14,87-92`) e não existe setter do valor — só `Increment/Decrement/Reset/Undo` (`:56-78`).
- **Por que é básico:** o contador começa no meio de uma contagem (ou erra um toque) e precisa ser corrigido; passo de 3, meta de 40 são comuns.
- **Impacto:** correção exige dezenas de cliques; contador não serve para metas reais.
- **Mínimo viável:** passo e meta editáveis, nome livre e "definir valor".

## C39 — Timer sem notificação de término e alarme não insiste
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ser avisado ao fim do timer mesmo com a estante fechada, com som que insiste até parar.
- **Hoje:** o fim toca `SystemSounds.Asterisk.Play` uma única vez (`src/Island.App/Views/Widgets/TimerWidget.xaml.cs:50-51`) e o widget pisca "Pronto!" por 3 s (`:22-24`); não há notice da ilha nem toast do Windows (busca `AppNotification|ToastNotification` em `src` vazia) e não há botão de parar/repetir o alarme.
- **Por que é básico:** timer serve para lembrar enquanto o usuário está em outro app; um som curto do esquema do Windows, que pode estar mudo, é fácil de perder.
- **Impacto:** comida queima, reunião atrasa; usuário usa o timer do celular.
- **Mínimo viável:** notice persistente da ilha + toast, som repetindo até "parar", com opção de repetir o timer.

## C40 — Um timer por vez
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** dois ou três timers independentes (forno, alongamento, pausa) com nome e tempo restante.
- **Hoje:** `TimerTools` guarda um `CountdownModel` e um `StopwatchModel` e o widget alterna entre esses dois modos (`src/Island.Core/Tools/Timers/TimerTools.cs:11-23`; `src/Island.App/Views/Widgets/TimerWidget.xaml.cs:58-70`); não há lista de timers.
- **Por que é básico:** na cozinha/estudo, timers se sobrepõem; com um só modelo, configurar o segundo destrói a contagem do primeiro.
- **Impacto:** o usuário volta para o timer do celular ou perde um dos prazos.
- **Mínimo viável:** lista de até N timers nomeados, cada um com play/pause/reset e alarme próprio.

## C41 — Presets fixos de duração e sem salvar
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** salvar durações usadas (ex.: 7, 12, 20 min) e dar um título ao timer.
- **Hoje:** presets fixos de 1, 3, 5, 10, 15, 25, 45 e 60 min em ciclo (`src/Island.Core/Tools/Timers/CountdownModel.cs:13-14,119-133`), com ajuste de ±1 min/10 s e faixa 10 s–180 min (`:10-11`); presets próprios não são salvos em lugar nenhum.
- **Por que é básico:** cada pessoa repete 2–3 durações; ciclar por 8 presets toda vez é atrito diário.
- **Impacto:** ajustes repetidos a cada uso; sem título, vários timers (C40) não se distinguem.
- **Mínimo viável:** salvar presets do usuário e título por timer.

## C42 — Timer e cronômetro não sobrevivem ao reinício
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** fechar o app (ou o app atualizar) e retomar o timer/cronômetro em andamento.
- **Hoje:** os modelos vivem no `ToolRegistry` em memória (`src/Island.Core/Tools/ToolRegistry.cs:4-7`) e não têm store — só o contador tem `CounterStore`; `TimerTools` não grava nada (`src/Island.Core/Tools/Timers/TimerTools.cs:7-23`).
- **Por que é básico:** o app tem auto-update e o usuário pode fechá-lo no meio de uma contagem; perder o timer é inaceitável para uma tarefa de 90 min.
- **Impacto:** contagem perdida; usuário desconfia de deixar timer no app.
- **Mínimo viável:** persistir estado (duração, iniciado em, pausa) e retomar com confirmação "continuar/zerar".

## C43 — Analisador só lê clipboard ou .txt arrastado
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** digitar/colar um texto ou abrir um arquivo pelo diálogo e analisar.
- **Hoje:** o widget só analisa o texto mais recente do clipboard ou um `.txt` arrastado (`src/Island.App/Views/Widgets/TextAnalyzerWidget.xaml.cs:18-20,53,226-232`); não há campo de entrada nem diálogo de abrir, e outros formatos de texto são recusados (`:231-232` só aceita `.txt`).
- **Por que é básico:** analisar um texto que não está no clipboard é o caso óbvio ("quanto deu este capítulo?"); depender do clipboard torna a ferramenta refém de outro app.
- **Impacto:** usuário copia de qualquer jeito e perde a seleção atual do clipboard; .md/.csv não servem.
- **Mínimo viável:** caixa de texto/colar manual e "Abrir arquivo" aceitando .txt/.md/.csv.

## C44 — Estatísticas incompletas e sem métricas úteis
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** linhas, caracteres sem espaços, palavras por frase, densidade de palavras e legibilidade.
- **Hoje:** `TextStats` calcula linhas e caracteres sem espaços (`src/Island.Core/Tools/TextAnalyzer/TextStats.cs:7-16`), mas a UI mostra só 6 cards — Caracteres, Palavras, Parágrafos, Leitura, Tokens e Frases (`src/Island.App/Views/Widgets/TextAnalyzerWidget.xaml.cs:43-51`; rótulos em `TextAnalyzerWidget.xaml:61-72`); as linhas calculadas nunca são exibidas e "caracteres sem espaços" só aparece em tooltip, sem métricas avançadas.
- **Por que é básico:** analisador de texto existe para decisão (cabe no limite? está denso? tempo de fala?); parte do que o motor já calcula não chega ao usuário.
- **Impacto:** dado calculado e descartado; usuário usa site externo para o resto.
- **Mínimo viável:** mostrar linhas/sem espaços e adicionar palavras por frase, densidade e tempo de fala.

## C45 — Sons de foco sem presets e sem persistência
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** salvar mixes (chuva+café), volume por som e timer de dormir, e reencontrar tudo ao reabrir o app.
- **Hoje:** a mistura, volume e timer vivem no modelo em memória (`src/Island.Core/Tools/FocusSounds/FocusSoundsModel.cs:8-9`; `src/Island.Core/Tools/ToolRegistry.cs:4-7`), e `IslandSettings` não tem nenhum campo de sons de foco (`src/Island.Core/Configuration/IslandSettings.cs:11-127`); não há preset salvo.
- **Por que é básico:** som de foco é usado sempre com a mesma combinação; reajustar 8 chips e o volume a cada sessão é atrito.
- **Impacto:** usuário não usa o recurso ou deixa tocando sem controle; configuração se perde a cada reinício.
- **Mínimo viável:** 2–3 presets (sons + volumes) e restauração do último mix/volume/timer.

## C46 — Nível por som só pela roda do mouse
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ajustar o volume de cada som com controle visível, arrastável e acessível por teclado.
- **Hoje:** o nível de cada som só muda pela roda do mouse sobre o chip (`src/Island.App/Views/Widgets/FocusSoundsWidget.xaml.cs:76-77`); o chip desenha uma barra fina (`src/Island.App/Views/Widgets/FocusSoundChip.cs:20-24`), mas não há alça, valor numérico nem ação de teclado — e o volume geral só tem slider na barra inferior (`FocusSoundsWidget.xaml:80-83`).
- **Por que é básico:** quem usa touchpad, teclado ou leitor de tela não consegue ajustar a mistura; a roda é gesto escondido até para mouse.
- **Impacto:** mix desbalanceado ou inutilizável para parte dos usuários.
- **Mínimo viável:** slider por som (ou menu com valor) focável e anunciado por automação.

## C47 — Sem escolher o dispositivo de saída
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** tocar os sons no fone e deixar o resto do PC na caixa, ou escolher o dispositivo.
- **Hoje:** `FocusAudioOutput` sempre abre o endpoint padrão `GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` (`src/Island.Windows/Audio/FocusAudioOutput.cs:107-113`); não há seletor nem setting no domínio.
- **Por que é básico:** em reunião/estudo, o dispositivo do som de foco precisa ser diferente do de notificação; apps de som ambiente oferecem isso.
- **Impacto:** som de foco sai no dispositivo errado; usuário evita usar.
- **Mínimo viável:** seletor de saída nas configurações, padrão "dispositivo do sistema".

## C48 — File Tray não persiste entre reinícios
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** reabrir o app e continuar com os arquivos separados na bandeja.
- **Hoje:** `FileTray` é uma lista em memória ("In-memory list of file paths") (`src/Island.Core/Shelf/FileTray.cs:1-9`) registrada como singleton (`src/Island.App/Composition/ServiceRegistration.cs:162`); não há store/settings, e fechar o app esvazia a bandeja.
- **Por que é básico:** bandeja de arquivos é área de trabalho temporária; se some a cada fechamento/atualização, não dá para confiar nela.
- **Impacto:** usuário perde a seleção de arquivos no meio de um fluxo; volta a usar o Explorer.
- **Mínimo viável:** salvar os caminhos (validando existência na leitura) e restaurar ao abrir, com "Limpar" explícito.

## C49 — Bandeja sem abrir, copiar caminho, prévia ou renomear
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** abrir/revelar o arquivo, copiar o caminho e ver a prévia (imagem/documento).
- **Hoje:** o chip da bandeja só remove ou é arrastado para fora (`src/Island.App/Views/Widgets/FileTrayWidget.xaml.cs:129-145,158-174`); não há menu de contexto, abrir, copiar caminho, renomear ou miniatura (o ícone é genérico por extensão, `:104-111`).
- **Por que é básico:** bandeja serve para agir sobre os arquivos; só remover/arrastar limita a "prateleira".
- **Impacto:** usuário abre o Explorer para qualquer ação; a bandeja vira decoração.
- **Mínimo viável:** menu com Abrir/Revelar/Copiar caminho/Remover e prévia de imagem.

## C50 — Conversões dependem de ferramentas externas não incluídas
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** converter vídeo/áudio/documento/PDF sem instalar nada à mão.
- **Hoje:** vídeo e áudio exigem `ffmpeg`, documentos PDF exigem LibreOffice e comprimir PDF exige Ghostscript; sem o binário a ação fica desabilitada com uma mensagem (`src/Island.Core/Files/FileTools.cs:26-31`; `src/Island.App/Shell/FileOrbitWindow.xaml.cs:174-176`; `docs/file-actions.md:29-31`). Não há download/instalação guiada pelo app nem alternativa embutida.
- **Por que é básico:** "converter arquivos" é a função que o usuário vem buscar; sem o binário, metade das ações simplesmente não funciona na instalação padrão.
- **Impacto:** recurso parece quebrado; usuário baixa outro conversor.
- **Mínimo viável:** detector com botão "instalar/instruções" (link e passo a passo) e, no mínimo, ações que não dependem de terceiros sempre disponíveis.

## C51 — Sem OCR (PDF/imagem para texto)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** extrair texto de um print/PDF digitalizado para a nota ou para buscar.
- **Hoje:** o catálogo tem 16 ações e nenhuma de OCR (`src/Island.Core/Files/FileActionCatalog.cs:3-7,27-40`); busca por `OCR` em `src/Island.Core/Files` não retorna nada.
- **Por que é básico:** print de reunião/nota fiscal é caso diário de produtividade; copiar o texto à mão é o contorno atual.
- **Impacto:** usuário usa ferramenta online (privacidade) ou digita tudo.
- **Mínimo viável:** OCR local pt-BR de imagem/PDF para .txt, com progresso e aviso quando o idioma não estiver disponível.

## C52 — Progresso sem percentual nas ações de arquivo
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver "12 de 40 arquivos, 30%" e tempo estimado em lotes grandes.
- **Hoje:** a barra é indeterminada (`src/Island.App/Shell/FileOrbitWindow.xaml:183`) e o serviço reporta apenas mensagens (`IProgress<string>` em `src/Island.Core/Files/FileActionService.cs:67-68`); não há contador por arquivo nem ETA.
- **Por que é básico:** converter 200 fotos ou um vídeo longo sem percentual parece travado; usuário cancela por achar que quebrou.
- **Impacto:** cancelamentos desnecessários e sensação de instabilidade.
- **Mínimo viável:** reportar arquivos processados/total e percentual na barra (por arquivo quando houver um só).

## C53 — Órbita só abre arrastando arquivos; sem adicionar/histórico
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** abrir a órbita por menu/atalho, adicionar mais arquivos e repetir a última ação.
- **Hoje:** a órbita só é aberta quando algo é arrastado sobre a ilha (`src/Island.App/Shell/IslandWindow.FileDrop.cs:12-52`); o menu da ilha não tem entrada de arquivos (`src/Island.App/Shell/IslandContextMenu.xaml:75-187`) e a janela não tem "adicionar arquivos" nem histórico/repetir.
- **Por que é básico:** depois de fechar, o usuário não consegue reabrir com os mesmos arquivos nem refazer uma ação; quem não usa drag-and-drop fica sem o recurso.
- **Impacto:** operações repetitivas (mesmos arquivos, outra saída) exigem arrastar tudo de novo.
- **Mínimo viável:** entrada no menu/atalho, "adicionar arquivos" na janela e histórico de operações para repetir.

## C54 — Histórico do clipboard só em memória
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** fechar/reabrir o app (ou reiniciar o PC) e ainda ter os últimos itens copiados.
- **Hoje:** `ClipboardHistory` é declaradamente "in-memory only (never persisted)" (`src/Island.Core/Clipboard/ClipboardHistory.cs:6`); fechar o app apaga tudo. Não há opção de persistência — nem como escolha explícita com aviso de privacidade.
- **Por que é básico:** a utilidade de um gerenciador de clipboard é recuperar algo copiado há minutos/horas; perder tudo no restart derruba metade do valor.
- **Impacto:** usuário perde o que copiou ao reiniciar (e o app se atualiza sozinho); não dá para contar com ele.
- **Mínimo viável:** persistência opcional dos últimos N itens (desligada por padrão), criptografada ou em memória protegida, com "limpar ao sair".

## C55 — Sem fixar/favoritar item do clipboard
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fixar um trecho (e-mail, assinatura, snippet) e mantê-lo no topo.
- **Hoje:** `ClipboardItem` não tem estado de fixação (`src/Island.Core/Clipboard/ClipboardItem.cs:6`); a entrada só oferece copiar/excluir (`src/Island.App/Widgets/ClipboardEntry.cs:22-31,47-51`; `src/Island.App/Views/ClipboardView.xaml.cs:164-187`) e não há seção de fixados.
- **Por que é básico:** todo gerenciador de clipboard maduro permite fixar; sem isso, o item útil é empurrado para fora pelo limite de itens.
- **Impacto:** snippets repetidos são recopiados da fonte; limite de itens fica ainda mais curto.
- **Mínimo viável:** fixar por item, seção "Fixados" no topo e fixos fora da rotação do limite.

## C56 — Clipboard sem busca nem filtro
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** digitar parte do texto e filtrar por tipo (texto/link/imagem/cor).
- **Hoje:** o painel só tem título, tira de cards e o menu "More → Clear history" (`src/Island.App/Views/ClipboardView.xaml:25-78`; `src/Island.App/Views/ClipboardView.xaml.cs:32-39`); não há campo de busca nem filtro, apesar de `ClipboardKind` existir (`src/Island.Core/Clipboard/ClipboardItem.cs:3`).
- **Por que é básico:** com 50 itens numa tira horizontal, achar "aquele link" é rolagem cega.
- **Impacto:** usuário desiste e copia de novo da fonte.
- **Mínimo viável:** busca incremental e filtros por tipo (chips).

## C57 — Captura do clipboard sem controles nas configurações
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** pausar/desligar a captura, definir quantos itens reter e limpar pelo app.
- **Hoje:** `ClipboardEnabled` e `ClipboardMaxItems` existem no modelo (`src/Island.Core/Configuration/IslandSettings.cs:58-59`) e são aplicados na captura (`src/Island.App/App.xaml.cs:314`; `src/Island.Core/Clipboard/ClipboardHistory.cs:44`), mas a janela de Settings não tem seção de clipboard (seções em `src/Island.App/Shell/SettingsWindow.xaml:328-706`); sem editar o JSON, não dá para desligar a captura nem mudar o limite.
- **Por que é básico:** clipboard é dado sensível; o usuário precisa de um botão para parar a captura e de um limite visível.
- **Impacto:** privacidade só controlável por arquivo; limite de 50 itens é uma surpresa.
- **Mínimo viável:** seção Clipboard com ligar/desligar, pausar 1 h, limite de itens e "limpar histórico".

## C58 — Ferramentas só existem dentro da estante
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** abrir calculadora/contador/timer/analisador/sons em janela própria ou por atalho, usando junto de outro app.
- **Hoje:** todas são widgets da estante (`src/Island.App/Widgets/WidgetCatalog.cs:45-54`) e não há janela dedicada para elas em `src/Island.App/Shell`; a estante recolhe quando se clica fora (`src/Island.App/Shell/IslandWindow.xaml.cs:119-122,530-536`) e a ilha não recebe teclado (`src/Island.App/Views/Widgets/CalculatorWidget.xaml.cs:11-13`); atalhos globais cobrem só notas, clipboard, captura e notas do jogo (`src/Island.App/App.xaml.cs:120,327`; `src/Island.App/Shell/CaptureHotkeys.cs`; `src/Island.App/Shell/GameNotesHotkeyController.cs:38`).
- **Por que é básico:** ferramenta de produtividade é usada no meio de outro trabalho (contador de repetição, timer, calculadora de orçamento) sem tirar o foco do que se está fazendo.
- **Impacto:** cada uso exige abrir a ilha e perder o foco do app; ferramentas viram brinquedo de demo.
- **Mínimo viável:** janela flutuante (opcionalmente topmost) e atalhos para calculadora/timer/contador, reaproveitando os mesmos modelos do `ToolRegistry`.

Total: 58 itens (2 críticas, 18 altas, 33 médias e 5 baixas), todos verificados no código.
