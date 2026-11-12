# Auditoria — Engenharia, dados, distribuição e operação

Escopo: camada transversal do app como produto instalável e operável — persistência e ciclo de vida dos dados, diagnóstico/suporte, instalação e auto-update, segurança e privacidade, i18n, acessibilidade global, temas/DPI, desempenho, testes/CI, versão, licenças e ajuda. Fora do escopo: comportamento visual de widgets individuais (cobertos nas auditorias A–D).
Método: leitura de `README.md`, `CLAUDE.md`, `Directory.Build.props`, `release.ps1`, `app.manifest`, `src/Island.App/App.xaml.cs`, `Composition/ServiceRegistration.cs`, todos os stores JSON de `src/Island.Windows`, `Updates/*`, `Storage/*`, `Diagnostics/SoakRunner.cs`, `Shell/OverlayWindowService.cs`, janelas de configuração, testes e `docs/*`; cada item cita `arquivo:linha` ou registra a ausência confirmada por busca textual. O app tem ~52 mil linhas de C# em 545 arquivos; a versão auditada é a de `Directory.Build.props` (0.4.40).

## E01 — Backup e restauração unificada dos dados
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** um botão "Fazer backup" (e "Restaurar backup") para guardar/recuperar tudo antes de formatar o PC, trocar de máquina ou depois de uma atualização ruim.
- **Hoje:** não existe backup nem restore; os dados estão espalhados em 12 JSONs (settings, notes, calendar, budgets, performance, weather, counter, gamenotes, pomodoro-schedule, pomodoro-weekly-schedule, angry-resume, fcm-service-account). Busca por `Backup`/`Restore` em `src/` não encontra nada de dados; o próprio roadmap adia a entrega para a v0.5 (`docs/notes-roadmap.md:292-294`).
- **Por que é básico:** sem cópia de segurança, qualquer corrupção, disco cheio, downgrade ou formatação apaga notas, agenda e orçamento sem caminho de volta.
- **Impacto:** perda permanente de dados do usuário; o suporte só pode pedir para copiar arquivos manualmente em `%LocalAppData%\DynamicIsland`.
- **Mínimo viável:** ZIP com os JSONs + versão do app + hashes, restauração com prévia e backup automático antes de migração/atualização.

## E02 — Exportação/importação e "abrir pasta de dados"
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** exportar/importar os dados (para backup ou troca de PC) e abrir a pasta onde eles ficam.
- **Hoje:** não existe exportação/importação de dados (o único `Import` do código é de PDF em `src/Island.Core/Files/FileActionService.cs:122`); não há item "Abrir pasta de dados" no menu da ilha (`src/Island.App/Shell/IslandContextMenu.xaml:75-187`) nem no tray (`src/Island.App/Shell/TrayIconService.cs:38-51`).
- **Por que é básico:** é a operação mínima de portabilidade/arquivamento que qualquer app com dados locais oferece.
- **Impacto:** usuário fica preso à máquina e não consegue inspecionar nem migrar os próprios dados.
- **Mínimo viável:** botões "Exportar tudo", "Importar" e "Abrir %LocalAppData%\DynamicIsland" em Settings/tray.

## E03 — Limpeza/reset dos dados com confirmação
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** apagar tudo (notas, agenda, orçamento, contador, capturas) com confirmação clara, por exemplo antes de emprestar o PC.
- **Hoje:** o único reset é "Reset to defaults", que devolve preferências ao padrão mantendo a credencial do celular e sem tocar em dados (`src/Island.App/ViewModels/SettingsViewModel.cs:410-437`; botão em `src/Island.App/Shell/SettingsWindow.xaml:731`).
- **Por que é básico:** o usuário precisa de uma saída limpa que não dependa de achar arquivos no Explorer.
- **Impacto:** dados pessoais permanecem mesmo depois de o usuário "limpar" o app; sensação de falta de controle.
- **Mínimo viável:** "Apagar dados do app" com lista do que será apagado, confirmação por digitação e backup automático prévio.

## E04 — Schema/migração ausente na maioria dos arquivos JSON
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** que uma atualização do app preserve os dados anteriores e que uma migração aconteça sozinha e com segurança.
- **Hoje:** só `notes.json` e `gamenotes.json` têm `schemaVersion` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:11,53-55`; `src/Island.Windows/GameNotes/JsonGameNotesStore.cs:15,57-59`); settings, calendar, budgets, performance, weather, counter e os três stores de Pomodoro não versionam nada e não há código de migração (busca por `Migration`/`Migrate` em `src/` = 0). O `settings.json` serializa apenas as propriedades conhecidas (`src/Island.Windows/Configuration/JsonSettingsStore.cs:15-27,92-102`), então campos de uma versão futura somem ao salvar numa versão antiga.
- **Por que é básico:** todo app com dados locais precisa dizer "este arquivo é da versão X" antes de evoluir o formato.
- **Impacto:** renomear/adicionar um campo vira perda silenciosa de configuração ou dado; atualização futura fica travada.
- **Mínimo viável:** `schemaVersion` em todos os arquivos, migrador executado no load com backup e arquivo `.pre-migration`, e testes por versão.

## E05 — Downgrade/reinstalação de versão antiga deixa dados ilegíveis
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** que reinstalar a versão anterior (o conserto que o próprio README recomenda) ainda abra as notas em modo somente leitura, com aviso para atualizar.
- **Hoje:** schema maior que o suportado lança `UnsupportedNotesSchemaException`/`UnsupportedGameNotesSchemaException` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:53-55`; `src/Island.Windows/GameNotes/JsonGameNotesStore.cs:57-59`); o app apenas registra no log e segue (`src/Island.App/App.xaml.cs:109-117`), e o README manda reinstalar o zip da Release anterior se algo der errado (`README.md:53`). O "modo somente leitura" existe só no plano futuro (`docs/notes-roadmap.md:199`).
- **Por que é básico:** rollback manual de versão é o caminho de recuperação documentado do produto.
- **Impacto:** depois de reverter uma atualização ruim, as notas somem da UI (sem mensagem), e o usuário conclui que perdeu os dados.
- **Mínimo viável:** abrir em somente leitura, mostrar "dados criados por uma versão mais nova" e nunca gravar por cima.

## E06 — Corrupção vai para `.bad`, mas não há recuperação na UI
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** ser avisado quando um arquivo de dados estiver corrompido e poder ver/restaurar a cópia preservada.
- **Hoje:** `AtomicJsonFile.Quarantine` move para `.bad` (`src/Island.Windows/Storage/AtomicJsonFile.cs:43-49`), mas nenhuma tela mostra ou restaura esse arquivo; o calendário cai em agenda vazia sem aviso (`src/Island.Windows/Calendar/JsonCalendarStore.cs:56-61`), performance e clima voltam ao padrão em silêncio (`src/Island.Windows/Performance/JsonPerformanceSettingsStore.cs:33-36`; `src/Island.Windows/Weather/JsonWeatherCache.cs:43-46`). Só o orçamento informa erro de leitura.
- **Por que é básico:** preservar o arquivo sem dizer ao usuário não evita a percepção de perda de dados.
- **Impacto:** agenda e alertas "desaparecem" do dia para a noite; o usuário não sabe que existe um `.bad` recuperável.
- **Mínimo viável:** aviso no widget/Settings com "abrir pasta", data da quarentena e "restaurar este arquivo".

## E07 — Falhas de gravação silenciosas em stores secundários
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** se uma alteração não foi salva, o app diz isso em vez de mostrar que aplicou.
- **Hoje:** salvar settings é `try { store.Save } catch { log }` sem retorno à UI (`src/Island.App/Composition/ServiceRegistration.cs:56-67`); o contador descarta falhas de escrita (`src/Island.Windows/Tools/Counter/CounterStore.cs:118-125`); os agendamentos de Pomodoro só chamam `Trace.TraceError` (`src/Island.Windows/Pomodoro/JsonPomodoroScheduleStore.cs:54-57`; `src/Island.Windows/Pomodoro/JsonPomodoroWeeklyScheduleStore.cs:86-89`), e esse Trace não vai para o log Serilog do app (`src/Island.App/App.xaml.cs:92-94`).
- **Por que é básico:** prometer "changes apply immediately" (`src/Island.App/Shell/SettingsWindow.xaml:316`) sem confirmar persistência é enganoso.
- **Impacto:** usuário configura tudo, reinicia o PC e descobre que nada foi salvo — sem nenhum erro na tela.
- **Mínimo viável:** propagar falha de escrita para um estado visível e oferecer "tentar salvar de novo".

## E08 — Demo, soak e smoke usam os dados reais do usuário
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que `--demo`/`--soak`/`--smoke-test` rodem isolados e nunca toquem nos seus dados.
- **Hoje:** `ServiceRegistration` registra os stores reais mesmo com `demo=true` (settings, notes, budgets, calendar, performance — `src/Island.App/Composition/ServiceRegistration.cs:85-86,109,113,166`) e o app salva settings no modo demo quando o estado muda (`src/Island.App/App.xaml.cs:160-168`); os argumentos cobrem só `--demo/--soak/--smoke-test` (`src/Island.App/App.xaml.cs:104-105,602-622`), sem `--data-dir`.
- **Por que é básico:** modo de teste precisa de diretório de dados isolado (ou reusa o do produto e contamina).
- **Impacto:** demonstrar/testar na máquina do usuário pode alterar `settings.json` e ler notas/agenda pessoais.
- **Mínimo viável:** `--data-dir <pasta>` (e modo demo usando diretório temporário por padrão).

## E09 — Desinstalação deixa dados, credenciais e auto-start órfãos
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** desinstalar pelo Windows (Aplicativos instalados) e sair sem resíduos.
- **Hoje:** a instalação é extrair zip em `%LocalAppData%\Programs\DynamicIsland` (`README.md:23-25`); não há desinstalador, entrada em "Aplicativos instalados" nem limpeza de `%LocalAppData%\DynamicIsland` (dados, logs, updates, chave FCM) nem da chave `Run\DynamicIsland` (`src/Island.Windows/Shell/OverlayWindowService.cs:37-57`).
- **Por que é básico:** instalar/desinstalar é o ciclo de vida mínimo de um app de desktop.
- **Impacto:** usuário apaga a pasta, o Windows continua tentando iniciar o exe (erro a cada login) e dados/credenciais ficam para trás.
- **Mínimo viável:** "Desinstalar" no menu/tray que remove Run key, atalhos e oferece apagar os dados com confirmação.

## E10 — Sem visualizador de logs, "copiar diagnóstico" ou abrir a pasta
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** um lugar no app para ver o log, copiar um diagnóstico e abrir a pasta de logs quando algo falha.
- **Hoje:** o log Serilog só vai para arquivo (`src/Island.App/App.xaml.cs:91-94`); não existe comando para abrir `%LocalAppData%\DynamicIsland\logs`, ver o log ou copiar versão/SO/data (grep por `OpenLogs`/`Diagnostic` em `src/` só acha o namespace do `SoakRunner`), e os erros mandam "See the logs" sem dizer onde (`src/Island.App/App.xaml.cs:450`).
- **Por que é básico:** todo app com suporte precisa de um caminho de 1 clique para o diagnóstico.
- **Impacto:** usuário leigo não reporta; o suporte perde tempo explicando caminho e coletando versão manualmente.
- **Mínimo viável:** "Ajuda/Diagnóstico" com abrir pasta, copiar bloco (versão, SO, arquitetura, últimos erros) e limpar logs.

## E11 — Tratamento de exceções incompleto: erro é engolido e o app pode morrer calado
- **Severidade:** Crítica
- **Tipo:** Função parcial
- **Usuário espera:** se o app falhar, uma mensagem amigável com opção de copiar detalhes/abrir log, e o app não seguir em estado inconsistente.
- **Hoje:** só `DispatcherUnhandledException` é tratado, com `ex.Handled = true` e um `Log.Error` (`src/Island.App/App.xaml.cs:97-102`); não há `AppDomain.CurrentDomain.UnhandledException` nem `TaskScheduler.UnobservedTaskException` (busca em `src/` = 0) e diálogo amigável só existe no startup (`src/Island.App/App.xaml.cs:63-72`). O caso v0.4.12 fechou sem nenhuma janela, só deixou log (`README.md:49`).
- **Por que é básico:** capturar exceção sem ação transforma crash em sumiço inexplicável.
- **Impacto:** app some sem explicação, o usuário reabre e repete a falha; bugs de campo viram invisíveis.
- **Mínimo viável:** handler global (UI + domínio + tasks) com janela "algo deu errado", "copiar detalhes" e "reabrir".

## E12 — Sem crash dump, relatório de falha nem telemetria opt-in
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** poder enviar a falha ao desenvolvedor (ou autorizar isso) e ter chance de diagnóstico post-mortem.
- **Hoje:** não há minidump/WER/crash report (busca por `MiniDump`/`WerFault` em `src/` = 0), nenhuma telemetria ou opt-in (busca por `Telemetry|Sentry|AppInsights` = 0) e nenhum health/métrica exposta; o `--soak` só escreve linhas no log (`src/Island.App/Diagnostics/SoakRunner.cs:146-171`).
- **Por que é básico:** com distribuição pública você precisa saber se a versão nova funciona no parque.
- **Impacto:** regressões só aparecem por reclamação; a taxa de falha do update é desconhecida.
- **Mínimo viável:** relatório de crash manual (window "enviar") com caminho de log/dump e consentimento explícito.

## E13 — Logs sem contexto de build/SO e `update-install.log` sem rotação
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que cada linha de log identifique versão, SO, arquitetura e sessão, e que nenhum arquivo cresça para sempre.
- **Hoje:** o logger tem apenas o File sink, sem enricher e sem registrar a versão no startup (`src/Island.App/App.xaml.cs:92-94`; a versão só é usada pelo updater, `src/Island.Windows/Updates/GitHubUpdateService.cs:79-85`); `island-.log` tem retenção de 5 arquivos mas nenhum limite de tamanho (`src/Island.App/App.xaml.cs:93`), e `update-install.log` é arquivo único, acrescentado para sempre (`src/Island.Windows/Updates/GitHubUpdateService.cs:29-30,286-288`).
- **Por que é básico:** log sem versão/OS não permite reproduzir nem triar um problema.
- **Impacto:** suporte não sabe qual build falhou; um loop de erro pode encher o disco.
- **Mínimo viável:** enrichers (versão, SO, arquitetura) e `fileSizeLimitBytes` + rotação no log de instalação.

## E14 — Segunda instância some sem ativar a que já está aberta
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** clicar no atalho de novo traga a ilha/settings para frente em vez de não acontecer nada.
- **Hoje:** `SingleInstanceGuard` existe (`src/Island.Windows/Shell/OverlayWindowService.cs:75-97`), mas a segunda instância só faz `Shutdown()` (`src/Island.App/App.xaml.cs:80-84`), sem IPC para pedir à primeira que apareça ou abra Settings.
- **Por que é básico:** usuário que perdeu a ilha de vista tenta reabrir; o "nada acontece" parece app quebrado.
- **Impacto:** usuário abre o Gerenciador de Tarefas ou reinstala; suporte desnecessário.
- **Mínimo viável:** named pipe `activate` que a instância existente escuta para `Show/Nudge` ou abrir Settings.

## E15 — Instalação por zip sem instalador, atalho ou entrada de desinstalação
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** baixar, executar um instalador, ter atalho no menu Iniciar e poder desinstalar como qualquer app.
- **Hoje:** o README manda extrair o zip à mão em `%LocalAppData%\Programs\DynamicIsland`, criar o atalho manualmente e diz explicitamente que não há MSIX/instalador (`README.md:23-25`; `CLAUDE.md`); o zip publicado em `release-out/` (36 entradas) contém binários, `.pdb` e só `Assets/Fonts/OFL.txt`, sem LEIA-ME, versão ou checksums (`release.ps1:61,72`).
- **Por que é básico:** o primeiro contato do usuário é baixar e instalar; hoje exige entender `%LocalAppData%`.
- **Impacto:** desistência na instalação, erros de extração (pasta errada = update sempre falha) e produto com cara de build de dev.
- **Mínimo viável:** instalador inline (ou zip com `INSTALL.txt`/checksum) que copia para `%LocalAppData%\Programs`, cria atalho e registra desinstalação.

## E16 — Dependência do .NET 10 sem aviso, sem self-contained e sem ARM64
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** abrir o app baixado sem precisar descobrir/instalar runtime, e rodar nativo em qualquer Windows 11 (inclusive ARM).
- **Hoje:** alvo `net10.0-windows10.0.19041.0` (`src/Island.App/Island.App.csproj:4`) e publish dependente de framework, `-r win-x64` (`release.ps1:44`); não há verificação da versão do .NET na inicialização (nenhum `Environment.Version`/checagem de Desktop Runtime — busca em `src/` = 0) nem publish `arm64`/self-contained (`release.ps1`; `docs/file-actions.md:29` diz "Windows x64").
- **Por que é básico:** runtime faltando é a primeira falha possível de um app framework-dependent.
- **Impacto:** erro genérico do apphost na primeira execução; em Snapdragon roda por emulação, com consumo maior.
- **Mínimo viável:** detectar runtime na inicialização e mostrar instrução com link; oferecer build `win-arm64` e/ou self-contained (com custo de tamanho documentado).

## E17 — Código e atualização sem assinatura
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** que o Windows não mostre "editor desconhecido"/SmartScreen e que a atualização tenha origem verificável.
- **Hoje:** nenhuma assinatura Authenticode (busca por `signtool|\.pfx|Authenticode` em `src/` = 0; `CLAUDE.md` registra "Não use MSIX nem assinatura: foi removido de propósito") e o updater baixa e executa um exe não assinado de uma Release do GitHub (`src/Island.Windows/Updates/GitHubUpdateService.cs:284-305`).
- **Por que é básico:** assinatura de código é o sinal de confiança que o Windows usa na instalação e no update.
- **Impacto:** aviso do SmartScreen em toda instalação nova e superfície de ataque: uma conta GitHub/Release comprometida vira execução de código em todos os PCs.
- **Mínimo viável:** assinar exe/zip com certificado (mesmo autoassinado documentado) e verificar a assinatura antes de executar o pacote.

## E18 — Auto-update sem progresso e sem cancelamento
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ver o andamento do download e poder cancelar sem fechar o app à força.
- **Hoje:** `InstallAsync` aceita `IProgress<double>` e `CancellationToken` (`src/Island.Core/Abstractions/IUpdateService.cs:20`; `src/Island.Windows/Updates/GitHubUpdateService.cs:150-172`), mas a chamada do app não passa nenhum dos dois (`src/Island.App/App.xaml.cs:438-440`): só aparece um notice "Downloading {tag}" e o menu fica travado por `_quitRequestInProgress` (`src/Island.App/App.xaml.cs:431-434`).
- **Por que é básico:** download de dezenas de MB sem % nem saída é o mínimo de feedback de update.
- **Impacto:** usuário acha que travou, mata o processo e pode interromper o flush de notas antes do update.
- **Mínimo viável:** barra com %/MB na ilha ou Settings + "Cancelar" ligado ao `CancellationToken` já suportado.

## E19 — Auto-update sem rollback e sem cópia da versão anterior
- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** se a versão nova não abrir, voltar sozinho à anterior (ou com um clique).
- **Hoje:** o script de update copia por cima com `robocopy /E` (sem `/MIR`, sem guardar cópia da instalação antiga) e reabre o app mesmo se a cópia falhar (`src/Island.Windows/Updates/UpdateRules.cs:116-132`); não existe "reverter" no app. O README relata três Releases em que a ilha ficou fechada após atualizar (`README.md:43-53`).
- **Por que é básico:** todo auto-update precisa de um caminho de volta quando a versão nova falha.
- **Impacto:** uma atualização quebrada deixa o app sem abrir e o usuário depende de baixar zip antigo à mão — justamente o cenário já ocorrido.
- **Mínimo viável:** copiar a instalação atual para `backup-<versão>` antes do robocopy e um "Restaurar versão anterior" que reusa o mesmo script.

## E20 — Sem release notes, canal beta ou "ignorar esta versão"
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** saber o que mudou antes de instalar, poder escolher canal estável/beta e pular uma versão problemática.
- **Hoje:** o check usa só `releases/latest` (`src/Island.Windows/Updates/GitHubUpdateService.cs:127`), `TryParseTag` rejeita pré-release (`src/Island.Windows/Updates/UpdateRules.cs:9-21`), não há canal nem opção "skip", e o app não lê o corpo da Release — `Evaluate` lê apenas tag/assets/digest (`src/Island.Windows/Updates/UpdateRules.cs:53-77`); `release.ps1:72` gera notas automáticas de commits e nada é mostrado no app.
- **Por que é básico:** atualizar às cegas é aceitável em 0.x, mas o usuário precisa ver o que muda.
- **Impacto:** desconfiança e atualizações adiadas; impossível segurar uma versão com bug.
- **Mínimo viável:** mostrar o corpo da Release no menu/Settings, suportar pré-release explícito e "ignorar até a próxima".

## E21 — Integridade opcional e escolha do asset sem validação
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** que todo pacote instalado seja verificado e que a arquitetura/versão do arquivo seja a certa.
- **Hoje:** o SHA-256 só é conferido quando a Release traz `digest` (`src/Island.Windows/Updates/GitHubUpdateService.cs:212-218`; `src/Island.Windows/Updates/UpdateRules.cs:65-76` — Releases antigas passam sem verificação); o asset escolhido é o primeiro `.zip` da Release, sem validar nome, arquitetura ou versão (`src/Island.Windows/Updates/UpdateRules.cs:79-96`), e a extração não limita tamanho/entradas (`src/Island.Windows/Updates/GitHubUpdateService.cs:269-278`).
- **Por que é básico:** o único conteúdo que o updater instala vem da rede; validar é pré-requisito.
- **Impacto:** pacote corrompido/errado é executado (ex.: futuro zip arm64 por engano); release antiga instala sem nenhuma conferência.
- **Mínimo viável:** exigir checksum publicado pelo release.ps1, validar nome `DynamicIsland-<versão>-win-<arch>.zip` e recusar arquivo fora do padrão.

## E22 — Update frágil: PowerShell/robocopy/permissões só falham no log
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ser avisado antes de começar se a pasta não for gravável ou se o ambiente bloquear o instalador.
- **Hoje:** o instalador é `powershell.exe -ExecutionPolicy Bypass` + `robocopy` (`src/Island.Windows/Updates/GitHubUpdateService.cs:284-305`; `src/Island.Windows/Updates/UpdateRules.cs:116-132`), sem pré-checar permissão de escrita, espaço em disco ou ExecutionPolicy; uma falha vira notice genérico "Update failed / See the logs" (`src/Island.App/App.xaml.cs:447-451`) e detalhe só em `update-install.log` (`README.md:41` lista "pasta sem permissão" como causa comum).
- **Por que é básico:** o app se instala em pasta de usuário, mas nada garante que ela seja gravável (ex.: cópia herdada de outro PC/OneDrive).
- **Impacto:** usuário tenta atualizar, o app fecha, volta na versão antiga e ele não entende por quê.
- **Mínimo viável:** testar escrita/renomeação de um arquivo na pasta antes do download e reportar o motivo com ação ("mover instalação para %LocalAppData%\Programs").

## E23 — `release.ps1` publica sem rodar a suíte de testes
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que nenhuma Release saia sem os testes passarem.
- **Hoje:** o script só faz `dotnet publish` e o smoke de abertura (`release.ps1:43-59`); rodar `dotnet test` é instrução manual do `CLAUDE.md`. Se `dotnet publish` falha, a versão nova em `Directory.Build.props` não é restaurada (o restore só existe no caminho de falha do smoke, `release.ps1:51-59`).
- **Por que é básico:** gate de release tem de ser automático e atômico.
- **Impacto:** regressão que os testes pegariam pode chegar ao usuário; árvore de trabalho fica com versão incrementada após falha.
- **Mínimo viável:** rodar `dotnet test` no script e abortar também em falha de publish restaurando o `<Version>`.

## E24 — Versão do app não aparece na UI
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** ver qual versão está instalada em About/Settings/tray — essencial para suporte e para saber se o update aplicou.
- **Hoje:** a versão existe só em `<Version>` (`Directory.Build.props:5`) e aparece de forma indireta no label "Install update (vX.Y.Z)" (`src/Island.App/Shell/IslandContextMenu.xaml.cs:60-65`); não há About/rodapé no tray ou Settings — nenhum texto de UI mostra versão (busca por `Versão`/`Sobre`/`About` nos XAML só acha comentários internos, `src/Island.App/Shell/ScheduleWindow.xaml:253` e `src/Island.App/Views/VerticalCompactView.xaml:28`).
- **Por que é básico:** qualquer relato de bug começa com "qual versão você usa?".
- **Impacto:** suporte não consegue triar; usuário não confirma que atualizou.
- **Mínimo viável:** "Sobre" com versão, commit/hash e botão "verificar atualizações".

## E25 — Sem licença, termos ou créditos de terceiros
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** saber sob quais termos o app é distribuído e quais bibliotecas/fontes são usadas.
- **Hoje:** não existem `LICENSE`, `CHANGELOG` ou `NOTICE`/third-party notices na raiz nem no zip (verificado); o único crédito é a licença OFL da fonte Sora (`src/Island.App/Assets/Fonts/OFL.txt`) e os pacotes MIT/Apache (Magick.NET, PDFsharp, NAudio, Serilog, CommunityToolkit) não têm nota de terceiros.
- **Por que é básico:** repositório público sem licença deixa uso/redistribuição juridicamente ambíguos.
- **Impacto:** ninguém pode reusar/citar legalmente; risco para o autor e para empresas que queiram instalar.
- **Mínimo viável:** LICENSE do app + THIRD-PARTY-NOTICES gerado a partir dos pacotes, ambos no zip e no "Sobre".

## E26 — Sem ajuda dentro do app, onboarding ou canal de suporte
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ajuda acessível (F1/"Ajuda"), explicação na primeira execução de atalhos e permissões e um link para reportar problema.
- **Hoje:** a única documentação é o `README.md` (153 linhas, foco em dev/instalação), que não é incluído no zip; não há Ajuda/FAQ/onboarding nem link de issues (busca por `FirstRun|Onboarding|Welcome` em `src/` = 0; menu da ilha sem Help — `src/Island.App/Shell/IslandContextMenu.xaml:75-187`).
- **Por que é básico:** o app tem muitos atalhos e armadilhas (Ctrl+Alt+N/V/P/R/G, Angry, FCM, update) que não se descobrem sozinhos.
- **Impacto:** usuário não descobre recursos, não sabe como desbloquear/atualizar e não tem onde reportar.
- **Mínimo viável:** janela "Ajuda" com atalhos, solução de problemas e link para abrir issue/copiar diagnóstico.

## E27 — Documentação contraditória, desatualizada e com caminhos errados
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** documentação que diga o que existe e onde ficam os dados.
- **Hoje:** `README.md:80` diz "Não implementado: conversão de arquivos", enquanto `docs/file-actions.md:29-47` documenta os conversores e 1.549 testes; caminhos de dados saem errados por escapes no README (`README.md:106-107` termina em "otes.json"; `README.md:116` mostra "DynamicIslandudgets.json"); o checklist manual de DPI/tela cheia continua desmarcado (`docs/stability-checklist.md:17-27`) e a fase 6 de notificações está pendente (`docs/development-phases.md:5`).
- **Por que é básico:** README é a porta de entrada e o "manual" do produto.
- **Impacto:** usuário procura arquivo no lugar errado e não sabe se um recurso existe; confiança cai.
- **Mínimo viável:** revisar README contra o binário, corrigir caminhos e marcar claramente "implementado/pendente" por recurso.

## E28 — Sem notificações nativas (toast) nem histórico no Windows
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** avisos do app apareçam como notificação do Windows e fiquem na Central de Notificações quando a ilha não estiver visível.
- **Hoje:** os avisos existem só dentro da ilha (`src/Island.App/Views/NoticeView.xaml.cs`), sem toast/AUMID/Action Center: `docs/development-phases.md:5` declara a fase 6 pendente porque `UserNotificationListener` exige identidade de pacote e o app não tem; `docs/notes-roadmap.md:86` confirma que "toast ... não existem"; nenhum pacote Windows App SDK no csproj (`src/Island.App/Island.App.csproj:15-19`).
- **Por que é básico:** usuário que joga em tela cheia ou pausa a ilha simplesmente perde o evento (fim de Pomodoro, captura, falha de update).
- **Impacto:** avisos importantes somem; sem histórico para conferir depois.
- **Mínimo viável:** notificação via `AppNotificationManager` com fallback para o notice atual (o próprio roadmap já prevê esse POC).

## E29 — Clima manda o IP para `ipwho.is` por padrão, sem consentimento nem política
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** ser informado, na primeira execução ou antes de ligar o clima, que o app consulta a localização aproximada pela internet e poder recusar.
- **Hoje:** `IdleModeEnabled` vem `true` de fábrica (`src/Island.Core/Configuration/IslandSettings.cs:73`) e, com cidade vazia, o app faz `GET https://ipwho.is/` com o IP da máquina (`src/Island.Windows/Weather/OpenMeteoWeatherService.cs:17,50-53`); a única menção é uma descrição dentro de Settings (`src/Island.App/Shell/SettingsWindow.xaml:638`) e não há tela de consentimento nem política de privacidade no repositório.
- **Por que é básico:** enviar IP a terceiro sem aviso é coleta que o usuário não autorizou.
- **Impacto:** problema de privacidade/LGPD e perda de confiança quando o usuário descobre; ambiente corporativo pode proibir.
- **Mínimo viável:** primeira execução pergunta "usar clima por IP?" ou cidade manual, e uma tela "Privacidade: o que o app envia" listando os 3 endpoints.

## E30 — Sem modo offline nem opção de desligar a checagem de atualização
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** um interruptor "sem rede/offline" que impeça qualquer acesso à internet.
- **Hoje:** não há modo offline (busca por `Offline` em `src/` = 0); a checagem de update sempre inicia (`src/Island.App/App.xaml.cs:324`; `src/Island.Windows/Updates/GitHubUpdateService.cs:91-95`) e não existe setting para desativá-la; clima pode ser desligado isoladamente, mas FCM e update continuam possíveis.
- **Por que é básico:** usuários offline/air-gapped e testes de rede precisam cortar todo tráfego.
- **Impacto:** uso proibido em ambiente restrito; impossível provar que o app não telefona.
- **Mínimo viável:** toggle global "Sem acesso à rede" que desliga update, clima e FCM e o deixa visível no "Sobre/Privacidade".

## E31 — Token do celular visível e chave FCM em texto até o primeiro envio
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** credenciais mascaradas e protegidas desde o primeiro momento.
- **Hoje:** o token FCM é editado em `TextBox` comum, visível e copiável (`src/Island.App/Shell/SettingsWindow.xaml:686`); a chave `fcm-service-account.json` fica em texto no disco até o primeiro envio, quando então é convertida para DPAPI (`src/Island.Windows/Phone/FcmPhoneBlockNotifier.cs:357-371`; `README.md:57`). O DPAPI só protege o arquivo, não a memória/UI.
- **Por que é básico:** token/chave privada são credenciais; expô-las em tela e disco é risco simples de evitar.
- **Impacto:** captura de tela/shoulder surfing expõe o token; backup do perfil copia a chave em claro antes do primeiro uso.
- **Mínimo viável:** campo mascarado com "mostrar/revelar", DPAPI já no primeiro load e botão "apagar credencial".

## E32 — Histórico de clipboard captura qualquer texto por padrão, inclusive o token colado
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que senhas/tokens colados não apareçam no histórico do app.
- **Hoje:** a captura de clipboard vem ligada por padrão (`ClipboardEnabled = true`, `src/Island.Core/Configuration/IslandSettings.cs:58`) e captura todo `CF_UNICODETEXT` que não esteja marcado como privado pelo app de origem (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:14,249-252`); a proteção depende de o app de origem marcar `ExcludeClipboardContentFromMonitorProcessing`/`CanIncludeInHistory=0`/`CanUploadToCloud=0` — texto comum de navegador/terminal não vem marcado. O histórico fica só em memória (`src/Island.Core/Clipboard/ClipboardHistory.cs:6`), mas aparece na estante enquanto o app roda — inclusive o token FCM colado em Settings.
- **Por que é básico:** clipboard é onde senhas/tokens circulam; capturar por padrão exige proteção mínima.
- **Impacto:** segredo visível na ilha durante compartilhamento de tela; vazamento por distração.
- **Mínimo viável:** mascarar no histórico itens parecidos com token/senha, botão "pausar captura" e limpar ao abrir; captura opt-in na primeira execução.

## E33 — Dados pessoais em JSON/PNG abertos, sem criptografia nem ACL restrita
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** que notas, agenda e orçamento não fiquem legíveis por qualquer processo/conta do PC.
- **Hoje:** notas, agenda, orçamento, notas de jogo e contador são JSON em texto em `%LocalAppData%` (`src/Island.Windows/Notes/JsonQuickNotesStore.cs:26-31`; `src/Island.Windows/Calendar/JsonCalendarStore.cs:35-41`; `src/Island.Windows/Budgets/JsonBudgetStore.cs:17-22`), capturas são PNG em `Pictures\DynamicIsland\Capturas` (`src/Island.Windows/Capture/CaptureLibrary.cs:8-10`) e logs ficam em `%LocalAppData%\DynamicIsland\logs` (`src/Island.App/App.xaml.cs:91`); não há criptografia em repouso nem ACL explícita, e o DPAPI cobre só token/chave (`src/Island.Windows/Storage/UserSecret.cs`).
- **Por que é básico:** o app já usa DPAPI; o usuário assume que o mesmo cuidado vale para conteúdo pessoal.
- **Impacto:** qualquer conta com acesso ao perfil lê tudo; backup/sincronização de perfil espalha os dados.
- **Mínimo viável:** documentar claramente "dados não são criptografados", restringir ACL da pasta e oferecer senha/DPAPI opcional para notas.

## E34 — "Start with Windows" sem reconciliação real com o registro
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que o checkbox reflita o estado real do Windows e que mover/instalar o app em outro lugar não quebre o auto-start.
- **Hoje:** ligar/desligar grava a chave `HKCU\...\Run\DynamicIsland` (`src/Island.Windows/Shell/OverlayWindowService.cs:37-57`; chamada em `src/Island.App/Composition/ServiceRegistration.cs:58-66`), mas `StartupRegistration.IsEnabled()` nunca é usado (busca: 1 ocorrência, a própria definição) e o checkbox lê apenas `settings.json` (`src/Island.App/ViewModels/SettingsViewModel.cs:247-257`).
- **Por que é básico:** estado persistido e estado do sistema divergirem é fonte clássica de "não inicia sozinho".
- **Impacto:** usuário desativa pelo Gerenciador de Tarefas e o app continua achando que está ligado; após mover a pasta, o login mostra erro.
- **Mínimo viável:** ler `IsEnabled()` ao abrir Settings, reconciliar a chave com o setting e limpar a chave quando o exe não existir mais.

## E35 — Nenhum sistema de localização
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** escolher o idioma do app e ver todos os textos traduzidos de forma consistente.
- **Hoje:** não existe nenhum `.resx` no repositório, nem `ResourceManager`/`CurrentUICulture` no código (verificado: 0 ocorrências); todas as strings estão hardcoded em XAML/C# (ex.: `src/Island.App/Shell/SettingsWindow.xaml:315-716`; `src/Island.App/App.xaml.cs:68,280,378`).
- **Por que é básico:** sem recurso central não há como corrigir/atualizar texto nem oferecer idioma.
- **Impacto:** qualquer ajuste de texto vira alteração de código; usuário não-inglês/português fica sem opção.
- **Mínimo viável:** arquivos `.resx` (ou JSON de recursos) com CurrentUICulture, pt-BR/en-US como primeiro par e menu de idioma.

## E36 — Inglês e português misturados na mesma janela
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** uma janela em um idioma só.
- **Hoje:** Settings mistura seções em inglês (POSITION, SIZE, TIMING, BEHAVIOR, PHONE, UPDATES — `src/Island.App/Shell/SettingsWindow.xaml:315-716`) com seções em português (SONS DO POMODORO, NOTAS RÁPIDAS, CÁPSULA OCIOSA — `SettingsWindow.xaml:529-655`); o menu da ilha/tray é inglês (`src/Island.App/Shell/IslandContextMenu.xaml:75-187`; `src/Island.App/Shell/TrayIconService.cs:38-51`) contra notices em português (`src/Island.App/App.xaml.cs:280,378,438`).
- **Por que é básico:** mistura de idiomas é o sinal mais visível de produto inacabado.
- **Impacto:** estranhamento imediato na primeira configuração; dificulta suporte e tradução.
- **Mínimo viável:** padronizar tudo em pt-BR agora e mover os textos para recursos antes de abrir tradução.

## E37 — Formatação de data/número inconsistente e quebra fora do pt-BR
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** datas, horas e números no formato da cultura do Windows, com opção 12/24 h.
- **Hoje:** formatadores fixam pt-BR (`src/Island.Core/Tools/TextAnalyzer/TextStatsFormat.cs:14`; `src/Island.Core/Budgets/PercentFormatter.cs:7`; `src/Island.App/Budgets/ViewModels/BudgetTextFormatter.cs:9`; `src/Island.App/Views/Widgets/PomodoroWidget.xaml.cs:45`), enquanto a data do calendário usa `CurrentCulture` com literais em português (`src/Island.App/Shell/CalendarEntryWindow.xaml.cs:48` — "dddd, d 'de' MMMM 'de' yyyy" produz "Friday, 10 de October de 2026" em en-US) e horas usam `InvariantCulture` "HH:mm" sem opção de 12 h (`src/Island.App/Shell/ScheduleWindow.xaml.cs:56-57`; `src/Island.App/Shell/WeeklyPomodoroScheduleWindow.xaml.cs:290`).
- **Por que é básico:** formatação por cultura é parte do contrato de qualquer app Windows.
- **Impacto:** texto errado em Windows em inglês/espanhol e em configurações regionais diferentes; aparência de texto "quebrado".
- **Mínimo viável:** usar `CultureInfo.CurrentCulture` em tudo (sem literais fixos) e expor 12/24 h e primeiro dia da semana.

## E38 — A ilha não recebe teclado: estante e widgets só com mouse
- **Severidade:** Alta
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** navegar e acionar os controles da ilha/estante pelo teclado (Tab/Enter/Setas) ou por leitor de tela.
- **Hoje:** a janela é `WS_EX_NOACTIVATE` (`src/Island.Windows/Shell/OverlayWindowService.cs:14-21`, aplicada em `src/Island.App/Shell/IslandWindow.xaml.cs:258`) e não ativa, então não recebe teclado; a Estante, o painel de clipboard, os controles do Pomodoro/contador/orçamento/captura e as notas dentro do widget só funcionam com clique. Os atalhos globais cobrem apenas 5 ações fixas (Ctrl+Alt+N/V/P/R/G — `src/Island.App/App.xaml.cs:115,120,327,332`; `src/Island.App/Shell/CaptureHotkeys.cs`).
- **Por que é básico:** sem teclado não há uso por quem tem limitação motora/visual, nem navegação rápida por usuários avançados.
- **Impacto:** app inacessível para uma parcela relevante dos usuários e impossível de operar sem mouse.
- **Mínimo viável:** atalho (ex.: Ctrl+Alt+I) que ativa a ilha/estante como janela normal, com Tab entre widgets e Esc para sair.

## E39 — Foco de teclado invisível: 27 estilos removem o indicador
- **Severidade:** Alta
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** enxergar onde está o foco ao navegar por Tab nos diálogos (Settings, Notas, Orçamento, Calendário, Desbloqueio).
- **Hoje:** há 27 ocorrências de `FocusVisualStyle="{x:Null}"` nos XAML, inclusive em `src/Island.App/Shell/SettingsWindow.xaml:112,144,208,235,281`, `src/Island.App/Shell/QuickNotesWindow.xaml:91-307`, `src/Island.App/Shell/BudgetWindow.xaml:25` e `src/Island.App/Views/Widgets/CalendarWidget.xaml:14`, sem contorno/realce alternativo.
- **Por que é básico:** indicador de foco é requisito de acessibilidade por teclado (WCAG).
- **Impacto:** usuário de teclado se perde; quem tem baixa visão não consegue usar os diálogos.
- **Mínimo viável:** um `FocusVisualStyle` global visível (anel de 2 px com contraste) aplicado a botões, toggles, sliders e campos.

## E40 — Controles desenhados à mão sem AutomationPeer (UIA incompleta)
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** que leitores de tela anunciem os controles customizados com nome, papel e valor.
- **Hoje:** há `AutomationProperties.Name` em boa parte da estante, mas nenhuma implementação de `AutomationPeer`/`OnCreateAutomationPeer` no projeto (busca em `src/` = 0); controles que não derivam de `Button`/`TextBox` — ex.: `TimerRing` (Canvas, `src/Island.App/Views/Widgets/TimerRing.cs:19`), `ShelfButton` (Border, `src/Island.App/Widgets/ShelfButton.cs:20`), `FractionBar` (Grid, `src/Island.App/Controls/FractionBar.cs:15`) e `EqualizerGlyph` (StackPanel, `src/Island.App/Controls/EqualizerGlyph.cs:12`) — não expõem papel/valor.
- **Por que é básico:** `AutomationProperties` sozinho não descreve um controle que o UIA não conhece.
- **Impacto:** leitor de tela lê "grupo" ou nada; o usuário não sabe o que o controle faz nem o valor atual.
- **Mínimo viável:** `AutomationPeer` para os controles interativos custom e uso de `AutomationProperties.LiveSetting` nos valores que mudam.

## E41 — Sem suporte a alto contraste
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** que o app respeite o modo de alto contraste do Windows.
- **Hoje:** não há nenhuma referência a `HighContrast` ou `SystemColors` em `src/` (busca = 0); as cores são fixas — 17 valores hex em `src/Island.App/Styles/Colors.xaml` e vários hex diretos nos XAML de widgets/janelas.
- **Por que é básico:** alto contraste é um dos modos de acessibilidade mais usados no Windows.
- **Impacto:** texto/ícones ficam ilegíveis para baixa visão; o app "ignora" uma configuração do sistema.
- **Mínimo viável:** tema de alto contraste com cores de `SystemColors` e detecção de `SystemParameters.HighContrast` em runtime.

## E42 — Sem escala de fonte/texto
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** aumentar o texto do app (pelo "Tamanho do texto" do Windows ou por um controle interno) sem cortar conteúdo.
- **Hoje:** há 195 declarações `FontSize=` fixas nos XAML de `src/Island.App`; não existe setting de escala nem leitura da escala de texto do Windows, e os tamanhos da cápsula são fixos (`CompactWidth`/`CompactHeight`, `src/Island.Core/Configuration/IslandSettings.cs:15-18`).
- **Por que é básico:** DPI/visão variam; app que não escala texto exclui quem precisa de fonte maior.
- **Impacto:** usuário com baixa visão aumenta o DPI do Windows e a ilha continua minúscula.
- **Mínimo viável:** escala global (100–150%) aplicada via `LayoutTransform`/fator nos tamanhos e limite de largura da cápsula.

## E43 — Só tema escuro, sem opção clara e sem seguir o tema do Windows
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** escolher claro/escuro/sistema, ou ao menos respeitar a preferência do Windows.
- **Hoje:** o app é escuro por construção: `WindowAppearance.EnableDarkTitleBar` força a barra escura (`src/Island.Windows/Display/WindowAppearance.cs:8-19`), `BudgetWindow.xaml:6` fixa `ThemeMode="Dark"`, as cores são hex fixos (`src/Island.App/Styles/Colors.xaml`) e não há leitura de `AppsUseLightTheme` nem setting de tema.
- **Por que é básico:** tema claro é expectativa padrão no Windows 11 e afeta legibilidade em ambientes claros.
- **Impacto:** app "destoa" do sistema; usuários de ambientes claros têm dificuldade.
- **Mínimo viável:** recurso de tema (claro/escuro/sistema) com paleta completa e título de janela coerente.

## E44 — DPI/multi-monitor declarado, mas não validado e com posição em pixels físicos
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** a ilha no lugar certo em monitores com escalas diferentes e após conectar/desconectar tela.
- **Hoje:** `PerMonitorV2` está no manifesto (`src/Island.App/app.manifest:5`), mas a posição é salva em pixels físicos da tela virtual (`src/Island.Core/Configuration/IslandSettings.cs:29-32`) e a validação 100/125/150/200% + dois monitores com DPI diferente continua manual e desmarcada (`docs/stability-checklist.md:17-23`).
- **Por que é básico:** multi-monitor com escalas diferentes é o caso comum em notebooks + monitor externo.
- **Impacto:** ilha fora do centro/escondida ao trocar de monitor ou escala; usuário acha que sumiu.
- **Mínimo viável:** posição em DIPs por monitor + reposicionamento ao `DisplaySettingsChanged` e checklist automatizado de DPI.

## E45 — Sem benchmark nem metas de desempenho
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** garantia de que o app em repouso consome pouco (CPU/RAM) e que isso é medido a cada versão.
- **Hoje:** não há benchmarks (`BenchmarkDotNet` = 0 em `src/`/`tests/`); o soak é manual e o próprio checklist manda "repetir em Release por 30+ min antes de liberar" (`docs/stability-checklist.md:8-13`), sem execução em CI.
- **Por que é básico:** um app sempre-ligado disputa CPU/RAM com o resto do PC; sem medida, qualquer regressão passa.
- **Impacto:** consumo ruim descoberto pelo usuário; reviews negativas por "app que consome".
- **Mínimo viável:** teste de fumaça de desempenho (CPU em repouso, RAM, handles) com limites e histórico por release.

## E46 — ~840 contadores de GPU a cada 3 s sem medição de custo
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** alertas de temperatura sem custo perceptível de CPU.
- **Hoje:** com o alerta ligado (padrão), a amostragem é a cada 3 s (`src/Island.Core/Performance/PerformanceSamplingPolicy.cs:7`) e usa wildcards `\GPU Engine(*)\Utilization Percentage`/GPU Adapter Memory (`src/Island.Windows/Performance/CounterGpuReader.cs:58-59`; `src/Island.Windows/Performance/PdhWildcardCounter.cs:7`); o README admite ~840 contadores por leitura e "custo de CPU não medido" (`README.md:95`), sem throttling adaptativo nem desligamento automático.
- **Por que é básico:** recurso ligado por padrão não pode ter custo desconhecido.
- **Impacto:** em notebooks, consumo contínuo de CPU/bateria e possível alerta de antivírus por acesso a muitos contadores.
- **Mínimo viável:** medir o custo, amostrar GPU com intervalo maior (ex.: 10 s), reusar consultas abertas e só monitorar o que o usuário habilitou.

## E47 — Histórico de clipboard limitado por contagem, não por bytes
- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** que o histórico em memória não cresça sem controle.
- **Hoje:** o limite é `ClipboardMaxItems` (default 50, `src/Island.Core/Configuration/IslandSettings.cs:59`; corte em `src/Island.Core/Clipboard/ClipboardHistory.cs:44-50`), sem teto agregado de bytes; cada imagem é aceita até 25 MB antes de comprimir (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:20`).
- **Por que é básico:** app residente que guarda 50 capturas de tela pode ficar centenas de MB sem aviso.
- **Impacto:** picos de memória em sessões longas; pressão no GC e no sistema.
- **Mínimo viável:** limite total em MB (ex.: 32 MB) descartando os itens mais antigos e mostrando o uso no painel.

## E48 — Sem CI: nenhum teste/build automático no repositório
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** que cada mudança seja compilada e testada automaticamente antes de virar Release.
- **Hoje:** `.github/` não existe (verificado) e nenhum workflow de CI foi encontrado — a única configuração de automação no repositório é `global.json` (busca por `*.yml`/`*.yaml` só acha esse arquivo); a suíte de ~1.549 testes (`docs/file-actions.md:43`) só roda quando alguém lembra de rodar `dotnet test`, e cobertura não é medida (sem coverlet/relatório nos `.csproj` de teste).
- **Por que é básico:** projeto público com auto-update precisa de gate automático; hoje a garantia é manual.
- **Impacto:** regressões como a v0.4.12 (app que não abre) só são pegas pelo smoke local do release; contribuições externas não são validadas.
- **Mínimo viável:** workflow de `build` + `dotnet test` em PR/push para `main` e publicação da Release a partir de tag verde.

## E49 — Sem testes de UI/E2E nem do fluxo de atualização real
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** confiança de que abrir a estante, editar uma nota, instalar update e reiniciar funcionam de ponta a ponta.
- **Hoje:** não há testes de UI/E2E (busca por `FlaUI|WinAppDriver|UIAutomation` = 0; os testes WPF usam host própria, `tests/Island.Windows.Tests/WpfStaTestHost.cs`); `UpdateInstallationTests` cobre apenas script/lançamento com dependências falsas e `release.ps1` só abre o exe publicado com `--smoke-test` (`release.ps1:47-59`) — nenhum teste executa "instalar versão anterior → update → confirmar versão nova".
- **Por que é básico:** o fluxo de update é o mais crítico do produto e o README lista três incidentes recentes nele.
- **Impacto:** falhas de integração só aparecem em produção; cada release exige teste manual completo.
- **Mínimo viável:** E2E em máquina de CI que instala o zip publicado, aplica um update fake e valida que o app reabre na versão nova.

## E50 — Testes instáveis conhecidos e tolerados na suíte
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que "testes passando" signifique algo e que o CI não fique vermelho intermitentemente.
- **Hoje:** há flakiness registrado e contornado manualmente: `WindowsClipboardServiceTests.ExcludedFormat_IsIgnored_WhileNormalCopyIsCaptured` (instrução no `CLAUDE.md`) e `QuickNotesViewModelTests.Autosave_persists_after_the_quiet_period` (`docs/file-actions.md:39`); não há retry/quarentena nem issue registrada no repositório.
- **Por que é básico:** suíte instável corrói a confiança e mascara regressões reais.
- **Impacto:** releases podem passar com teste pulado; investigação de bug começa desacreditada.
- **Mínimo viável:** marcar os flaky com `[Trait]`/quarentena, corrigir a sincronização (relógio virtual) e rodar a suíte 3× no CI.
