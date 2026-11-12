# Auditoria — Pomodoro e foco

Escopo: timer e ciclos, perfis e objetivo, agendamento avulso e rotina semanal, Pomodoro raivoso (bloqueio/desbloqueio e celular), sons, tela de transição, controles na ilha/tray e persistência do domínio.
Método: leitura de `src/Island.Core/Pomodoro`, das janelas/widgets em `src/Island.App` e dos testes `Pomodoro*`; cada item traz evidência `arquivo:linha` ou ausência confirmada por busca textual. O README foi usado só para separar o que já existe do que falta.

## A01 — Descanso longo após N ciclos
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** depois de 4 pomodoros, um descanso longo (15–30 min) antes do próximo bloco, com o intervalo configurável.
- **Hoje:** só há foco e descanso únicos: o perfil tem um par de tempos (`FocusMinutes`/`BreakMinutes`, `src/Island.Core/Pomodoro/PomodoroProfile.cs:9-13`), settings só `PomodoroFocusMinutes`/`PomodoroBreakMinutes` (`src/Island.Core/Configuration/IslandSettings.cs:45-48`) e o timer só tem `Focus/Break/Prep` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:6`); busca por `LongBreak`/`long break` não retorna nada.
- **Por que é básico:** é metade da técnica Pomodoro (pausas curtas frequentes + pausa longa periódica); sem isso o app cronometra, mas não estrutura a recuperação.
- **Impacto:** fadiga em estudo/trabalho longo; o usuário mantém um timer externo.
- **Mínimo viável:** N por perfil, minutos do descanso longo e troca automática a cada N ciclos, visível no track.

## A02 — Pular a etapa atual mantendo o plano
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** encerrar o foco agora e ir para o descanso (ou voltar ao foco) sem cancelar o plano.
- **Hoje:** não existe "skip": `SetPhase` limpa o plano (`src/Island.Core/Pomodoro/PomodoroTimer.cs:409-425`) e `SelectPhase` recusa clique com plano ativo (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:364-371`); busca por `SkipPhase`/`Pular` no domínio vazia.
- **Por que é básico:** na vida real a tarefa acaba antes ou depois; sem pular, o usuário espera à toa ou perde o plano.
- **Impacto:** ciclos desalinhados do dia; abandono do Pomodoro.
- **Mínimo viável:** botão "Pular" que avança a fase atual do plano (ou encerra o plano no último descanso).

## A03 — Estender ou encurtar a etapa em andamento
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** +5/−5 min no foco ou no descanso atuais sem perder o plano.
- **Hoje:** `SetMinutes` é a única mudança de duração e limpa o plano (`src/Island.Core/Pomodoro/PomodoroTimer.cs:427-443`); a régua fica travada enquanto roda (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:223-228`); busca por `Extend`/`AddMinutes` no domínio vazia.
- **Por que é básico:** quem está rendendo estende o foco; quem precisa sair encurta.
- **Impacto:** perda do plano e do contador por um ajuste corriqueiro.
- **Mínimo viável:** +5/−5 na fase corrente, registrando a duração real.

## A04 — Iniciar ou adiar o descanso durante o plano
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** começar o descanso antes da hora ou empurrá-lo quando não pode parar agora.
- **Hoje:** a pílula Break só funciona sem plano (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:188-198` mostra as pílulas; `364-371` ignora o clique com plano ativo) e o README diz que "o plano decide a fase" (`README.md:119`); não há adiar.
- **Por que é básico:** descanso é parte do ciclo e precisa da mesma flexibilidade do foco.
- **Impacto:** descanso perdido ou fora de hora; o usuário pausa sem descansar.
- **Mínimo viável:** "Iniciar descanso agora" e "adiar 5 min" durante o foco, dentro do plano.

## A05 — Cancelar o pomodoro sem confirmação
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmação (ou desfazer) ao cancelar um plano com progresso.
- **Hoje:** o botão Reset chama `Pomodoro.Reset()` direto (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:88`) e `Reset` apaga plano e ciclo na hora (`src/Island.Core/Pomodoro/PomodoroTimer.cs:387-406`); não há diálogo nem undo.
- **Por que é básico:** é um clique acidental em botão colado ao Play e apaga 30–90 min planejados.
- **Impacto:** perda de progresso e quebra de confiança no app.
- **Mínimo viável:** confirmar quando houver plano/progresso; ou undo de alguns segundos.

## A06 — Sem pausa automática por inatividade
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** pausar o foco quando o PC fica ocioso/tela bloqueada e retomar ao voltar.
- **Hoje:** o domínio não lê inatividade do usuário (sem `GetLastInputInfo`/hook; busca em `src` só encontra idle de timer, não do usuário); em `OnSessionSwitch`/`OnPowerModeChanged` o app apenas atualiza a rotina semanal (`src/Island.App/App.xaml.cs:546-556`) e não há setting de pausa por inatividade.
- **Por que é básico:** foco real termina quando a pessoa sai; contagem "fantasma" invalida o registro.
- **Impacto:** o app contabiliza tempo que não existiu.
- **Mínimo viável:** pausar após X min sem input, com opção de ligar/desligar.

## A07 — Suspensão do PC sem reconciliação do foco
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ao voltar da suspensão, ver o que perdeu e decidir retomar/ajustar, sem o cochilo contar como foco.
- **Hoje:** o relógio padrão do timer "keeps counting through sleep" (`src/Island.Core/Pomodoro/PomodoroTimer.cs:83-88`) e o primeiro tick ao acordar conclui só uma fase (`src/Island.Core/Pomodoro/PomodoroTimer.cs:484-506`); não há tratamento de suspensão para o timer (`src/Island.App/App.xaml.cs:551-556`).
- **Por que é básico:** suspender/fechar a tampa é situação diária.
- **Impacto:** foco dado como concluído e descanso começando sem o usuário.
- **Mínimo viável:** detectar `PowerModes.Resume`, pausar e avisar "foco interrompido por suspensão".

## A08 — Sessão normal não sobrevive a fechar/reiniciar/crash
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** continuar a sessão normal depois de fechar/reabrir o app ou de uma queda.
- **Hoje:** `PomodoroTimer.Capture/Restore` existem (`src/Island.Core/Pomodoro/PomodoroTimer.cs:247-287`), mas só a sessão Angry é salva, e apenas antes de instalar update (`src/Island.Windows/Pomodoro/JsonAngryResumeStore.cs:8-11`, uso em `src/Island.App/App.xaml.cs:443,470-474`); foco normal não tem store (`src/Island.App/Composition/ServiceRegistration.cs:153-159`).
- **Por que é básico:** o app reinicia para atualizar e fecha com o PC.
- **Impacto:** a sessão some e o usuário recomeça do zero.
- **Mínimo viável:** salvar o `PomodoroTimerState` ao sair/periodicamente e restaurar com aviso.

## A09 — Pré fixo de 5 minutos
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar ou desligar o preparo antes do foco.
- **Hoje:** `PrepMinutes = 5` é constante (`src/Island.Core/Pomodoro/PomodoroTimer.cs:23-24`), a régua ignora o Pré (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:226`) e não há setting (o README confirma "preparo fixo de 5 minutos", `README.md:130`).
- **Por que é básico:** preparação é preferência pessoal.
- **Impacto:** quem não quer Pré perde 5 min por dia; quem precisa de 10 não tem.
- **Mínimo viável:** duração 0–15 min em Settings ou opção de desligar.

## A10 — Sessão de foco livre (contagem crescente)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** registrar uma sessão de foco sem ciclos quando não quer descanso programado.
- **Hoje:** `PomodoroPhase` só tem `Focus/Break/Prep` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:6`); o cronômetro crescente existe só no widget Timer genérico (`src/Island.App/Views/Widgets/TimerWidget.xaml.cs:185-190`), fora do Angry, do celular e de qualquer estatística.
- **Por que é básico:** estudo/trabalho nem sempre cabe em ciclos de 25 min.
- **Impacto:** usuário usa cronômetro externo e o app não registra o tempo.
- **Mínimo viável:** modo "foco livre" que soma tempo no dia/perfil.

## A11 — Previsão de término enganosa no avanço manual
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** no modo manual, ver duração restante sem horário prometido (como o README descreve).
- **Hoje:** o widget calcula `DateTime.Now + rest` sempre, inclusive no manual, e só acrescenta " · manual" (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:300-309`); o README afirma que a previsão manual mostra a duração "sem prometer um horário de término" (`README.md:124`).
- **Por que é básico:** horário errado gera compromisso errado.
- **Impacto:** o usuário planeja em cima de um fim que não acontece.
- **Mínimo viável:** mostrar "N min restantes + esperas" em vez de "até HH:mm" no manual.

## A12 — Perfil sem quantidade de pomodoros
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher o perfil e já vir com o número de pomodoros (ex.: Trabalho ×4).
- **Hoje:** `PomodoroProfile` não tem `Cycles` (`src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16`) e `Apply` não toca em `PomodoroCycles` (`src/Island.Core/Pomodoro/PomodoroProfile.cs:54-58`); o número é global (`src/Island.Core/Configuration/IslandSettings.cs:47-48`) e ajustado à parte no stepper.
- **Por que é básico:** perfil é o preset da sessão; sem ciclos ele está incompleto.
- **Impacto:** trocar de perfil exige reajustar ciclos sempre.
- **Mínimo viável:** campo "Pomodoros" no perfil, aplicado ao preparar a sessão.

## A13 — Perfil sem objetivo padrão
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** cada perfil com objetivo/assunto padrão (ex.: "Revisar cálculo").
- **Hoje:** o objetivo é uma string global (`src/Island.Core/Configuration/IslandSettings.cs:50`) aplicada por `Goal` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:50-59`), fora do perfil (`src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16`); trocar de perfil mantém o objetivo anterior.
- **Por que é básico:** é o contexto do foco; repetir toda vez é atrito.
- **Impacto:** sessões sem objetivo ou com objetivo errado.
- **Mínimo viável:** `Goal` no perfil, pré-preenchido ao selecionar.

## A14 — Sem duplicar, reordenar ou restaurar perfis
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** duplicar, reordenar e voltar aos modelos padrão.
- **Hoje:** só existem `NewProfileCommand`, `SaveProfileCommand` e `DeleteProfileCommand` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:34-36`); `Profiles` é `ObservableCollection` sem `Move` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:39`) e não há reset (busca por `Duplicate`/`RestoreDefault` vazia).
- **Por que é básico:** organizar 5–20 perfis é uso normal.
- **Impacto:** perfis desordenados e recriação manual.
- **Mínimo viável:** duplicar, subir/descer, restaurar Estudo/Leitura/Trabalho.

## A15 — Excluir perfil sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmação antes de excluir um perfil.
- **Hoje:** `DeleteProfile` remove na hora e aplica outro perfil sem aviso (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:140-153`); não há diálogo nem undo.
- **Por que é básico:** ação destrutiva com um clique.
- **Impacto:** perda de tempos/sons configurados.
- **Mínimo viável:** confirmar ou oferecer undo (o bloqueio do último perfil já existe, linha 36).

## A16 — Sem exportar/importar perfis
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** levar os perfis para backup e para outro PC.
- **Hoje:** os perfis vivem embutidos no `settings.json` (`src/Island.Core/Configuration/IslandSettings.cs:55`); busca por `Export`/`Import` no domínio e no ViewModel não encontra nada.
- **Por que é básico:** configuração valiosa e reutilizável.
- **Impacto:** retrabalho a cada troca de PC ou reset.
- **Mínimo viável:** JSON de perfis com import validado.

## A17 — Objetivo é texto único, sem lista de tarefas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** uma lista de tarefas/objetivos para escolher na sessão e riscar ao terminar.
- **Hoje:** existe só `PomodoroGoal`, texto de 160 caracteres (`src/Island.Core/Configuration/IslandSettings.cs:50`), sem id, estado ou lista; `CalendarTask` é de outro domínio e não é referenciado no Pomodoro (busca por `Task`/`Calendar` em `src/Island.Core/Pomodoro` vazia).
- **Por que é básico:** Pomodoro existe para executar tarefas; sem elas o timer é abstrato.
- **Impacto:** o usuário anota em outro app e a sessão fica sem contexto.
- **Mínimo viável:** lista simples com seleção por sessão e marcação de concluída.

## A18 — Sem tags/categorias na sessão
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** marcar a sessão com tag/categoria (disciplina, projeto, leitura).
- **Hoje:** nem perfil nem estado da sessão têm tag — `src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16` e `src/Island.Core/Pomodoro/PomodoroResumeState.cs:7-24` só têm tempos/sons/objetivo; busca por `Tag` no domínio vazia.
- **Por que é básico:** agrupar tempo por área é a análise mínima.
- **Impacto:** qualquer estatística futura fica sem dimensão.
- **Mínimo viável:** tag opcional na sessão e filtro no histórico.

## A19 — Sem notas da sessão
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** anotar o que foi feito e o que ficou pendente ao fim da sessão.
- **Hoje:** não há campo de nota na sessão nem no histórico; o único texto é o objetivo curto (`SessionGoal`, `src/Island.Core/Pomodoro/PomodoroTimer.cs:53`).
- **Por que é básico:** o fim do foco é o momento natural de registrar a próxima ação.
- **Impacto:** nada do que foi produzido fica ligado à sessão.
- **Mínimo viável:** nota por sessão no resumo/histórico.

## A20 — Sem integração com o calendário existente
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver ou agendar foco no calendário e evitar choque com compromissos.
- **Hoje:** o app tem `CalendarAgenda` para outros widgets (`src/Island.App/Composition/ServiceRegistration.cs:194`), mas o domínio Pomodoro não referencia Calendar (busca em `src/Island.Core/Pomodoro` vazia) e o agendamento não consulta a agenda.
- **Por que é básico:** foco cabe dentro de uma agenda do dia.
- **Impacto:** foco agendado choca com reunião.
- **Mínimo viável:** mostrar os próximos compromissos no agendamento e avisar conflito.

## A21 — Sem perfil automático por horário/rotina
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** perfil por período (manhã = Trabalho, noite = Leitura) aplicado sem escolher toda vez.
- **Hoje:** a seleção é sempre manual (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:60-73`); faixas semanais têm nome/modo/tempos, mas não `ProfileId` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:9-17`), e o início avulso idem (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:12-13`).
- **Por que é básico:** automatizar o preset evita decisão diária.
- **Impacto:** sessões agendadas rodam com tempos digitados no formulário, não com o perfil.
- **Mínimo viável:** perfil opcional na faixa/agendamento e regra "perfil por período".

## A22 — Sem histórico de sessões
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** histórico com início/fim, objetivo, perfil, duração e pausas/interrupções.
- **Hoje:** não existe store de sessões: em `src/Island.Windows/Pomodoro/` só há agendamento, rotina semanal e retomada do Angry; o timer guarda apenas `PlanCycle` em memória, zerado ao fim (`src/Island.Core/Pomodoro/PomodoroTimer.cs:44-45,534-541`).
- **Por que é básico:** sem registro, nada do que o app mede hoje pode ser consultado amanhã.
- **Impacto:** o usuário não sabe seu desempenho e não há como provar progresso.
- **Mínimo viável:** gravar um registro por sessão/fase concluída em JSON local e uma lista simples.

## A23 — Sem estatísticas de uso
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver pomodoros feitos, tempo focado e médias por dia e por perfil.
- **Hoje:** busca por `Stats`/`Statistics`/`Streak`/`Heatmap` em `src` não retorna nada; o único contador é o `Cycle` da sessão atual (`src/Island.Core/Pomodoro/PomodoroTimer.cs:140-156`).
- **Por que é básico:** Pomodoro é um método de medição; sem números não há método.
- **Impacto:** motivação e ajuste de metas ficam impossíveis.
- **Mínimo viável:** painel "Hoje/Semana" com três números.

## A24 — Sem meta diária/semanal nem sequência
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** definir meta diária/semanal (N pomodoros ou X min) e ver sequência de dias.
- **Hoje:** não há settings de meta (`src/Island.Core/Configuration/IslandSettings.cs:45-57`) nem campo de sequência; busca por `DailyGoal`/`Streak` em `src` vazia.
- **Por que é básico:** meta é o objetivo do uso diário.
- **Impacto:** uso esporádico, sem alvo.
- **Mínimo viável:** meta configurável + barra de progresso na ilha.

## A25 — Sem relatório semanal/heatmap
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver padrões por hora e dia da semana.
- **Hoje:** não há dados históricos (A22) nem tela; busca por `Report`/`Heatmap` no domínio vazia.
- **Por que é básico:** ver padrões é o uso analítico mínimo.
- **Impacto:** nenhuma visão de tendência.
- **Mínimo viável:** tela semanal derivada do histórico.

## A26 — Sem exportar dados de foco
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** exportar o histórico (CSV/JSON) para planilha ou backup.
- **Hoje:** não há exportação no domínio; busca por `Export`/`Import` em `src/Island.Core/Pomodoro` e em `src/Island.App/ViewModels/PomodoroSessionViewModel.cs` vazia.
- **Por que é básico:** dados locais devem ser portáveis.
- **Impacto:** dados presos no app.
- **Mínimo viável:** botão para exportar CSV/JSON do histórico.

## A27 — Desistências do Angry não ficam registradas
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber quantas vezes desistiu de um foco Angry e quando.
- **Hoje:** `TryUnlock` só valida a frase e encerra (`src/Island.Core/Pomodoro/AngryPomodoro.cs:206-237`); nenhum contador/registro é gravado e o arquivo de retomada é transitório (`src/Island.Windows/Pomodoro/JsonAngryResumeStore.cs:8-11`).
- **Por que é básico:** o Angry existe para medir disciplina; sem registro não há feedback.
- **Impacto:** desistências invisíveis e nenhum histórico de resistência.
- **Mínimo viável:** registrar evento de desbloqueio no histórico.

## A28 — Agendamento avulso não repete
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** repetir um início agendado (diário/úteis/semanal) sem recriar na mão.
- **Hoje:** o início avulso tem `Add` de um único instante (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:108-117`) e a janela cobre no máximo 30 dias (`src/Island.App/Shell/ScheduleWindow.xaml.cs:22,148-158`); recorrência só existe no modelo separado de faixas semanais.
- **Por que é básico:** rotina de estudo se repete.
- **Impacto:** retrabalho e esquecimento.
- **Mínimo viável:** opção "repetir" no `ScheduledStart` com regras simples.

## A29 — Início agendado sem nome, objetivo ou perfil
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** nomear o início agendado e escolher objetivo/perfil.
- **Hoje:** `ScheduledStart` não tem nome/objetivo/perfil (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:12-17`); ao disparar, `StartPlan(..., sessionName: null, ...)` (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:285-287`) usa o objetivo global, e o formulário não tem esses campos (`src/Island.App/Shell/ScheduleWindow.xaml:106-244`).
- **Por que é básico:** o agendamento deveria preparar a sessão, não só ligar o relógio.
- **Impacto:** sessão agendada sem contexto e com objetivo esquecido.
- **Mínimo viável:** nome/objetivo e perfil opcionais no formulário e na linha.

## A30 — Início agendado pode apagar a sessão em andamento
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser avisado ou ter o agendado adiado quando ele cai no meio de um foco manual.
- **Hoje:** `RunDue` só ignora se `ControlsLocked`/Angry; fora disso chama `_timer.Reset()` e inicia o plano agendado (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:277-295`), apagando a sessão em curso sem confirmação.
- **Por que é básico:** perda silenciosa de progresso por um evento de fundo.
- **Impacto:** o foco some no meio e o usuário não entende o porquê.
- **Mínimo viável:** perguntar/adiar quando `PlanActive && !locked`, ou agendar para depois do plano.

## A31 — Sem validação de conflito entre agendamentos
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** o formulário impedir dois inícios sobrepostos.
- **Hoje:** `Insert` só ordena a lista (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:144-162`); não existe checagem de sobreposição (a rotina semanal tem `OverlapsLocked`, `src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:425-427`; o avulso não).
- **Por que é básico:** evita dado inconsistente.
- **Impacto:** dois inícios no mesmo intervalo disparam e um reseta o outro.
- **Mínimo viável:** validar sobreposição no avulso e entre avulso × rotina.

## A32 — Sem lembrete antes do início agendado
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** aviso "começa em 5 min" para se preparar.
- **Hoje:** o agendamento só dispara no horário (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:244-274`); não há lead time nem notice antes (busca por `Reminder`/`lead` no domínio vazia) e o ocioso mostra a hora sem alerta (`src/Island.App/ViewModels/IdlePresenter.cs:130-143`).
- **Por que é básico:** preparar o ambiente é parte do método.
- **Impacto:** o início pega o usuário no meio de outra coisa.
- **Mínimo viável:** notice/som X min antes, configurável.

## A33 — Rotina semanal sem exceções além de "pular amanhã"
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** pular hoje, pular uma ocorrência específica ou pausar a rotina por férias/feriado.
- **Hoje:** `SkipNextDay` só marca amanhã (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:219-230`) e a janela tem um único botão "Pular amanhã" (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml:75-76`); não há exceção de hoje, de ocorrência ou de período.
- **Por que é básico:** rotina semanal sempre tem exceção.
- **Impacto:** para pular hoje o usuário desliga a rotina inteira.
- **Mínimo viável:** pular ocorrência de hoje e intervalo de datas.

## A34 — Sem desfazer o "pular amanhã"
- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** desfazer o pulo com um clique.
- **Hoje:** depois do pulo, a janela só mostra o status (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:251-264`) e não há botão de cancelar; o único caminho é desligar/ligar a rotina, que limpa o pulo em `SetEnabled` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:204-216`).
- **Por que é básico:** erro de clique precisa ser reversível.
- **Impacto:** o usuário desativa a rotina só para consertar.
- **Mínimo viável:** botão "Desfazer pulo".

## A35 — Sem visão das próximas ocorrências; rotina semanal não aparece no ocioso
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver as próximas ocorrências (hoje/7 dias) e o próximo foco na cápsula.
- **Hoje:** a janela lista faixas, não datas (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:267-281`); o ocioso recebe só `_schedule.Pending` do agendamento avulso (`src/Island.App/ViewModels/IdlePresenter.cs:79`), então a rotina semanal nunca aparece na cápsula.
- **Por que é básico:** saber "quando é o próximo foco" é o mínimo de agenda.
- **Impacto:** o usuário não vê a rotina funcionando.
- **Mínimo viável:** próximas 5 ocorrências e incluir a semanal no ocioso.

## A36 — Fim de faixa encerra a sessão sem aviso
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser avisado quando a faixa semanal termina e encerra a sessão.
- **Hoje:** no fim da faixa, `StopManagedSession` chama `_timer.Reset()`/`EndScheduledSession` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:363-366`) sem notice nem transição; `Reset` não emite `Transitioned` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:387-406`).
- **Por que é básico:** a sessão sumir sem explicação parece defeito.
- **Impacto:** perda de foco inesperada.
- **Mínimo viável:** notice "faixa terminou" com resumo.

## A37 — Início perdido há mais de 4 h é descartado em silêncio
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** saber que um início agendado foi perdido.
- **Hoje:** inícios salvos com mais de 4 h de atraso são filtrados sem aviso (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:35,71-75`); nenhum notice/log chega ao usuário.
- **Por que é básico:** silêncio esconde a falha de o app estar fechado.
- **Impacto:** o usuário acha que agendou e não aconteceu.
- **Mínimo viável:** notice "início perdido às HH:mm" ao abrir.

## A38 — Lista de sites bloqueados fixa no código
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher/editar a lista de sites bloqueados.
- **Hoje:** a lista é `static` em `src/Island.Core/Pomodoro/BlockedSites.cs:18-56` e nenhuma setting guarda sites (`src/Island.Core/Configuration/IslandSettings.cs:45-57`); busca por configuração de `BlockedSites` não encontra nada.
- **Por que é básico:** distração é pessoal (TikTok, Twitch, LinkedIn, fórum).
- **Impacto:** bloqueia o que não incomoda e deixa o que incomoda.
- **Mínimo viável:** lista editável por nome/palavra no settings.

## A39 — Frases de desbloqueio fixas no código
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** usar uma frase própria (ou mais curta) para o desbloqueio.
- **Hoje:** `UnlockPhrases.All` tem 8 frases fixas (`src/Island.Core/Pomodoro/UnlockPhrases.cs:12-22`) e `Pick` sorteia (`src/Island.Core/Pomodoro/AngryPomodoro.cs:22-25`); não há setting.
- **Por que é básico:** o atrito é pessoal; frase longa demais vira colar/automatizar.
- **Impacto:** o usuário desiste de usar o Angry.
- **Mínimo viável:** frase ou lista personalizada, com comprimento mínimo.

## A40 — Só navegadores são bloqueados (sem apps)
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** bloquear apps de distração (Discord, Steam, TikTok/YouTube instalados, jogos).
- **Hoje:** o bloqueio só olha processos de navegador (`src/Island.Core/Pomodoro/BlockedSites.cs:58-62`; `src/Island.Windows/Focus/ForegroundSiteGuard.cs:97,110-111,175`); não há lista de apps bloqueados em settings nem no código (busca por `BlockedApps`/`BlockedProcesses` vazia).
- **Por que é básico:** no Windows a distração costuma ser app, não aba.
- **Impacto:** o Angry é contornável em um clique.
- **Mínimo viável:** lista de processos a minimizar durante o foco.

## A41 — Angry começa sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar tamanho e regras antes de começar uma sessão que não pode ser pausada.
- **Hoje:** clicar Play com Angry selecionado chama `Engage` direto (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:454-465`), que trava pausa/reset (`src/Island.Core/Pomodoro/AngryPomodoro.cs:127-129`); não há diálogo.
- **Por que é básico:** é uma sessão "sem saída"; o usuário precisa confirmar.
- **Impacto:** começa um compromisso rígido sem querer.
- **Mínimo viável:** confirmar "Angry ×N, sem pausa".

## A42 — Celular "bloqueado" sem confirmação real
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** confirmação de que o celular realmente bloqueou.
- **Hoje:** `Delivered` significa só que o FCM aceitou (`src/Island.App/App.xaml.cs:371-379`); o próprio README admite que a ilha mostra "Celular bloqueado" mesmo sem a acessibilidade ligada no aparelho (`README.md:139`).
- **Por que é básico:** bloqueio que não bloqueia é pior que ausente.
- **Impacto:** falsa sensação de proteção.
- **Mínimo viável:** estado "confirmado/pendente" ou aviso explícito.

## A43 — Sem toast nativo do Windows
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** notificação do Windows quando a etapa termina, mesmo com a ilha escondida/pausada.
- **Hoje:** todo aviso do Pomodoro é `Notice` da ilha (`src/Island.Core/Pomodoro/PomodoroNotices.cs:5-32`; `src/Island.App/App.xaml.cs:254-258`); não existe `AppNotificationManager`/toast nativo no app (busca por `AppNotification` só encontra comentários).
- **Por que é básico:** a ilha pode estar oculta (tela cheia/pausa/mini) e o som pode estar mudo.
- **Impacto:** o usuário perde o fim do foco/descanso.
- **Mínimo viável:** toast nativo opcional como fallback.

## A44 — Tela de transição fixa em 6 s, sem ajuste
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar a duração ou desligar essa tela.
- **Hoje:** `LifetimeSeconds = 6` é constante (`src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:17,115-119`) e não há setting (busca por `TransitionSeconds` vazia).
- **Por que é básico:** uma tela cheia de 6 s interrompe quem está em outro app ou jogo.
- **Impacto:** o usuário fecha toda vez; atrapalha o fluxo.
- **Mínimo viável:** 0/3/6/10 s configurável em Settings.

## A45 — Sem integrar "Não perturbe"/Focus Assist
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ativar o Não Perturbe junto com o foco.
- **Hoje:** não há integração; busca por `FocusAssist`/`Dnd`/`Não perturbe` em `src` vazia, e os notices do Pomodoro são todos urgentes (`src/Island.Core/Pomodoro/PomodoroNotices.cs:5`).
- **Por que é básico:** foco é não ser interrompido por notificações do sistema.
- **Impacto:** o foco é furado por toasts de outros apps.
- **Mínimo viável:** ligar/desligar o modo por sessão (best effort).

## A46 — Sem som de tique/ambiente
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** som de tique opcional durante o foco.
- **Hoje:** os únicos sons são os 4 cues de transição (`src/Island.App/Audio/PomodoroSoundWave.cs:16-22`), tocados apenas no evento (`src/Island.App/App.xaml.cs:257-258`); busca por `TickSound`/`PomodoroTick` vazia.
- **Por que é básico:** muita gente se concentra com tique/ruído constante.
- **Impacto:** o usuário põe música/ruído por fora.
- **Mínimo viável:** tique suave ligável, com volume próprio.

## A47 — Volume dos sons não ajustável
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** volume do aviso (de preferência por perfil).
- **Hoje:** Settings só tem liga/desliga e o tipo (`src/Island.App/ViewModels/SettingsViewModel.cs:106-130`); o PCM é sintetizado com ganho fixo (`src/Island.App/Audio/PomodoroSoundWave.cs:16-22`) e o player não recebe volume (`src/Island.App/Audio/PomodoroSoundPlayer.cs:17-40`).
- **Por que é básico:** som alto interrompe; baixo não avisa.
- **Impacto:** o usuário desliga os sons de vez.
- **Mínimo viável:** volume 0–100 global e por perfil.

## A48 — Alarme de fim de descanso não é distinto nem insistente
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** alarme próprio do fim do descanso, que insista até ser notado.
- **Hoje:** o fim do descanso usa o som de foco (`src/Island.Core/Pomodoro/PomodoroSoundKind.cs:9-16`) tocado uma vez (`src/Island.App/Audio/PomodoroSoundPlayer.cs:26-30`); sem repetição ou confirmação.
- **Por que é básico:** voltar ao foco é o momento que mais falha (descanso esticado).
- **Impacto:** o usuário volta tarde e come o próximo ciclo.
- **Mínimo viável:** alarme próprio com repetição configurável.

## A49 — Sem lembrete de alongamento durante o descanso
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** um lembrete para alongar/beber água no descanso.
- **Hoje:** só existe o texto estático "Respire, alonge-se" na tela de transição (`src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:55`); nenhum lembrete temporizado ou setting.
- **Por que é básico:** descanso ativo é parte do método.
- **Impacto:** o descanso vira rolagem de celular.
- **Mínimo viável:** um lembrete por descanso.

## A50 — Sem atalho global de play/pause/pular
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** controlar a sessão de qualquer app.
- **Hoje:** só existem hotkeys de clipboard, notas e captura (`src/Island.App/App.xaml.cs:327-332,119-127`; `src/Island.App/Composition/ServiceRegistration.cs:181-184`; `src/Island.App/Shell/GameNotesHotkeyController.cs:38`); busca por hotkey do Pomodoro vazia.
- **Por que é básico:** durante o foco o usuário está em outro app.
- **Impacto:** precisa abrir a ilha para pausar ou pular.
- **Mínimo viável:** um atalho configurável para play/pause e pular.

## A51 — Sessão invisível na pílula Mini e no tray
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver o tempo restante com a ilha minimizada ou na bandeja.
- **Hoje:** a Mini pill é "bare capsule, no content" (`src/Island.App/Shell/IslandWindow.xaml:38`) e o tray tem `Text = "Dynamic Island"` fixo (`src/Island.App/Shell/TrayIconService.cs:53-57`); nada do Pomodoro aparece fora da Compact/Expanded.
- **Por que é básico:** quem minimiza a ilha perde o timer.
- **Impacto:** mantém a ilha aberta sempre (atrapalha) ou perde o foco.
- **Mínimo viável:** contador na Mini e no tooltip/menu do tray.

## A52 — Sair do app com foco ativo sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** aviso ao sair com foco em andamento (com opção de continuar depois).
- **Hoje:** `RequestQuit` só consulta a frase do Angry (`src/Island.App/App.xaml.cs:407-423`, `ConfirmNotLocked` em 522-523) e o flush de notas; foco normal é descartado no `Shutdown` sem aviso.
- **Por que é básico:** fechar o app não deveria apagar a sessão em silêncio.
- **Impacto:** progresso perdido ao fechar/reiniciar.
- **Mínimo viável:** confirmar "há um foco em andamento" com sair/cancelar.
