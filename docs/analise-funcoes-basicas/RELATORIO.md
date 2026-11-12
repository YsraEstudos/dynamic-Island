# Análise profunda — funções básicas ausentes no Dynamic Island

**Data:** 10 de novembro de 2026
**Versão auditada:** 0.4.40 (commit `f783d56` + alterações locais)
**Aplicação:** Dynamic Island for Windows — WPF / .NET 10 — 441 arquivos-fonte (~42.200 linhas), 16 widgets, 53 arquivos XAML, 39 configurações em `settings.json`, 162 classes de teste (~1.164 casos)
**Método:** 5 auditorias independentes em paralelo (subagentes) + consolidação do lead; 271 itens brutos, 24 duplicidades entre domínios consolidadas, **247 itens distintos**, cada um com evidência `arquivo:linha` ou ausência confirmada por busca no código. Auditoria estática (o app não foi executado).

---

## Resumo executivo

O Dynamic Island não é um app abandonado nem tecnicamente fraco. Ele tem captura de tela com Media Foundation, mixer por sessão de áudio, bloqueio de sites no Pomodoro raivoso, DPAPI para o token do celular, auto-update com SHA-256, modo demo, soak test e 1.164 casos de teste. O problema que esta auditoria encontrou é outro: **o produto cresceu em superfície (16 widgets) mais rápido do que cresceu em funções básicas de ciclo de vida — dados, recuperação, configuração, acessibilidade, distribuição e memória do que o usuário fez.**

Em uma frase: **o app faz muita coisa, mas ainda não é um lugar em que o usuário pode confiar para guardar notas, agenda, histórico de foco e configurações — e não tem o "básico de Windows" que faz um utilitário parecer instalado, e não baixado.**

### Números

| Domínio | Itens | Críticas | Altas | Médias | Baixas |
|---|---:|---:|---:|---:|---:|
| **A — Pomodoro e foco** | 51 | 2 | 11 | 33 | 5 |
| **B — Ilha, estante, configurações e UX do Windows** | 49 | 2 | 15 | 28 | 4 |
| **C — Produtividade e ferramentas** (notas, calendário, orçamento, tarefas, calculadora, contador, timer, analisador, sons de foco, arquivos, clipboard) | 57 | 2 | 18 | 32 | 5 |
| **D — Jogo, captura, mixer e desempenho** | 51 | 5 | 15 | 24 | 7 |
| **E — Engenharia, dados, distribuição e operação** | 39 | 3 | 17 | 18 | 1 |
| **Total** | **247** | **14** | **76** | **135** | **22** |

**Legenda de severidade:**
- **Crítica** — perda de dados, ação irreversível sem aviso, fluxo central quebrado ou risco de segurança/privacidade.
- **Alta** — função que qualquer app maduro da categoria tem; o usuário sente falta no uso semanal; geralmente há contorno externo.
- **Média** — conveniência importante ou configuração esperada; o uso continua possível com atrito.
- **Baixa** — polimento e consistência.

### Os cinco padrões por trás dos 247 itens

1. **Dados sem rede de segurança.** São 12 arquivos JSON espalhados em `%LocalAppData%\DynamicIsland`, sem backup, sem exportação unificada, sem versão de schema na maioria e sem recuperação na interface quando um arquivo é corrompido (E01–E07, C04). O app já sabe mover o arquivo ruim para `.bad`; o usuário, não.
2. **Ações destrutivas de um clique.** Reset do Pomodoro apaga um plano em andamento (A05); "Reset to defaults" apaga todas as preferências (B06); o início agendado pode zerar um foco manual (A30); "Clear history" limpa o clipboard (B38); apagar nota do jogo é definitivo (D50). Quase nenhuma dessas ações pede confirmação ou oferece desfazer.
3. **O app mede, mas não lembra.** Pomodoro não tem histórico, estatísticas nem meta (A22–A24); o histórico de desempenho fica em memória (D24); o clipboard morre ao fechar (C54); o File Tray também (C48). O que não é lembrado não vira hábito.
4. **Falta o básico de Windows.** Não há instalador/desinstalador (E15), notificação nativa do Windows (E28), tema claro/alto contraste (B11), idioma consistente (B13), atalhos remapeáveis (B04) nem operação por teclado/leitor de tela (B57). A ilha é boa no mouse e fraca fora dele.
5. **Distribuição ainda é "build de dev".** Zip manual, dependência do .NET 10 sem aviso, sem CI, sem assinatura de código, sem rollback de update, release publicada sem rodar a suíte (E15–E23, E48). O próprio README registra três versões em que a ilha ficou fechada após atualizar.

### Se você só puder corrigir 10 coisas

1. **Backup/restauração de todos os dados (E01)** e recuperação visível de `notes.json` corrompido (C04).
2. **Editar e excluir eventos, tarefas e aniversários do calendário (C14)** — hoje um erro de digitação é permanente.
3. **Avisar quando salvar/ler configurações falha (B09)** — hoje o usuário acha que "o app se desconfigura sozinho".
4. **Confirmação/desfazer nos resets (A05, B06)** e não apagar a sessão em andamento quando um agendamento dispara (A30).
5. **Lista completa das notas do jogo (D41)** — hoje a 4ª nota em diante é inacessível.
6. **Histórico + estatísticas + meta do Pomodoro (A22, A23, A24)** — é o que transforma timer em método.
7. **Rollback de update (E19) e diálogo global de erro (E11)** — o app já "sumiu" sem aviso em versões passadas.
8. **Busca de verdade no clipboard (B37)** — o botão de lupa existente é declaradamente decorativo.
9. **Instalador/desinstalador (E15) e notificação nativa (E28)** — o mínimo para não parecer um projeto baixado de zip.
10. **Tema claro/contraste (B11), idioma consistente (B13) e acessibilidade da ilha (B57)** — o app não é operável sem mouse.

---

## Sobre o exemplo citado: "o Pomodoro não tem perfis de estudo/trabalho"

Esse exemplo específico **já está implementado na versão auditada** — e a auditoria foi além dele:

- Há três perfis padrão com tempos diferentes: Estudo 25/5, Leitura 50/10 e Trabalho 45/15 (`src/Island.Core/Pomodoro/PomodoroProfile.cs:21-26`).
- A interface permite criar, editar e excluir perfis (até 24), com nome, foco, descanso, sons e avanço automático (`src/Island.App/Shell/PomodoroSessionWindow.xaml:124-155`; `PomodoroProfile.cs:5-16,54-58`).
- Escolher um perfil prepara a sessão com os tempos e sons dele (`PomodoroProfiles.Apply`).

O instinto por trás do exemplo, porém, estava certo: **o perfil é incompleto.** O que ainda falta *dentro* da ideia de perfil e do Pomodoro como produto:

- não inclui a quantidade de pomodoros (A12);
- não inclui objetivo padrão (A13);
- não inclui descanso longo (A01);
- não tem histórico, estatísticas, meta nem sequência de dias (A22, A23, A24);
- não pode ser duplicado, reordenado, exportado/importado nem restaurado ao padrão (A14, A16);
- não pode ser usado por um agendamento (A21, A29);
- excluir um perfil não pede confirmação (A15).

Ou seja: se a meta era "perfil de estudo e perfil de trabalho com tempos diferentes", isso já existe. A lacuna real é **perfil completo e mensurável**, e é assim que a Parte A trata o assunto.

---

## Como ler este relatório

- Cada item tem um ID (`A01`, `B12`, `C34`, `D41`, `E19`) e um bloco de 7 campos: **Severidade**, **Tipo**, **Usuário espera**, **Hoje** (com evidência `arquivo:linha` ou ausência confirmada), **Por que é básico**, **Impacto** e **Mínimo viável**.
- **Tipo** classifica a lacuna: `Função ausente` (não existe), `Função parcial` (existe, mas não fecha o fluxo), `Lacuna de configuração` (existe no código, não na UI), `Lacuna de dados` (não persiste/exporta), `Problema básico de UX` e `Lacuna de acessibilidade`.
- **"Hoje" não é opinião:** aponta o arquivo e a linha do comportamento atual, ou diz explicitamente que a busca por um identificador não encontrou nada. Ausências são relativas ao código-fonte da versão 0.4.40.
- **"Mínimo viável"** não é a única solução; é o menor recorte que já resolve a dor.
- **Itens não são bugs internos.** Refatoração, estilo e arquitetura só entraram quando o usuário final sente (ex.: erro engolido, dados que somem).
- 24 itens brutos foram consolidados por serem o mesmo problema visto de dois domínios; estão listados no **Apêndice B**. Por isso os IDs têm lacunas.

---

## Prioridades recomendadas

Ordem sugerida por impacto × risco. **Esforço:** P = até 1 dia; M = 2 a 5 dias; G = mais de uma semana (um desenvolvedor).

### P0 — Perda de dados, ação irreversível e fluxo central quebrado (todos os 14 críticos)

| # | ID | Lacuna | Por que primeiro | Esforço |
|---:|---|---|---|---|
| 1 | C04 | Notas sem backup/restauração; `notes.json` corrompido abre vazio | Perda permanente de notas do usuário, sem caminho de volta | M |
| 2 | E01 | Sem backup/restauração unificada dos 12 JSONs | Formatar o PC, trocar de máquina ou update ruim apaga tudo | M/G |
| 3 | C14 | Calendário não permite editar nem excluir eventos/tarefas/aniversários | Qualquer erro de digitação fica para sempre; agenda perde confiança | M |
| 4 | B09 | Falha ao salvar/ler configurações é silenciosa | Usuário ajusta tudo, reinicia e "desconfigurou"; acha que é bug | M |
| 5 | A05 | Reset do Pomodoro apaga plano em andamento sem confirmar | Um clique ao lado do Play apaga 30–90 min planejados | P |
| 6 | A30 | Início agendado chama `Reset()` e apaga foco manual em curso | Perda silenciosa de sessão disparada por evento de fundo | P/M |
| 7 | B06 | "Reset to defaults" apaga todas as preferências sem confirmação | Ação de um clique em janela com scroll | P |
| 8 | D41 | Notas do jogo: 4ª nota em diante fica inacessível | Função central do widget não escala além de 3 notas | M |
| 9 | E11 | Exceções engolidas; app pode fechar sem diálogo | Já ocorreu na v0.4.12; sem diagnóstico o suporte fica cego | M |
| 10 | E19 | Auto-update sem cópia da versão anterior/rollback | Já houve três releases em que a ilha não abriu após atualizar | M |
| 11 | D01 | Captura só do monitor inteiro; sem região/janela | 80–90% do arquivo é irrelevante; privacidade (chat/notificações) | G |
| 12 | D03 | Gravação de vídeo sem áudio do sistema e sem microfone | Clipe de jogo sem áudio perde o momento | G |
| 13 | D12 | Tela cheia exclusiva (DirectX) sai preta | Falha total justamente no cenário de jogo | G |
| 14 | D17 | Sem FPS/frametime/1% low no widget de desempenho | FPS é a métrica nº 1 de quem joga; widget mede o PC, não o jogo | G |

### P1 — Funções básicas sentidas no uso semanal (14 altos)

| # | ID | Lacuna | Por que agora | Esforço |
|---:|---|---|---|---|
| 15 | A22 | Sem histórico de sessões do Pomodoro | Sem registro, nada do que o app mede pode ser consultado depois | M |
| 16 | A23 | Sem estatísticas (pomodoros, tempo focado, médias) | É o que dá sentido ao método e sustenta a motivação | M |
| 17 | A24 | Sem meta diária/semanal nem sequência de dias | Sem alvo, o uso vira esporádico | P/M |
| 18 | A01 | Sem descanso longo após N ciclos | Metade da técnica Pomodoro; hoje só há foco+descanso únicos | P/M |
| 19 | A08 | Sessão normal não sobrevive a fechar/reiniciar/crash | O app reinicia para atualizar; o foco some e recomeça do zero | M |
| 20 | C01 | Notas sem lembretes/notificações | O fluxo de captura não fecha o ciclo (registrar → ser lembrado) | M |
| 21 | C39 | Timer termina com um `SystemSounds` e flash; sem alarme insistente | Usuário perde o fim do timer com facilidade | P/M |
| 22 | C48 | File Tray não persiste entre reinícios | Bandeja de arquivos volátil não é confiável para trabalho | P |
| 23 | B05 | Lista de jogos (`GameProcesses`) só editável no JSON | O README manda configurar em Settings, mas o campo não existe | P |
| 24 | B04 | Atalhos globais fixos, sem lista nem remapeamento | Conflito de atalho é comum; o usuário não tem saída | M |
| 25 | B02 | Sem tela "Sobre"; versão instalada não aparece em lugar nenhum | Primeiro pedido de suporte é "qual versão você usa?" | P |
| 26 | B03 | Sem ajuda, tutorial ou experiência de primeira execução | Gestos (segurar 350 ms para arrastar) não são descobríveis | M |
| 27 | B26 | Monitor salvo por índice de enumeração | Hot-plug/reboot pode jogar a ilha para outra tela | P/M |
| 28 | A38 | Lista de sites bloqueados do Angry fixa no código | Distração é pessoal; bloqueia o que não incomoda e libera o que incomoda | M |

### P2 — Sensação de produto inacabado (8 itens)

| # | ID | Lacuna | Por que agora | Esforço |
|---:|---|---|---|---|
| 29 | E15 | Instalação por zip, sem instalador, atalho ou desinstalador | Não aparece em "Apps e recursos"; desinstalar deixa dados órfãos | M |
| 30 | E28 | Sem notificação nativa do Windows (toast) | Ilha escondida/pausada = usuário perde o aviso | M |
| 31 | B13 | Interface mistura inglês e português na mesma janela | Percepção imediata de produto inacabado | M |
| 32 | B11 | Sem tema claro, sem seguir o tema do Windows e sem alto contraste | Preferência de sistema ignorada; acessibilidade visual | G |
| 33 | B57 | Ilha não é operável por teclado nem por leitor de tela | Acessibilidade básica; hoje tudo depende do mouse | G |
| 34 | E48 | Sem CI: nenhum build/teste automático no repositório | Regressões como a v0.4.12 só aparecem em produção | P |
| 35 | B31 | Abrir o app de novo não avisa nem traz a janela existente | Usuário acha que não abriu e reinstala | P |
| 36 | A40 | Angry bloqueia só navegadores, não apps (Discord, Steam etc.) | No Windows a distração costuma ser app, não aba | M |

---

---

## Parte A — Pomodoro e foco (51 itens)

Escopo: timer e ciclos, perfis e objetivo, agendamento avulso e rotina semanal, Pomodoro raivoso (bloqueio/desbloqueio e celular), sons, tela de transição, controles na ilha/tray e persistência do domínio.
Método: leitura de `src/Island.Core/Pomodoro`, das janelas/widgets em `src/Island.App` e dos testes `Pomodoro*`; cada item traz evidência `arquivo:linha` ou ausência confirmada por busca textual. O README foi usado só para separar o que já existe do que falta.

### A01 — Descanso longo após N ciclos
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** depois de 4 pomodoros, um descanso longo (15–30 min) antes do próximo bloco, com o intervalo configurável.
- **Hoje:** só há foco e descanso únicos: o perfil tem um par de tempos (`FocusMinutes`/`BreakMinutes`, `src/Island.Core/Pomodoro/PomodoroProfile.cs:9-13`), settings só `PomodoroFocusMinutes`/`PomodoroBreakMinutes` (`src/Island.Core/Configuration/IslandSettings.cs:45-48`) e o timer só tem `Focus/Break/Prep` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:6`); busca por `LongBreak`/`long break` não retorna nada.
- **Por que é básico:** é metade da técnica Pomodoro (pausas curtas frequentes + pausa longa periódica); sem isso o app cronometra, mas não estrutura a recuperação.
- **Impacto:** fadiga em estudo/trabalho longo; o usuário mantém um timer externo.
- **Mínimo viável:** N por perfil, minutos do descanso longo e troca automática a cada N ciclos, visível no track.

### A02 — Pular a etapa atual mantendo o plano
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** encerrar o foco agora e ir para o descanso (ou voltar ao foco) sem cancelar o plano.
- **Hoje:** não existe "skip": `SetPhase` limpa o plano (`src/Island.Core/Pomodoro/PomodoroTimer.cs:409-425`) e `SelectPhase` recusa clique com plano ativo (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:364-371`); busca por `SkipPhase`/`Pular` no domínio vazia.
- **Por que é básico:** na vida real a tarefa acaba antes ou depois; sem pular, o usuário espera à toa ou perde o plano.
- **Impacto:** ciclos desalinhados do dia; abandono do Pomodoro.
- **Mínimo viável:** botão "Pular" que avança a fase atual do plano (ou encerra o plano no último descanso).

### A03 — Estender ou encurtar a etapa em andamento
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** +5/−5 min no foco ou no descanso atuais sem perder o plano.
- **Hoje:** `SetMinutes` é a única mudança de duração e limpa o plano (`src/Island.Core/Pomodoro/PomodoroTimer.cs:427-443`); a régua fica travada enquanto roda (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:223-228`); busca por `Extend`/`AddMinutes` no domínio vazia.
- **Por que é básico:** quem está rendendo estende o foco; quem precisa sair encurta.
- **Impacto:** perda do plano e do contador por um ajuste corriqueiro.
- **Mínimo viável:** +5/−5 na fase corrente, registrando a duração real.

### A04 — Iniciar ou adiar o descanso durante o plano
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** começar o descanso antes da hora ou empurrá-lo quando não pode parar agora.
- **Hoje:** a pílula Break só funciona sem plano (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:188-198` mostra as pílulas; `364-371` ignora o clique com plano ativo) e o README diz que "o plano decide a fase" (`README.md:119`); não há adiar.
- **Por que é básico:** descanso é parte do ciclo e precisa da mesma flexibilidade do foco.
- **Impacto:** descanso perdido ou fora de hora; o usuário pausa sem descansar.
- **Mínimo viável:** "Iniciar descanso agora" e "adiar 5 min" durante o foco, dentro do plano.

### A05 — Cancelar o pomodoro sem confirmação
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmação (ou desfazer) ao cancelar um plano com progresso.
- **Hoje:** o botão Reset chama `Pomodoro.Reset()` direto (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:88`) e `Reset` apaga plano e ciclo na hora (`src/Island.Core/Pomodoro/PomodoroTimer.cs:387-406`); não há diálogo nem undo.
- **Por que é básico:** é um clique acidental em botão colado ao Play e apaga 30–90 min planejados.
- **Impacto:** perda de progresso e quebra de confiança no app.
- **Mínimo viável:** confirmar quando houver plano/progresso; ou undo de alguns segundos.

### A06 — Sem pausa automática por inatividade
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** pausar o foco quando o PC fica ocioso/tela bloqueada e retomar ao voltar.
- **Hoje:** o domínio não lê inatividade do usuário (sem `GetLastInputInfo`/hook; busca em `src` só encontra idle de timer, não do usuário); em `OnSessionSwitch`/`OnPowerModeChanged` o app apenas atualiza a rotina semanal (`src/Island.App/App.xaml.cs:546-556`) e não há setting de pausa por inatividade.
- **Por que é básico:** foco real termina quando a pessoa sai; contagem "fantasma" invalida o registro.
- **Impacto:** o app contabiliza tempo que não existiu.
- **Mínimo viável:** pausar após X min sem input, com opção de ligar/desligar.

### A07 — Suspensão do PC sem reconciliação do foco
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ao voltar da suspensão, ver o que perdeu e decidir retomar/ajustar, sem o cochilo contar como foco.
- **Hoje:** o relógio padrão do timer "keeps counting through sleep" (`src/Island.Core/Pomodoro/PomodoroTimer.cs:83-88`) e o primeiro tick ao acordar conclui só uma fase (`src/Island.Core/Pomodoro/PomodoroTimer.cs:484-506`); não há tratamento de suspensão para o timer (`src/Island.App/App.xaml.cs:551-556`).
- **Por que é básico:** suspender/fechar a tampa é situação diária.
- **Impacto:** foco dado como concluído e descanso começando sem o usuário.
- **Mínimo viável:** detectar `PowerModes.Resume`, pausar e avisar "foco interrompido por suspensão".

### A08 — Sessão normal não sobrevive a fechar/reiniciar/crash
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** continuar a sessão normal depois de fechar/reabrir o app ou de uma queda.
- **Hoje:** `PomodoroTimer.Capture/Restore` existem (`src/Island.Core/Pomodoro/PomodoroTimer.cs:247-287`), mas só a sessão Angry é salva, e apenas antes de instalar update (`src/Island.Windows/Pomodoro/JsonAngryResumeStore.cs:8-11`, uso em `src/Island.App/App.xaml.cs:443,470-474`); foco normal não tem store (`src/Island.App/Composition/ServiceRegistration.cs:153-159`).
- **Por que é básico:** o app reinicia para atualizar e fecha com o PC.
- **Impacto:** a sessão some e o usuário recomeça do zero.
- **Mínimo viável:** salvar o `PomodoroTimerState` ao sair/periodicamente e restaurar com aviso.

### A09 — Pré fixo de 5 minutos
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar ou desligar o preparo antes do foco.
- **Hoje:** `PrepMinutes = 5` é constante (`src/Island.Core/Pomodoro/PomodoroTimer.cs:23-24`), a régua ignora o Pré (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:226`) e não há setting (o README confirma "preparo fixo de 5 minutos", `README.md:130`).
- **Por que é básico:** preparação é preferência pessoal.
- **Impacto:** quem não quer Pré perde 5 min por dia; quem precisa de 10 não tem.
- **Mínimo viável:** duração 0–15 min em Settings ou opção de desligar.

### A10 — Sessão de foco livre (contagem crescente)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** registrar uma sessão de foco sem ciclos quando não quer descanso programado.
- **Hoje:** `PomodoroPhase` só tem `Focus/Break/Prep` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:6`); o cronômetro crescente existe só no widget Timer genérico (`src/Island.App/Views/Widgets/TimerWidget.xaml.cs:185-190`), fora do Angry, do celular e de qualquer estatística.
- **Por que é básico:** estudo/trabalho nem sempre cabe em ciclos de 25 min.
- **Impacto:** usuário usa cronômetro externo e o app não registra o tempo.
- **Mínimo viável:** modo "foco livre" que soma tempo no dia/perfil.

### A11 — Previsão de término enganosa no avanço manual
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** no modo manual, ver duração restante sem horário prometido (como o README descreve).
- **Hoje:** o widget calcula `DateTime.Now + rest` sempre, inclusive no manual, e só acrescenta " · manual" (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:300-309`); o README afirma que a previsão manual mostra a duração "sem prometer um horário de término" (`README.md:124`).
- **Por que é básico:** horário errado gera compromisso errado.
- **Impacto:** o usuário planeja em cima de um fim que não acontece.
- **Mínimo viável:** mostrar "N min restantes + esperas" em vez de "até HH:mm" no manual.

### A12 — Perfil sem quantidade de pomodoros
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher o perfil e já vir com o número de pomodoros (ex.: Trabalho ×4).
- **Hoje:** `PomodoroProfile` não tem `Cycles` (`src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16`) e `Apply` não toca em `PomodoroCycles` (`src/Island.Core/Pomodoro/PomodoroProfile.cs:54-58`); o número é global (`src/Island.Core/Configuration/IslandSettings.cs:47-48`) e ajustado à parte no stepper.
- **Por que é básico:** perfil é o preset da sessão; sem ciclos ele está incompleto.
- **Impacto:** trocar de perfil exige reajustar ciclos sempre.
- **Mínimo viável:** campo "Pomodoros" no perfil, aplicado ao preparar a sessão.

### A13 — Perfil sem objetivo padrão
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** cada perfil com objetivo/assunto padrão (ex.: "Revisar cálculo").
- **Hoje:** o objetivo é uma string global (`src/Island.Core/Configuration/IslandSettings.cs:50`) aplicada por `Goal` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:50-59`), fora do perfil (`src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16`); trocar de perfil mantém o objetivo anterior.
- **Por que é básico:** é o contexto do foco; repetir toda vez é atrito.
- **Impacto:** sessões sem objetivo ou com objetivo errado.
- **Mínimo viável:** `Goal` no perfil, pré-preenchido ao selecionar.

### A14 — Sem duplicar, reordenar ou restaurar perfis
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** duplicar, reordenar e voltar aos modelos padrão.
- **Hoje:** só existem `NewProfileCommand`, `SaveProfileCommand` e `DeleteProfileCommand` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:34-36`); `Profiles` é `ObservableCollection` sem `Move` (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:39`) e não há reset (busca por `Duplicate`/`RestoreDefault` vazia).
- **Por que é básico:** organizar 5–20 perfis é uso normal.
- **Impacto:** perfis desordenados e recriação manual.
- **Mínimo viável:** duplicar, subir/descer, restaurar Estudo/Leitura/Trabalho.

### A15 — Excluir perfil sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmação antes de excluir um perfil.
- **Hoje:** `DeleteProfile` remove na hora e aplica outro perfil sem aviso (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:140-153`); não há diálogo nem undo.
- **Por que é básico:** ação destrutiva com um clique.
- **Impacto:** perda de tempos/sons configurados.
- **Mínimo viável:** confirmar ou oferecer undo (o bloqueio do último perfil já existe, linha 36).

### A16 — Sem exportar/importar perfis
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** levar os perfis para backup e para outro PC.
- **Hoje:** os perfis vivem embutidos no `settings.json` (`src/Island.Core/Configuration/IslandSettings.cs:55`); busca por `Export`/`Import` no domínio e no ViewModel não encontra nada.
- **Por que é básico:** configuração valiosa e reutilizável.
- **Impacto:** retrabalho a cada troca de PC ou reset.
- **Mínimo viável:** JSON de perfis com import validado.

### A17 — Objetivo é texto único, sem lista de tarefas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** uma lista de tarefas/objetivos para escolher na sessão e riscar ao terminar.
- **Hoje:** existe só `PomodoroGoal`, texto de 160 caracteres (`src/Island.Core/Configuration/IslandSettings.cs:50`), sem id, estado ou lista; `CalendarTask` é de outro domínio e não é referenciado no Pomodoro (busca por `Task`/`Calendar` em `src/Island.Core/Pomodoro` vazia).
- **Por que é básico:** Pomodoro existe para executar tarefas; sem elas o timer é abstrato.
- **Impacto:** o usuário anota em outro app e a sessão fica sem contexto.
- **Mínimo viável:** lista simples com seleção por sessão e marcação de concluída.

### A18 — Sem tags/categorias na sessão
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** marcar a sessão com tag/categoria (disciplina, projeto, leitura).
- **Hoje:** nem perfil nem estado da sessão têm tag — `src/Island.Core/Pomodoro/PomodoroProfile.cs:5-16` e `src/Island.Core/Pomodoro/PomodoroResumeState.cs:7-24` só têm tempos/sons/objetivo; busca por `Tag` no domínio vazia.
- **Por que é básico:** agrupar tempo por área é a análise mínima.
- **Impacto:** qualquer estatística futura fica sem dimensão.
- **Mínimo viável:** tag opcional na sessão e filtro no histórico.

### A19 — Sem notas da sessão
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** anotar o que foi feito e o que ficou pendente ao fim da sessão.
- **Hoje:** não há campo de nota na sessão nem no histórico; o único texto é o objetivo curto (`SessionGoal`, `src/Island.Core/Pomodoro/PomodoroTimer.cs:53`).
- **Por que é básico:** o fim do foco é o momento natural de registrar a próxima ação.
- **Impacto:** nada do que foi produzido fica ligado à sessão.
- **Mínimo viável:** nota por sessão no resumo/histórico.

### A20 — Sem integração com o calendário existente
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver ou agendar foco no calendário e evitar choque com compromissos.
- **Hoje:** o app tem `CalendarAgenda` para outros widgets (`src/Island.App/Composition/ServiceRegistration.cs:194`), mas o domínio Pomodoro não referencia Calendar (busca em `src/Island.Core/Pomodoro` vazia) e o agendamento não consulta a agenda.
- **Por que é básico:** foco cabe dentro de uma agenda do dia.
- **Impacto:** foco agendado choca com reunião.
- **Mínimo viável:** mostrar os próximos compromissos no agendamento e avisar conflito.

### A21 — Sem perfil automático por horário/rotina
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** perfil por período (manhã = Trabalho, noite = Leitura) aplicado sem escolher toda vez.
- **Hoje:** a seleção é sempre manual (`src/Island.App/ViewModels/PomodoroSessionViewModel.cs:60-73`); faixas semanais têm nome/modo/tempos, mas não `ProfileId` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:9-17`), e o início avulso idem (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:12-13`).
- **Por que é básico:** automatizar o preset evita decisão diária.
- **Impacto:** sessões agendadas rodam com tempos digitados no formulário, não com o perfil.
- **Mínimo viável:** perfil opcional na faixa/agendamento e regra "perfil por período".

### A22 — Sem histórico de sessões
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** histórico com início/fim, objetivo, perfil, duração e pausas/interrupções.
- **Hoje:** não existe store de sessões: em `src/Island.Windows/Pomodoro/` só há agendamento, rotina semanal e retomada do Angry; o timer guarda apenas `PlanCycle` em memória, zerado ao fim (`src/Island.Core/Pomodoro/PomodoroTimer.cs:44-45,534-541`).
- **Por que é básico:** sem registro, nada do que o app mede hoje pode ser consultado amanhã.
- **Impacto:** o usuário não sabe seu desempenho e não há como provar progresso.
- **Mínimo viável:** gravar um registro por sessão/fase concluída em JSON local e uma lista simples.

### A23 — Sem estatísticas de uso
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver pomodoros feitos, tempo focado e médias por dia e por perfil.
- **Hoje:** busca por `Stats`/`Statistics`/`Streak`/`Heatmap` em `src` não retorna nada; o único contador é o `Cycle` da sessão atual (`src/Island.Core/Pomodoro/PomodoroTimer.cs:140-156`).
- **Por que é básico:** Pomodoro é um método de medição; sem números não há método.
- **Impacto:** motivação e ajuste de metas ficam impossíveis.
- **Mínimo viável:** painel "Hoje/Semana" com três números.

### A24 — Sem meta diária/semanal nem sequência
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** definir meta diária/semanal (N pomodoros ou X min) e ver sequência de dias.
- **Hoje:** não há settings de meta (`src/Island.Core/Configuration/IslandSettings.cs:45-57`) nem campo de sequência; busca por `DailyGoal`/`Streak` em `src` vazia.
- **Por que é básico:** meta é o objetivo do uso diário.
- **Impacto:** uso esporádico, sem alvo.
- **Mínimo viável:** meta configurável + barra de progresso na ilha.

### A25 — Sem relatório semanal/heatmap
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver padrões por hora e dia da semana.
- **Hoje:** não há dados históricos (A22) nem tela; busca por `Report`/`Heatmap` no domínio vazia.
- **Por que é básico:** ver padrões é o uso analítico mínimo.
- **Impacto:** nenhuma visão de tendência.
- **Mínimo viável:** tela semanal derivada do histórico.

### A26 — Sem exportar dados de foco
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** exportar o histórico (CSV/JSON) para planilha ou backup.
- **Hoje:** não há exportação no domínio; busca por `Export`/`Import` em `src/Island.Core/Pomodoro` e em `src/Island.App/ViewModels/PomodoroSessionViewModel.cs` vazia.
- **Por que é básico:** dados locais devem ser portáveis.
- **Impacto:** dados presos no app.
- **Mínimo viável:** botão para exportar CSV/JSON do histórico.

### A27 — Desistências do Angry não ficam registradas
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber quantas vezes desistiu de um foco Angry e quando.
- **Hoje:** `TryUnlock` só valida a frase e encerra (`src/Island.Core/Pomodoro/AngryPomodoro.cs:206-237`); nenhum contador/registro é gravado e o arquivo de retomada é transitório (`src/Island.Windows/Pomodoro/JsonAngryResumeStore.cs:8-11`).
- **Por que é básico:** o Angry existe para medir disciplina; sem registro não há feedback.
- **Impacto:** desistências invisíveis e nenhum histórico de resistência.
- **Mínimo viável:** registrar evento de desbloqueio no histórico.

### A28 — Agendamento avulso não repete
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** repetir um início agendado (diário/úteis/semanal) sem recriar na mão.
- **Hoje:** o início avulso tem `Add` de um único instante (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:108-117`) e a janela cobre no máximo 30 dias (`src/Island.App/Shell/ScheduleWindow.xaml.cs:22,148-158`); recorrência só existe no modelo separado de faixas semanais.
- **Por que é básico:** rotina de estudo se repete.
- **Impacto:** retrabalho e esquecimento.
- **Mínimo viável:** opção "repetir" no `ScheduledStart` com regras simples.

### A29 — Início agendado sem nome, objetivo ou perfil
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** nomear o início agendado e escolher objetivo/perfil.
- **Hoje:** `ScheduledStart` não tem nome/objetivo/perfil (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:12-17`); ao disparar, `StartPlan(..., sessionName: null, ...)` (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:285-287`) usa o objetivo global, e o formulário não tem esses campos (`src/Island.App/Shell/ScheduleWindow.xaml:106-244`).
- **Por que é básico:** o agendamento deveria preparar a sessão, não só ligar o relógio.
- **Impacto:** sessão agendada sem contexto e com objetivo esquecido.
- **Mínimo viável:** nome/objetivo e perfil opcionais no formulário e na linha.

### A30 — Início agendado pode apagar a sessão em andamento
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser avisado ou ter o agendado adiado quando ele cai no meio de um foco manual.
- **Hoje:** `RunDue` só ignora se `ControlsLocked`/Angry; fora disso chama `_timer.Reset()` e inicia o plano agendado (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:277-295`), apagando a sessão em curso sem confirmação.
- **Por que é básico:** perda silenciosa de progresso por um evento de fundo.
- **Impacto:** o foco some no meio e o usuário não entende o porquê.
- **Mínimo viável:** perguntar/adiar quando `PlanActive && !locked`, ou agendar para depois do plano.

### A31 — Sem validação de conflito entre agendamentos
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** o formulário impedir dois inícios sobrepostos.
- **Hoje:** `Insert` só ordena a lista (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:144-162`); não existe checagem de sobreposição (a rotina semanal tem `OverlapsLocked`, `src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:425-427`; o avulso não).
- **Por que é básico:** evita dado inconsistente.
- **Impacto:** dois inícios no mesmo intervalo disparam e um reseta o outro.
- **Mínimo viável:** validar sobreposição no avulso e entre avulso × rotina.

### A32 — Sem lembrete antes do início agendado
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** aviso "começa em 5 min" para se preparar.
- **Hoje:** o agendamento só dispara no horário (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:244-274`); não há lead time nem notice antes (busca por `Reminder`/`lead` no domínio vazia) e o ocioso mostra a hora sem alerta (`src/Island.App/ViewModels/IdlePresenter.cs:130-143`).
- **Por que é básico:** preparar o ambiente é parte do método.
- **Impacto:** o início pega o usuário no meio de outra coisa.
- **Mínimo viável:** notice/som X min antes, configurável.

### A33 — Rotina semanal sem exceções além de "pular amanhã"
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** pular hoje, pular uma ocorrência específica ou pausar a rotina por férias/feriado.
- **Hoje:** `SkipNextDay` só marca amanhã (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:219-230`) e a janela tem um único botão "Pular amanhã" (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml:75-76`); não há exceção de hoje, de ocorrência ou de período.
- **Por que é básico:** rotina semanal sempre tem exceção.
- **Impacto:** para pular hoje o usuário desliga a rotina inteira.
- **Mínimo viável:** pular ocorrência de hoje e intervalo de datas.

### A34 — Sem desfazer o "pular amanhã"
- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** desfazer o pulo com um clique.
- **Hoje:** depois do pulo, a janela só mostra o status (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:251-264`) e não há botão de cancelar; o único caminho é desligar/ligar a rotina, que limpa o pulo em `SetEnabled` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:204-216`).
- **Por que é básico:** erro de clique precisa ser reversível.
- **Impacto:** o usuário desativa a rotina só para consertar.
- **Mínimo viável:** botão "Desfazer pulo".

### A35 — Sem visão das próximas ocorrências; rotina semanal não aparece no ocioso
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver as próximas ocorrências (hoje/7 dias) e o próximo foco na cápsula.
- **Hoje:** a janela lista faixas, não datas (`src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:267-281`); o ocioso recebe só `_schedule.Pending` do agendamento avulso (`src/Island.App/ViewModels/IdlePresenter.cs:79`), então a rotina semanal nunca aparece na cápsula.
- **Por que é básico:** saber "quando é o próximo foco" é o mínimo de agenda.
- **Impacto:** o usuário não vê a rotina funcionando.
- **Mínimo viável:** próximas 5 ocorrências e incluir a semanal no ocioso.

### A36 — Fim de faixa encerra a sessão sem aviso
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser avisado quando a faixa semanal termina e encerra a sessão.
- **Hoje:** no fim da faixa, `StopManagedSession` chama `_timer.Reset()`/`EndScheduledSession` (`src/Island.Core/Pomodoro/PomodoroWeeklySchedule.cs:363-366`) sem notice nem transição; `Reset` não emite `Transitioned` (`src/Island.Core/Pomodoro/PomodoroTimer.cs:387-406`).
- **Por que é básico:** a sessão sumir sem explicação parece defeito.
- **Impacto:** perda de foco inesperada.
- **Mínimo viável:** notice "faixa terminou" com resumo.

### A37 — Início perdido há mais de 4 h é descartado em silêncio
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** saber que um início agendado foi perdido.
- **Hoje:** inícios salvos com mais de 4 h de atraso são filtrados sem aviso (`src/Island.Core/Pomodoro/PomodoroSchedule.cs:35,71-75`); nenhum notice/log chega ao usuário.
- **Por que é básico:** silêncio esconde a falha de o app estar fechado.
- **Impacto:** o usuário acha que agendou e não aconteceu.
- **Mínimo viável:** notice "início perdido às HH:mm" ao abrir.

### A38 — Lista de sites bloqueados fixa no código
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher/editar a lista de sites bloqueados.
- **Hoje:** a lista é `static` em `src/Island.Core/Pomodoro/BlockedSites.cs:18-56` e nenhuma setting guarda sites (`src/Island.Core/Configuration/IslandSettings.cs:45-57`); busca por configuração de `BlockedSites` não encontra nada.
- **Por que é básico:** distração é pessoal (TikTok, Twitch, LinkedIn, fórum).
- **Impacto:** bloqueia o que não incomoda e deixa o que incomoda.
- **Mínimo viável:** lista editável por nome/palavra no settings.

### A39 — Frases de desbloqueio fixas no código
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** usar uma frase própria (ou mais curta) para o desbloqueio.
- **Hoje:** `UnlockPhrases.All` tem 8 frases fixas (`src/Island.Core/Pomodoro/UnlockPhrases.cs:12-22`) e `Pick` sorteia (`src/Island.Core/Pomodoro/AngryPomodoro.cs:22-25`); não há setting.
- **Por que é básico:** o atrito é pessoal; frase longa demais vira colar/automatizar.
- **Impacto:** o usuário desiste de usar o Angry.
- **Mínimo viável:** frase ou lista personalizada, com comprimento mínimo.

### A40 — Só navegadores são bloqueados (sem apps)
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** bloquear apps de distração (Discord, Steam, TikTok/YouTube instalados, jogos).
- **Hoje:** o bloqueio só olha processos de navegador (`src/Island.Core/Pomodoro/BlockedSites.cs:58-62`; `src/Island.Windows/Focus/ForegroundSiteGuard.cs:97,110-111,175`); não há lista de apps bloqueados em settings nem no código (busca por `BlockedApps`/`BlockedProcesses` vazia).
- **Por que é básico:** no Windows a distração costuma ser app, não aba.
- **Impacto:** o Angry é contornável em um clique.
- **Mínimo viável:** lista de processos a minimizar durante o foco.

### A41 — Angry começa sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar tamanho e regras antes de começar uma sessão que não pode ser pausada.
- **Hoje:** clicar Play com Angry selecionado chama `Engage` direto (`src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:454-465`), que trava pausa/reset (`src/Island.Core/Pomodoro/AngryPomodoro.cs:127-129`); não há diálogo.
- **Por que é básico:** é uma sessão "sem saída"; o usuário precisa confirmar.
- **Impacto:** começa um compromisso rígido sem querer.
- **Mínimo viável:** confirmar "Angry ×N, sem pausa".

### A42 — Celular "bloqueado" sem confirmação real
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** confirmação de que o celular realmente bloqueou.
- **Hoje:** `Delivered` significa só que o FCM aceitou (`src/Island.App/App.xaml.cs:371-379`); o próprio README admite que a ilha mostra "Celular bloqueado" mesmo sem a acessibilidade ligada no aparelho (`README.md:139`).
- **Por que é básico:** bloqueio que não bloqueia é pior que ausente.
- **Impacto:** falsa sensação de proteção.
- **Mínimo viável:** estado "confirmado/pendente" ou aviso explícito.

### A44 — Tela de transição fixa em 6 s, sem ajuste
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar a duração ou desligar essa tela.
- **Hoje:** `LifetimeSeconds = 6` é constante (`src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:17,115-119`) e não há setting (busca por `TransitionSeconds` vazia).
- **Por que é básico:** uma tela cheia de 6 s interrompe quem está em outro app ou jogo.
- **Impacto:** o usuário fecha toda vez; atrapalha o fluxo.
- **Mínimo viável:** 0/3/6/10 s configurável em Settings.

### A45 — Sem integrar "Não perturbe"/Focus Assist
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ativar o Não Perturbe junto com o foco.
- **Hoje:** não há integração; busca por `FocusAssist`/`Dnd`/`Não perturbe` em `src` vazia, e os notices do Pomodoro são todos urgentes (`src/Island.Core/Pomodoro/PomodoroNotices.cs:5`).
- **Por que é básico:** foco é não ser interrompido por notificações do sistema.
- **Impacto:** o foco é furado por toasts de outros apps.
- **Mínimo viável:** ligar/desligar o modo por sessão (best effort).

### A46 — Sem som de tique/ambiente
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** som de tique opcional durante o foco.
- **Hoje:** os únicos sons são os 4 cues de transição (`src/Island.App/Audio/PomodoroSoundWave.cs:16-22`), tocados apenas no evento (`src/Island.App/App.xaml.cs:257-258`); busca por `TickSound`/`PomodoroTick` vazia.
- **Por que é básico:** muita gente se concentra com tique/ruído constante.
- **Impacto:** o usuário põe música/ruído por fora.
- **Mínimo viável:** tique suave ligável, com volume próprio.

### A47 — Volume dos sons não ajustável
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** volume do aviso (de preferência por perfil).
- **Hoje:** Settings só tem liga/desliga e o tipo (`src/Island.App/ViewModels/SettingsViewModel.cs:106-130`); o PCM é sintetizado com ganho fixo (`src/Island.App/Audio/PomodoroSoundWave.cs:16-22`) e o player não recebe volume (`src/Island.App/Audio/PomodoroSoundPlayer.cs:17-40`).
- **Por que é básico:** som alto interrompe; baixo não avisa.
- **Impacto:** o usuário desliga os sons de vez.
- **Mínimo viável:** volume 0–100 global e por perfil.

### A48 — Alarme de fim de descanso não é distinto nem insistente
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** alarme próprio do fim do descanso, que insista até ser notado.
- **Hoje:** o fim do descanso usa o som de foco (`src/Island.Core/Pomodoro/PomodoroSoundKind.cs:9-16`) tocado uma vez (`src/Island.App/Audio/PomodoroSoundPlayer.cs:26-30`); sem repetição ou confirmação.
- **Por que é básico:** voltar ao foco é o momento que mais falha (descanso esticado).
- **Impacto:** o usuário volta tarde e come o próximo ciclo.
- **Mínimo viável:** alarme próprio com repetição configurável.

### A49 — Sem lembrete de alongamento durante o descanso
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** um lembrete para alongar/beber água no descanso.
- **Hoje:** só existe o texto estático "Respire, alonge-se" na tela de transição (`src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:55`); nenhum lembrete temporizado ou setting.
- **Por que é básico:** descanso ativo é parte do método.
- **Impacto:** o descanso vira rolagem de celular.
- **Mínimo viável:** um lembrete por descanso.

### A50 — Sem atalho global de play/pause/pular
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** controlar a sessão de qualquer app.
- **Hoje:** só existem hotkeys de clipboard, notas e captura (`src/Island.App/App.xaml.cs:327-332,119-127`; `src/Island.App/Composition/ServiceRegistration.cs:181-184`; `src/Island.App/Shell/GameNotesHotkeyController.cs:38`); busca por hotkey do Pomodoro vazia.
- **Por que é básico:** durante o foco o usuário está em outro app.
- **Impacto:** precisa abrir a ilha para pausar ou pular.
- **Mínimo viável:** um atalho configurável para play/pause e pular.

### A51 — Sessão invisível na pílula Mini e no tray
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver o tempo restante com a ilha minimizada ou na bandeja.
- **Hoje:** a Mini pill é "bare capsule, no content" (`src/Island.App/Shell/IslandWindow.xaml:38`) e o tray tem `Text = "Dynamic Island"` fixo (`src/Island.App/Shell/TrayIconService.cs:53-57`); nada do Pomodoro aparece fora da Compact/Expanded.
- **Por que é básico:** quem minimiza a ilha perde o timer.
- **Impacto:** mantém a ilha aberta sempre (atrapalha) ou perde o foco.
- **Mínimo viável:** contador na Mini e no tooltip/menu do tray.

### A52 — Sair do app com foco ativo sem confirmação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** aviso ao sair com foco em andamento (com opção de continuar depois).
- **Hoje:** `RequestQuit` só consulta a frase do Angry (`src/Island.App/App.xaml.cs:407-423`, `ConfirmNotLocked` em 522-523) e o flush de notas; foco normal é descartado no `Shutdown` sem aviso.
- **Por que é básico:** fechar o app não deveria apagar a sessão em silêncio.
- **Impacto:** progresso perdido ao fechar/reiniciar.
- **Mínimo viável:** confirmar "há um foco em andamento" com sair/cancelar.

---

## Parte B — Ilha, estante, configurações e UX do Windows (49 itens)

Escopo: funções básicas da ilha, da estante, do clipboard, da cápsula ociosa (clima), da bandeja, das configurações e do update — o que o usuário sente falta na primeira semana de uso.
Método: leitura do README e dos arquivos-chave em `src/` e grep dos termos que provariam cada função (About, Help, Language, Theme, Export, Search, HighContrast, AutomationProperties, PauseUntil, Topmost etc.); cada item cita `arquivo:linha` e foi confirmado no código.
O README serviu de pista, não de verdade: há afirmações que o código não sustenta (ex.: "adicione em GameProcesses (Settings)" não tem campo equivalente em Settings).

### B01 — Não há busca nas configurações
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** digitar "atalho", "tema" ou "cidade" e chegar direto à opção.
- **Hoje:** a janela é um `StackPanel` fixo com nove cartões dentro de um `ScrollViewer`, sem campo de busca (`src/Island.App/Shell/SettingsWindow.xaml:320-323`, cartões `:326-719`).
- **Por que é básico:** há cerca de duas dúzias de controles, e parte dos rótulos está em inglês, então o usuário precisa varrer a janela inteira.
- **Impacto:** quem procura uma opção desiste ou vai editar `settings.json` na mão.
- **Mínimo viável:** caixa de busca no topo filtrando rótulos/descrições e rolando até o cartão correspondente.

### B02 — Sem tela "Sobre" e a versão instalada não aparece em lugar nenhum
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver a versão em execução, o link do projeto e as licenças.
- **Hoje:** os menus da ilha (`src/Island.App/Shell/IslandContextMenu.xaml:75-200`), da bandeja (`src/Island.App/Shell/TrayIconService.cs:38-51`) e de Settings (`src/Island.App/Shell/SettingsWindow.xaml:314-736`) não exibem a versão em execução; `RunningVersion` só é usado internamente pelo update (`src/Island.Windows/Updates/GitHubUpdateService.cs:79-85`).
- **Por que é básico:** o app se atualiza sozinho, então o usuário precisa confirmar em que build está e conseguir reportar um problema.
- **Impacto:** suporte às cegas e desconfiança sobre se a atualização aplicou.
- **Mínimo viável:** linha "Versão x.y.z" no rodapé de Settings com botão "Ver release" e, se possível, item "Sobre" no menu da ilha/bandeja.

### B03 — Nenhuma ajuda, tutorial ou experiência de primeira execução
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** na primeira vez, aprender que clique abre a estante, botão direito abre o menu, roda troca de linha, segurar arrasta e bandeja pausa.
- **Hoje:** não há tutorial, boas-vindas nem "Ajuda"; os gestos só existem no código (segurar 350 ms para arrastar, `src/Island.App/Shell/IslandWindow.xaml.cs:41-48,738-753`; roda troca de página, `:417-456`) e nada é mostrado na tela.
- **Por que é básico:** o produto depende de gestos que não são visíveis nem convencionais.
- **Impacto:** o usuário não descobre a estante, a personalização, o dock lateral nem o mini; parece um retângulo preto inútil.
- **Mínimo viável:** cartão único de 3–4 dicas na primeira execução e um item "Ajuda" repetindo esse texto.

### B04 — Atalhos globais fixos, sem lista e sem remapeamento
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** uma tela com todos os atalhos, o estado de registro e a possibilidade de trocar as teclas.
- **Hoje:** as teclas estão no código: Ctrl+Alt+N (`src/Island.App/App.xaml.cs:120`), Ctrl+Alt+V (`:327`), Ctrl+Alt+P/R (`src/Island.App/Shell/CaptureHotkeys.cs:13-14`), Ctrl+Alt+G (`src/Island.App/Shell/GameNotesHotkeyController.cs:38`). Só o N tem interruptor (`src/Island.App/Shell/SettingsWindow.xaml:589-610`); não há lista, captura de tecla nem aviso central de conflito — o conflito do V só vai para o log (`src/Island.App/App.xaml.cs:327-330`).
- **Por que é básico:** atalhos fixos colidem com outros apps, e trocar tecla é padrão em utilitário Windows.
- **Impacto:** atalho morto sem o usuário saber; nota rápida/captura ficam inacessíveis.
- **Mínimo viável:** tabela só-leitura com "registrado/em conflito" e botão "Alterar" capturando a combinação para N/V/P/R/G.

### B05 — A lista de jogos (`GameProcesses`) não tem campo em Settings
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** adicionar/remover jogos em que a ilha deve se esconder (e onde as notas do jogo devem casar).
- **Hoje:** `GameProcesses` existe só no JSON (`src/Island.Core/Configuration/IslandSettings.cs:33-34`) e é lido direto das configurações (`src/Island.App/Composition/ServiceRegistration.cs:101,137`); não há nenhum controle em `SettingsWindow.xaml` e o README manda "adicione em GameProcesses (Settings)".
- **Por que é básico:** é a configuração que evita a ilha por cima de jogo em janela comum.
- **Impacto:** o usuário precisa editar JSON manualmente; jogo fora de tela cheia nunca é reconhecido.
- **Mínimo viável:** cartão "Jogos" com lista, "Adicionar processo" (nome ou .exe) e remover.

### B06 — "Reset to defaults" apaga tudo sem confirmação e sem desfazer
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar (ou poder desfazer) uma ação que apaga todas as preferências.
- **Hoje:** o botão dispara o comando direto (`src/Island.App/Shell/SettingsWindow.xaml:730-733`; `src/Island.App/ViewModels/SettingsViewModel.cs:72,410-437`), sem diálogo e sem undo.
- **Por que é básico:** toda ação destrutiva de um clique em uma janela com Esc/scroll pede confirmação.
- **Impacto:** configuração perdida por clique acidental e o usuário refaz tudo.
- **Mínimo viável:** `MessageBox` "Restaurar padrões? Isso não altera token do celular" e um toast "Configurações restauradas".

### B07 — O reset é parcial e não devolve a estante ao padrão
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que "padrões" signifique o estado de primeira execução.
- **Hoje:** o reset enumera só parte dos campos (`src/Island.App/ViewModels/SettingsViewModel.cs:414-434`): mantém `ShelfWidgets`/`ShelfRows`, `ClipboardEnabled`/`ClipboardMaxItems`, `NoticeSeconds`, `GameProcesses`, `Minimized`, tempos/perfis do Pomodoro e `UpdateRepository`.
- **Por que é básico:** o botão promete "defaults" e o usuário usa isso para consertar um layout quebrado.
- **Impacto:** "resetar" não resolve estante errada nem preferências de clipboard/avisos.
- **Mínimo viável:** resetar o record inteiro, preservando apenas credenciais, e listar no diálogo o que será mantido.

### B08 — Não é possível exportar nem importar as configurações
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fazer backup e migrar preferências para outro PC/perfil.
- **Hoje:** não existe `OpenFileDialog`/`SaveFileDialog` no app (grep sem ocorrência) e o rodapé de Settings tem só "Reset to defaults" (`src/Island.App/Shell/SettingsWindow.xaml:725-735`).
- **Por que é básico:** o app concentra dezenas de preferências e várias só existem no JSON.
- **Impacto:** trocar de máquina/perfil vira edição manual de arquivos; reinstalação perde tudo.
- **Mínimo viável:** botões "Exportar…" e "Importar…" de um JSON, com validação e confirmação antes de aplicar.

### B09 — Falha ao salvar/ler as configurações é silenciosa
- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber que a alteração não foi gravada (ou que a configuração anterior foi descartada).
- **Hoje:** `SettingsApplier.Apply` só registra o erro no log (`src/Island.App/Composition/ServiceRegistration.cs:60`); JSON corrompido é movido para `.bad` e o app abre com padrões sem avisar (`src/Island.Windows/Configuration/JsonSettingsStore.cs:75-79,104-108`), e as gravações ficam bloqueadas sem feedback quando houve erro de leitura (`:97`).
- **Por que é básico:** a configuração é o contrato de memória do app; perder em silêncio é inaceitável.
- **Impacto:** o usuário ajusta tudo, reinicia e "desconfigura" sem entender por quê.
- **Mínimo viável:** banner de erro em Settings com "Tentar de novo" e "Abrir pasta de dados", e aviso quando um arquivo corrompido foi posto de lado.

### B10 — "Start with Windows" não tem feedback, estado real nem modo discreto
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ligar/desligar com confiança e o app iniciar sem incomodar.
- **Hoje:** o toggle grava no Run do registro (`src/Island.App/Composition/ServiceRegistration.cs:61-65`; `src/Island.Windows/Shell/OverlayWindowService.cs:37-72`), mas o estado exibido vem do `settings.json` — `StartupRegistration.IsEnabled` nunca é chamado —, falhas só vão para o log (`:63-64`) e não há "iniciar minimizado/atrasado".
- **Por que é básico:** autostart é uma das primeiras opções que o usuário mexe, e o Windows pode remover a entrada pelo Gerenciador de Tarefas.
- **Impacto:** o app deixa de abrir no boot (ou abre atrapalhando) e o switch continua "ligado".
- **Mínimo viável:** reconciliar com o registro ao abrir, mostrar erro no toggle e oferecer "iniciar como Mini".

### B11 — Sem tema claro, sem seguir o tema do Windows e sem contraste
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** a janela de Settings e os painéis acompanharem o modo claro/escuro (ou uma escolha explícita).
- **Hoje:** a paleta é fixa escura (`src/Island.App/Styles/Colors.xaml:2-25`) e Settings tem fundo `#121212` fixo (`src/Island.App/Shell/SettingsWindow.xaml:10-11,20-29`); não há campo de tema em `IslandSettings` (`src/Island.Core/Configuration/IslandSettings.cs:11-75`).
- **Por que é básico:** é uma janela de configuração do Windows, não só a pílula preta da ilha.
- **Impacto:** usuários de tema claro e de alto contraste ficam com uma janela ilegível/estranha.
- **Mínimo viável:** opções "Seguir Windows / Escuro / Claro" trocando os brushes por `ResourceDictionary`.

### B12 — Sem escala de fonte ou zoom da interface
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** aumentar texto e controles para enxergar melhor ou usar monitor grande.
- **Hoje:** os tamanhos são fixos nos estilos (`src/Island.App/Styles/Typography.xaml:8-39`, `SettingsWindow.xaml:13`) e a janela é 460x560 com `ResizeMode="NoResize"` (`src/Island.App/Shell/SettingsWindow.xaml:5-7`); não existe preferência de escala em `IslandSettings`.
- **Por que é básico:** baixa visão e monitores grandes são requisitos de app desktop maduro, e hoje a única saída é o zoom do Windows.
- **Impacto:** usuários com baixa visão não conseguem aumentar texto/controles; em telas grandes a interface fica pequena demais.
- **Mínimo viável:** seletor "Tamanho do texto: 100/125/150%" aplicado ao FontSize base e à largura da cápsula.

### B13 — A interface mistura inglês e português no mesmo fluxo
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** uma língua consistente do começo ao fim.
- **Hoje:** Settings mistura "POSITION/SIZE/TIMING/BEHAVIOR/UPDATES" (`src/Island.App/Shell/SettingsWindow.xaml:328,365,402,439,703`) com "SONS DO POMODORO/NOTAS RÁPIDAS/CÁPSULA OCIOSA" (`:529,591,615`); o menu da ilha é 100% inglês (`src/Island.App/Shell/IslandContextMenu.xaml:75,90,105,121,139,155,170,187`) e o clipboard mostra "Nothing copied yet", "Clear history" e "Delete" (`src/Island.App/Views/ClipboardView.xaml:60,78`; `src/Island.App/Views/ClipboardCard.cs:91`).
- **Por que é básico:** a primeira impressão do produto é a mistura de idiomas.
- **Impacto:** parece inacabado; o usuário não acha "Configurações" em um menu em inglês.
- **Mínimo viável:** padronizar o app em PT-BR (ou EN, se for o alvo) incluindo menu da ilha, clipboard e seções de Settings.

### B15 — A pausa é só um interruptor, sem duração, lembrete, atalho ou memória
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** "Pausar 1 h" (ou até amanhã), ver quanto falta, ser lembrado ao voltar e conseguir pausar pelo teclado; saber que está pausado.
- **Hoje:** a pausa é um booleano de execução, não persistido (`src/Island.Core/Models/IslandEvent.cs:38-39`; `src/Island.Core/Application/IslandStateReducer.cs:28-29,106-108`), só existe no menu da bandeja (`src/Island.App/Shell/TrayIconService.cs:41-42,117-127`) e simplesmente esconde a ilha (`src/Island.App/Shell/IslandWindow.xaml.cs:396-412`) sem aviso, duração ou retomada.
- **Por que é básico:** overlay sempre no topo incomoda em call/jogo e a alternativa atual é fechar o app.
- **Impacto:** pausa esquecida (app "sumido") ou Quit no lugar de pausa; sem atalho, é preciso mirar no ícone da bandeja.
- **Mínimo viável:** "Pausar 30 min / 1 h / até amanhã" no menu, tooltip da bandeja e toast "Pausa terminou".

### B16 — Não dá para desligar avisos por tipo (Caps Lock, dispositivos, etc.)
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher quais avisos aparecem — Caps Lock, USB/Bluetooth, pomodoro, captura.
- **Hoje:** as fontes de aviso são registradas e iniciadas sempre (`src/Island.App/App.xaml.cs:171-178`); Caps Lock avisa a cada alternância (`src/Island.Windows/Input/WindowsCapsLockNoticeSource.cs:103-105`) e dispositivos a cada conexão/desconexão (`src/Island.Windows/Devices/WindowsDeviceNoticeSource.cs:178-203`). Só volume e mídia têm toggle (`src/Island.App/Shell/SettingsWindow.xaml:492-522`).
- **Por que é básico:** aviso de Caps Lock/dispositivo a cada tecla é o motivo mais comum para desligar um overlay.
- **Impacto:** o usuário pausa ou fecha o app para parar os avisos e perde as funções boas.
- **Mínimo viável:** cartão "Avisos" com switches por tipo, incluindo "Silenciar todos" temporário.

### B17 — A duração do aviso não é configurável na interface
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar quanto tempo o aviso fica na tela.
- **Hoje:** `NoticeSeconds` existe (`src/Island.Core/Configuration/IslandSettings.cs:62`) e é usado pelo reducer (`src/Island.Core/Application/IslandStateReducer.cs:174-182`), mas o cartão TIMING de Settings só tem volume e mídia (`src/Island.App/Shell/SettingsWindow.xaml:399-434`).
- **Por que é básico:** 3 s pode ser pouco para ler um subtítulo e demais para quem trabalha.
- **Impacto:** usuário perde avisos ou se irrita, sem conseguir mudar pela UI.
- **Mínimo viável:** slider "Duração do aviso" de 1–10 s no cartão TIMING, com preview.

### B18 — Avisos têm um único slot: sem histórico e sem fila
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** rever o aviso que já passou (print salvo, update, dispositivo) e saber que há mais na fila.
- **Hoje:** o estado guarda um único `Notice` (`src/Island.Core/Models/IslandState.cs:4-9`) e o novo sobrescreve o anterior pela prioridade (`src/Island.Core/Application/EventPriorityPolicy.cs:27-30`); não há histórico nem contador (os dispositivos até enfileiram para despacho, `src/Island.Windows/Devices/WindowsDeviceNoticeSource.cs:178-228`, mas chegam quando outro estado está na frente).
- **Por que é básico:** overlay erra avisos e o usuário deve poder recuperá-los.
- **Impacto:** perde "update disponível", "print salvo" e "celular bloqueado" sem chance de reler.
- **Mínimo viável:** pilha de até 5 avisos com indicador "+N" e lista "Avisos recentes" em Settings.

### B19 — Nenhum aviso tem som
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** um toque curto opcional para avisos, e poder silenciar.
- **Hoje:** só existem os sons do Pomodoro (`src/Island.App/Shell/SettingsWindow.xaml:527-586`) e o `Notice` não tem som/canal (`src/Island.Core/Models/Notice.cs:7`); as fontes de sistema emitem sem áudio (`src/Island.App/App.xaml.cs:368-369`).
- **Por que é básico:** notificação sem som passa despercebida em tela cheia/segundo monitor.
- **Impacto:** eventos como dispositivo e captura passam em branco.
- **Mínimo viável:** switch "Som nos avisos" reutilizando o player do Pomodoro, com 2–3 tons curtos.

### B20 — "Sempre no topo" é obrigatório e não há comportamento de sobreposição
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher se a ilha fica sempre em cima, desce em segundo plano ou aparece só em alguns apps.
- **Hoje:** a janela nasce `Topmost="True"` (`src/Island.App/Shell/IslandWindow.xaml:12`) e o serviço reafirma `HWND_TOPMOST` (`src/Island.Windows/Shell/OverlayWindowService.cs:24,29-34`); não há opção em `IslandSettings`.
- **Por que é básico:** overlay sempre no topo conflita com apresentação, videochamada e apps de tela cheia sem borda.
- **Impacto:** a ilha cobre conteúdo e a única saída é pausar ou sair.
- **Mínimo viável:** "Sempre no topo" (padrão ligado) com modo "descer quando janela maximizada estiver em foco".

### B21 — A ilha não se esconde quando outra janela cobre a área
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** a ilha sair da frente quando uma janela maximizada, vídeo ou jogo em janela ocupa o topo da tela.
- **Hoje:** a suspensão automática só olha tela cheia e a lista de jogos (`src/Island.Core/Application/IslandStateReducer.cs:28-29`; `src/Island.Windows/Display/FullscreenDetector.cs:102-124`), e uma janela maximizada é explicitamente classificada como "não fullscreen" (`:119-121`); não há checagem de sobreposição/ordem Z.
- **Por que é básico:** a cápsula fica exatamente sobre barras de título/abas de apps maximizados.
- **Impacto:** atrapalha o trabalho e o usuário pausa o app.
- **Mínimo viável:** opção "Ocultar quando houver janela maximizada em primeiro plano" usando o mesmo detector com `WS_MAXIMIZE` + cobertura.

### B22 — O modo Mini é uma cápsula vazia, persistida, sem dica de saída
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** entender o que é o Mini e como voltar ao normal.
- **Hoje:** o Mini é um `Grid` vazio (`src/Island.App/Shell/IslandWindow.xaml:38-39`), sem ícone/tooltip; sai-se com um clique (`src/Island.App/Shell/IslandWindow.xaml.cs:466-471`) e o estado é persistido (`Minimized`, `src/Island.Core/Configuration/IslandSettings.cs:28`; `src/Island.App/App.xaml.cs:158-168`), então o app pode já abrir "sumido".
- **Por que é básico:** o usuário precisa descobrir o clique por tentativa e pode achar que o app quebrou.
- **Impacto:** suporte desnecessário e usuários que abandonam o app após o reinício.
- **Mínimo viável:** ponto/ícone sutil no Mini, dica "clique para expandir" no primeiro uso e opção "iniciar como Mini".

### B23 — Arrastar a ilha exige segurar 350 ms e não há pista visual
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** arrastar a ilha diretamente, como qualquer janela, ou ver uma alça.
- **Hoje:** o arraste exige pressionar e manter parado 350 ms (`HoldDuration`/`HoldSlop`, `src/Island.App/Shell/IslandWindow.xaml.cs:41-48,667-676,738-753`); a única pista é a opacidade cair durante o arraste (`:772`), e não há alça/cursor.
- **Por que é básico:** mover janela é gesto universal; long-press escondido é invenção.
- **Impacto:** o usuário não reposiciona a ilha nem descobre o encaixe lateral.
- **Mínimo viável:** alça visível no hover (mantendo o hold como alternativa) ou cartão de dica na primeira execução.

### B24 — Não existe "travar posição" da ilha
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** travar a ilha no lugar para não movê-la sem querer.
- **Hoje:** não há flag de trava; qualquer arraste grava `UseCustomPosition`/`PositionX`/`PositionY` (`src/Island.App/Shell/IslandWindow.xaml.cs:834-841`) e o menu só oferece "Reset position" (`src/Island.App/Shell/IslandContextMenu.xaml:121-134`).
- **Por que é básico:** quem usa em produção/apresentação precisa de posição fixa garantida.
- **Impacto:** toque acidental em touchpad move a ilha e o usuário "perde" o app.
- **Mínimo viável:** item "Travar posição" no menu da ilha, com "Desbloquear" no mesmo lugar.

### B25 — Existe uma única posição global, não uma por monitor
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** cada monitor com sua posição e seu encaixe lembrados.
- **Hoje:** há uma só posição (`UseCustomPosition`, `PositionX`, `PositionY`, `Dock` em `src/Island.Core/Configuration/IslandSettings.cs:23-32`), e `MonitorIndex` só é consultado quando não há posição personalizada (`src/Island.App/Shell/IslandWindow.xaml.cs:559-577`); arrastar para o monitor 2 substitui a posição usada no 1.
- **Por que é básico:** setups de 2–3 monitores são comuns em 2026.
- **Impacto:** toda troca de monitor exige re-arrastar; dock esquerda/direita se perde.
- **Mínimo viável:** mapa monitor→(X, Y, dock) com fallback para o primário quando o monitor não existir.

### B26 — O monitor é salvo por índice; hot-plug e reboot podem trocar a ilha de tela
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** a ilha voltar sempre à mesma tela física.
- **Hoje:** `MonitorIndex` é a posição na ordem de enumeração (`src/Island.Core/Configuration/IslandSettings.cs:13-14`; `src/Island.Windows/Display/MonitorService.cs:17-58`, com primário primeiro e o resto na ordem do sistema) e os nomes são "Monitor N" (`src/Island.App/App.xaml.cs:529-531`).
- **Por que é básico:** identificar o monitor por identidade é o mínimo em multi-monitor.
- **Impacto:** dock/undock ou reboot mudam a tela da ilha e o usuário reconfigura.
- **Mínimo viável:** salvar o nome do dispositivo (`MONITORINFOEX.szDevice`) e resolver por identidade, com fallback para o primário.

### B27 — O seletor "Display" não faz nada quando a ilha foi arrastada
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** trocar a tela no combo e ver a ilha mudar na hora.
- **Hoje:** com posição personalizada (qualquer arraste) `Reposition` retorna antes de ler `MonitorIndex` (`src/Island.App/Shell/IslandWindow.xaml.cs:559-577`), mas o combo continua habilitado (`src/Island.App/Shell/SettingsWindow.xaml:335-341`) e sem aviso.
- **Por que é básico:** controle que não responde é pior do que controle ausente.
- **Impacto:** o usuário acha o seletor quebrado e não sabe que precisa resetar posição.
- **Mínimo viável:** ao trocar Display, limpar a posição personalizada e reposicionar; ou desabilitar o combo com texto "Volte a ilha ao centro para escolher a tela".

### B28 — O encaixe só existe nas laterais
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** encaixar em qualquer borda/canto, como os painéis do Windows.
- **Hoje:** `DockEdge` só tem None/Left/Right (`src/Island.Core/Configuration/IslandSettings.cs:3-9`) e o detector testa apenas as duas bordas laterais (`src/Island.App/Shell/IslandWindow.xaml.cs:618-629`); a margem do encaixe é fixa em 8 DIP (`:75`).
- **Por que é básico:** barra de tarefas no topo/inferior e monitores em retrato mudam a melhor borda.
- **Impacto:** quem usa a ilha embaixo não tem opção; o encaixe lateral é a única alternativa.
- **Mínimo viável:** `DockEdge.Bottom` + margem configurável no cartão POSITION.

### B29 — Não há atalho global para abrir a estante (ou mostrar/ocultar a ilha)
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** invocar a ilha pelo teclado, sem mirar numa cápsula de 124 px.
- **Hoje:** existem atalhos só para clipboard (V, `src/Island.App/App.xaml.cs:327`), captura (P/R, `src/Island.App/Shell/CaptureHotkeys.cs:13-14`), notas (N, `App.xaml.cs:120`) e notas do jogo (G, `src/Island.App/Shell/GameNotesHotkeyController.cs:38`); não há tecla para expandir a estante nem para mostrar/ocultar a ilha (nenhum outro `new GlobalHotkey`).
- **Por que é básico:** utilitário de produtividade é usado pelo teclado; mouse em overlay é o caminho mais lento.
- **Impacto:** usuários de teclado não abrem a estante; o fluxo é interrompido.
- **Mínimo viável:** Ctrl+Alt+D (configurável) alternando estante, registrado como os demais e com estado de conflito visível.

### B30 — Esc não fecha a estante, o clipboard nem a personalização
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** Esc fecha o painel aberto, como em qualquer overlay.
- **Hoje:** estante e clipboard fecham só com clique fora (`OutsideClickWatcher`, `src/Island.App/Shell/IslandWindow.xaml.cs:522-536`); o Esc global só é capturado na tela de transição do Pomodoro (`EscapeKeyWatcher` usado em `src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:26,91`), e a ilha não ativa (`src/Island.App/Shell/IslandWindow.xaml:14-15`), então o Esc vai para o app anterior.
- **Por que é básico:** Esc é o "fechar" universal de painéis.
- **Impacto:** o usuário tenta Esc, nada acontece e a ilha fica aberta por cima do trabalho.
- **Mínimo viável:** armar o `EscapeKeyWatcher` também em Expanded/Clipboard/Customize e fechar no Esc.

### B31 — Abrir o app uma segunda vez não faz nada e não avisa
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** trazer Settings/estante para frente, ou pelo menos "já está em execução".
- **Hoje:** a segunda instância falha no mutex e chama `Shutdown()` em silêncio, sem mensagem nem IPC (`src/Island.App/App.xaml.cs:80-84`; `src/Island.Windows/Shell/OverlayWindowService.cs:89-97`).
- **Por que é básico:** o usuário abre pelo menu Iniciar quando "não vê" a ilha.
- **Impacto:** acha que o app não abre; clica várias vezes ou reinstala.
- **Mínimo viável:** evento nomeado/IPC mínimo: a instância viva abre Settings (ou a estante) quando recebe um segundo lançamento.

### B32 — Personalizar a estante só é possível pelo clique direito na ilha
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** encontrar "editar estante" na própria estante.
- **Hoje:** só o menu de contexto oferece "Customize Shelf" (`src/Island.App/Shell/IslandContextMenu.xaml:75-88`), e a estante não tem botão de editar/engrenagem (`src/Island.App/Views/ShelfView.xaml:1-116`); o modo de edição tem check/cancelar (`:41-77`), mas não explica arrastar/remover.
- **Por que é básico:** personalização é o recurso central da estante e fica atrás de um clique direito invisível.
- **Impacto:** o usuário não descobre os 16 widgets do catálogo e acha a estante fixa.
- **Mínimo viável:** botão "Editar" discreto na estante e dica de primeira vez "clique direito para personalizar".

### B33 — A troca de linha da estante só funciona pela roda do mouse
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** setas ou pontos clicáveis e indicação de que existem mais linhas.
- **Hoje:** a troca de linha é só pela roda, com debounce de 400 ms (`src/Island.App/Shell/IslandWindow.xaml.cs:417-456`); os pontos existem, mas são `IsHitTestVisible="False"` (`src/Island.App/Views/ShelfView.xaml:107-114`) e só aparecem com mais de uma linha.
- **Por que é básico:** trackpad em modo de rolagem horizontal/touch e mouses sem roda ficam sem navegação.
- **Impacto:** os widgets da segunda linha são praticamente inacessíveis.
- **Mínimo viável:** setas ‹ › clicáveis ao lado dos pontos e suporte a gesto horizontal.

### B34 — Widgets que não cabem na linha somem sem aviso
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber que a linha atingiu o limite e que um widget salvo não está sendo mostrado.
- **Hoje:** a linha é limitada a 1080 DIP e os excedentes são descartados em memória (`src/Island.App/Animations/IslandTransitions.cs:451-472`, aplicado em `src/Island.App/Views/ShelfView.xaml.cs:108,126,136`) sem mensagem; na edição, um widget que não cabe fica 40% opaco, cursor "No" e sem texto explicando (`ShelfView.xaml.cs:519,528-581`).
- **Por que é básico:** o que está salvo deveria ser o que aparece, ou o app deve dizer o porquê.
- **Impacto:** widget "desaparece" e o usuário não entende que precisa de outra linha.
- **Mínimo viável:** tooltip "Não cabe nesta linha — solte na linha 2" e aviso ao sair da edição quando houver widget oculto.

### B36 — Não há layouts/perfis salvos da estante
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** salvar 2–3 arranjos (trabalho, jogo, estudo) e alternar.
- **Hoje:** existe um único layout (`ShelfWidgets`/`ShelfRows`, `src/Island.Core/Configuration/IslandSettings.cs:42-44`) editado no Customize; não há perfis nomeados e o "Reset to defaults" não restaura o layout (`src/Island.App/ViewModels/SettingsViewModel.cs:414-434`).
- **Por que é básico:** com 16 widgets, o arranjo útil muda por atividade.
- **Impacto:** re-arrastar widgets toda vez desestimula o uso da estante.
- **Mínimo viável:** "Salvar layout atual como…" + combo para aplicar, guardado no JSON.

### B37 — A busca do clipboard é um botão decorativo
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** digitar e filtrar os itens do histórico.
- **Hoje:** o botão de lupa é explicitamente decorativo (comentário "search and square are decorative", `src/Island.App/Views/ClipboardView.xaml:13-21`) e não há `TextBox`/handler de busca; o painel só lista os mais recentes (`src/Island.App/Views/ClipboardView.xaml.cs:118-162`).
- **Por que é básico:** o histórico chega a 50 itens; sem busca ele fica inútil.
- **Impacto:** o usuário rola às cegas e desiste de usar o clipboard do app.
- **Mínimo viável:** campo de busca no topo filtrando por texto (case-insensitive), com Esc limpando.

### B38 — "Clear history" apaga tudo sem confirmação
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar ou desfazer a limpeza de todo o histórico.
- **Hoje:** o botão limpa na hora, sem diálogo (`src/Island.App/Views/ClipboardView.xaml.cs:35-39`; menu inline em `src/Island.App/Views/ClipboardView.xaml:67-79`).
- **Por que é básico:** ação destrutiva sobre dado útil, a um clique de distância.
- **Impacto:** perda acidental de todo o histórico, sem undo.
- **Mínimo viável:** confirmação ou "Desfazer" por 5 s no lugar do painel.

### B41 — Sem lista de apps excluídos da captura
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** excluir apps específicos (banco, gerenciador de senhas, chat privado) do histórico.
- **Hoje:** só são ignorados os formatos de privacidade do Windows e o conteúdo copiado de dentro do próprio app (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:241-256`); não há lista configurável de processos/janelas.
- **Por que é básico:** exclusão por app é recurso padrão de clipboard manager.
- **Impacto:** conteúdo sensível entra no histórico e o usuário desliga a feature inteira.
- **Mínimo viável:** lista "Apps ignorados" (nome do processo) checada no dono do clipboard/janela em foco.

### B43 — Escolher um item copia, mas não cola no app em foco
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** escolher o item e ele ser colado no app de onde veio (ou ao menos Enter/Ctrl+V).
- **Hoje:** o card só copia para o clipboard (`copy` em `src/Island.App/Views/ClipboardView.xaml.cs:171-186`; clique em `src/Island.App/Views/ClipboardCard.cs:107-108`), e o usuário precisa voltar e apertar Ctrl+V; a ilha não ativa (`src/Island.App/Shell/IslandWindow.xaml:14-15`) e não há navegação por teclado; o app não simula colagem em lugar nenhum (grep sem `SendInput`).
- **Por que é básico:** o fluxo "abrir clipboard → escolher → colar" deve ser um passo.
- **Impacto:** dois passos extras a cada uso; o recurso perde para o Win+V do Windows.
- **Mínimo viável:** opção "colar ao escolher" (copia + `SendInput` Ctrl+V), com fallback quando o alvo não aceitar.

### B44 — Arquivos, HTML e rich text copiados são ignorados
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** copiar um arquivo no Explorer (ou texto formatado) e vê-lo no histórico.
- **Hoje:** o serviço só lê `CF_UNICODETEXT` e imagem DIB/BMP (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:218-234,303-330`); listas de arquivos, HTML e RTF são descartados sem aviso.
- **Por que é básico:** copiar arquivos é uso diário no Windows.
- **Impacto:** boa parte dos "copiar" do dia não aparece e o app parece não funcionar.
- **Mínimo viável:** suporte a `CF_HDROP` como tipo "Arquivos" (lista de caminhos) e HTML com fallback para texto.

### B45 — O clima é sempre em °C
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher °C ou °F.
- **Hoje:** a cápsula sempre formata `{(int)Math.Round(snapshot.TemperatureC)}°C` (`src/Island.App/ViewModels/IdlePresenter.cs:122`) e o snapshot só guarda Celsius; não há preferência em Settings.
- **Por que é básico:** °F é o padrão de parte dos usuários (e do público de apps em inglês).
- **Impacto:** número sem sentido para quem usa °F; estranheza no primeiro uso.
- **Mínimo viável:** toggle °C/°F no cartão "Cápsula ociosa", convertendo na exibição e na descrição acessível.

### B47 — Falha de clima e cidade inválida não geram nenhum feedback
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** "sem conexão" ou "cidade não encontrada" e como corrigir.
- **Hoje:** erros de rede e cidade desconhecida são engolidos (`src/Island.Core/Idle/WeatherRefresher.cs:138-142` diz que é "never an error the island shows"; `src/Island.Windows/Weather/OpenMeteoWeatherService.cs:59,64` só loga); a leitura velha apenas esmaece (`src/Island.App/ViewModels/IdlePresenter.cs:124-127`) e desaparece após 3 h (`src/Island.Core/Idle/WeatherRefreshPolicy.cs:17-22`).
- **Por que é básico:** sem mensagem o usuário conclui que a função quebrou.
- **Impacto:** não sabe que digitou a cidade errado nem que está sem rede; clima "sumiu".
- **Mínimo viável:** estado "Sem conexão — última leitura há X" e "Cidade não encontrada" no cartão de clima, com "Tentar de novo".

### B48 — Sem previsão horária, mín/máx ou detalhes do clima
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** mín/máx do dia ou próximas horas, como em qualquer widget de clima.
- **Hoje:** a consulta pede só `current` + `hourly=precipitation_probability` de 1 dia (`src/Island.Windows/Weather/OpenMeteoWeatherService.cs:74-75`), e a cápsula mostra a temperatura atual e a maior chance de chuva das 3 próximas horas (`src/Island.Windows/Weather/OpenMeteoParser.cs:12-13,49-59`).
- **Por que é básico:** a pergunta real ("vai chover? que casaco?") exige mais que um número.
- **Impacto:** informação pouco útil; o usuário abre o site ou outro app.
- **Mínimo viável:** tooltip/expansão com mín/máx e as próximas 3 h (mesma resposta, `daily=temperature_2m_min,max`).

### B51 — Falha de update vira um aviso genérico em inglês, sem ação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** entender o motivo e tentar de novo.
- **Hoje:** a falha mostra "Update failed / See the logs" (`src/Island.App/App.xaml.cs:447-451`), e o status de "Check for updates" também é em inglês e sem ação (`:499-512`; `src/Island.App/Shell/SettingsWindow.xaml:709-716`); não abre log nem oferece retry.
- **Por que é básico:** o auto-update é a única via de instalação (não há MSIX), então falhar bem importa.
- **Impacto:** o usuário fica preso em versão antiga sem saber o que fazer.
- **Mínimo viável:** mensagem em PT com motivo resumido + "Tentar de novo" e "Abrir update-install.log".

### B54 — Uma atualização disponível não é anunciada na ilha
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber que há atualização sem abrir o menu de contexto.
- **Hoje:** a checagem só guarda `_available` para o menu (`src/Island.Windows/Updates/GitHubUpdateService.cs:143-146`; `src/Island.App/Shell/IslandContextMenu.xaml.cs:59-65`) e registra em log; nenhum aviso/indicador é emitido quando uma Release nova aparece.
- **Por que é básico:** a maioria dos usuários nunca abre o clique direito da ilha, então o auto-update nunca acontece.
- **Impacto:** o app fica desatualizado indefinidamente, com correções sem chegar.
- **Mínimo viável:** aviso não urgente "Atualização vX disponível" (respeitando pausa/tela cheia) e ponto no ícone da bandeja.

### B55 — O menu da bandeja é mínimo e não abre o produto
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** abrir a ilha/estante, ver estado, atualizar e sair pelo ícone.
- **Hoje:** o menu tem só "Settings…", "Pause island" e "Quit" (`src/Island.App/Shell/TrayIconService.cs:38-51`); clique simples não faz nada, duplo abre Settings (`:59`) e o tooltip é fixo "Dynamic Island" (`:56`), sem versão ou estado.
- **Por que é básico:** a bandeja é o ponto de entrada padrão de utilitários Windows.
- **Impacto:** o usuário procura o app na bandeja e só encontra configurações/sair; não vê pausado nem versão.
- **Mínimo viável:** "Abrir ilha", "Pausar por…", "Check for updates", "Sobre" e tooltip com versão/estado.

### B56 — Não é possível ocultar o ícone da bandeja
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** esconder o ícone (ou movê-lo para a bandeja secundária) em setups minimalistas.
- **Hoje:** o `NotifyIcon` é criado com `Visible = true` e nunca é ocultável (`src/Island.App/Shell/TrayIconService.cs:53-60`); não há campo em `IslandSettings` nem controle em Settings.
- **Por que é básico:** "mostrar/ocultar ícone da bandeja" é convenção em apps de utilidade.
- **Impacto:** a única saída é a configuração de ícones da própria bandeja do Windows, que muda de versão para versão; o app não tem controle.
- **Mínimo viável:** switch "Mostrar ícone na bandeja", avisando que sem ele o único ponto de entrada visível passa a ser a própria ilha.

### B57 — A ilha não é operável por teclado nem por leitor de tela
- **Severidade:** Alta
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** abrir a estante e navegar com Tab/Enter, e ter os conteúdos anunciados.
- **Hoje:** a janela é `ShowActivated="False"` e `Focusable="False"` (`src/Island.App/Shell/IslandWindow.xaml:14-15`) e recebe `WS_EX_NOACTIVATE` (`src/Island.App/Shell/IslandWindow.xaml.cs:267-274`); a estante e os widgets são operados por mouse (`src/Island.App/Views/ShelfView.xaml.cs:646-660`), então não há foco, Tab nem navegação por teclado dentro da ilha.
- **Por que é básico:** acessibilidade e uso sem mouse são requisitos de app desktop maduro.
- **Impacto:** usuários de teclado/leitor de tela não conseguem usar nenhuma função da ilha.
- **Mínimo viável:** atalho "modo focado" que abre uma janela de estante acessível (Tab + AutomationProperties) ou navegação por teclado via hook global.

### B58 — Configurações removem o indicador de foco do teclado
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ver onde está o foco ao navegar com Tab.
- **Hoje:** os estilos custom de Settings zeram `FocusVisualStyle` em sliders, switches, combos, itens e botões (`src/Island.App/Shell/SettingsWindow.xaml:112,144,208,235,281`) e não há trigger de foco; o Tab percorre os controles, mas nada indica onde está.
- **Por que é básico:** sem foco visível, navegação por teclado é inutilizável (WCAG 2.4.7).
- **Impacto:** usuário de teclado se perde e pode alterar a configuração errada, inclusive o reset.
- **Mínimo viável:** anel/borda de 2 px no `IsKeyboardFocused` dos cinco estilos e do `ComboBoxItem`.

### B59 — Tiles do Customize e cards do clipboard não têm nome acessível
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** o leitor de tela anunciar "Widget Pomodoro", "Copiar item", "Limpar histórico".
- **Hoje:** os tiles do Customize e os cards do clipboard são `Border`/`Grid` com eventos de mouse e sem `AutomationProperties.Name` (`src/Island.App/Views/ShelfView.xaml.cs:528-581`; `src/Island.App/Views/ClipboardCard.cs:50-123`), enquanto outros widgets do app já usam `AutomationProperties` (por exemplo `src/Island.App/Views/Widgets/CounterWidget.xaml:30-75`).
- **Por que é básico:** mesmo quando a estante está aberta, não há o que o leitor de tela anuncie.
- **Impacto:** a personalização e o clipboard são inacessíveis para leitor de tela.
- **Mínimo viável:** `AutomationProperties.Name` nos tiles/cards e nos botões de limpar/excluir, com `HelpText` explicando o gesto.

---

## Parte C — Produtividade e ferramentas (57 itens)

Escopo: notas rápidas (captura, lixeira, arquivo, busca, etiquetas), calendário (eventos, tarefas, aniversários), orçamento de IA, tarefas/progresso da Suite, ferramentas da estante (calculadora, contador, timer/cronômetro, analisador de texto, sons de foco), File Tray/órbita de arquivos e histórico do clipboard.
Método: leitura dos arquivos de cada domínio e busca textual pelos termos que provariam a função (`Reminder`, `Export`, `Import`, `Undo`, `Recurring`, `ICS`, `Preset`, `History`, `SleepTimer`, `OCR`, `Pin`, `Backup`, `SpellCheck`...). Cada item cita `arquivo:linha` ou registra a ausência confirmada; docs/README só foram usados depois de conferir o código.
Observação: o README está desatualizado em `README.md:80` ("Não implementado: conversão de arquivos"), mas `src/Island.Core/Files` já executa 16 ações; esta análise segue o código.

### C01 — Lembretes em notas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** marcar data/hora (e recorrência/snooze) em uma nota e ser avisado mesmo com a ilha recolhida.
- **Hoje:** `QuickNote` guarda só título, conteúdo, checklist, tags, cor, fixação, arquivo e datas (`src/Island.Core/Notes/QuickNote.cs:20-31`); não há campo de lembrete, scheduler ou entrega, e o próprio roadmap diz "Lembretes, OCR, toast, histórico de versões e diário de rascunho não existem" (`docs/notes-roadmap.md:86`).
- **Por que é básico:** nota de captura só fecha o ciclo quando vira tarefa com hora; sem lembrete ela morre na lista.
- **Impacto:** o usuário mantém Notas/ToDo/Alexa externos e deixa de confiar no app para o dia a dia.
- **Mínimo viável:** um lembrete por nota (data/hora local, sem recorrência na primeira versão), notice da ilha + toast do Windows e estado entregue persistido.

### C02 — Anexos e imagens nas notas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** colar ou arrastar uma imagem/arquivo para a nota e vê-lo ao reabrir.
- **Hoje:** o record não tem coleção de anexos (`src/Island.Core/Notes/QuickNote.cs:20-31`), o conteúdo é um `TextBox` puro (`src/Island.App/Shell/QuickNotesWindow.xaml:730-735`) e nenhuma janela de notas aceita drop (`AllowDrop`/`Drop=` ausente em `QuickNotesWindow.xaml` e `QuickCaptureWindow.xaml`).
- **Por que é básico:** metade das ideias capturadas é print, foto de quadro ou PDF; sem colar, a nota fica incompleta.
- **Impacto:** o usuário volta para o Explorer/WhatsApp e a nota deixa de ser o lugar da informação.
- **Mínimo viável:** Ctrl+V/arrastar imagem e arquivo, cópia para pasta do app, miniatura, abrir/revelar e limites de tamanho.

### C03 — Exportar/importar notas e imprimir
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** exportar a nota atual em Markdown/TXT, exportar todas em ZIP, importar `.md`/`.txt` e imprimir.
- **Hoje:** os comandos do editor acabam em criar, selecionar, fixar, arquivar, lixeira, restaurar e checklist (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`); não há classe de exportação/importação em `src/Island.Core/Notes` nem `src/Island.Windows/Notes` (busca `Export|Import` vazia); o roadmap só promete isso para a v1.0 (`docs/notes-roadmap.md:306-313,507-511`).
- **Por que é básico:** dado local precisa sair do app (backup manual, outro editor, apresentação); sem isso, o app é uma gaiola.
- **Impacto:** migração e impressão impossíveis; o usuário duplica conteúdo em outro editor.
- **Mínimo viável:** exportar a nota selecionada (.md) e todas (.zip), importar .md/.txt com prévia e imprimir a nota.

### C04 — Backup, restauração e notes.json corrompido
- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** recuperar as notas de um backup e ser avisado quando o arquivo principal está ilegível.
- **Hoje:** todas as notas vivem em um único `notes.json` reescrito inteiro a cada edição (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:83-103`); se o JSON está inválido, o arquivo é movido para `notes.json.bad` e o app segue vazio, sem aviso na interface e sem caminho de restauração (`:66-75`); não há serviço de backup (`Backup|Restore` ausente em `src/Island.Windows/Notes`; plano em `docs/notes-roadmap.md:292-306`).
- **Por que é básico:** é o único dado insubstituível do domínio; qualquer editor pode salvar um JSON inválido por bug e o usuário não percebe a perda.
- **Impacto:** perda silenciosa de todas as notas; quebra de confiança no app como caderno.
- **Mínimo viável:** backup automático (diário + rotação) e tela "Restaurar backup", com aviso e botão "recuperar arquivo .bad" quando a leitura falhar.

### C05 — Diário de rascunho contra encerramento forçado
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** não perder o que digitou se o app for encerrado à força ou o PC cair.
- **Hoje:** o autosave só escreve após debounce de 450 ms (`src/Island.App/ViewModels/QuickNotesViewModel.cs:59,520-546`) e o flush existe apenas no fechamento normal (`src/Island.App/Shell/QuickNotesWindow.xaml.cs:286-315`); não há diário de rascunho (`docs/notes-roadmap.md:259-280` descreve o que é "planejado").
- **Por que é básico:** um editor de texto maduro assume crash; autosave com debounce não substitui uma cópia em disco da digitação pendente.
- **Impacto:** perda da digitação ainda não gravada (debounce + escrita pendente) em queda de energia/kill; erro de I/O mantém o texto só na memória do editor.
- **Mínimo viável:** gravar rascunho a cada ~250 ms, removê-lo após salvar e oferecer "Recuperar rascunho/Descartar" na abertura.

### C06 — Desfazer e histórico de versões
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** Ctrl+Z depois de excluir/arquivar e poder ver/restaurar uma versão anterior da nota.
- **Hoje:** a lixeira permite restaurar e o arquivo é reversível, mas não há pilha de undo nem histórico: os comandos destrutivos são chamados direto (`src/Island.App/ViewModels/QuickNotesViewModel.cs:401-474`), a tecla Delete manda para a lixeira sem confirmação (`src/Island.App/Shell/QuickNotesWindow.xaml.cs:189-195`) e `Undo|History` não aparece no domínio; o roadmap planeja 8 passos de undo e versões (`docs/notes-roadmap.md:282-290,488-490`).
- **Por que é básico:** toda ação em lista é passível de clique errado; o usuário espera Ctrl+Z imediato.
- **Impacto:** restauração dá trabalho (ir à lixeira) e "esvaziar lixeira" é irreversível; edições antigas não podem ser recuperadas.
- **Mínimo viável:** undo de 8 passos para excluir/arquivar/limpar checklist e histórico simples por nota antes de cada importação/migração.

### C07 — Lixeira sem expiração nem prazo visível
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que notas na lixeira sejam apagadas após um prazo (30 dias) e ver quanto tempo falta.
- **Hoje:** mover para a lixeira só grava `DeletedAt` (`src/Island.Core/Notes/QuickNotesService.cs:188-200`) e o único expurgo é o botão manual "Esvaziar lixeira" (`:152-160`); não há job, prazo ou aviso (plano em `docs/notes-roadmap.md:284-286`).
- **Por que é básico:** lixeira sem expiração vira depósito eterno de lixo e esvaziá-la manualmente é a única forma de liberar espaço.
- **Impacto:** lixeira cresce sem limite; "esvaziar" vira uma decisão arriscada em bloco.
- **Mínimo viável:** expurgo automático após 30 dias com backup, contagem de dias por item e texto "será apagada em X dias".

### C08 — Busca escopada, sem checklist e sem busca global
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** procurar em todas as notas, ignorando maiúsculas/acentos, incluindo itens de checklist, e buscar também no calendário.
- **Hoje:** `GetNotes(collection, query)` filtra apenas a aba atual e só título/conteúdo/tags, com `Contains(..., OrdinalIgnoreCase)` (sensível a acento) (`src/Island.Core/Notes/QuickNotesService.cs:41-71`, condição em `:62-68`); o checklist não é pesquisado e a janela chama com `SelectedCollection` (`src/Island.App/ViewModels/QuickNotesViewModel.cs:605-614`). Não há busca global no app (nenhum índice; `Palette`/`Ctrl+K` ausente).
- **Por que é básico:** em 100+ notas, busca é o principal meio de navegação; limitar à aba esconde justamente o que está arquivado.
- **Impacto:** o usuário esquece onde salvou a nota e não encontra itens de checklist; busca no calendário é impossível.
- **Mínimo viável:** busca em todas as coleções, sem acento, com destaque e inclusão do checklist; segundo passo, incluir calendário.

### C09 — Etiquetas sem gerenciamento nem filtro
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** autocomplete ao digitar, renomear/mesclar/excluir etiquetas e clicar em uma para filtrar.
- **Hoje:** a etiqueta é um `TextBox` de vírgulas no editor (`src/Island.App/Shell/QuickNotesWindow.xaml:809-830`; `src/Island.App/ViewModels/QuickNotesViewModel.cs:252-259`) e a lista só exibe o texto (`QuickNotesWindow.xaml:547-550`); não há catálogo, contagem, renomear/mesclar nem filtro (busca `TagsCatalog`/`ITagCatalog` em `src` vazia); só o texto da busca procura dentro das tags.
- **Por que é básico:** tags digitadas à mão viram sinônimos ("trabalho", "Trabalho", "trab") e não servem para organizar.
- **Impacto:** duplicidade de etiquetas e organização manual inútil; a lista por etiqueta não existe.
- **Mínimo viável:** catálogo derivado com autocomplete, renomear/mesclar com atualização em lote e clique na etiqueta para filtrar.

### C10 — Multi-seleção e ações em lote
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** Ctrl/Shift-clicar várias notas e arquivar, etiquetar ou mandar para a lixeira de uma vez.
- **Hoje:** a lista não define `SelectionMode` (fica o padrão de seleção única) (`src/Island.App/Shell/QuickNotesWindow.xaml:502-509`) e todos os comandos operam em `SelectedNote` (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`); o lote está no roadmap para depois do contrato de operações (`docs/notes-roadmap.md:452-453`).
- **Por que é básico:** limpar a lista depois de uma semana exige uma ação por nota; tarefas de organização são naturalmente em lote.
- **Impacto:** trabalho braçal e desistência de organizar; lixeira/arquivo crescem desordenados.
- **Mínimo viável:** seleção múltipla e barra com Arquivar/Lixeira/Etiqueta (com confirmação e undo).

### C11 — Duplicar nota
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** criar uma cópia da nota (checklist/tags/cor) para outro contexto.
- **Hoje:** não existe comando de duplicar no view model (`src/Island.App/ViewModels/QuickNotesViewModel.cs:95-107`) nem atalho na janela; a única criação é `BeginNewCommand`.
- **Por que é básico:** templates e checklists se repetem (mesma lista de compras, mesmo roteiro); copiar e colar manualmente é o contorno comum.
- **Impacto:** retrabalho para repetir checklists e notas recorrentes.
- **Mínimo viável:** "Duplicar" que cria nota com novo Id e título "cópia", mantendo o resto.

### C12 — Templates e captura inteligente
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** começar de um template (reunião, compras, nota do dia, projeto) e digitar `#tag`, `- [ ]` ou `@amanhã` na captura rápida.
- **Hoje:** a captura rápida só tem texto, cor e fixar — `SaveAsync` chama `CaptureAsync(text, color, pinned)` (`src/Island.App/Shell/QuickCaptureWindow.xaml.cs:240-269`; `src/Island.Core/Notes/QuickNotesService.cs:122-149`); não há templates embutidos nem parser (o roadmap descreve o parser e os 5 templates como v0.8, `docs/notes-roadmap.md:426-452`).
- **Por que é básico:** captura rápida sem sintaxe cria sempre nota crua; templates eliminam digitação repetitiva.
- **Impacto:** o usuário digita listas/tags manualmente depois; captura deixa de ser "uma linha".
- **Mínimo viável:** 5 templates fixos + parser de `#tag`, checklist e data relativa no capture, com chips de prévia.

### C13 — Nota fixada na ilha/recolhida
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** ver a nota fixada (ou a próxima) na cápsula recolhida e abri-la com um clique.
- **Hoje:** o conteúdo ocioso só conhece clima e Pomodoro agendado (`src/Island.Core/Idle/IdleContent.cs:11-20`; `src/Island.Core/Idle/IdleContentResolver.cs:38-49`), e o widget mostra apenas as 2 notas mais recentes (`src/Island.App/Views/Widgets/QuickNotesWidget.xaml.cs:24,72-76`); a nota fixada na ilha é plano v0.6 (`docs/notes-roadmap.md:329-373`).
- **Por que é básico:** a proposta do app é presença constante; a nota importante precisa estar visível sem abrir a estante.
- **Impacto:** a ilha não ajuda a manter a tarefa/nota do dia à vista; usuário cola post-it físico no monitor.
- **Mínimo viável:** exibir a nota fixada mais recente na cápsula (com truncamento e opção de esconder o texto) e abrir a nota no clique.

### C14 — Calendário sem editar nem excluir itens
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** corrigir título/hora e apagar um evento, tarefa ou aniversário criado errado.
- **Hoje:** `CalendarAgenda` expõe apenas `AddEvent`, `AddBirthday`, `AddTask` e `SetTaskCompleted` (`src/Island.Core/Calendar/CalendarAgenda.cs:88-137`); busca por `Delete|Remove|Edit|Update` em `src/Island.Core/Calendar` não retorna nada, e a janela do dia só adiciona e alterna conclusão (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:40-42,173-176,238-246`).
- **Por que é básico:** calendário sem edição/apagar garante erro permanente no primeiro typo; é a função mais básica de uma agenda.
- **Impacto:** agenda poluída por itens errados para sempre; usuário abandona a agenda do app.
- **Mínimo viável:** editar qualquer campo, excluir com confirmação e desfazer; mesmo sem recorrência.

### C15 — Evento sem hora de término/duração
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** marcar início e fim (ou duração) e ver "14:00–15:30" na agenda.
- **Hoje:** `CalendarEvent` é `(Id, Title, Date, Time?)` (`src/Island.Core/Calendar/CalendarEvent.cs:3`); a janela aceita um único HH:mm e avisa "Deixe vazio para dia inteiro" (`src/Island.App/Shell/CalendarEntryWindow.xaml:80-90`; `src/Island.App/Shell/CalendarEntryWindow.xaml.cs:118-129`).
- **Por que é básico:** reunião/consulta tem duração; sem fim não há noção de conflito, "ocupado até", nem sequência do dia.
- **Impacto:** a agenda não serve para planejar o dia; usuário mantém Google Agenda ao lado.
- **Mínimo viável:** fim opcional (ou duração) e exibição do intervalo na lista do dia.

### C16 — Sem recorrência de eventos/tarefas
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** repetir diariamente, em dias úteis, semanalmente ou anualmente (e editar "esta/estas/todas").
- **Hoje:** nenhum campo de recorrência em evento, tarefa ou aniversário além do aniversário anual implícito (`src/Island.Core/Calendar/CalendarEvent.cs:3`, `CalendarTask.cs:3`, `CalendarBirthday.cs:3`); o roadmap confirma que `CalendarTask` "não tem hora, recorrência, snooze nem ocorrências" (`docs/notes-roadmap.md:87-88`).
- **Por que é básico:** compromissos fixos (aula, reunião semanal, remédio) são maioria; sem recorrência a agenda é só "feriado pessoal".
- **Impacto:** recriar o mesmo evento toda semana; usuário desiste da agenda do app.
- **Mínimo viável:** regras diária/semanal/mensal/anual com ocorrências derivadas e exclusão de uma ocorrência.

### C17 — Eventos sem lembrete/notificação
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso "evento em 10 min" mesmo com o app sem foco.
- **Hoje:** não há scheduler nem remetente de aviso no calendário (`Reminder|Notification` ausente em `src/Island.Core/Calendar` e `src/Island.Windows/Calendar`); existe o canal de `Notice` da ilha, mas nenhum fluxo do calendário o dispara, e não há toast do Windows (`AppNotification` ausente em `src`).
- **Por que é básico:** evento com hora só serve se avisar; sem isso a agenda não compete com a do Windows/telefone.
- **Impacto:** eventos marcados são esquecidos; o app vira registro passivo.
- **Mínimo viável:** antecedência configurável por evento, notice da ilha e toast/repetição opcional.

### C18 — Só visão mês + janela do dia
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** visão de semana, dia/agenda e "próximos 7 dias", com navegação e criação no contexto.
- **Hoje:** só existe a grade de mês (`src/Island.App/Views/Widgets/CalendarWidget.xaml.cs:149-228`) e a janela de uma data (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:102-176`); não há tela de semana/agenda nem lista de próximos (o "Hoje" unificado está no roadmap, `docs/notes-roadmap.md:411-418`).
- **Por que é básico:** planejar a semana é o uso principal de um calendário; a grade de mês não mostra horários nem conflitos.
- **Impacto:** usuário não consegue enxergar a semana; volta para a agenda do celular.
- **Mínimo viável:** visão agenda (próximos 7 dias) e visão semana, reutilizando os mesmos itens.

### C19 — Sem importar/exportar .ics
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** exportar a agenda para .ics e importar um convite/calendário.
- **Hoje:** o calendário persiste em JSON próprio (`src/Island.Windows/Calendar/JsonCalendarStore.cs`) e não há `iCalendar`/`VCALENDAR`/`.ics` em `src` (busca `ICS|VCALENDAR|iCalendar` vazia).
- **Por que é básico:** .ics é o formato universal de troca; sem ele não há integração com Outlook/Google sem digitação manual.
- **Impacto:** retrabalho para levar eventos de/para outro calendário; impossível participar de convites.
- **Mínimo viável:** exportar todos os itens e importar .ics simples (VEVENT/DTSTART) com prévia e deduplicação.

### C20 — Calendário sem busca
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** procurar "dentista" e pular para a data, sem varrer mês por mês.
- **Hoje:** nenhuma das duas telas tem campo de busca (`src/Island.App/Views/Widgets/CalendarWidget.xaml` e `src/Island.App/Shell/CalendarDayWindow.xaml` não têm `TextBox`/`Search`/`Buscar`), e `CalendarAgenda` só consulta por dia/mês (`src/Island.Core/Calendar/CalendarAgenda.cs:45-86`).
- **Por que é básico:** busca é a forma de achar o compromisso futuro ("quando era a consulta?").
- **Impacto:** o usuário navega meses manualmente e desiste.
- **Mínimo viável:** busca por título em todos os itens com lista de resultados e salto para a data.

### C21 — Evento só com título/data/hora
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** cor/categoria, local, descrição/link e talvez participantes.
- **Hoje:** `CalendarEvent` tem apenas título, data e hora (`src/Island.Core/Calendar/CalendarEvent.cs:3`); a janela de criação pede só nome e HH:mm (`src/Island.App/Shell/CalendarEntryWindow.xaml:70-90`).
- **Por que é básico:** evento real carrega contexto (onde, com quem, link da reunião) e cor/categoria para distinguir trabalho de pessoal.
- **Impacto:** informação fica em outro app; cores não distinguem tipos de compromisso.
- **Mínimo viável:** local e descrição opcionais, uma cor/categoria por evento.

### C22 — Tarefa sem hora, prioridade e descrição
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** prazo com hora, prioridade e uma nota/descrição; ordenar por prioridade.
- **Hoje:** `CalendarTask` é `(Id, Title, DueDate, IsCompleted)` (`src/Island.Core/Calendar/CalendarTask.cs:3`); não há hora, prioridade, descrição, etiqueta nem recorrência, e a linha do dia só mostra o título (`src/Island.App/Shell/CalendarDayWindow.xaml.cs:200-248`).
- **Por que é básico:** tarefa de calendário precisa de prazo/urgência; sem prioridade ou até que horas, não dá para planejar o dia.
- **Impacto:** tarefas urgentes se misturam às triviais; usuário mantém lista externa.
- **Mínimo viável:** hora opcional, prioridade (alta/média/baixa) e descrição curta; ordenar por prioridade/hora.

### C23 — Aniversários sem idade e sem antecedência
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** registrar o ano de nascimento, ver "N anos hoje" e ser lembrado alguns dias antes.
- **Hoje:** `CalendarBirthday` é `(Id, Name, Month, Day)` (`src/Island.Core/Calendar/CalendarBirthday.cs:3`); `AddBirthday(name, month, day)` usa só mês/dia da data selecionada (`src/Island.Core/Calendar/CalendarAgenda.cs:95-104`; `src/Island.App/Shell/CalendarEntryWindow.xaml.cs:95-104,141-143`), e a linha do dia mostra apenas "Aniversário" (`CalendarDayWindow.xaml.cs:250-251`); não há aviso/notificação.
- **Por que é básico:** aniversário sem idade e sem aviso é só um marcador no mês; quem organiza presente quer antecedência.
- **Impacto:** a data passa; o usuário usa o calendário do telefone.
- **Mínimo viável:** ano opcional, idade calculada no dia e lembrete X dias antes.

### C24 — Orçamento sem histórico de consumo
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber quando e quanto consumiu em cada dia (e desfazer um lançamento).
- **Hoje:** o consumo é um único `EnteredValue` acumulado (`src/Island.Core/Budgets/Budget.cs:14,25-26`), e `AddUsage` só soma/subtrai no total (`src/Island.Core/Budgets/BudgetBook.cs:88-96`); não existe log por dia/hora, então o "+1%" não deixa rastro.
- **Por que é básico:** orçamento de IA é usado para controlar ritmo; sem histórico não há como saber se o gasto foi ontem ou hoje.
- **Impacto:** impossível auditar o uso; erro de +5% não tem origem para corrigir.
- **Mínimo viável:** lançamentos com data/hora e delta, saldo calculado e extrato no editor.

### C25 — Sem desfazer os +1%/+5%
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** desfazer um toque acidental (ou corrigir digitando o valor).
- **Hoje:** os botões do widget chamam `AddUsage` direto (`src/Island.App/Views/Widgets/BudgetWidget.xaml.cs:25-28,54-57`; `src/Island.Core/Budgets/BudgetBook.cs:88-96`), sem confirmação, undo ou histórico; a única correção é editar o valor total no editor (`src/Island.App/Budgets/ViewModels/BudgetViewModel.cs:68-72`).
- **Por que é básico:** +5% é um toque em botão pequeno na estante; errar é comum e não deveria exigir abrir editor.
- **Impacto:** números do orçamento perdem confiança; usuário recalcula tudo à mão.
- **Mínimo viável:** desfazer o último lançamento por alguns segundos (como já existe no contador).

### C26 — Orçamento sem alerta de esgotamento
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso quando o restante chega a zero ou o consumo do dia passa do previsto.
- **Hoje:** o status do orçamento só trata "Começa em", "Período encerrado" e "Hoje você marcou como dia sem uso" (`src/Island.App/Budgets/ViewModels/BudgetTextFormatter.cs:55-69`); não há notice/alerta e o valor fica limitado ao total (`src/Island.Core/Budgets/Budget.cs:25-26`), então o usuário só descobre olhando o widget.
- **Por que é básico:** um orçamento serve para frear o consumo; sem alerta ele é um medidor passivo.
- **Impacto:** estouro silencioso; o usuário percebe quando já gastou a cota.
- **Mínimo viável:** aviso da ilha ao atingir 90%/100% e ao ultrapassar o valor previsto do dia (uma vez por dia, sem spam).

### C27 — Sem exportar extrato do orçamento
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** exportar CSV (período, total, usado, restante, dias excluídos) para planilha.
- **Hoje:** não há exportação/impressão no domínio (`Export|Csv|Print` ausente em `src/Island.App/Budgets`); os dados só existem no `budgets.json`.
- **Por que é básico:** controle de gasto pede registro externo/relatório mensal; sem exportar, o histórico (que nem existe, C24) fica preso.
- **Impacto:** impossível conciliar com planilha ou apresentar o gasto.
- **Mínimo viável:** exportar CSV do período atual e, com C24, do histórico de lançamentos.

### C28 — Sem novo período/arquivar período anterior
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** iniciar um novo ciclo com um botão, guardando o resultado do período anterior.
- **Hoje:** o editor só ajusta nome, início, fim, hora de reinício, total, modo e consumo (`src/Island.App/Budgets/Views/BudgetEditor.xaml:16-86`; `src/Island.App/Budgets/ViewModels/BudgetViewModel.cs:38-84`); para recomeçar é preciso editar as datas e zerar o consumo manualmente, e o período encerrado vira "Período encerrado" sem arquivo (não há "Novo período").
- **Por que é básico:** o orçamento tem que ser recorrente; toda semana o usuário precisa fazer essa manutenção manual e perde o comparativo.
- **Impacto:** trabalho semanal e perda do histórico de semanas anteriores.
- **Mínimo viável:** "Novo período" que arquiva os números do ciclo encerrado e reinicia o consumo.

### C29 — Sem lista própria de tarefas/visão agregada
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** uma tela com todos os pendentes/atrasados/hoje, independente da data em que foram criados.
- **Hoje:** tarefas do calendário só aparecem na janela de um dia (`src/Island.Core/Calendar/CalendarAgenda.cs:45-63`; `src/Island.App/Shell/CalendarDayWindow.xaml.cs`), e o widget Tarefas mostra apenas as tarefas da Suite (`src/Island.App/Views/Widgets/TasksWidget.xaml.cs:1-17`); não há lista "pendentes/atrasadas" no app.
- **Por que é básico:** lista de tarefas existe para não esquecer o que está aberto; esconder por data é o oposto de um To-Do.
- **Impacto:** tarefas de ontem não concluídas somem da vista; usuário mantém lista paralela.
- **Mínimo viável:** visão com atrasadas/hoje/próximas/concluídas, concluir e adiar (data) direto na lista.

### C30 — Tarefa sem lembrete de prazo
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** aviso no dia (e na hora, se houver) e destaque de atrasadas ao abrir o app.
- **Hoje:** `CalendarTask` só tem `DueDate` (`src/Island.Core/Calendar/CalendarTask.cs:3`); nada no app agenda aviso de vencimento (`Reminder`/`Notification` ausente no calendário; o `Notice` da ilha nunca é disparado por tarefa) e o widget do mês só marca o dia com um ponto (`src/Island.App/Views/Widgets/CalendarWidget.xaml.cs:182-212`).
- **Por que é básico:** tarefa com prazo sem aviso é anotação; o usuário precisa ser lembrado.
- **Impacto:** prazos perdidos; o app não substitui lembrete do celular.
- **Mínimo viável:** notice de tarefas do dia ao iniciar e de atrasadas, com ação "concluir/adiar".

### C31 — Widget Tarefas é passivo (só leitura da Suite)
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** criar uma tarefa local, concluir ou cancelar dentro do próprio widget.
- **Hoje:** `TasksWidget` só define o `DataContext` para `IslandViewModel.TaskProgress` (`src/Island.App/Views/Widgets/TasksWidget.xaml.cs:1-17`), o presenter não expõe comandos (`src/Island.App/ViewModels/ProgressPresenter.cs:19-104`) e a lista mostra no máximo 3 linhas (`:24`); criar/concluir tarefa só pelo calendário, cancelar não existe em lugar nenhum.
- **Por que é básico:** widget de tarefas que não aceita ação obriga a abrir outra janela para o gesto mais frequente (concluir).
- **Impacto:** widget vira painel decorativo; usuário ignora.
- **Mínimo viável:** botão de concluir/cancelar por linha e "+ tarefa" com captura de uma linha.

### C32 — Sem histórico das tarefas da Suite
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver as últimas tarefas concluídas/falhas, com percentual final e horário.
- **Hoje:** `ProgressTracker` mantém tudo em memória, remove a tarefa concluída após 4 s (`src/Island.Core/Progress/ProgressTracker.cs:17,154-161,188-204`) e `Dispose` limpa as entradas (`:93-104`); nada é persistido, então ao reiniciar a ilha não há histórico.
- **Por que é básico:** tarefa longa (corte/exportação) pode durar minutos e o usuário sai da mesa; depois quer conferir se terminou e qual foi o resultado.
- **Impacto:** perde-se a prova de conclusão/erro; usuário refaz trabalho por dúvida.
- **Mínimo viável:** histórico das últimas 20 tarefas com estado final, hora e mensagem, sobrevivendo ao reinício.

### C33 — Calculadora sem histórico
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver as últimas contas e reaproveitar um resultado anterior.
- **Hoje:** o motor guarda só a conta corrente (`src/Island.Core/Tools/Calculator/CalculatorEngine.cs:18-24`) e o widget só tem visor e keypad (`src/Island.App/Views/Widgets/CalculatorWidget.xaml:7-31`); não há lista de histórico.
- **Por que é básico:** histórico é padrão em calculadora de desktop e evita redigitar a mesma soma.
- **Impacto:** contas repetidas do zero; impossível conferir o cálculo anterior.
- **Mínimo viável:** últimas 10 contas com expressão + resultado, clicáveis para reutilizar.

### C34 — Calculadora sem memória (M+/M−/MR/MC)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** guardar um número na memória e reutilizá-lo em outra conta.
- **Hoje:** `CalculatorKey` tem apenas dígitos, operadores, `%`, `±`, backspace e C (`src/Island.Core/Tools/Calculator/CalculatorKey.cs:3-26`); não há variável nem teclas de memória.
- **Por que é básico:** memória é a segunda função mais usada de uma calculadora (somar várias parcelas, aplicar um fator).
- **Impacto:** usuário anota o intermediário no papel/bloco.
- **Mínimo viável:** memória única com indicador e teclas M+/M−/MR/MC.

### C35 — Calculadora sem funções científicas nem precedência
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** parênteses, potência/raiz e operações com precedência correta.
- **Hoje:** o motor é "pocket" e calcula da esquerda para a direita, declaradamente sem precedência (2+3×4 = 20) (`src/Island.Core/Tools/Calculator/CalculatorEngine.cs:5-8`); não há raiz, potência, parênteses nem funções trigonométricas (enum `CalculatorKey.cs:3-26`).
- **Por que é básico:** contas de produtividade envolvem percentual composto, área, raiz; sem precedência, o resultado está errado para expressões comuns.
- **Impacto:** resultado incorreto sem aviso; usuário não confia e abre a calculadora do Windows.
- **Mínimo viável:** parênteses e potenciação/raiz em modo científico, ou explorar precedência correta.

### C36 — Copiar resultado escondido no clique direito
- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** botão/ícone "copiar resultado" (e Ctrl+C quando houver teclado).
- **Hoje:** copiar só funciona com clique direito no visor (`src/Island.App/Views/Widgets/CalculatorWidget.xaml.cs:36-42,77-89`); o XAML só tem os dois `TextBlock` do visor e o keypad (`src/Island.App/Views/Widgets/CalculatorWidget.xaml:7-31`), sem botão, dica ou atalho.
- **Por que é básico:** o motivo de usar a calculadora no app é jogar o número em outro lugar; um gesto invisível faz a função parecer inexistente.
- **Impacto:** usuário redigita o número ou usa a calculadora do Windows.
- **Mínimo viável:** botão "copiar" no visor com feedback ("copiado").

### C37 — Contador único, sem múltiplos contadores
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** vários contadores nomeados (água, repetições, partidas) convivendo e alternando.
- **Hoje:** existe um único `counter.json` e um único `CounterModel` (`src/Island.Windows/Tools/Counter/CounterStore.cs:12-40`; `src/Island.App/Views/Widgets/CounterWidget.xaml.cs:66-70`); "trocar rótulo" só percorre 6 presets (`src/Island.Core/Tools/Counter/CounterModel.cs:15,94-99`), sem criar/selecionar contadores.
- **Por que é básico:** contador de hábito é usado para mais de uma coisa; um contador por vez obriga a zerar e perder o outro.
- **Impacto:** usuário anota no papel ou perde a contagem anterior.
- **Mínimo viável:** lista de contadores nomeados, com criar/renomear/excluir e um valor cada.

### C38 — Contador preso a presets e sem valor digitável
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** definir passo livre, nome próprio, meta livre e digitar/corrigir o valor atual.
- **Hoje:** passo só 1/5/10 (`src/Island.Core/Tools/Counter/CounterModel.cs:13,80-85`), rótulo só 6 presets (`:15,94-99`), meta só 0/10/20/50/100 (`:14,87-92`) e não existe setter do valor — só `Increment/Decrement/Reset/Undo` (`:56-78`).
- **Por que é básico:** o contador começa no meio de uma contagem (ou erra um toque) e precisa ser corrigido; passo de 3, meta de 40 são comuns.
- **Impacto:** correção exige dezenas de cliques; contador não serve para metas reais.
- **Mínimo viável:** passo e meta editáveis, nome livre e "definir valor".

### C39 — Timer sem notificação de término e alarme não insiste
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ser avisado ao fim do timer mesmo com a estante fechada, com som que insiste até parar.
- **Hoje:** o fim toca `SystemSounds.Asterisk.Play` uma única vez (`src/Island.App/Views/Widgets/TimerWidget.xaml.cs:50-51`) e o widget pisca "Pronto!" por 3 s (`:22-24`); não há notice da ilha nem toast do Windows (busca `AppNotification|ToastNotification` em `src` vazia) e não há botão de parar/repetir o alarme.
- **Por que é básico:** timer serve para lembrar enquanto o usuário está em outro app; um som curto do esquema do Windows, que pode estar mudo, é fácil de perder.
- **Impacto:** comida queima, reunião atrasa; usuário usa o timer do celular.
- **Mínimo viável:** notice persistente da ilha + toast, som repetindo até "parar", com opção de repetir o timer.

### C40 — Um timer por vez
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** dois ou três timers independentes (forno, alongamento, pausa) com nome e tempo restante.
- **Hoje:** `TimerTools` guarda um `CountdownModel` e um `StopwatchModel` e o widget alterna entre esses dois modos (`src/Island.Core/Tools/Timers/TimerTools.cs:11-23`; `src/Island.App/Views/Widgets/TimerWidget.xaml.cs:58-70`); não há lista de timers.
- **Por que é básico:** na cozinha/estudo, timers se sobrepõem; com um só modelo, configurar o segundo destrói a contagem do primeiro.
- **Impacto:** o usuário volta para o timer do celular ou perde um dos prazos.
- **Mínimo viável:** lista de até N timers nomeados, cada um com play/pause/reset e alarme próprio.

### C41 — Presets fixos de duração e sem salvar
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** salvar durações usadas (ex.: 7, 12, 20 min) e dar um título ao timer.
- **Hoje:** presets fixos de 1, 3, 5, 10, 15, 25, 45 e 60 min em ciclo (`src/Island.Core/Tools/Timers/CountdownModel.cs:13-14,119-133`), com ajuste de ±1 min/10 s e faixa 10 s–180 min (`:10-11`); presets próprios não são salvos em lugar nenhum.
- **Por que é básico:** cada pessoa repete 2–3 durações; ciclar por 8 presets toda vez é atrito diário.
- **Impacto:** ajustes repetidos a cada uso; sem título, vários timers (C40) não se distinguem.
- **Mínimo viável:** salvar presets do usuário e título por timer.

### C42 — Timer e cronômetro não sobrevivem ao reinício
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** fechar o app (ou o app atualizar) e retomar o timer/cronômetro em andamento.
- **Hoje:** os modelos vivem no `ToolRegistry` em memória (`src/Island.Core/Tools/ToolRegistry.cs:4-7`) e não têm store — só o contador tem `CounterStore`; `TimerTools` não grava nada (`src/Island.Core/Tools/Timers/TimerTools.cs:7-23`).
- **Por que é básico:** o app tem auto-update e o usuário pode fechá-lo no meio de uma contagem; perder o timer é inaceitável para uma tarefa de 90 min.
- **Impacto:** contagem perdida; usuário desconfia de deixar timer no app.
- **Mínimo viável:** persistir estado (duração, iniciado em, pausa) e retomar com confirmação "continuar/zerar".

### C43 — Analisador só lê clipboard ou .txt arrastado
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** digitar/colar um texto ou abrir um arquivo pelo diálogo e analisar.
- **Hoje:** o widget só analisa o texto mais recente do clipboard ou um `.txt` arrastado (`src/Island.App/Views/Widgets/TextAnalyzerWidget.xaml.cs:18-20,53,226-232`); não há campo de entrada nem diálogo de abrir, e outros formatos de texto são recusados (`:231-232` só aceita `.txt`).
- **Por que é básico:** analisar um texto que não está no clipboard é o caso óbvio ("quanto deu este capítulo?"); depender do clipboard torna a ferramenta refém de outro app.
- **Impacto:** usuário copia de qualquer jeito e perde a seleção atual do clipboard; .md/.csv não servem.
- **Mínimo viável:** caixa de texto/colar manual e "Abrir arquivo" aceitando .txt/.md/.csv.

### C44 — Estatísticas incompletas e sem métricas úteis
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** linhas, caracteres sem espaços, palavras por frase, densidade de palavras e legibilidade.
- **Hoje:** `TextStats` calcula linhas e caracteres sem espaços (`src/Island.Core/Tools/TextAnalyzer/TextStats.cs:7-16`), mas a UI mostra só 6 cards — Caracteres, Palavras, Parágrafos, Leitura, Tokens e Frases (`src/Island.App/Views/Widgets/TextAnalyzerWidget.xaml.cs:43-51`; rótulos em `TextAnalyzerWidget.xaml:61-72`); as linhas calculadas nunca são exibidas e "caracteres sem espaços" só aparece em tooltip, sem métricas avançadas.
- **Por que é básico:** analisador de texto existe para decisão (cabe no limite? está denso? tempo de fala?); parte do que o motor já calcula não chega ao usuário.
- **Impacto:** dado calculado e descartado; usuário usa site externo para o resto.
- **Mínimo viável:** mostrar linhas/sem espaços e adicionar palavras por frase, densidade e tempo de fala.

### C45 — Sons de foco sem presets e sem persistência
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** salvar mixes (chuva+café), volume por som e timer de dormir, e reencontrar tudo ao reabrir o app.
- **Hoje:** a mistura, volume e timer vivem no modelo em memória (`src/Island.Core/Tools/FocusSounds/FocusSoundsModel.cs:8-9`; `src/Island.Core/Tools/ToolRegistry.cs:4-7`), e `IslandSettings` não tem nenhum campo de sons de foco (`src/Island.Core/Configuration/IslandSettings.cs:11-127`); não há preset salvo.
- **Por que é básico:** som de foco é usado sempre com a mesma combinação; reajustar 8 chips e o volume a cada sessão é atrito.
- **Impacto:** usuário não usa o recurso ou deixa tocando sem controle; configuração se perde a cada reinício.
- **Mínimo viável:** 2–3 presets (sons + volumes) e restauração do último mix/volume/timer.

### C46 — Nível por som só pela roda do mouse
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ajustar o volume de cada som com controle visível, arrastável e acessível por teclado.
- **Hoje:** o nível de cada som só muda pela roda do mouse sobre o chip (`src/Island.App/Views/Widgets/FocusSoundsWidget.xaml.cs:76-77`); o chip desenha uma barra fina (`src/Island.App/Views/Widgets/FocusSoundChip.cs:20-24`), mas não há alça, valor numérico nem ação de teclado — e o volume geral só tem slider na barra inferior (`FocusSoundsWidget.xaml:80-83`).
- **Por que é básico:** quem usa touchpad, teclado ou leitor de tela não consegue ajustar a mistura; a roda é gesto escondido até para mouse.
- **Impacto:** mix desbalanceado ou inutilizável para parte dos usuários.
- **Mínimo viável:** slider por som (ou menu com valor) focável e anunciado por automação.

### C47 — Sem escolher o dispositivo de saída
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** tocar os sons no fone e deixar o resto do PC na caixa, ou escolher o dispositivo.
- **Hoje:** `FocusAudioOutput` sempre abre o endpoint padrão `GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` (`src/Island.Windows/Audio/FocusAudioOutput.cs:107-113`); não há seletor nem setting no domínio.
- **Por que é básico:** em reunião/estudo, o dispositivo do som de foco precisa ser diferente do de notificação; apps de som ambiente oferecem isso.
- **Impacto:** som de foco sai no dispositivo errado; usuário evita usar.
- **Mínimo viável:** seletor de saída nas configurações, padrão "dispositivo do sistema".

### C48 — File Tray não persiste entre reinícios
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** reabrir o app e continuar com os arquivos separados na bandeja.
- **Hoje:** `FileTray` é uma lista em memória ("In-memory list of file paths") (`src/Island.Core/Shelf/FileTray.cs:1-9`) registrada como singleton (`src/Island.App/Composition/ServiceRegistration.cs:162`); não há store/settings, e fechar o app esvazia a bandeja.
- **Por que é básico:** bandeja de arquivos é área de trabalho temporária; se some a cada fechamento/atualização, não dá para confiar nela.
- **Impacto:** usuário perde a seleção de arquivos no meio de um fluxo; volta a usar o Explorer.
- **Mínimo viável:** salvar os caminhos (validando existência na leitura) e restaurar ao abrir, com "Limpar" explícito.

### C49 — Bandeja sem abrir, copiar caminho, prévia ou renomear
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** abrir/revelar o arquivo, copiar o caminho e ver a prévia (imagem/documento).
- **Hoje:** o chip da bandeja só remove ou é arrastado para fora (`src/Island.App/Views/Widgets/FileTrayWidget.xaml.cs:129-145,158-174`); não há menu de contexto, abrir, copiar caminho, renomear ou miniatura (o ícone é genérico por extensão, `:104-111`).
- **Por que é básico:** bandeja serve para agir sobre os arquivos; só remover/arrastar limita a "prateleira".
- **Impacto:** usuário abre o Explorer para qualquer ação; a bandeja vira decoração.
- **Mínimo viável:** menu com Abrir/Revelar/Copiar caminho/Remover e prévia de imagem.

### C50 — Conversões dependem de ferramentas externas não incluídas
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** converter vídeo/áudio/documento/PDF sem instalar nada à mão.
- **Hoje:** vídeo e áudio exigem `ffmpeg`, documentos PDF exigem LibreOffice e comprimir PDF exige Ghostscript; sem o binário a ação fica desabilitada com uma mensagem (`src/Island.Core/Files/FileTools.cs:26-31`; `src/Island.App/Shell/FileOrbitWindow.xaml.cs:174-176`; `docs/file-actions.md:29-31`). Não há download/instalação guiada pelo app nem alternativa embutida.
- **Por que é básico:** "converter arquivos" é a função que o usuário vem buscar; sem o binário, metade das ações simplesmente não funciona na instalação padrão.
- **Impacto:** recurso parece quebrado; usuário baixa outro conversor.
- **Mínimo viável:** detector com botão "instalar/instruções" (link e passo a passo) e, no mínimo, ações que não dependem de terceiros sempre disponíveis.

### C51 — Sem OCR (PDF/imagem para texto)
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** extrair texto de um print/PDF digitalizado para a nota ou para buscar.
- **Hoje:** o catálogo tem 16 ações e nenhuma de OCR (`src/Island.Core/Files/FileActionCatalog.cs:3-7,27-40`); busca por `OCR` em `src/Island.Core/Files` não retorna nada.
- **Por que é básico:** print de reunião/nota fiscal é caso diário de produtividade; copiar o texto à mão é o contorno atual.
- **Impacto:** usuário usa ferramenta online (privacidade) ou digita tudo.
- **Mínimo viável:** OCR local pt-BR de imagem/PDF para .txt, com progresso e aviso quando o idioma não estiver disponível.

### C52 — Progresso sem percentual nas ações de arquivo
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver "12 de 40 arquivos, 30%" e tempo estimado em lotes grandes.
- **Hoje:** a barra é indeterminada (`src/Island.App/Shell/FileOrbitWindow.xaml:183`) e o serviço reporta apenas mensagens (`IProgress<string>` em `src/Island.Core/Files/FileActionService.cs:67-68`); não há contador por arquivo nem ETA.
- **Por que é básico:** converter 200 fotos ou um vídeo longo sem percentual parece travado; usuário cancela por achar que quebrou.
- **Impacto:** cancelamentos desnecessários e sensação de instabilidade.
- **Mínimo viável:** reportar arquivos processados/total e percentual na barra (por arquivo quando houver um só).

### C53 — Órbita só abre arrastando arquivos; sem adicionar/histórico
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** abrir a órbita por menu/atalho, adicionar mais arquivos e repetir a última ação.
- **Hoje:** a órbita só é aberta quando algo é arrastado sobre a ilha (`src/Island.App/Shell/IslandWindow.FileDrop.cs:12-52`); o menu da ilha não tem entrada de arquivos (`src/Island.App/Shell/IslandContextMenu.xaml:75-187`) e a janela não tem "adicionar arquivos" nem histórico/repetir.
- **Por que é básico:** depois de fechar, o usuário não consegue reabrir com os mesmos arquivos nem refazer uma ação; quem não usa drag-and-drop fica sem o recurso.
- **Impacto:** operações repetitivas (mesmos arquivos, outra saída) exigem arrastar tudo de novo.
- **Mínimo viável:** entrada no menu/atalho, "adicionar arquivos" na janela e histórico de operações para repetir.

### C54 — Histórico do clipboard só em memória
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** fechar/reabrir o app (ou reiniciar o PC) e ainda ter os últimos itens copiados.
- **Hoje:** `ClipboardHistory` é declaradamente "in-memory only (never persisted)" (`src/Island.Core/Clipboard/ClipboardHistory.cs:6`); fechar o app apaga tudo. Não há opção de persistência — nem como escolha explícita com aviso de privacidade.
- **Por que é básico:** a utilidade de um gerenciador de clipboard é recuperar algo copiado há minutos/horas; perder tudo no restart derruba metade do valor.
- **Impacto:** usuário perde o que copiou ao reiniciar (e o app se atualiza sozinho); não dá para contar com ele.
- **Mínimo viável:** persistência opcional dos últimos N itens (desligada por padrão), criptografada ou em memória protegida, com "limpar ao sair".

### C55 — Sem fixar/favoritar item do clipboard
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fixar um trecho (e-mail, assinatura, snippet) e mantê-lo no topo.
- **Hoje:** `ClipboardItem` não tem estado de fixação (`src/Island.Core/Clipboard/ClipboardItem.cs:6`); a entrada só oferece copiar/excluir (`src/Island.App/Widgets/ClipboardEntry.cs:22-31,47-51`; `src/Island.App/Views/ClipboardView.xaml.cs:164-187`) e não há seção de fixados.
- **Por que é básico:** todo gerenciador de clipboard maduro permite fixar; sem isso, o item útil é empurrado para fora pelo limite de itens.
- **Impacto:** snippets repetidos são recopiados da fonte; limite de itens fica ainda mais curto.
- **Mínimo viável:** fixar por item, seção "Fixados" no topo e fixos fora da rotação do limite.

### C57 — Captura do clipboard sem controles nas configurações
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** pausar/desligar a captura, definir quantos itens reter e limpar pelo app.
- **Hoje:** `ClipboardEnabled` e `ClipboardMaxItems` existem no modelo (`src/Island.Core/Configuration/IslandSettings.cs:58-59`) e são aplicados na captura (`src/Island.App/App.xaml.cs:314`; `src/Island.Core/Clipboard/ClipboardHistory.cs:44`), mas a janela de Settings não tem seção de clipboard (seções em `src/Island.App/Shell/SettingsWindow.xaml:328-706`); sem editar o JSON, não dá para desligar a captura nem mudar o limite.
- **Por que é básico:** clipboard é dado sensível; o usuário precisa de um botão para parar a captura e de um limite visível.
- **Impacto:** privacidade só controlável por arquivo; limite de 50 itens é uma surpresa.
- **Mínimo viável:** seção Clipboard com ligar/desligar, pausar 1 h, limite de itens e "limpar histórico".

### C58 — Ferramentas só existem dentro da estante
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** abrir calculadora/contador/timer/analisador/sons em janela própria ou por atalho, usando junto de outro app.
- **Hoje:** todas são widgets da estante (`src/Island.App/Widgets/WidgetCatalog.cs:45-54`) e não há janela dedicada para elas em `src/Island.App/Shell`; a estante recolhe quando se clica fora (`src/Island.App/Shell/IslandWindow.xaml.cs:119-122,530-536`) e a ilha não recebe teclado (`src/Island.App/Views/Widgets/CalculatorWidget.xaml.cs:11-13`); atalhos globais cobrem só notas, clipboard, captura e notas do jogo (`src/Island.App/App.xaml.cs:120,327`; `src/Island.App/Shell/CaptureHotkeys.cs`; `src/Island.App/Shell/GameNotesHotkeyController.cs:38`).
- **Por que é básico:** ferramenta de produtividade é usada no meio de outro trabalho (contador de repetição, timer, calculadora de orçamento) sem tirar o foco do que se está fazendo.
- **Impacto:** cada uso exige abrir a ilha e perder o foco do app; ferramentas viram brinquedo de demo.
- **Mínimo viável:** janela flutuante (opcionalmente topmost) e atalhos para calculadora/timer/contador, reaproveitando os mesmos modelos do `ToolRegistry`.

Total: 58 itens (2 críticas, 18 altas, 33 médias e 5 baixas), todos verificados no código.

---

## Parte D — Jogo, captura, mixer e desempenho (51 itens)

Escopo: widgets para jogo (Captura, Desempenho, Mixer e Notas do Jogo), backend em `src/Island.Core`/`src/Island.Windows` e UI em `src/Island.App`, comparados ao que ferramentas maduras de captura, overlay, áudio e monitoramento oferecem. Cada ausência foi lida no código e confirmada por grep antes de ser afirmada; limitações do README (`README.md:82-113`) foram conferidas na implementação. Total: 52 itens — 16 de captura, 12 de desempenho, 12 de mixer e 12 de notas do jogo; itens transversais (i18n, acessibilidade global, instalador, backup geral) ficaram fora.

### D01 — Captura só do monitor do app em primeiro plano, sem região ou janela

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** escolher uma região (killfeed, dano, mapa) ou a janela do jogo antes do print/vídeo.
- **Hoje:** o único recorte é `ScreenGrabber.TryGetForegroundMonitor` (`src/Island.Windows/Capture/ScreenGrabber.cs:17-35`) e o serviço grava sempre o monitor inteiro (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:72-79`); não existe overlay de seleção nem HWND alvo.
- **Por que é básico:** Snipping Tool, Xbox Game Bar, ShareX e OBS sempre perguntam o que capturar; jogador quer recorte e privacidade.
- **Impacto:** prints/vídeos com 80–90% de tela irrelevante, incluindo chat e notificações; impossível documentar um detalhe do jogo.
- **Mínimo viável:** overlay de seleção por arrasto e opção "janela ativa", reusando `ScreenGrabber.Grab` com um `ScreenRect` arbitrário.

### D02 — Sem cursor no print nem na gravação

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver o ponteiro no vídeo (ou poder ligar/desligar isso).
- **Hoje:** `DibFrame.CopyFromScreen` usa `StretchBlt` com `SRCCOPY|CAPTUREBLT` e não compõe o cursor (`src/Island.Windows/Capture/DibFrame.cs:59-68`); não há uso de `GetCursorInfo`/`DrawIcon` no código; `README.md:89` lista "sem cursor".
- **Por que é básico:** tutorial/gameplay sem cursor não mostra onde o jogador clica; OBS e Game Bar têm cursor por padrão.
- **Impacto:** gravação instrutiva inútil para demonstrar menus/builds; passo a passo sem o ponteiro.
- **Mínimo viável:** opção de desenhar o cursor com `GetCursorInfo`+`DrawIconEx` no frame, ou migrar para Windows.Graphics.Capture (inclui cursor).

### D03 — Gravação sem áudio do sistema e sem microfone

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** vídeo com som do jogo e/ou microfone, com escolha de fonte e mix.
- **Hoje:** `Mp4VideoWriter` cria um único stream H.264 (`src/Island.Windows/Capture/Mp4/Mp4VideoWriter.cs:36-55`) e não há captura WASAPI loopback/mic no projeto (grep sem `AudioClient`/`Loopback` fora do áudio de foco do Pomodoro).
- **Por que é básico:** clipe de jogo sem áudio perde o momento; qualquer gravador maduro captura sistema + mic.
- **Impacto:** vídeos precisam de pós-produção em outro app; memos de voz e reações do time ficam de fora.
- **Mínimo viável:** `WasapiLoopbackCapture` + stream de áudio (AAC) no Sink Writer e opção "sistema / microfone / ambos".

### D04 — Gravação em 30 fps, 1080p e bitrate fixos, sem configuração

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher fps (30/60), resolução (720p/1080p/nativa) e qualidade/bitrate, com a escolha salva.
- **Hoje:** `Fps = 30` (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:15`), teto 1920x1080 e bitrate calculado entre 2 e 12 Mbps (`src/Island.Core/Capture/CaptureSize.cs:6-7,21-22`); não há store nem UI de captura (grep de "Capture" em `src/Island.Core/Configuration` não retorna nada).
- **Por que é básico:** 60 fps e bitrate controlado são requisito mínimo de clip de gameplay; 1080p decepciona em monitor QHD/4K.
- **Impacto:** vídeo borrado em movimento e sem opção de arquivo leve; aparência de ferramenta "de brinquedo".
- **Mínimo viável:** três preferências (fps, altura máxima, Mbps) em JSON de captura, repassadas a `CaptureSize.Fit`/`BitrateFor`.

### D05 — Pasta e nome dos arquivos fixos; sem "abrir pasta"

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher pasta de destino, modelo de nome e abrir a pasta após salvar.
- **Hoje:** pasta fixa `%UserProfile%\Pictures\DynamicIsland\Capturas` (`src/Island.Windows/Capture/CaptureLibrary.cs:9-10`, injetada em `src/Island.App/Composition/ServiceRegistration.cs:177-180`); nomes fixos `Captura-*`/`Gravacao-*` (`src/Island.Core/Capture/CaptureNaming.cs:10-13`); o aviso "Pasta Capturas" não é clicável (`src/Island.Core/Capture/CaptureController.cs:108,170`).
- **Por que é básico:** quem grava clips tem pasta de acervo (SSD de jogos, OneDrive); a notificação deveria levar ao arquivo.
- **Impacto:** arquivos escondidos em Pictures; usuário não sabe onde estão nem como mudar; não há "Abrir pasta" no tray/settings.
- **Mínimo viável:** caminho configurável com "Restaurar padrão", prefixo opcional e ação "Abrir pasta de capturas".

### D06 — Sem galeria/histórico: só o último print é acessível e nenhum vídeo

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver as últimas capturas/vídeos, abrir, copiar ou apagar.
- **Hoje:** `CaptureController` guarda apenas `_lastScreenshot` e mantém o caminho do vídeo privado (`src/Island.Core/Capture/CaptureController.cs:26-27,80-87`); `CaptureLibrary.LatestScreenshot` lista só o PNG mais novo (`CaptureLibrary.cs:13-25`) e o widget mostra uma miniatura só (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:129`).
- **Por que é básico:** qualquer ferramenta tem histórico/bandeja de capturas; "gravei e quero rever/enviar" é uso diário.
- **Impacto:** o print seguinte substitui a referência; o vídeo fica "perdido" no Explorer e nem dá para conferir se saiu bom.
- **Mínimo viável:** grade das últimas N capturas (prints e vídeos) com abrir/copiar/apagar e botão "Abrir no Explorer".

### D07 — Copiar print é manual e só do último arquivo

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** print direto na área de transferência por atalho, ou opção "copiar ao salvar".
- **Hoje:** o print sempre grava PNG primeiro (`src/Island.Core/Capture/CaptureController.cs:90-110`); copiar exige abrir a estante e clicar "Copiar" (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:64-67,202-218`); o atalho global só salva (`README.md:86`).
- **Por que é básico:** colar print no Discord/chat é o destino mais comum; Snipping Tool e Game Bar fazem isso por padrão.
- **Impacto:** dois cliques e abrir a ilha para uma tarefa que deveria ser um atalho; se o arquivo sumir, o botão não faz nada.
- **Mínimo viável:** preferência/atalho "print para clipboard (salvar opcional)" e feedback de cópia no aviso.

### D08 — Gravação não pausa nem retoma

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** pausar (intervalo, loading) e continuar no mesmo arquivo.
- **Hoje:** a máquina de estados só tem Idle→Starting→Recording→Stopping (`src/Island.Core/Capture/RecordingStateMachine.cs:3-9`) e a interface só expõe iniciar/parar (`src/Island.Core/Abstractions/IScreenCaptureService.cs:15-22`).
- **Por que é básico:** pausar e retomar é controle padrão de gravador; evita cortar o vídeo em vários arquivos.
- **Impacto:** clips com intervalo precisam ser gravados inteiros e cortados depois, ou ficam em vários MP4s.
- **Mínimo viável:** fase `Paused` no estado e no loop do `ScreenRecorder` (não escrever frames, manter o timeline) com botão/atalho.

### D09 — Indicador de gravação só existe com a estante aberta no widget

- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber que está gravando (e conseguir parar) com a ilha recolhida ou em outra tela.
- **Hoje:** o "Gravando" com ponto vermelho e cronômetro vive só dentro do `CaptureWidget` (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:99-131`); o tray tem apenas Settings/Pause/Quit (`src/Island.App/Shell/TrayIconService.cs:38-51`); a cápsula não consome `RecordingPhase` (grep só acha uso no widget).
- **Por que é básico:** gravação "invisível" gera horas de vídeo acidental e esquecimento de parar; OBS/Game Bar mostram indicador persistente.
- **Impacto:** usuário esquece gravando; disco enche; risco de privacidade (capturou reunião sem querer).
- **Mínimo viável:** estado no tray ou item "Parar gravação" no menu + ponto vermelho na cápsula recolhida.

### D10 — Atalhos fixos (Ctrl+Alt+P/R) sem configuração nem alternativa

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** trocar/desativar atalhos, principalmente quando outro app (Discord, OBS, Steam) usa a combinação.
- **Hoje:** teclas constantes (`src/Island.App/Shell/CaptureHotkeys.cs:13-14`) e modificadores fixos `ModControl|ModAlt` (`src/Island.App/Composition/ServiceRegistration.cs:181-184`); o conflito só gera texto no widget (`CaptureWidget.xaml.cs:192-200`), sem remapear.
- **Por que é básico:** atalho global conflitante é comum; ferramenta madura permite gravar a combinação.
- **Impacto:** usuário com Ctrl+Alt+P/R ocupado fica sem captura global para sempre (só clicando na ilha).
- **Mínimo viável:** editor de atalhos em Settings com validação de conflito e combinação alternativa.

### D11 — Sem replay buffer (salvar os últimos N segundos)

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** "salvar os últimos 30 s" depois que algo aconteceu no jogo.
- **Hoje:** `StartRecordingAsync` abre o MP4 e grava desde o início (`src/Island.Windows/Capture/ScreenRecorder.cs:44-58`); não há buffer circular de frames nem gravação contínua descartável (grep por "replay" só acha um comentário de animação do widget de timer).
- **Por que é básico:** é o recurso-assinatura de game capture (Instant Replay do ShadowPlay/Game Bar); sem ele, o jogador precisa gravar a partida toda.
- **Impacto:** perde o clipe do momento; gravações longas e pesadas; dependência de software de terceiros.
- **Mínimo viável:** buffer circular de N segundos em arquivo temporário e atalho "Salvar últimos N s" com tamanho configurável.

### D12 — Tela cheia exclusiva (DirectX) sai preta

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** capturar o jogo rodando em tela cheia exclusiva.
- **Hoje:** a captura é GDI do desktop (`src/Island.Windows/Capture/ScreenGrabber.cs:10-13`) e o serviço assume a limitação no comentário de classe (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:7-12`); não há Windows.Graphics.Capture/D3D no projeto; `README.md:89` confirma.
- **Por que é básico:** a maioria dos jogos ainda oferece "tela cheia" exclusiva; preto é falha total da função para esses títulos.
- **Impacto:** print/vídeo inútil justamente no jogo; usuário precisa mudar o jogo para borderless.
- **Mínimo viável:** migrar o backend para Windows.Graphics.Capture (WGC), que captura exclusive fullscreen via D3D, mantendo GDI como fallback.

### D13 — A própria ilha aparece no print e no vídeo

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** captura limpa, sem o overlay do próprio app.
- **Hoje:** `StretchBlt` com `CAPTUREBLT` inclui janelas em camadas (`src/Island.Windows/Capture/DibFrame.cs:58-66`) e não há `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` no projeto (grep sem resultados); `README.md:89` lista o efeito.
- **Por que é básico:** a ilha fica no topo da tela; toda captura carrega a pílula no meio do frame.
- **Impacto:** prints/vídeos com o overlay alheio; refazer captura ou recortar manualmente.
- **Mínimo viável:** esconder a ilha durante a captura ou marcá-la com `WDA_EXCLUDEFROMCAPTURE` (efeito em WGC).

### D14 — Sem limite de duração/tamanho nem aviso de disco cheio

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** limite de gravação (ex.: 10 min) ou aviso/parada quando o disco enche.
- **Hoje:** o loop do `ScreenRecorder` não checa tempo nem espaço (`src/Island.Windows/Capture/ScreenRecorder.cs:69-83`); não há `DriveInfo`/checagem de espaço livre no projeto (grep) e falha de escrita só vira "Gravação interrompida" (`src/Island.Core/Capture/CaptureController.cs:192-204`).
- **Por que é básico:** gravação esquecida enche o disco; ferramenta madura avisa ou limita.
- **Impacto:** MP4 corrompido/curto e disco cheio derrubando outros apps, sem causa clara.
- **Mínimo viável:** limite configurável de duração e checagem periódica de espaço livre com aviso e parada graciosa (finaliza o MP4).

### D15 — Erro de captura sem diagnóstico acionável

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber por que falhou (permissão, pasta, monitor, encoder) e o que fazer.
- **Hoje:** falhas viram `Notice("Print não salvo", "Veja os logs")`/`"Gravação não iniciou"` (`src/Island.Core/Capture/CaptureController.cs:99-109,146-148,203`); `Notice` não tem ação/clique (`src/Island.Core/Models/Notice.cs:7`) e não há item de menu para abrir os logs, guardados em `%LocalAppData%\DynamicIsland\logs` (`src/Island.App/App.xaml.cs:91-94`).
- **Por que é básico:** quando a função principal falha, o usuário precisa de causa e saída, não de "veja os logs".
- **Impacto:** suporte por tentativa e erro; usuário conclui que o widget "não funciona".
- **Mínimo viável:** mensagem com causa provável, ação "Abrir pasta de logs" e, no print preto, orientar "use borderless".

### D16 — Sem atraso/countdown para print e gravação

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** "print em 3 s" para abrir um menu/tooltip, ou contagem 3-2-1 antes de gravar.
- **Hoje:** `TakeScreenshotAsync` e `StartRecordingAsync` executam na hora (`src/Island.Core/Capture/CaptureController.cs:90-150`); não há timer/contagem no fluxo (grep por "countdown" só acha o widget de timer).
- **Por que é básico:** atraso é padrão em captura de tela; evita precisar da mão em dois lugares ao mesmo tempo.
- **Impacto:** impossível capturar menus que somem ao clicar; início de gravação com a barra de controles visível.
- **Mínimo viável:** atraso configurável (0/3/5/10 s) com contagem discreta no widget/cápsula antes de executar.

### D17 — Sem FPS/frametime nem 1% low

- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** FPS ao vivo (e frametime/1% low) no jogo, como MSI Afterburner, Game Bar ou Steam.
- **Hoje:** `PerformanceSnapshot` só tem CPU, GPU, temperaturas, RAM e VRAM (`src/Island.Core/Performance/PerformanceSnapshot.cs:7-15`) e o sampler só lê essas fontes (`src/Island.Windows/Performance/WindowsPerformanceSampler.cs:20-38`); grep por `Fps/PresentMon/ETW/FrameRate` só acha o encoder de captura.
- **Por que é básico:** FPS é a métrica nº 1 de quem joga; um widget "de desempenho" sem FPS mede o PC, não o jogo.
- **Impacto:** usuário não vê stutter nem quedas; mantém overlay de terceiros (RTSS/Afterburner).
- **Mínimo viável:** FPS por ETW (PresentMon) ou contador de apresentação por processo, com média e 1% low no sparkline.

### D18 — Sem HUD/overlay de desempenho sobre o jogo

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** métricas pequenas no canto do jogo, sem alt-tab nem segunda tela.
- **Hoje:** o widget é um cartão da estante (`src/Island.App/Views/Widgets/PerformanceWidget.xaml:1-8`) e a ilha se esconde em fullscreen por padrão (`src/Island.Core/Configuration/IslandSettings.cs:21`; `src/Island.Core/Application/IslandStateReducer.cs:29`); não existe HUD de métricas sobre o jogo — o cartão de desempenho só existe na estante.
- **Por que é básico:** o caso de uso é durante a partida; abrir a estante interrompe a experiência e a ilha fica escondida.
- **Impacto:** desempenho só é visto em outra tela (como o README assume) ou desligando a proteção de fullscreen; nada compacto sobre o jogo.
- **Mínimo viável:** HUD opcional topmost pequeno preso a um canto, com métricas escolhidas e transparência.

### D19 — Temperatura cobre pouco hardware: GPU só NVIDIA e CPU só zonas ACPI "CPU"

- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** temperatura de CPU e GPU em qualquer placa (AMD, Intel, NVIDIA).
- **Hoje:** GPU térmica só via NVML, primeira GPU NVIDIA (`src/Island.Windows/Performance/NvmlGpuReader.cs:20-23,44-47`); os contadores de GPU dão carga mas não temperatura (`src/Island.Windows/Performance/CounterGpuReader.cs:36-38`); CPU só de zonas térmicas com "CPU" no nome (`src/Island.Windows/Performance/ThermalZoneReader.cs:43`); `README.md:95` admite.
- **Por que é básico:** o alerta de temperatura é a função mais elaborada do widget e não funciona em boa parte do hardware (GPU AMD/Intel, muitos notebooks).
- **Impacto:** AMD/Intel e muitos notebooks mostram "—" e nunca alertam; o recurso central vira decorativo.
- **Mínimo viável:** temperatura de CPU por contadores/WMI adicionais e GPU AMD/Intel por ADL/Level Zero, com aviso claro de indisponível.

### D20 — Sem CPU por núcleo nem frequência/clock

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver se um núcleo está saturado e a frequência atual (throttling).
- **Hoje:** CPU vem de `GetSystemTimes`, só o total da máquina (`src/Island.Windows/Performance/CpuUsageReader.cs:5-7,18-25`); não há campos de núcleos nem MHz no snapshot (`PerformanceSnapshot.cs:7-15`) nem leitor de clock (grep por `MHz/Frequency` vazio).
- **Por que é básico:** stutter por um núcleo e throttle são diagnosticados por núcleo/frequência; o Gerenciador de Tarefas já mostra.
- **Impacto:** usuário não entende gargalo de CPU nem queda de clock; o widget parece raso.
- **Mínimo viável:** uso por núcleo via PDH `Processor Information`/`NtQuerySystemInformation` e clock via `CallNtPowerInformation`.

### D21 — Sem rede (download/upload)

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver throughput/latência enquanto joga ou baixa atualização.
- **Hoje:** o snapshot não tem campos de rede (`src/Island.Core/Performance/PerformanceSnapshot.cs:7-15`) e o sampler não lê `NetworkInterface`/PDH (grep vazio); `README.md:94` lista só CPU/RAM/GPU e temperaturas.
- **Por que é básico:** lag de rede é tão sentido quanto FPS; overlays de jogo mostram rede.
- **Impacto:** jogador não sabe se o problema é ping ou FPS; precisa de outro app.
- **Mínimo viável:** bytes/s por adaptador ativo (`NetworkInterface.GetIPv4Statistics`) a 1 Hz e um painel/linha opcional no widget.

### D22 — Sem disco (uso/espaço/IO)

- **Severidade:** Baixa
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver se o disco está a 100% ou sem espaço no SSD do jogo.
- **Hoje:** não há campos nem leitor de disco (`PerformanceSnapshot.cs:7-15`; `WindowsPerformanceSampler.cs:14-17` só CPU/NVML/contadores/térmica); grep sem leitor de disco/`DriveInfo`.
- **Por que é básico:** stutter por disco em 100% é comum em PC gamer; espaço livre importa para gravações.
- **Impacto:** diagnóstico incompleto; usuário abre o Gerenciador de Tarefas.
- **Mínimo viável:** PDH `PhysicalDisk`/`LogicalDisk` (uso/fila) e espaço livre do volume das capturas, com painel opcional.

### D23 — Sem ventoinhas nem consumo de energia

- **Severidade:** Baixa
- **Tipo:** Lacuna de dados
- **Usuário espera:** RPM de fans e watts da CPU/GPU para saber se o PC está no limite.
- **Hoje:** nenhum leitor de fan/watt no projeto (`WindowsPerformanceSampler.cs:14-17`; grep `Fan/Power/Watt` só acha eventos de energia do Windows).
- **Por que é básico:** junto com temperatura, é o trio de monitoramento de hardware de ferramentas maduras (HWiNFO/Afterburner).
- **Impacto:** usuário não vê fan parada ou consumo anormal; o widget fica atrás dos concorrentes.
- **Mínimo viável:** NVML (`NvmlDeviceGetFanSpeed`/`GetPowerUsage`) e, para CPU, PDH/WMI de consumo quando disponível; exibir só se houver dado.

### D24 — Histórico não persiste e não há exportação/CSV

- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** rever o gráfico da sessão, comparar antes/depois e exportar os dados.
- **Hoje:** o histórico é um anel de 60 amostras em memória (`src/Island.Core/Performance/PerformanceHistory.cs:6-7`), zerado ao fechar; o único JSON de desempenho salva limites de alerta (`src/Island.Windows/Performance/JsonPerformanceSettingsStore.cs:25-39`); não há export de métricas (o CSV do projeto existe só na suíte de arquivos).
- **Por que é básico:** para investigar queda de FPS/temperatura, precisa do histórico da partida, não do último minuto visível.
- **Impacto:** nada para mostrar em suporte/benchmark; comparação impossível; reinício perde tudo.
- **Mínimo viável:** gravar amostras em CSV/JSONL rotativo enquanto o widget estiver visível, com "Exportar última sessão" e abrir a pasta.

### D25 — Alerta só de temperatura; sem limite de uso de CPU/GPU/RAM

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** alertar "GPU 99%", "RAM 95%" ou "FPS abaixo de X", com limites próprios.
- **Hoje:** `PerformanceAlertSettings` só tem dois limites em °C (`src/Island.Core/Performance/PerformanceAlertSettings.cs:4,11`) e a política avalia apenas temperaturas (`src/Island.Core/Performance/TemperatureAlertPolicy.cs:42-45`); não há alerta de uso/FPS.
- **Por que é básico:** uso travado em 100% e RAM estourando são sinais de problema; só temperatura cobre parte.
- **Impacto:** usuário só descobre gargalo abrindo o widget; sem aviso enquanto joga.
- **Mínimo viável:** limites configuráveis de CPU%, GPU%, RAM% e FPS mínimo, reusando a histerese/cooldown existentes.

### D26 — Intervalo de amostragem fixo (1 s visível, 3 s com alerta)

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** amostrar mais rápido para picos (500 ms) ou menos para economizar (5 s).
- **Hoje:** constantes `VisibleInterval = 1 s` e `AlertInterval = 3 s` (`src/Island.Core/Performance/PerformanceSamplingPolicy.cs:6-11`); nenhuma preferência no `performance.json` (só alertas).
- **Por que é básico:** picos de GPU/RAM somem entre amostras de 1 s; o usuário não controla o custo de leitura.
- **Impacto:** gráfico impreciso em stutter curto; impossível reduzir o custo de CPU (~840 contadores de GPU a cada 3 s, `README.md:95`).
- **Mínimo viável:** intervalo configurável (0,5–5 s) com aviso de custo e opção de desligar os contadores de GPU.

### D27 — Métricas e tamanho fixos: não escolhe sensores nem painéis

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher o que ver (ex.: só FPS+GPU, ou CPU+RAM) e ter um modo compacto.
- **Hoje:** o widget sempre mostra dois painéis CPU/GPU fixos (`src/Island.App/Views/Widgets/PerformanceWidget.xaml.cs:93-101`) em 300x152 fixos (`PerformanceWidget.xaml:6-7`); não há configuração de layout/sensores.
- **Por que é básico:** cada jogo/PC precisa de métricas diferentes; cartão fixo ocupa espaço sem mostrar RAM/rede.
- **Impacto:** informação irrelevante polui; não dá para deixar um HUD enxuto.
- **Mínimo viável:** seleção de 2–4 métricas e modo compacto de uma linha, persistidos.

### D28 — "—" sem explicação para métricas ausentes

- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber por que falta o dado e como resolver (sensor ausente, driver sem suporte, precisa de admin).
- **Hoje:** só a temperatura tem `ToolTip` explicativo (`src/Island.App/Views/Widgets/PerformanceMetricPanel.cs:106-119`); CPU/GPU/RAM sem valor mostram "—" seco; `README.md:95` reconhece.
- **Por que é básico:** "—" persistente parece bug; ferramentas maduras dizem "sensor indisponível".
- **Impacto:** usuário acha que o app quebrou e desinstala; suporte sem pista.
- **Mínimo viável:** tooltip/estado por métrica ("sem sensor nesta máquina", "driver NVIDIA ausente") com link para a limitação.

### D29 — Sem seletor de dispositivo de saída (nem nome do atual)

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** alternar entre fone, caixa e HDMI, ver qual está ativo e ter os apps do dispositivo escolhido.
- **Hoje:** o serviço fixa `GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` (`src/Island.Windows/Audio/WindowsAudioMixerService.cs:139`) e a interface só tem volume/mute por sessão (`src/Island.Core/Abstractions/IAudioMixerService.cs:20-24`); não lista dispositivos nem mostra o nome do atual; `README.md:100` admite.
- **Por que é básico:** mixer de ferramenta madura (EarTrumpet/Volume2) troca o dispositivo; jogador alterna headset/caixa toda hora.
- **Impacto:** usuário precisa ir às configurações do Windows; apps aparecem "no dispositivo errado" sem explicação.
- **Mínimo viável:** enumerar endpoints de renderização (`MMDeviceEnumerator.EnumerateAudioEndPoints`), seletor no cabeçalho e sessões do endpoint escolhido.

### D30 — Volume/mudo por app não é lembrado

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** "Chrome sempre em 30%, Discord em 70%" restaurado quando o app abre.
- **Hoje:** `SetVolume`/`SetMuted` só chamam a sessão viva (`WindowsAudioMixerService.cs:67-84`) e não existe store nem preferência de mixer (grep sem store de áudio); ao reiniciar o app ou trocar de dispositivo as sessões são recriadas (`:132-151,190-230`).
- **Por que é básico:** ajustar volume de cada app todo dia é o uso típico; persistência é o diferencial do EarTrumpet.
- **Impacto:** retrabalho diário; o valor do mixer cai para "volume do Windows".
- **Mínimo viável:** JSON de mixer (processo/executável → volume/mudo) aplicado na criação de cada sessão.

### D31 — Sem controle de microfone/entrada

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** volume e mudo do microfone e dos dispositivos de entrada, além dos apps.
- **Hoje:** o mixer só usa `DataFlow.Render` (`src/Island.Windows/Audio/WindowsAudioMixerService.cs:139`) e `IAudioMixerService` não tem APIs de entrada; nenhum `DataFlow.Capture` no projeto (grep).
- **Por que é básico:** quem joga em call precisa abaixar/mutar o mic; mixers maduros mostram "Microfone" e "Sons do sistema".
- **Impacto:** usuário abre o painel do Discord/Windows; não há mute global de mic.
- **Mínimo viável:** endpoint de captura padrão com slider/mudo e atalho "mudo o mic" (ver D39).

### D32 — Sem volume master nem "mutar todos" no mixer

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** um slider/ícone no topo do mixer para o volume geral e um botão para silenciar tudo de uma vez.
- **Hoje:** o mixer só oferece controles por sessão (`IAudioMixerService.cs:20-24`; `src/Island.App/Views/Widgets/MixerWidget.xaml:50-63` sem controle mestre); o volume master existe apenas no fluxo de volume separado (`WindowsVolumeService`), fora do mixer.
- **Por que é básico:** controles globais são a primeira linha do misturador do Windows; mutar todos resolve "alguém chamou no Discord".
- **Impacto:** usuário precisa de outro lugar para o volume geral; não há mute geral de apps.
- **Mínimo viável:** master (volume/mudo do endpoint) no cabeçalho + "Mutar todos" que guarda o estado anterior para desfazer.

### D33 — Sessões do mesmo app aparecem como linhas separadas (sem agrupamento)

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** um app = uma linha, com o volume valendo para o app inteiro (como no mixer do Windows).
- **Hoje:** cada session instance id vira uma linha (`WindowsAudioMixerService.cs:190-230`; `src/Island.App/Views/Widgets/MixerWidget.cs:119-139`), sem agrupar por processo/executável; `OnGroupingParamChanged` é ignorado (`src/Island.Windows/Audio/MixerSessionHandle.cs:245-248`).
- **Por que é básico:** navegadores e jogos com vários processos de áudio geram duplicatas; ajustar volume duas vezes é confuso.
- **Impacto:** lista poluída e volume "que não obedece" (outra linha do mesmo app continua alta).
- **Mínimo viável:** agrupar por caminho do executável, espelhando volume/mudo em todas as sessões do grupo.

### D34 — Primeiro plano na ordem só atualiza quando algo muda

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** o app em foco ir para o topo da lista imediatamente ao alt-tab.
- **Hoje:** a ordem usa `ForegroundProcess.CurrentId()` só dentro de `Refresh` (`src/Island.App/Views/Widgets/MixerWidget.cs:104`), disparado por eventos do mixer; o serviço de áudio não tem hook de foco (só consulta pontual a `GetForegroundWindow` em `src/Island.Windows/Audio/ForegroundProcess.cs:9-16`); `README.md:100` admite.
- **Por que é básico:** a ordenação é o principal atalho para achar o app do jogo; desatualizada, anula o recurso.
- **Impacto:** o app recém-focado não sobe; usuário caça a linha na lista paginada.
- **Mínimo viável:** `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` no serviço (já usado em GameNotes) reordenando sem reabrir.

### D35 — Sem fixar nem ocultar apps na lista

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fixar o app do jogo/chat no topo e esconder apps que nunca ajusta.
- **Hoje:** a ordem é só regra fixa (`src/Island.Core/Audio/AudioSessionOrdering.cs:12-23`) e o widget renderiza todas as sessões (`MixerWidget.cs:126-139`); não existe estado de fixar/ocultar por app.
- **Por que é básico:** mixers maduros deixam fixar (pin) e filtrar; com paginação de 3 linhas, a ordem importa.
- **Impacto:** app importante pode ficar na página 2; lista cheia de apps irrelevantes.
- **Mínimo viável:** pin/filtro por executável em JSON de mixer, aplicado na ordenação e na paginação.

### D36 — Não abre/foca o app a partir da linha

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** clique/duplo clique ou menu para trazer a janela do app que está tocando.
- **Hoje:** a linha tem só slider, mudo e medidor (`src/Island.App/Views/Widgets/MixerRow.cs:53-156`); `ExecutablePath` é usado apenas para o ícone (`MixerRow.cs:167-174`).
- **Por que é básico:** o mixer do Windows tem menu "Abrir volume do app"; aqui a linha não leva a lugar nenhum.
- **Impacto:** usuário precisa achar a janela no Alt+Tab; o recurso fica só de "controle".
- **Mínimo viável:** menu de contexto "Abrir app" (o caminho do executável já está no modelo).

### D37 — Sem boost acima de 100% nem equalizador

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** passar de 100% em áudio baixo e ajustar graves/agudos (ou pelo menos um "reforço de volume").
- **Hoje:** o volume é limitado a 0–100 (`src/Island.Core/Audio/AudioVolumeMath.cs:6-11`) e o scalar do Core Audio vai até 1.0 (`src/Island.Windows/Audio/MixerSessionHandle.cs:142-157`); não há processamento de áudio por app (grep sem EQ/DSP).
- **Por que é básico:** jogos com mix baixo se beneficiam de boost; é diferencial comum (Volume2/EarTrumpet com add-ons).
- **Impacto:** usuário não resolve áudio baixo dentro do app; recorre a outro software.
- **Mínimo viável:** boost 100–200% via processamento próprio/APO; equalizador pode ficar para depois.

### D38 — Mute rápido hardcoded só para o Discord

- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** um botão de pânico para o app de voz que usa (Discord, Teams, Steam).
- **Hoje:** `DiscordProcess = "Discord"` fixo (`src/Island.App/Views/Widgets/MixerWidget.cs:21`) e só esse botão aparece (`:212-226`); outros apps dependem de achar a linha e clicar no mudo.
- **Por que é básico:** mutar a call é a ação mais comum no meio da partida; a solução deveria ser configurável, não um nome de processo no código.
- **Impacto:** usuário de Teams/Steam Chat não tem o atalho; usuário de Discord tem.
- **Mínimo viável:** escolher o app do botão em Settings (ou um por linha fixada), mantendo os dois rótulos atuais.

### D39 — Sem atalho global de mute geral/mic

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** uma tecla para mutar tudo (ou o mic) sem abrir a ilha.
- **Hoje:** os únicos atalhos globais são Ctrl+Alt+V (clipboard), N (nota rápida), G (nota do jogo) e P/R (captura) (`src/Island.App/App.xaml.cs:120,327`; `src/Island.App/Shell/GameNotesHotkeyController.cs:15`; `src/Island.App/Shell/CaptureHotkeys.cs:13-14`); o mixer não registra atalho.
- **Por que é básico:** "mutar tudo" no meio da partida é o caso de uso clássico de hotkey de áudio.
- **Impacto:** usuário abre a estante (ou o mixer do Windows) no meio do jogo; perde tempo.
- **Mínimo viável:** hotkey configurável de mute geral (endpoint) e de mic, com aviso na cápsula.

### D40 — Slider de volume só responde ao mouse

- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ajustar volume pelo teclado (setas/Home/End) e por leitor de tela, como um slider padrão.
- **Hoje:** `VolumeSlider` é `FrameworkElement` desenhado, trata só eventos de mouse e não tem `AutomationPeer`/teclas (`src/Island.App/Mixer/VolumeSlider.cs:16,32-35,68-113`); só o botão de mudo tem nome de automação (`MixerRow.cs:149`).
- **Por que é básico:** é o único controle contínuo do widget; sem teclado, quem não usa mouse (ou usa leitor de tela) não ajusta volume.
- **Impacto:** usuário de teclado/leitor de tela fica sem o controle principal; quebra o padrão WPF de acessibilidade.
- **Mínimo viável:** implementar `IRangeValueProvider` e `OnKeyDown` (setas, PageUp/Down), preservando o desenho fino.

### D41 — Não há lista completa/editor das notas do jogo

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** abrir uma janela com todas as notas do jogo, marcar, editar e apagar.
- **Hoje:** o widget mostra no máximo 3 linhas e a quarta em diante vira um `TextBlock` "+N notas" não clicável (`src/Island.App/Views/Widgets/GameNotesWidget.xaml.cs:26,194-209`); a única janela (`GameNotesWindow`) só adiciona (`src/Island.App/Shell/GameNotesWindow.xaml.cs:86-113`) e o gerenciador a mantém apenas para captura (`src/Island.App/Shell/GameNotesWindowManager.cs:5-9`).
- **Por que é básico:** com 4+ notas não há como ler ou agir na nota 5; é o mínimo de um widget de notas.
- **Impacto:** notas antigas ficam inacessíveis na prática; usuário volta para bloco de notas/planilha.
- **Mínimo viável:** janela "Notas do jogo" com lista completa (rolagem), checkbox, editar, apagar e filtro por jogo.

### D42 — Nota não pode ser editada depois de criada

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** corrigir texto (typo, preço, build) e complementar detalhes.
- **Hoje:** `GameNotesService` expõe só adicionar, alternar concluída, fixar e apagar (`src/Island.Core/GameNotes/GameNotesService.cs:132-152`); `GameNotesBook` idem (`src/Island.Core/GameNotes/GameNotesBook.cs:77-95`); a janela captura apenas texto novo.
- **Por que é básico:** nota de jogo envelhece (patch muda build/nomes); sem editar, a saída é apagar e recriar.
- **Impacto:** notas erradas/obsoletas permanecem; apagar e recriar perde o histórico e o pin.
- **Mínimo viável:** `UpdateNoteTextAsync` no serviço + edição inline na lista (Enter salva, Esc cancela).

### D44 — Detecção não cobre jogo em janela nem UWP/Game Pass

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** notas funcionarem no jogo em janela e nos jogos da Store/Xbox.
- **Hoje:** só fullscreen/borderless vale (`src/Island.Core/GameNotes/GameDetectionRules.cs:49`) e a lista de ignorados contém `applicationframehost` (`GameDetectionRules.cs:23-33`), que hospeda apps UWP; jogo em janela precisa estar em `GameProcesses` (sem UI, ver B05).
- **Por que é básico:** muitos jogadores usam janela/borderless; Game Pass é comum no Windows.
- **Impacto:** notas "não aparecem" nesses jogos; usuário conclui que a detecção é aleatória.
- **Mínimo viável:** detectar UWP pelo processo real do jogo (AppUserModelId/ApplicationFrameHost) e oferecer "este jogo" quando a janela em primeiro plano não for ignorada.

### D45 — Jogo fixado não sobrevive ao reinício

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** o jogo fixado à mão continuar fixado depois de fechar/abrir o app.
- **Hoje:** o pin vive só em memória, em `GameNotesSelection.PinnedKey` (`src/Island.Core/GameNotes/GameNotesSelection.cs:9-16`), e o arquivo de notas não tem campo de pin de jogo (`src/Island.Windows/GameNotes/JsonGameNotesStore.cs:180-188`); ao reiniciar, volta a valer o último detectado.
- **Por que é básico:** fixar é a correção para detecção errada; se some, o usuário refaz todo dia.
- **Impacto:** correção manual repetida; notas do jogo errado aparecem após reiniciar.
- **Mínimo viável:** persistir `pinnedKey`/`browsedKey` em `gamenotes.json` (ou settings) e restaurar ao carregar.

### D46 — Sem data/hora nem contexto de sessão

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver quando anotou (e "da última sessão"/"da run anterior") para saber o que já é obsoleto.
- **Hoje:** `GameNote.CreatedAt` é gravado (`src/Island.Core/GameNotes/GameNote.cs:4-9`), mas a linha do widget mostra só o texto (`GameNotesWidget.xaml.cs:258-268`); não há sessão/partida no modelo (`GameNotesBook.cs:4-10`).
- **Por que é básico:** notas de jogo são por run/patch; sem data, não se sabe o que foi resolvido.
- **Impacto:** notas velhas poluem; impossível saber "o que anotei ontem".
- **Mínimo viável:** data relativa na linha (tooltip com data completa) e agrupamento opcional por dia/sessão.

### D47 — Sem busca/filtro nas notas

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** buscar "chave", "boss", "build" dentro das notas do jogo.
- **Hoje:** `GameNotesService` só entrega `NotesFor(key)` (`src/Island.Core/GameNotes/GameNotesService.cs:62-66`) e o widget exibe um recorte fixo (`src/Island.App/Views/Widgets/GameNotesWidget.xaml:60-65`); não há campo de busca (o widget de Notas rápidas tem busca, mas não se aplica ao jogo).
- **Por que é básico:** com 10–200 notas, achar uma é o uso principal; notas rápidas já têm busca.
- **Impacto:** usuário rola/apanha visualmente; nota "some".
- **Mínimo viável:** busca no widget/janela completa (substring, sem acento) com contagem e navegação.

### D48 — Não exporta nem compartilha as notas

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** copiar todas as notas (texto) ou exportar para arquivo/backup.
- **Hoje:** não há método de exportação no serviço (`GameNotesService.cs:132-152`) nem UI; o dado só existe no JSON interno (`JsonGameNotesStore.cs:30-36`); `GameNotesFormat` formata apenas contagem/vazio/overflow (`src/Island.Core/GameNotes/GameNotesFormat.cs:6-17`).
- **Por que é básico:** guias de jogo circulam em texto/Discord; backup é proteção contra perder a run.
- **Impacto:** compartilhar exige abrir o JSON; migração/reinstalação arriscada.
- **Mínimo viável:** "Copiar notas do jogo" (markdown/lista) e "Exportar .txt/.md" com diálogo de salvar.

### D49 — Nenhuma forma de consultar as notas dentro do jogo

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** ver um checklist/HUD enquanto joga, sem alt-tab para a ilha.
- **Hoje:** a consulta só existe na estante/`GameNotesWidget`; o Ctrl+Alt+G abre uma janela topmost que só adiciona (`src/Island.App/Shell/GameNotesWindow.xaml.cs:86-113`) e a ilha se esconde em fullscreen (`src/Island.Core/Application/IslandStateReducer.cs:29`); não há HUD com a lista.
- **Por que é básico:** notas de jogo existem para serem consultadas durante a partida; esconder a ilha anula o widget.
- **Impacto:** o usuário faz alt-tab ou imprime/cola em outro lugar; o recurso perde a função principal.
- **Mínimo viável:** janela/overlay topmost de checklist (abre com atalho, fecha com Esc) com as notas abertas/fixadas.

### D50 — Apagar nota é definitivo, sem confirmação nem desfazer

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar ou poder desfazer a exclusão.
- **Hoje:** o X na linha chama `RemoveNoteAsync` direto (`src/Island.App/Views/Widgets/GameNotesWidget.xaml.cs:296-320`) e o registro é removido na hora (`src/Island.Core/GameNotes/GameNotesBook.cs:86-95`); `README.md:113` diz "Apagar é definitivo"; não há lixeira/undo.
- **Por que é básico:** o X fica ao lado do pin, num widget pequeno; clique errado custa a nota.
- **Impacto:** perda de dados sem recuperação; usuário evita usar o X ou perde notas valiosas.
- **Mínimo viável:** snackbar "Nota apagada — Desfazer" por alguns segundos, ou confirmação.

### D51 — Não dá para limpar as notas do jogo em lote

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** "limpar concluídas" ou "apagar todas as notas deste jogo" ao terminar a run.
- **Hoje:** só existe remoção por nota (`src/Island.Core/GameNotes/GameNotesService.cs:151-152`; `GameNotesBook.cs:86-95`); não há método em lote nem seleção múltipla; o limite é 200 notas/jogo (`GameNotesBook.cs:18`), e o jogo só some da lista quando a última nota é apagada.
- **Por que é básico:** terminar a run/jogo é comum; apagar 20 notas uma a uma inviabiliza a limpeza.
- **Impacto:** notas e jogos antigos acumulam; dados obsoletos atrapalham a busca.
- **Mínimo viável:** "Apagar concluídas" e "Apagar todas do jogo" (com confirmação) no menu do jogo.

### D52 — Prints/screenshots não se ligam ao jogo

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** apertar print e a imagem aparecer anexada ao jogo/nota em que estava.
- **Hoje:** a captura vai para a pasta fixa com nome por data (`src/Island.Windows/Capture/CaptureLibrary.cs:9-10`; `src/Island.Core/Capture/CaptureNaming.cs:17-21`) e `GameNotesGame` não tem mídia/print (`src/Island.Core/GameNotes/GameNotesBook.cs:4-10`); os dois widgets não se conhecem — a nota é só texto (`src/Island.Core/GameNotes/GameNote.cs:4-9`).
- **Por que é básico:** capturas e notas do jogo andam juntas (mapa, build, bug); ferramentas maduras anexam print à nota.
- **Impacto:** usuário não encontra o print daquele jogo; notas sem prova visual.
- **Mínimo viável:** "Print para esta nota/jogo" salvando o caminho no registro, com miniatura na lista.

---

## Parte E — Engenharia, dados, distribuição e operação (39 itens)

Escopo: camada transversal do app como produto instalável e operável — persistência e ciclo de vida dos dados, diagnóstico/suporte, instalação e auto-update, segurança e privacidade, i18n, acessibilidade global, temas/DPI, desempenho, testes/CI, versão, licenças e ajuda. Fora do escopo: comportamento visual de widgets individuais (cobertos nas auditorias A–D).
Método: leitura de `README.md`, `CLAUDE.md`, `Directory.Build.props`, `release.ps1`, `app.manifest`, `src/Island.App/App.xaml.cs`, `Composition/ServiceRegistration.cs`, todos os stores JSON de `src/Island.Windows`, `Updates/*`, `Storage/*`, `Diagnostics/SoakRunner.cs`, `Shell/OverlayWindowService.cs`, janelas de configuração, testes e `docs/*`; cada item cita `arquivo:linha` ou registra a ausência confirmada por busca textual. O app tem ~52 mil linhas de C# em 545 arquivos; a versão auditada é a de `Directory.Build.props` (0.4.40).

### E01 — Backup e restauração unificada dos dados
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** um botão "Fazer backup" (e "Restaurar backup") para guardar/recuperar tudo antes de formatar o PC, trocar de máquina ou depois de uma atualização ruim.
- **Hoje:** não existe backup nem restore; os dados estão espalhados em 12 JSONs (settings, notes, calendar, budgets, performance, weather, counter, gamenotes, pomodoro-schedule, pomodoro-weekly-schedule, angry-resume, fcm-service-account). Busca por `Backup`/`Restore` em `src/` não encontra nada de dados; o próprio roadmap adia a entrega para a v0.5 (`docs/notes-roadmap.md:292-294`).
- **Por que é básico:** sem cópia de segurança, qualquer corrupção, disco cheio, downgrade ou formatação apaga notas, agenda e orçamento sem caminho de volta.
- **Impacto:** perda permanente de dados do usuário; o suporte só pode pedir para copiar arquivos manualmente em `%LocalAppData%\DynamicIsland`.
- **Mínimo viável:** ZIP com os JSONs + versão do app + hashes, restauração com prévia e backup automático antes de migração/atualização.

### E02 — Exportação/importação e "abrir pasta de dados"
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** exportar/importar os dados (para backup ou troca de PC) e abrir a pasta onde eles ficam.
- **Hoje:** não existe exportação/importação de dados (o único `Import` do código é de PDF em `src/Island.Core/Files/FileActionService.cs:122`); não há item "Abrir pasta de dados" no menu da ilha (`src/Island.App/Shell/IslandContextMenu.xaml:75-187`) nem no tray (`src/Island.App/Shell/TrayIconService.cs:38-51`).
- **Por que é básico:** é a operação mínima de portabilidade/arquivamento que qualquer app com dados locais oferece.
- **Impacto:** usuário fica preso à máquina e não consegue inspecionar nem migrar os próprios dados.
- **Mínimo viável:** botões "Exportar tudo", "Importar" e "Abrir %LocalAppData%\DynamicIsland" em Settings/tray.

### E03 — Limpeza/reset dos dados com confirmação
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** apagar tudo (notas, agenda, orçamento, contador, capturas) com confirmação clara, por exemplo antes de emprestar o PC.
- **Hoje:** o único reset é "Reset to defaults", que devolve preferências ao padrão mantendo a credencial do celular e sem tocar em dados (`src/Island.App/ViewModels/SettingsViewModel.cs:410-437`; botão em `src/Island.App/Shell/SettingsWindow.xaml:731`).
- **Por que é básico:** o usuário precisa de uma saída limpa que não dependa de achar arquivos no Explorer.
- **Impacto:** dados pessoais permanecem mesmo depois de o usuário "limpar" o app; sensação de falta de controle.
- **Mínimo viável:** "Apagar dados do app" com lista do que será apagado, confirmação por digitação e backup automático prévio.

### E04 — Schema/migração ausente na maioria dos arquivos JSON
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** que uma atualização do app preserve os dados anteriores e que uma migração aconteça sozinha e com segurança.
- **Hoje:** só `notes.json` e `gamenotes.json` têm `schemaVersion` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:11,53-55`; `src/Island.Windows/GameNotes/JsonGameNotesStore.cs:15,57-59`); settings, calendar, budgets, performance, weather, counter e os três stores de Pomodoro não versionam nada e não há código de migração (busca por `Migration`/`Migrate` em `src/` = 0). O `settings.json` serializa apenas as propriedades conhecidas (`src/Island.Windows/Configuration/JsonSettingsStore.cs:15-27,92-102`), então campos de uma versão futura somem ao salvar numa versão antiga.
- **Por que é básico:** todo app com dados locais precisa dizer "este arquivo é da versão X" antes de evoluir o formato.
- **Impacto:** renomear/adicionar um campo vira perda silenciosa de configuração ou dado; atualização futura fica travada.
- **Mínimo viável:** `schemaVersion` em todos os arquivos, migrador executado no load com backup e arquivo `.pre-migration`, e testes por versão.

### E05 — Downgrade/reinstalação de versão antiga deixa dados ilegíveis
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** que reinstalar a versão anterior (o conserto que o próprio README recomenda) ainda abra as notas em modo somente leitura, com aviso para atualizar.
- **Hoje:** schema maior que o suportado lança `UnsupportedNotesSchemaException`/`UnsupportedGameNotesSchemaException` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:53-55`; `src/Island.Windows/GameNotes/JsonGameNotesStore.cs:57-59`); o app apenas registra no log e segue (`src/Island.App/App.xaml.cs:109-117`), e o README manda reinstalar o zip da Release anterior se algo der errado (`README.md:53`). O "modo somente leitura" existe só no plano futuro (`docs/notes-roadmap.md:199`).
- **Por que é básico:** rollback manual de versão é o caminho de recuperação documentado do produto.
- **Impacto:** depois de reverter uma atualização ruim, as notas somem da UI (sem mensagem), e o usuário conclui que perdeu os dados.
- **Mínimo viável:** abrir em somente leitura, mostrar "dados criados por uma versão mais nova" e nunca gravar por cima.

### E06 — Corrupção vai para `.bad`, mas não há recuperação na UI
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** ser avisado quando um arquivo de dados estiver corrompido e poder ver/restaurar a cópia preservada.
- **Hoje:** `AtomicJsonFile.Quarantine` move para `.bad` (`src/Island.Windows/Storage/AtomicJsonFile.cs:43-49`), mas nenhuma tela mostra ou restaura esse arquivo; o calendário cai em agenda vazia sem aviso (`src/Island.Windows/Calendar/JsonCalendarStore.cs:56-61`), performance e clima voltam ao padrão em silêncio (`src/Island.Windows/Performance/JsonPerformanceSettingsStore.cs:33-36`; `src/Island.Windows/Weather/JsonWeatherCache.cs:43-46`). Só o orçamento informa erro de leitura.
- **Por que é básico:** preservar o arquivo sem dizer ao usuário não evita a percepção de perda de dados.
- **Impacto:** agenda e alertas "desaparecem" do dia para a noite; o usuário não sabe que existe um `.bad` recuperável.
- **Mínimo viável:** aviso no widget/Settings com "abrir pasta", data da quarentena e "restaurar este arquivo".

### E07 — Falhas de gravação silenciosas em stores secundários
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** se uma alteração não foi salva, o app diz isso em vez de mostrar que aplicou.
- **Hoje:** salvar settings é `try { store.Save } catch { log }` sem retorno à UI (`src/Island.App/Composition/ServiceRegistration.cs:56-67`); o contador descarta falhas de escrita (`src/Island.Windows/Tools/Counter/CounterStore.cs:118-125`); os agendamentos de Pomodoro só chamam `Trace.TraceError` (`src/Island.Windows/Pomodoro/JsonPomodoroScheduleStore.cs:54-57`; `src/Island.Windows/Pomodoro/JsonPomodoroWeeklyScheduleStore.cs:86-89`), e esse Trace não vai para o log Serilog do app (`src/Island.App/App.xaml.cs:92-94`).
- **Por que é básico:** prometer "changes apply immediately" (`src/Island.App/Shell/SettingsWindow.xaml:316`) sem confirmar persistência é enganoso.
- **Impacto:** usuário configura tudo, reinicia o PC e descobre que nada foi salvo — sem nenhum erro na tela.
- **Mínimo viável:** propagar falha de escrita para um estado visível e oferecer "tentar salvar de novo".

### E08 — Demo, soak e smoke usam os dados reais do usuário
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que `--demo`/`--soak`/`--smoke-test` rodem isolados e nunca toquem nos seus dados.
- **Hoje:** `ServiceRegistration` registra os stores reais mesmo com `demo=true` (settings, notes, budgets, calendar, performance — `src/Island.App/Composition/ServiceRegistration.cs:85-86,109,113,166`) e o app salva settings no modo demo quando o estado muda (`src/Island.App/App.xaml.cs:160-168`); os argumentos cobrem só `--demo/--soak/--smoke-test` (`src/Island.App/App.xaml.cs:104-105,602-622`), sem `--data-dir`.
- **Por que é básico:** modo de teste precisa de diretório de dados isolado (ou reusa o do produto e contamina).
- **Impacto:** demonstrar/testar na máquina do usuário pode alterar `settings.json` e ler notas/agenda pessoais.
- **Mínimo viável:** `--data-dir <pasta>` (e modo demo usando diretório temporário por padrão).

### E09 — Desinstalação deixa dados, credenciais e auto-start órfãos
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** desinstalar pelo Windows (Aplicativos instalados) e sair sem resíduos.
- **Hoje:** a instalação é extrair zip em `%LocalAppData%\Programs\DynamicIsland` (`README.md:23-25`); não há desinstalador, entrada em "Aplicativos instalados" nem limpeza de `%LocalAppData%\DynamicIsland` (dados, logs, updates, chave FCM) nem da chave `Run\DynamicIsland` (`src/Island.Windows/Shell/OverlayWindowService.cs:37-57`).
- **Por que é básico:** instalar/desinstalar é o ciclo de vida mínimo de um app de desktop.
- **Impacto:** usuário apaga a pasta, o Windows continua tentando iniciar o exe (erro a cada login) e dados/credenciais ficam para trás.
- **Mínimo viável:** "Desinstalar" no menu/tray que remove Run key, atalhos e oferece apagar os dados com confirmação.

### E10 — Sem visualizador de logs, "copiar diagnóstico" ou abrir a pasta
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** um lugar no app para ver o log, copiar um diagnóstico e abrir a pasta de logs quando algo falha.
- **Hoje:** o log Serilog só vai para arquivo (`src/Island.App/App.xaml.cs:91-94`); não existe comando para abrir `%LocalAppData%\DynamicIsland\logs`, ver o log ou copiar versão/SO/data (grep por `OpenLogs`/`Diagnostic` em `src/` só acha o namespace do `SoakRunner`), e os erros mandam "See the logs" sem dizer onde (`src/Island.App/App.xaml.cs:450`).
- **Por que é básico:** todo app com suporte precisa de um caminho de 1 clique para o diagnóstico.
- **Impacto:** usuário leigo não reporta; o suporte perde tempo explicando caminho e coletando versão manualmente.
- **Mínimo viável:** "Ajuda/Diagnóstico" com abrir pasta, copiar bloco (versão, SO, arquitetura, últimos erros) e limpar logs.

### E11 — Tratamento de exceções incompleto: erro é engolido e o app pode morrer calado
- **Severidade:** Crítica
- **Tipo:** Função parcial
- **Usuário espera:** se o app falhar, uma mensagem amigável com opção de copiar detalhes/abrir log, e o app não seguir em estado inconsistente.
- **Hoje:** só `DispatcherUnhandledException` é tratado, com `ex.Handled = true` e um `Log.Error` (`src/Island.App/App.xaml.cs:97-102`); não há `AppDomain.CurrentDomain.UnhandledException` nem `TaskScheduler.UnobservedTaskException` (busca em `src/` = 0) e diálogo amigável só existe no startup (`src/Island.App/App.xaml.cs:63-72`). O caso v0.4.12 fechou sem nenhuma janela, só deixou log (`README.md:49`).
- **Por que é básico:** capturar exceção sem ação transforma crash em sumiço inexplicável.
- **Impacto:** app some sem explicação, o usuário reabre e repete a falha; bugs de campo viram invisíveis.
- **Mínimo viável:** handler global (UI + domínio + tasks) com janela "algo deu errado", "copiar detalhes" e "reabrir".

### E12 — Sem crash dump, relatório de falha nem telemetria opt-in
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** poder enviar a falha ao desenvolvedor (ou autorizar isso) e ter chance de diagnóstico post-mortem.
- **Hoje:** não há minidump/WER/crash report (busca por `MiniDump`/`WerFault` em `src/` = 0), nenhuma telemetria ou opt-in (busca por `Telemetry|Sentry|AppInsights` = 0) e nenhum health/métrica exposta; o `--soak` só escreve linhas no log (`src/Island.App/Diagnostics/SoakRunner.cs:146-171`).
- **Por que é básico:** com distribuição pública você precisa saber se a versão nova funciona no parque.
- **Impacto:** regressões só aparecem por reclamação; a taxa de falha do update é desconhecida.
- **Mínimo viável:** relatório de crash manual (window "enviar") com caminho de log/dump e consentimento explícito.

### E13 — Logs sem contexto de build/SO e `update-install.log` sem rotação
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que cada linha de log identifique versão, SO, arquitetura e sessão, e que nenhum arquivo cresça para sempre.
- **Hoje:** o logger tem apenas o File sink, sem enricher e sem registrar a versão no startup (`src/Island.App/App.xaml.cs:92-94`; a versão só é usada pelo updater, `src/Island.Windows/Updates/GitHubUpdateService.cs:79-85`); `island-.log` tem retenção de 5 arquivos mas nenhum limite de tamanho (`src/Island.App/App.xaml.cs:93`), e `update-install.log` é arquivo único, acrescentado para sempre (`src/Island.Windows/Updates/GitHubUpdateService.cs:29-30,286-288`).
- **Por que é básico:** log sem versão/OS não permite reproduzir nem triar um problema.
- **Impacto:** suporte não sabe qual build falhou; um loop de erro pode encher o disco.
- **Mínimo viável:** enrichers (versão, SO, arquitetura) e `fileSizeLimitBytes` + rotação no log de instalação.

### E15 — Instalação por zip sem instalador, atalho ou entrada de desinstalação
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** baixar, executar um instalador, ter atalho no menu Iniciar e poder desinstalar como qualquer app.
- **Hoje:** o README manda extrair o zip à mão em `%LocalAppData%\Programs\DynamicIsland`, criar o atalho manualmente e diz explicitamente que não há MSIX/instalador (`README.md:23-25`; `CLAUDE.md`); o zip publicado em `release-out/` (36 entradas) contém binários, `.pdb` e só `Assets/Fonts/OFL.txt`, sem LEIA-ME, versão ou checksums (`release.ps1:61,72`).
- **Por que é básico:** o primeiro contato do usuário é baixar e instalar; hoje exige entender `%LocalAppData%`.
- **Impacto:** desistência na instalação, erros de extração (pasta errada = update sempre falha) e produto com cara de build de dev.
- **Mínimo viável:** instalador inline (ou zip com `INSTALL.txt`/checksum) que copia para `%LocalAppData%\Programs`, cria atalho e registra desinstalação.

### E16 — Dependência do .NET 10 sem aviso, sem self-contained e sem ARM64
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** abrir o app baixado sem precisar descobrir/instalar runtime, e rodar nativo em qualquer Windows 11 (inclusive ARM).
- **Hoje:** alvo `net10.0-windows10.0.19041.0` (`src/Island.App/Island.App.csproj:4`) e publish dependente de framework, `-r win-x64` (`release.ps1:44`); não há verificação da versão do .NET na inicialização (nenhum `Environment.Version`/checagem de Desktop Runtime — busca em `src/` = 0) nem publish `arm64`/self-contained (`release.ps1`; `docs/file-actions.md:29` diz "Windows x64").
- **Por que é básico:** runtime faltando é a primeira falha possível de um app framework-dependent.
- **Impacto:** erro genérico do apphost na primeira execução; em Snapdragon roda por emulação, com consumo maior.
- **Mínimo viável:** detectar runtime na inicialização e mostrar instrução com link; oferecer build `win-arm64` e/ou self-contained (com custo de tamanho documentado).

### E17 — Código e atualização sem assinatura
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** que o Windows não mostre "editor desconhecido"/SmartScreen e que a atualização tenha origem verificável.
- **Hoje:** nenhuma assinatura Authenticode (busca por `signtool|\.pfx|Authenticode` em `src/` = 0; `CLAUDE.md` registra "Não use MSIX nem assinatura: foi removido de propósito") e o updater baixa e executa um exe não assinado de uma Release do GitHub (`src/Island.Windows/Updates/GitHubUpdateService.cs:284-305`).
- **Por que é básico:** assinatura de código é o sinal de confiança que o Windows usa na instalação e no update.
- **Impacto:** aviso do SmartScreen em toda instalação nova e superfície de ataque: uma conta GitHub/Release comprometida vira execução de código em todos os PCs.
- **Mínimo viável:** assinar exe/zip com certificado (mesmo autoassinado documentado) e verificar a assinatura antes de executar o pacote.

### E18 — Auto-update sem progresso e sem cancelamento
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver o andamento do download e poder cancelar sem fechar o app à força.
- **Hoje:** `InstallAsync` aceita `IProgress<double>` e `CancellationToken` (`src/Island.Core/Abstractions/IUpdateService.cs:20`; `src/Island.Windows/Updates/GitHubUpdateService.cs:150-172`), mas a chamada do app não passa nenhum dos dois (`src/Island.App/App.xaml.cs:438-440`): só aparece um notice "Downloading {tag}" e o menu fica travado por `_quitRequestInProgress` (`src/Island.App/App.xaml.cs:431-434`).
- **Por que é básico:** download de dezenas de MB sem % nem saída é o mínimo de feedback de update.
- **Impacto:** usuário acha que travou, mata o processo e pode interromper o flush de notas antes do update.
- **Mínimo viável:** barra com %/MB na ilha ou Settings + "Cancelar" ligado ao `CancellationToken` já suportado.

### E19 — Auto-update sem rollback e sem cópia da versão anterior
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** se a versão nova não abrir, voltar sozinho à anterior (ou com um clique).
- **Hoje:** o script de update copia por cima com `robocopy /E` (sem `/MIR`, sem guardar cópia da instalação antiga) e reabre o app mesmo se a cópia falhar (`src/Island.Windows/Updates/UpdateRules.cs:116-132`); não existe "reverter" no app. O README relata três Releases em que a ilha ficou fechada após atualizar (`README.md:43-53`).
- **Por que é básico:** todo auto-update precisa de um caminho de volta quando a versão nova falha.
- **Impacto:** uma atualização quebrada deixa o app sem abrir e o usuário depende de baixar zip antigo à mão — justamente o cenário já ocorrido.
- **Mínimo viável:** copiar a instalação atual para `backup-<versão>` antes do robocopy e um "Restaurar versão anterior" que reusa o mesmo script.

### E20 — Sem release notes, canal beta ou "ignorar esta versão"
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** saber o que mudou antes de instalar, poder escolher canal estável/beta e pular uma versão problemática.
- **Hoje:** o check usa só `releases/latest` (`src/Island.Windows/Updates/GitHubUpdateService.cs:127`), `TryParseTag` rejeita pré-release (`src/Island.Windows/Updates/UpdateRules.cs:9-21`), não há canal nem opção "skip", e o app não lê o corpo da Release — `Evaluate` lê apenas tag/assets/digest (`src/Island.Windows/Updates/UpdateRules.cs:53-77`); `release.ps1:72` gera notas automáticas de commits e nada é mostrado no app.
- **Por que é básico:** atualizar às cegas é aceitável em 0.x, mas o usuário precisa ver o que muda.
- **Impacto:** desconfiança e atualizações adiadas; impossível segurar uma versão com bug.
- **Mínimo viável:** mostrar o corpo da Release no menu/Settings, suportar pré-release explícito e "ignorar até a próxima".

### E21 — Integridade opcional e escolha do asset sem validação
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** que todo pacote instalado seja verificado e que a arquitetura/versão do arquivo seja a certa.
- **Hoje:** o SHA-256 só é conferido quando a Release traz `digest` (`src/Island.Windows/Updates/GitHubUpdateService.cs:212-218`; `src/Island.Windows/Updates/UpdateRules.cs:65-76` — Releases antigas passam sem verificação); o asset escolhido é o primeiro `.zip` da Release, sem validar nome, arquitetura ou versão (`src/Island.Windows/Updates/UpdateRules.cs:79-96`), e a extração não limita tamanho/entradas (`src/Island.Windows/Updates/GitHubUpdateService.cs:269-278`).
- **Por que é básico:** o único conteúdo que o updater instala vem da rede; validar é pré-requisito.
- **Impacto:** pacote corrompido/errado é executado (ex.: futuro zip arm64 por engano); release antiga instala sem nenhuma conferência.
- **Mínimo viável:** exigir checksum publicado pelo release.ps1, validar nome `DynamicIsland-<versão>-win-<arch>.zip` e recusar arquivo fora do padrão.

### E22 — Update frágil: PowerShell/robocopy/permissões só falham no log
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ser avisado antes de começar se a pasta não for gravável ou se o ambiente bloquear o instalador.
- **Hoje:** o instalador é `powershell.exe -ExecutionPolicy Bypass` + `robocopy` (`src/Island.Windows/Updates/GitHubUpdateService.cs:284-305`; `src/Island.Windows/Updates/UpdateRules.cs:116-132`), sem pré-checar permissão de escrita, espaço em disco ou ExecutionPolicy; uma falha vira notice genérico "Update failed / See the logs" (`src/Island.App/App.xaml.cs:447-451`) e detalhe só em `update-install.log` (`README.md:41` lista "pasta sem permissão" como causa comum).
- **Por que é básico:** o app se instala em pasta de usuário, mas nada garante que ela seja gravável (ex.: cópia herdada de outro PC/OneDrive).
- **Impacto:** usuário tenta atualizar, o app fecha, volta na versão antiga e ele não entende por quê.
- **Mínimo viável:** testar escrita/renomeação de um arquivo na pasta antes do download e reportar o motivo com ação ("mover instalação para %LocalAppData%\Programs").

### E23 — `release.ps1` publica sem rodar a suíte de testes
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que nenhuma Release saia sem os testes passarem.
- **Hoje:** o script só faz `dotnet publish` e o smoke de abertura (`release.ps1:43-59`); rodar `dotnet test` é instrução manual do `CLAUDE.md`. Se `dotnet publish` falha, a versão nova em `Directory.Build.props` não é restaurada (o restore só existe no caminho de falha do smoke, `release.ps1:51-59`).
- **Por que é básico:** gate de release tem de ser automático e atômico.
- **Impacto:** regressão que os testes pegariam pode chegar ao usuário; árvore de trabalho fica com versão incrementada após falha.
- **Mínimo viável:** rodar `dotnet test` no script e abortar também em falha de publish restaurando o `<Version>`.

### E25 — Sem licença, termos ou créditos de terceiros
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** saber sob quais termos o app é distribuído e quais bibliotecas/fontes são usadas.
- **Hoje:** não existem `LICENSE`, `CHANGELOG` ou `NOTICE`/third-party notices na raiz nem no zip (verificado); o único crédito é a licença OFL da fonte Sora (`src/Island.App/Assets/Fonts/OFL.txt`) e os pacotes MIT/Apache (Magick.NET, PDFsharp, NAudio, Serilog, CommunityToolkit) não têm nota de terceiros.
- **Por que é básico:** repositório público sem licença deixa uso/redistribuição juridicamente ambíguos.
- **Impacto:** ninguém pode reusar/citar legalmente; risco para o autor e para empresas que queiram instalar.
- **Mínimo viável:** LICENSE do app + THIRD-PARTY-NOTICES gerado a partir dos pacotes, ambos no zip e no "Sobre".

### E27 — Documentação contraditória, desatualizada e com caminhos errados
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** documentação que diga o que existe e onde ficam os dados.
- **Hoje:** `README.md:80` diz "Não implementado: conversão de arquivos", enquanto `docs/file-actions.md:29-47` documenta os conversores e 1.549 testes; caminhos de dados saem errados por escapes no README (`README.md:106-107` termina em "otes.json"; `README.md:116` mostra "DynamicIslandudgets.json"); o checklist manual de DPI/tela cheia continua desmarcado (`docs/stability-checklist.md:17-27`) e a fase 6 de notificações está pendente (`docs/development-phases.md:5`).
- **Por que é básico:** README é a porta de entrada e o "manual" do produto.
- **Impacto:** usuário procura arquivo no lugar errado e não sabe se um recurso existe; confiança cai.
- **Mínimo viável:** revisar README contra o binário, corrigir caminhos e marcar claramente "implementado/pendente" por recurso.

### E28 — Sem notificações nativas (toast) nem histórico no Windows
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** avisos do app apareçam como notificação do Windows e fiquem na Central de Notificações quando a ilha não estiver visível.
- **Hoje:** os avisos existem só dentro da ilha (`src/Island.App/Views/NoticeView.xaml.cs`), sem toast/AUMID/Action Center: `docs/development-phases.md:5` declara a fase 6 pendente porque `UserNotificationListener` exige identidade de pacote e o app não tem; `docs/notes-roadmap.md:86` confirma que "toast ... não existem"; nenhum pacote Windows App SDK no csproj (`src/Island.App/Island.App.csproj:15-19`).
- **Por que é básico:** usuário que joga em tela cheia ou pausa a ilha simplesmente perde o evento (fim de Pomodoro, captura, falha de update).
- **Impacto:** avisos importantes somem; sem histórico para conferir depois.
- **Mínimo viável:** notificação via `AppNotificationManager` com fallback para o notice atual (o próprio roadmap já prevê esse POC).

### E29 — Clima manda o IP para `ipwho.is` por padrão, sem consentimento nem política
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser informado, na primeira execução ou antes de ligar o clima, que o app consulta a localização aproximada pela internet e poder recusar.
- **Hoje:** `IdleModeEnabled` vem `true` de fábrica (`src/Island.Core/Configuration/IslandSettings.cs:73`) e, com cidade vazia, o app faz `GET https://ipwho.is/` com o IP da máquina (`src/Island.Windows/Weather/OpenMeteoWeatherService.cs:17,50-53`); a única menção é uma descrição dentro de Settings (`src/Island.App/Shell/SettingsWindow.xaml:638`) e não há tela de consentimento nem política de privacidade no repositório.
- **Por que é básico:** enviar IP a terceiro sem aviso é coleta que o usuário não autorizou.
- **Impacto:** problema de privacidade/LGPD e perda de confiança quando o usuário descobre; ambiente corporativo pode proibir.
- **Mínimo viável:** primeira execução pergunta "usar clima por IP?" ou cidade manual, e uma tela "Privacidade: o que o app envia" listando os 3 endpoints.

### E30 — Sem modo offline nem opção de desligar a checagem de atualização
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** um interruptor "sem rede/offline" que impeça qualquer acesso à internet.
- **Hoje:** não há modo offline (busca por `Offline` em `src/` = 0); a checagem de update sempre inicia (`src/Island.App/App.xaml.cs:324`; `src/Island.Windows/Updates/GitHubUpdateService.cs:91-95`) e não existe setting para desativá-la; clima pode ser desligado isoladamente, mas FCM e update continuam possíveis.
- **Por que é básico:** usuários offline/air-gapped e testes de rede precisam cortar todo tráfego.
- **Impacto:** uso proibido em ambiente restrito; impossível provar que o app não telefona.
- **Mínimo viável:** toggle global "Sem acesso à rede" que desliga update, clima e FCM e o deixa visível no "Sobre/Privacidade".

### E31 — Token do celular visível e chave FCM em texto até o primeiro envio
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** credenciais mascaradas e protegidas desde o primeiro momento.
- **Hoje:** o token FCM é editado em `TextBox` comum, visível e copiável (`src/Island.App/Shell/SettingsWindow.xaml:686`); a chave `fcm-service-account.json` fica em texto no disco até o primeiro envio, quando então é convertida para DPAPI (`src/Island.Windows/Phone/FcmPhoneBlockNotifier.cs:357-371`; `README.md:57`). O DPAPI só protege o arquivo, não a memória/UI.
- **Por que é básico:** token/chave privada são credenciais; expô-las em tela e disco é risco simples de evitar.
- **Impacto:** captura de tela/shoulder surfing expõe o token; backup do perfil copia a chave em claro antes do primeiro uso.
- **Mínimo viável:** campo mascarado com "mostrar/revelar", DPAPI já no primeiro load e botão "apagar credencial".

### E32 — Histórico de clipboard captura qualquer texto por padrão, inclusive o token colado
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que senhas/tokens colados não apareçam no histórico do app.
- **Hoje:** a captura de clipboard vem ligada por padrão (`ClipboardEnabled = true`, `src/Island.Core/Configuration/IslandSettings.cs:58`) e captura todo `CF_UNICODETEXT` que não esteja marcado como privado pelo app de origem (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:14,249-252`); a proteção depende de o app de origem marcar `ExcludeClipboardContentFromMonitorProcessing`/`CanIncludeInHistory=0`/`CanUploadToCloud=0` — texto comum de navegador/terminal não vem marcado. O histórico fica só em memória (`src/Island.Core/Clipboard/ClipboardHistory.cs:6`), mas aparece na estante enquanto o app roda — inclusive o token FCM colado em Settings.
- **Por que é básico:** clipboard é onde senhas/tokens circulam; capturar por padrão exige proteção mínima.
- **Impacto:** segredo visível na ilha durante compartilhamento de tela; vazamento por distração.
- **Mínimo viável:** mascarar no histórico itens parecidos com token/senha, botão "pausar captura" e limpar ao abrir; captura opt-in na primeira execução.

### E33 — Dados pessoais em JSON/PNG abertos, sem criptografia nem ACL restrita
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** que notas, agenda e orçamento não fiquem legíveis por qualquer processo/conta do PC.
- **Hoje:** notas, agenda, orçamento, notas de jogo e contador são JSON em texto em `%LocalAppData%` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:26-31`; `src/Island.Windows/Calendar/JsonCalendarStore.cs:35-41`; `src/Island.Windows/Budgets/JsonBudgetStore.cs:17-22`), capturas são PNG em `Pictures\DynamicIsland\Capturas` (`src/Island.Windows/Capture/CaptureLibrary.cs:8-10`) e logs ficam em `%LocalAppData%\DynamicIsland\logs` (`src/Island.App/App.xaml.cs:91`); não há criptografia em repouso nem ACL explícita, e o DPAPI cobre só token/chave (`src/Island.Windows/Storage/UserSecret.cs`).
- **Por que é básico:** o app já usa DPAPI; o usuário assume que o mesmo cuidado vale para conteúdo pessoal.
- **Impacto:** qualquer conta com acesso ao perfil lê tudo; backup/sincronização de perfil espalha os dados.
- **Mínimo viável:** documentar claramente "dados não são criptografados", restringir ACL da pasta e oferecer senha/DPAPI opcional para notas.

### E35 — Nenhum sistema de localização
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** escolher o idioma do app e ver todos os textos traduzidos de forma consistente.
- **Hoje:** não existe nenhum `.resx` no repositório, nem `ResourceManager`/`CurrentUICulture` no código (verificado: 0 ocorrências); todas as strings estão hardcoded em XAML/C# (ex.: `src/Island.App/Shell/SettingsWindow.xaml:315-716`; `src/Island.App/App.xaml.cs:68,280,378`).
- **Por que é básico:** sem recurso central não há como corrigir/atualizar texto nem oferecer idioma.
- **Impacto:** qualquer ajuste de texto vira alteração de código; usuário não-inglês/português fica sem opção.
- **Mínimo viável:** arquivos `.resx` (ou JSON de recursos) com CurrentUICulture, pt-BR/en-US como primeiro par e menu de idioma.

### E37 — Formatação de data/número inconsistente e quebra fora do pt-BR
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** datas, horas e números no formato da cultura do Windows, com opção 12/24 h.
- **Hoje:** formatadores fixam pt-BR (`src/Island.Core/Tools/TextAnalyzer/TextStatsFormat.cs:14`; `src/Island.Core/Budgets/PercentFormatter.cs:7`; `src/Island.App/Budgets/ViewModels/BudgetTextFormatter.cs:9`; `src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:45`), enquanto a data do calendário usa `CurrentCulture` com literais em português (`src/Island.App/Shell/CalendarEntryWindow.xaml.cs:48` — "dddd, d 'de' MMMM 'de' yyyy" produz "Friday, 10 de October de 2026" em en-US) e horas usam `InvariantCulture` "HH:mm" sem opção de 12 h (`src/Island.App/Shell/ScheduleWindow.xaml.cs:56-57`; `src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:290`).
- **Por que é básico:** formatação por cultura é parte do contrato de qualquer app Windows.
- **Impacto:** texto errado em Windows em inglês/espanhol e em configurações regionais diferentes; aparência de texto "quebrado".
- **Mínimo viável:** usar `CultureInfo.CurrentCulture` em tudo (sem literais fixos) e expor 12/24 h e primeiro dia da semana.

### E44 — DPI/multi-monitor declarado, mas não validado e com posição em pixels físicos
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** a ilha no lugar certo em monitores com escalas diferentes e após conectar/desconectar tela.
- **Hoje:** `PerMonitorV2` está no manifesto (`src/Island.App/app.manifest:5`), mas a posição é salva em pixels físicos da tela virtual (`src/Island.Core/Configuration/IslandSettings.cs:29-32`) e a validação 100/125/150/200% + dois monitores com DPI diferente continua manual e desmarcada (`docs/stability-checklist.md:17-23`).
- **Por que é básico:** multi-monitor com escalas diferentes é o caso comum em notebooks + monitor externo.
- **Impacto:** ilha fora do centro/escondida ao trocar de monitor ou escala; usuário acha que sumiu.
- **Mínimo viável:** posição em DIPs por monitor + reposicionamento ao `DisplaySettingsChanged` e checklist automatizado de DPI.

### E45 — Sem benchmark nem metas de desempenho
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** garantia de que o app em repouso consome pouco (CPU/RAM) e que isso é medido a cada versão.
- **Hoje:** não há benchmarks (`BenchmarkDotNet` = 0 em `src/`/`tests/`); o soak é manual e o próprio checklist manda "repetir em Release por 30+ min antes de liberar" (`docs/stability-checklist.md:8-13`), sem execução em CI.
- **Por que é básico:** um app sempre-ligado disputa CPU/RAM com o resto do PC; sem medida, qualquer regressão passa.
- **Impacto:** consumo ruim descoberto pelo usuário; reviews negativas por "app que consome".
- **Mínimo viável:** teste de fumaça de desempenho (CPU em repouso, RAM, handles) com limites e histórico por release.

### E46 — ~840 contadores de GPU a cada 3 s sem medição de custo
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** alertas de temperatura sem custo perceptível de CPU.
- **Hoje:** com o alerta ligado (padrão), a amostragem é a cada 3 s (`src/Island.Core/Performance/PerformanceSamplingPolicy.cs:7`) e usa wildcards `\GPU Engine(*)\Utilization Percentage`/GPU Adapter Memory (`src/Island.Windows/Performance/CounterGpuReader.cs:58-59`; `src/Island.Windows/Performance/PdhWildcardCounter.cs:7`); o README admite ~840 contadores por leitura e "custo de CPU não medido" (`README.md:95`), sem throttling adaptativo nem desligamento automático.
- **Por que é básico:** recurso ligado por padrão não pode ter custo desconhecido.
- **Impacto:** em notebooks, consumo contínuo de CPU/bateria e possível alerta de antivírus por acesso a muitos contadores.
- **Mínimo viável:** medir o custo, amostrar GPU com intervalo maior (ex.: 10 s), reusar consultas abertas e só monitorar o que o usuário habilitou.

### E47 — Histórico de clipboard limitado por contagem, não por bytes
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** que o histórico em memória não cresça sem controle.
- **Hoje:** o limite é `ClipboardMaxItems` (default 50, `src/Island.Core/Configuration/IslandSettings.cs:59`; corte em `src/Island.Core/Clipboard/ClipboardHistory.cs:44-50`), sem teto agregado de bytes; cada imagem é aceita até 25 MB antes de comprimir (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:20`).
- **Por que é básico:** app residente que guarda 50 capturas de tela pode ficar centenas de MB sem aviso.
- **Impacto:** picos de memória em sessões longas; pressão no GC e no sistema.
- **Mínimo viável:** limite total em MB (ex.: 32 MB) descartando os itens mais antigos e mostrando o uso no painel.

### E48 — Sem CI: nenhum teste/build automático no repositório
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que cada mudança seja compilada e testada automaticamente antes de virar Release.
- **Hoje:** `.github/` não existe (verificado) e nenhum workflow de CI foi encontrado — a única configuração de automação no repositório é `global.json` (busca por `*.yml`/`*.yaml` só acha esse arquivo); a suíte de ~1.549 testes (`docs/file-actions.md:43`) só roda quando alguém lembra de rodar `dotnet test`, e cobertura não é medida (sem coverlet/relatório nos `.csproj` de teste).
- **Por que é básico:** projeto público com auto-update precisa de gate automático; hoje a garantia é manual.
- **Impacto:** regressões como a v0.4.12 (app que não abre) só são pegas pelo smoke local do release; contribuições externas não são validadas.
- **Mínimo viável:** workflow de `build` + `dotnet test` em PR/push para `main` e publicação da Release a partir de tag verde.

### E49 — Sem testes de UI/E2E nem do fluxo de atualização real
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** confiança de que abrir a estante, editar uma nota, instalar update e reiniciar funcionam de ponta a ponta.
- **Hoje:** não há testes de UI/E2E (busca por `FlaUI|WinAppDriver|UIAutomation` = 0; os testes WPF usam host própria, `tests/Island.Windows.Tests/WpfStaTestHost.cs`); `UpdateInstallationTests` cobre apenas script/lançamento com dependências falsas e `release.ps1` só abre o exe publicado com `--smoke-test` (`release.ps1:47-59`) — nenhum teste executa "instalar versão anterior → update → confirmar versão nova".
- **Por que é básico:** o fluxo de update é o mais crítico do produto e o README lista três incidentes recentes nele.
- **Impacto:** falhas de integração só aparecem em produção; cada release exige teste manual completo.
- **Mínimo viável:** E2E em máquina de CI que instala o zip publicado, aplica um update fake e valida que o app reabre na versão nova.

### E50 — Testes instáveis conhecidos e tolerados na suíte
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que "testes passando" signifique algo e que o CI não fique vermelho intermitentemente.
- **Hoje:** há flakiness registrado e contornado manualmente: `WindowsClipboardServiceTests.ExcludedFormat_IsIgnored_WhileNormalCopyIsCaptured` (instrução no `CLAUDE.md`) e `QuickNotesViewModelTests.Autosave_persists_after_the_quiet_period` (`docs/file-actions.md:39`); não há retry/quarentena nem issue registrada no repositório.
- **Por que é básico:** suíte instável corrói a confiança e mascara regressões reais.
- **Impacto:** releases podem passar com teste pulado; investigação de bug começa desacreditada.
- **Mínimo viável:** marcar os flaky com `[Trait]`/quarentena, corrigir a sincronização (relógio virtual) e rodar a suíte 3× no CI.

---

## Apêndice A — Inventário do que o app já tem (para calibrar as lacunas)

Esta auditoria não parte de um app vazio. Muitas funções existem e o item correspondente aponta o que falta *ao redor* delas. Para referência, o que já está implementado na v0.4.40:

- **Ilha e sistema:** 16 widgets na estante, modos compacto/expandido/Mini, encaixe lateral, posição arrastável, tela cheia, pausa, modo demo, soak test, smoke test, single instance, reinício do Explorer tratado.
- **Pomodoro:** perfis (Estudo/Leitura/Trabalho), ciclos, avanço automático/manual, pílula Pré, agendamento avulso e rotina semanal, tela de transição, 4 sons por etapa, modo Angry com bloqueio de sites e desktop, bloqueio de celular via FCM, recuperação da sessão Angry após update.
- **Notas:** captura rápida global (Ctrl+Alt+N), notas com cor, tags, checklist, fixar, arquivar, lixeira, busca, autosave e tratamento de erro de salvamento; notas por jogo.
- **Calendário:** visão mês, janela do dia, eventos, tarefas com conclusão, aniversários, marcadores.
- **Orçamento de IA:** múltiplas IAs, modo consumido/restante, dias sem uso, cálculo por dias úteis, editor e widget.
- **Ferramentas:** calculadora, contador, timer/cronômetro, analisador de texto, sons de foco gerados por síntese, File Tray, File Orbit, 16 ações de arquivo, clipboard em memória com imagens.
- **Jogo:** captura de tela e gravação MP4 (GDI + Media Foundation), desempenho CPU/GPU/RAM/VRAM/temperaturas, mixer por aplicativo com medidor, notas por jogo.
- **Operação:** escuta de mídia e volume orientada a eventos, 12 arquivos JSON com escrita atômica e quarentena `.bad`, DPAPI para token, auto-update com verificação de repositório e SHA-256 opcional, logs Serilog com retenção de 5 dias, 162 classes de teste.

O que isso significa: **as lacunas deste relatório são de produto e de ciclo de vida, não de "o app não faz nada".** Um app que já tem tudo isso e ainda não tem backup, notificação nativa, histórico de foco e instalador está a poucos passos de parecer muito mais maduro do que é — e é exatamente aí que a lista abaixo deve ser usada.

---

## Apêndice B — Itens consolidados (24)

Os itens abaixo foram produzidos por dois domínios diferentes descrevendo a mesma lacuna. Para não inflar a contagem, o item menos específico foi removido e o tema permanece coberto pelo item indicado. Nenhum assunto foi descartado.

| Removido | Coberto por | Motivo da consolidação |
|---|---|---|
| A43 | E28 | "Sem toast nativo do Windows" — E28 trata do tema de forma global (inclusive histórico). |
| B14 | E35, E37 | "Sem seletor de idioma" — E35 cobre a ausência de localização e E37 a formatação regional. |
| B35 | C48 | "File Tray perde os arquivos ao fechar" — C48 é o item de persistência do File Tray. |
| B39 | C55 | "Clipboard sem favoritos/pin" — C55 tem o mesmo escopo com mais detalhe. |
| B40 | C57 | "Pausar captura/limite do clipboard sem UI" — C57 cobre os controles de clipboard. |
| B42 | C54 | "Histórico do clipboard só da sessão" — C54 cobre persistência e a opção de privacidade. |
| B46 | E29 | "Clima manda IP sem consentimento" — E29 trata privacidade/rede do clima. |
| B49 | E18 | "Update sem progresso/cancelar" — E18 cobre o fluxo de auto-update. |
| B50 | E20 | "Sem release notes antes de instalar" — E20 cobre release notes/canal beta/ignorar versão. |
| B52 | E19, E20 | "Sem canal beta/rollback" — E19 cobre rollback e E20 canal/ignorar versão. |
| B53 | E30 | "Verificação automática de update não pode ser desligada" — E30 cobre offline e desligar a checagem. |
| C56 | B37 | "Clipboard sem busca/filtro" — B37 registra o botão de busca decorativo e a ausência de filtro. |
| D43 | B05 | "GameProcesses só editável no JSON" — B05 é o mesmo campo visto pelas configurações. |
| E14 | B31 | "Segunda instância some sem ativar a existente" — B31 descreve o comportamento e a saída. |
| E24 | B02 | "Versão não aparece na UI" — B02 cobre a tela Sobre e a versão instalada. |
| E26 | B03 | "Sem ajuda/onboarding no app" — B03 cobre primeira execução e ajuda. |
| E34 | B10 | "Start with Windows sem reconciliação com o registro" — B10 trata do toggle e do estado real. |
| E36 | B13 | "Inglês e português misturados" — B13 traz as evidências na mesma janela. |
| E38 | B57 | "Ilha não recebe teclado" — B57 é o item de operação por teclado/leitor de tela. |
| E39 | B58 | "Foco de teclado invisível" — B58 cobre os estilos que removem o indicador. |
| E40 | B59 | "Controles sem AutomationPeer/nome acessível" — B59 cobre tiles e cards. |
| E41 | B11 | "Sem alto contraste" — B11 cobre tema/contraste/siga o Windows. |
| E42 | B12 | "Sem escala de fonte" — B12 cobre zoom da interface. |
| E43 | B11 | "Só tema escuro" — B11 cobre tema claro e seguir o sistema. |

---

## Apêndice C — Método, cobertura e limites

### Como foi feito

1. **Reconhecimento do lead:** inventário do repositório (441 arquivos-fonte, 42.200 linhas, 16 widgets, 53 XAML, 39 campos de configuração, 162 classes de teste, 1.164 casos), leitura do README, `docs/` e pontos de composição (`App.xaml.cs`, `ServiceRegistration`, `WidgetCatalog`, `SettingsWindow`).
2. **Cinco auditorias independentes em paralelo**, cada uma com escopo, arquivos-chave e meta mínima de itens:
   - **A — Pomodoro e foco** (timer, ciclos, perfis, agendamento, Angry, celular, sons, transição).
   - **B — Ilha, estante, configurações e UX do Windows** (posição, atalhos, pausa, avisos, clipboard, clima, update, bandeja, acessibilidade).
   - **C — Produtividade e ferramentas** (notas, calendário, orçamento, tarefas, calculadora, contador, timer, analisador, sons de foco, arquivos, clipboard).
   - **D — Jogo, captura, mixer e desempenho**.
   - **E — Engenharia, dados, distribuição e operação** (persistência, diagnóstico, instalador/update, segurança/privacidade, i18n, acessibilidade global, desempenho, testes/CI).
3. **Consolidação do lead:** verificação estrutural (IDs únicos, 7 campos por item, severidade/tipo no padrão, referências de arquivo existentes), deduplicação de 24 itens entre domínios e montagem deste relatório.

### Verificação

- **Presença:** cada item cita `caminho/arquivo.cs:linha` do comportamento atual.
- **Ausência:** confirmada por busca textual (grep) pelo identificador que provaria a existência; o item diz qual termo foi buscado e onde.
- **README:** usado para separar o que já existe do que falta, nunca como prova de existência de uma função.
- **Nada foi alterado em `src/`, `tests/` ou `README.md`.** As auditorias só escreveram os arquivos de origem em `docs/analise-funcoes-basicas/`.

### Limites (o que esta análise não prova)

- **Não é teste de runtime.** O app não foi executado durante a auditoria; itens de percepção visual, fluidez e comportamento em hardware real precisam de validação manual.
- **Não cobre hardware real:** captura em tela cheia exclusiva, NVML, zonas térmicas, fones Bluetooth, DPI 150/200%, leitor de tela (Narrator/NVDA) e FCM real não foram exercitados.
- **Não é revisão de segurança ofensiva.** Itens como DPAPI, token FCM e integridade de update foram avaliados pelo que o código faz, não por pentest.
- **Não é auditoria de estilo/arquitetura interna.** Refatorações só entraram quando produzem dor visível ao usuário.
- **Algumas ausências podem ser decisão de escopo.** `docs/notes-roadmap.md` declara, por exemplo, que compartilhamento/sincronização de notas não é objetivo; a lista inclui o que um app maduro da categoria costuma ter, mesmo que o roadmap tenha adiado — cabe ao produto decidir o que aceitar.
- **Contradições README × código encontradas:**
  1. O exemplo do dono do produto ("faltam perfis") já está implementado na 0.4.40 (ver seção específica acima).
  2. `README.md:80` diz que conversão de arquivos não está implementada, mas `src/Island.Core/Files` já expõe 16 ações de arquivo (a auditoria seguiu o código).
  3. `README.md:124` diz que a previsão no avanço manual mostra duração "sem prometer horário de término", mas o widget calcula `DateTime.Now + rest` e mostra `HH:mm` (A11).

### Números do repositório auditado

| Item | Valor |
|---|---|
| Arquivos-fonte (`src/`) | 441 (`.cs` + `.xaml`) |
| Linhas de código-fonte | ~42.200 |
| Widgets registrados | 16 |
| Arquivos XAML | 53 |
| Campos em `settings.json` | 39 |
| Classes de teste | 162 |
| Casos/atributos de teste | ~1.164 |
| Arquivos de dados JSON | 12 |
| CI (`.github`) | ausente |
| `LICENSE` / `CHANGELOG` / `NOTICE` | ausentes |

---

## Apêndice D — Do relatório ao backlog

**Sequência sugerida de releases:**

1. **0.4.x — parar a perda de dados (P0 de confirmação/erro):** confirmação/undo nos resets (A05, B06), proteção contra o agendamento apagar sessão (A30), feedback de falha de settings (B09), handler global de exceção (E11), edição/exclusão no calendário (C14).
2. **0.5 — dados do usuário:** backup/restauração unificada (E01), recuperação de `.bad` na UI (C04, E06), versão de schema/migração (E04), exportação de histórico de foco (A22–A26), persistência de File Tray e clipboard com opção de privacidade (C48, C54).
3. **0.6 — básico de Windows:** instalador/desinstalador e atalhos (E15), toast nativo (E28), tema claro/contraste e escala (B11, B12), idioma único e recursos (B13, E35), atalhos remapeáveis (B04), acessibilidade da ilha (B57–B59), tela Sobre/Ajuda (B02, B03).
4. **0.7 — jogo:** região/janela na captura (D01), áudio no vídeo (D03), WGC para tela cheia exclusiva (D12), FPS/frametime (D17), lista completa das notas do jogo (D41), seletor de dispositivo no mixer (D29).
5. **Contínuo:** CI com build+testes (E48), rollback de update (E19), assinatura de código (E17), remoção dos testes instáveis tolerados (E50).

**Definição de pronto sugerida para "função básica":** existe na UI (não só no JSON); persiste entre reinícios; tem confirmação/desfazer quando destrutiva; tem estado vazio e estado de erro; é exportável/backupável quando são dados do usuário; funciona por teclado e leitor de tela; tem teste automatizado; e aparece na ajuda/onboarding.

**Como manter esta lista viva:** cada item tem ID estável — use o ID em issues/commits. Quando um item for corrigido, marque `feito (versão)` no relatório de origem em `docs/analise-funcoes-basicas/`. Uma re-auditoria a cada duas ou três releases deve produzir menos itens, não mais.

---

## Apêndice E — Rastreabilidade

- `docs/analise-funcoes-basicas/A-pomodoro-e-foco.md` — 52 itens brutos; 51 após consolidação.
- `docs/analise-funcoes-basicas/B-ilha-estante-settings.md` — 59 itens brutos; 49 após consolidação.
- `docs/analise-funcoes-basicas/C-produtividade-ferramentas.md` — 58 itens brutos; 57 após consolidação.
- `docs/analise-funcoes-basicas/D-jogo-captura-audio.md` — 52 itens brutos; 51 após consolidação.
- `docs/analise-funcoes-basicas/E-engenharia-operacao.md` — 50 itens brutos; 39 após consolidação.
- **Total bruto:** 271. **Consolidados:** 24. **Total distinto neste relatório:** **247 itens** — 14 críticos, 76 altos, 135 médios e 22 baixos.

---

*Relatório gerado por auditoria multiagente (5 subagentes + consolidação do lead). Análise estática da versão 0.4.40; nenhum arquivo de código do app foi modificado.*
