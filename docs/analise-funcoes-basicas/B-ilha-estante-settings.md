# Auditoria — Ilha, estante, configurações e UX do sistema

Escopo: funções básicas da ilha, da estante, do clipboard, da cápsula ociosa (clima), da bandeja, das configurações e do update — o que o usuário sente falta na primeira semana de uso.
Método: leitura do README e dos arquivos-chave em `src/` e grep dos termos que provariam cada função (About, Help, Language, Theme, Export, Search, HighContrast, AutomationProperties, PauseUntil, Topmost etc.); cada item cita `arquivo:linha` e foi confirmado no código.
O README serviu de pista, não de verdade: há afirmações que o código não sustenta (ex.: "adicione em GameProcesses (Settings)" não tem campo equivalente em Settings).

## B01 — Não há busca nas configurações
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** digitar "atalho", "tema" ou "cidade" e chegar direto à opção.
- **Hoje:** a janela é um `StackPanel` fixo com nove cartões dentro de um `ScrollViewer`, sem campo de busca (`src/Island.App/Shell/SettingsWindow.xaml:320-323`, cartões `:326-719`).
- **Por que é básico:** há cerca de duas dúzias de controles, e parte dos rótulos está em inglês, então o usuário precisa varrer a janela inteira.
- **Impacto:** quem procura uma opção desiste ou vai editar `settings.json` na mão.
- **Mínimo viável:** caixa de busca no topo filtrando rótulos/descrições e rolando até o cartão correspondente.

## B02 — Sem tela "Sobre" e a versão instalada não aparece em lugar nenhum
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver a versão em execução, o link do projeto e as licenças.
- **Hoje:** os menus da ilha (`src/Island.App/Shell/IslandContextMenu.xaml:75-200`), da bandeja (`src/Island.App/Shell/TrayIconService.cs:38-51`) e de Settings (`src/Island.App/Shell/SettingsWindow.xaml:314-736`) não exibem a versão em execução; `RunningVersion` só é usado internamente pelo update (`src/Island.Windows/Updates/GitHubUpdateService.cs:79-85`).
- **Por que é básico:** o app se atualiza sozinho, então o usuário precisa confirmar em que build está e conseguir reportar um problema.
- **Impacto:** suporte às cegas e desconfiança sobre se a atualização aplicou.
- **Mínimo viável:** linha "Versão x.y.z" no rodapé de Settings com botão "Ver release" e, se possível, item "Sobre" no menu da ilha/bandeja.

## B03 — Nenhuma ajuda, tutorial ou experiência de primeira execução
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** na primeira vez, aprender que clique abre a estante, botão direito abre o menu, roda troca de linha, segurar arrasta e bandeja pausa.
- **Hoje:** não há tutorial, boas-vindas nem "Ajuda"; os gestos só existem no código (segurar 350 ms para arrastar, `src/Island.App/Shell/IslandWindow.xaml.cs:41-48,738-753`; roda troca de página, `:417-456`) e nada é mostrado na tela.
- **Por que é básico:** o produto depende de gestos que não são visíveis nem convencionais.
- **Impacto:** o usuário não descobre a estante, a personalização, o dock lateral nem o mini; parece um retângulo preto inútil.
- **Mínimo viável:** cartão único de 3–4 dicas na primeira execução e um item "Ajuda" repetindo esse texto.

## B04 — Atalhos globais fixos, sem lista e sem remapeamento
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** uma tela com todos os atalhos, o estado de registro e a possibilidade de trocar as teclas.
- **Hoje:** as teclas estão no código: Ctrl+Alt+N (`src/Island.App/App.xaml.cs:120`), Ctrl+Alt+V (`:327`), Ctrl+Alt+P/R (`src/Island.App/Shell/CaptureHotkeys.cs:13-14`), Ctrl+Alt+G (`src/Island.App/Shell/GameNotesHotkeyController.cs:38`). Só o N tem interruptor (`src/Island.App/Shell/SettingsWindow.xaml:589-610`); não há lista, captura de tecla nem aviso central de conflito — o conflito do V só vai para o log (`src/Island.App/App.xaml.cs:327-330`).
- **Por que é básico:** atalhos fixos colidem com outros apps, e trocar tecla é padrão em utilitário Windows.
- **Impacto:** atalho morto sem o usuário saber; nota rápida/captura ficam inacessíveis.
- **Mínimo viável:** tabela só-leitura com "registrado/em conflito" e botão "Alterar" capturando a combinação para N/V/P/R/G.

## B05 — A lista de jogos (`GameProcesses`) não tem campo em Settings
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** adicionar/remover jogos em que a ilha deve se esconder (e onde as notas do jogo devem casar).
- **Hoje:** `GameProcesses` existe só no JSON (`src/Island.Core/Configuration/IslandSettings.cs:33-34`) e é lido direto das configurações (`src/Island.App/Composition/ServiceRegistration.cs:101,137`); não há nenhum controle em `SettingsWindow.xaml` e o README manda "adicione em GameProcesses (Settings)".
- **Por que é básico:** é a configuração que evita a ilha por cima de jogo em janela comum.
- **Impacto:** o usuário precisa editar JSON manualmente; jogo fora de tela cheia nunca é reconhecido.
- **Mínimo viável:** cartão "Jogos" com lista, "Adicionar processo" (nome ou .exe) e remover.

## B06 — "Reset to defaults" apaga tudo sem confirmação e sem desfazer
- **Severidade:** Crítica
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar (ou poder desfazer) uma ação que apaga todas as preferências.
- **Hoje:** o botão dispara o comando direto (`src/Island.App/Shell/SettingsWindow.xaml:730-733`; `src/Island.App/ViewModels/SettingsViewModel.cs:72,410-437`), sem diálogo e sem undo.
- **Por que é básico:** toda ação destrutiva de um clique em uma janela com Esc/scroll pede confirmação.
- **Impacto:** configuração perdida por clique acidental e o usuário refaz tudo.
- **Mínimo viável:** `MessageBox` "Restaurar padrões? Isso não altera token do celular" e um toast "Configurações restauradas".

## B07 — O reset é parcial e não devolve a estante ao padrão
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** que "padrões" signifique o estado de primeira execução.
- **Hoje:** o reset enumera só parte dos campos (`src/Island.App/ViewModels/SettingsViewModel.cs:414-434`): mantém `ShelfWidgets`/`ShelfRows`, `ClipboardEnabled`/`ClipboardMaxItems`, `NoticeSeconds`, `GameProcesses`, `Minimized`, tempos/perfis do Pomodoro e `UpdateRepository`.
- **Por que é básico:** o botão promete "defaults" e o usuário usa isso para consertar um layout quebrado.
- **Impacto:** "resetar" não resolve estante errada nem preferências de clipboard/avisos.
- **Mínimo viável:** resetar o record inteiro, preservando apenas credenciais, e listar no diálogo o que será mantido.

## B08 — Não é possível exportar nem importar as configurações
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fazer backup e migrar preferências para outro PC/perfil.
- **Hoje:** não existe `OpenFileDialog`/`SaveFileDialog` no app (grep sem ocorrência) e o rodapé de Settings tem só "Reset to defaults" (`src/Island.App/Shell/SettingsWindow.xaml:725-735`).
- **Por que é básico:** o app concentra dezenas de preferências e várias só existem no JSON.
- **Impacto:** trocar de máquina/perfil vira edição manual de arquivos; reinstalação perde tudo.
- **Mínimo viável:** botões "Exportar…" e "Importar…" de um JSON, com validação e confirmação antes de aplicar.

## B09 — Falha ao salvar/ler as configurações é silenciosa
- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber que a alteração não foi gravada (ou que a configuração anterior foi descartada).
- **Hoje:** `SettingsApplier.Apply` só registra o erro no log (`src/Island.App/Composition/ServiceRegistration.cs:60`); JSON corrompido é movido para `.bad` e o app abre com padrões sem avisar (`src/Island.Windows/Configuration/JsonSettingsStore.cs:75-79,104-108`), e as gravações ficam bloqueadas sem feedback quando houve erro de leitura (`:97`).
- **Por que é básico:** a configuração é o contrato de memória do app; perder em silêncio é inaceitável.
- **Impacto:** o usuário ajusta tudo, reinicia e "desconfigura" sem entender por quê.
- **Mínimo viável:** banner de erro em Settings com "Tentar de novo" e "Abrir pasta de dados", e aviso quando um arquivo corrompido foi posto de lado.

## B10 — "Start with Windows" não tem feedback, estado real nem modo discreto
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** ligar/desligar com confiança e o app iniciar sem incomodar.
- **Hoje:** o toggle grava no Run do registro (`src/Island.App/Composition/ServiceRegistration.cs:61-65`; `src/Island.Windows/Shell/OverlayWindowService.cs:37-72`), mas o estado exibido vem do `settings.json` — `StartupRegistration.IsEnabled` nunca é chamado —, falhas só vão para o log (`:63-64`) e não há "iniciar minimizado/atrasado".
- **Por que é básico:** autostart é uma das primeiras opções que o usuário mexe, e o Windows pode remover a entrada pelo Gerenciador de Tarefas.
- **Impacto:** o app deixa de abrir no boot (ou abre atrapalhando) e o switch continua "ligado".
- **Mínimo viável:** reconciliar com o registro ao abrir, mostrar erro no toggle e oferecer "iniciar como Mini".

## B11 — Sem tema claro, sem seguir o tema do Windows e sem contraste
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** a janela de Settings e os painéis acompanharem o modo claro/escuro (ou uma escolha explícita).
- **Hoje:** a paleta é fixa escura (`src/Island.App/Styles/Colors.xaml:2-25`) e Settings tem fundo `#121212` fixo (`src/Island.App/Shell/SettingsWindow.xaml:10-11,20-29`); não há campo de tema em `IslandSettings` (`src/Island.Core/Configuration/IslandSettings.cs:11-75`).
- **Por que é básico:** é uma janela de configuração do Windows, não só a pílula preta da ilha.
- **Impacto:** usuários de tema claro e de alto contraste ficam com uma janela ilegível/estranha.
- **Mínimo viável:** opções "Seguir Windows / Escuro / Claro" trocando os brushes por `ResourceDictionary`.

## B12 — Sem escala de fonte ou zoom da interface
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** aumentar texto e controles para enxergar melhor ou usar monitor grande.
- **Hoje:** os tamanhos são fixos nos estilos (`src/Island.App/Styles/Typography.xaml:8-39`, `SettingsWindow.xaml:13`) e a janela é 460x560 com `ResizeMode="NoResize"` (`src/Island.App/Shell/SettingsWindow.xaml:5-7`); não existe preferência de escala em `IslandSettings`.
- **Por que é básico:** baixa visão e monitores grandes são requisitos de app desktop maduro, e hoje a única saída é o zoom do Windows.
- **Impacto:** usuários com baixa visão não conseguem aumentar texto/controles; em telas grandes a interface fica pequena demais.
- **Mínimo viável:** seletor "Tamanho do texto: 100/125/150%" aplicado ao FontSize base e à largura da cápsula.

## B13 — A interface mistura inglês e português no mesmo fluxo
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** uma língua consistente do começo ao fim.
- **Hoje:** Settings mistura "POSITION/SIZE/TIMING/BEHAVIOR/UPDATES" (`src/Island.App/Shell/SettingsWindow.xaml:328,365,402,439,703`) com "SONS DO POMODORO/NOTAS RÁPIDAS/CÁPSULA OCIOSA" (`:529,591,615`); o menu da ilha é 100% inglês (`src/Island.App/Shell/IslandContextMenu.xaml:75,90,105,121,139,155,170,187`) e o clipboard mostra "Nothing copied yet", "Clear history" e "Delete" (`src/Island.App/Views/ClipboardView.xaml:60,78`; `src/Island.App/Views/ClipboardCard.cs:91`).
- **Por que é básico:** a primeira impressão do produto é a mistura de idiomas.
- **Impacto:** parece inacabado; o usuário não acha "Configurações" em um menu em inglês.
- **Mínimo viável:** padronizar o app em PT-BR (ou EN, se for o alvo) incluindo menu da ilha, clipboard e seções de Settings.

## B14 — Não há seletor de idioma nem respeito ao formato regional
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** idioma escolhível e hora/data no formato do Windows.
- **Hoje:** não há recursos de localização (`*.resx` inexistentes; textos inline em XAML/C#) nem opção de idioma; horários são sempre "HH:mm" (`src/Island.App/ViewModels/IdlePresenter.cs:132`; `src/Island.App/Shell/ScheduleWindow.xaml.cs:365-367`) e datas "dd/MM" (`ScheduleWindow.xaml.cs:139`), mesmo com Windows em en-US/12 h.
- **Por que é básico:** app de sistema deve respeitar o locale do usuário.
- **Impacto:** quem usa 12 h vê 24 h; um estrangeiro não entende nada além dos ícones.
- **Mínimo viável:** strings em recursos PT/EN e formatar hora/data com `CultureInfo.CurrentCulture`.

## B15 — A pausa é só um interruptor, sem duração, lembrete, atalho ou memória
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** "Pausar 1 h" (ou até amanhã), ver quanto falta, ser lembrado ao voltar e conseguir pausar pelo teclado; saber que está pausado.
- **Hoje:** a pausa é um booleano de execução, não persistido (`src/Island.Core/Models/IslandEvent.cs:38-39`; `src/Island.Core/Application/IslandStateReducer.cs:28-29,106-108`), só existe no menu da bandeja (`src/Island.App/Shell/TrayIconService.cs:41-42,117-127`) e simplesmente esconde a ilha (`src/Island.App/Shell/IslandWindow.xaml.cs:396-412`) sem aviso, duração ou retomada.
- **Por que é básico:** overlay sempre no topo incomoda em call/jogo e a alternativa atual é fechar o app.
- **Impacto:** pausa esquecida (app "sumido") ou Quit no lugar de pausa; sem atalho, é preciso mirar no ícone da bandeja.
- **Mínimo viável:** "Pausar 30 min / 1 h / até amanhã" no menu, tooltip da bandeja e toast "Pausa terminou".

## B16 — Não dá para desligar avisos por tipo (Caps Lock, dispositivos, etc.)
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher quais avisos aparecem — Caps Lock, USB/Bluetooth, pomodoro, captura.
- **Hoje:** as fontes de aviso são registradas e iniciadas sempre (`src/Island.App/App.xaml.cs:171-178`); Caps Lock avisa a cada alternância (`src/Island.Windows/Input/WindowsCapsLockNoticeSource.cs:103-105`) e dispositivos a cada conexão/desconexão (`src/Island.Windows/Devices/WindowsDeviceNoticeSource.cs:178-203`). Só volume e mídia têm toggle (`src/Island.App/Shell/SettingsWindow.xaml:492-522`).
- **Por que é básico:** aviso de Caps Lock/dispositivo a cada tecla é o motivo mais comum para desligar um overlay.
- **Impacto:** o usuário pausa ou fecha o app para parar os avisos e perde as funções boas.
- **Mínimo viável:** cartão "Avisos" com switches por tipo, incluindo "Silenciar todos" temporário.

## B17 — A duração do aviso não é configurável na interface
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** ajustar quanto tempo o aviso fica na tela.
- **Hoje:** `NoticeSeconds` existe (`src/Island.Core/Configuration/IslandSettings.cs:62`) e é usado pelo reducer (`src/Island.Core/Application/IslandStateReducer.cs:174-182`), mas o cartão TIMING de Settings só tem volume e mídia (`src/Island.App/Shell/SettingsWindow.xaml:399-434`).
- **Por que é básico:** 3 s pode ser pouco para ler um subtítulo e demais para quem trabalha.
- **Impacto:** usuário perde avisos ou se irrita, sem conseguir mudar pela UI.
- **Mínimo viável:** slider "Duração do aviso" de 1–10 s no cartão TIMING, com preview.

## B18 — Avisos têm um único slot: sem histórico e sem fila
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** rever o aviso que já passou (print salvo, update, dispositivo) e saber que há mais na fila.
- **Hoje:** o estado guarda um único `Notice` (`src/Island.Core/Models/IslandState.cs:4-9`) e o novo sobrescreve o anterior pela prioridade (`src/Island.Core/Application/EventPriorityPolicy.cs:27-30`); não há histórico nem contador (os dispositivos até enfileiram para despacho, `src/Island.Windows/Devices/WindowsDeviceNoticeSource.cs:178-228`, mas chegam quando outro estado está na frente).
- **Por que é básico:** overlay erra avisos e o usuário deve poder recuperá-los.
- **Impacto:** perde "update disponível", "print salvo" e "celular bloqueado" sem chance de reler.
- **Mínimo viável:** pilha de até 5 avisos com indicador "+N" e lista "Avisos recentes" em Settings.

## B19 — Nenhum aviso tem som
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** um toque curto opcional para avisos, e poder silenciar.
- **Hoje:** só existem os sons do Pomodoro (`src/Island.App/Shell/SettingsWindow.xaml:527-586`) e o `Notice` não tem som/canal (`src/Island.Core/Models/Notice.cs:7`); as fontes de sistema emitem sem áudio (`src/Island.App/App.xaml.cs:368-369`).
- **Por que é básico:** notificação sem som passa despercebida em tela cheia/segundo monitor.
- **Impacto:** eventos como dispositivo e captura passam em branco.
- **Mínimo viável:** switch "Som nos avisos" reutilizando o player do Pomodoro, com 2–3 tons curtos.

## B20 — "Sempre no topo" é obrigatório e não há comportamento de sobreposição
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher se a ilha fica sempre em cima, desce em segundo plano ou aparece só em alguns apps.
- **Hoje:** a janela nasce `Topmost="True"` (`src/Island.App/Shell/IslandWindow.xaml:12`) e o serviço reafirma `HWND_TOPMOST` (`src/Island.Windows/Shell/OverlayWindowService.cs:24,29-34`); não há opção em `IslandSettings`.
- **Por que é básico:** overlay sempre no topo conflita com apresentação, videochamada e apps de tela cheia sem borda.
- **Impacto:** a ilha cobre conteúdo e a única saída é pausar ou sair.
- **Mínimo viável:** "Sempre no topo" (padrão ligado) com modo "descer quando janela maximizada estiver em foco".

## B21 — A ilha não se esconde quando outra janela cobre a área
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** a ilha sair da frente quando uma janela maximizada, vídeo ou jogo em janela ocupa o topo da tela.
- **Hoje:** a suspensão automática só olha tela cheia e a lista de jogos (`src/Island.Core/Application/IslandStateReducer.cs:28-29`; `src/Island.Windows/Display/FullscreenDetector.cs:102-124`), e uma janela maximizada é explicitamente classificada como "não fullscreen" (`:119-121`); não há checagem de sobreposição/ordem Z.
- **Por que é básico:** a cápsula fica exatamente sobre barras de título/abas de apps maximizados.
- **Impacto:** atrapalha o trabalho e o usuário pausa o app.
- **Mínimo viável:** opção "Ocultar quando houver janela maximizada em primeiro plano" usando o mesmo detector com `WS_MAXIMIZE` + cobertura.

## B22 — O modo Mini é uma cápsula vazia, persistida, sem dica de saída
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** entender o que é o Mini e como voltar ao normal.
- **Hoje:** o Mini é um `Grid` vazio (`src/Island.App/Shell/IslandWindow.xaml:38-39`), sem ícone/tooltip; sai-se com um clique (`src/Island.App/Shell/IslandWindow.xaml.cs:466-471`) e o estado é persistido (`Minimized`, `src/Island.Core/Configuration/IslandSettings.cs:28`; `src/Island.App/App.xaml.cs:158-168`), então o app pode já abrir "sumido".
- **Por que é básico:** o usuário precisa descobrir o clique por tentativa e pode achar que o app quebrou.
- **Impacto:** suporte desnecessário e usuários que abandonam o app após o reinício.
- **Mínimo viável:** ponto/ícone sutil no Mini, dica "clique para expandir" no primeiro uso e opção "iniciar como Mini".

## B23 — Arrastar a ilha exige segurar 350 ms e não há pista visual
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** arrastar a ilha diretamente, como qualquer janela, ou ver uma alça.
- **Hoje:** o arraste exige pressionar e manter parado 350 ms (`HoldDuration`/`HoldSlop`, `src/Island.App/Shell/IslandWindow.xaml.cs:41-48,667-676,738-753`); a única pista é a opacidade cair durante o arraste (`:772`), e não há alça/cursor.
- **Por que é básico:** mover janela é gesto universal; long-press escondido é invenção.
- **Impacto:** o usuário não reposiciona a ilha nem descobre o encaixe lateral.
- **Mínimo viável:** alça visível no hover (mantendo o hold como alternativa) ou cartão de dica na primeira execução.

## B24 — Não existe "travar posição" da ilha
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** travar a ilha no lugar para não movê-la sem querer.
- **Hoje:** não há flag de trava; qualquer arraste grava `UseCustomPosition`/`PositionX`/`PositionY` (`src/Island.App/Shell/IslandWindow.xaml.cs:834-841`) e o menu só oferece "Reset position" (`src/Island.App/Shell/IslandContextMenu.xaml:121-134`).
- **Por que é básico:** quem usa em produção/apresentação precisa de posição fixa garantida.
- **Impacto:** toque acidental em touchpad move a ilha e o usuário "perde" o app.
- **Mínimo viável:** item "Travar posição" no menu da ilha, com "Desbloquear" no mesmo lugar.

## B25 — Existe uma única posição global, não uma por monitor
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** cada monitor com sua posição e seu encaixe lembrados.
- **Hoje:** há uma só posição (`UseCustomPosition`, `PositionX`, `PositionY`, `Dock` em `src/Island.Core/Configuration/IslandSettings.cs:23-32`), e `MonitorIndex` só é consultado quando não há posição personalizada (`src/Island.App/Shell/IslandWindow.xaml.cs:559-577`); arrastar para o monitor 2 substitui a posição usada no 1.
- **Por que é básico:** setups de 2–3 monitores são comuns em 2026.
- **Impacto:** toda troca de monitor exige re-arrastar; dock esquerda/direita se perde.
- **Mínimo viável:** mapa monitor→(X, Y, dock) com fallback para o primário quando o monitor não existir.

## B26 — O monitor é salvo por índice; hot-plug e reboot podem trocar a ilha de tela
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** a ilha voltar sempre à mesma tela física.
- **Hoje:** `MonitorIndex` é a posição na ordem de enumeração (`src/Island.Core/Configuration/IslandSettings.cs:13-14`; `src/Island.Windows/Display/MonitorService.cs:17-58`, com primário primeiro e o resto na ordem do sistema) e os nomes são "Monitor N" (`src/Island.App/App.xaml.cs:529-531`).
- **Por que é básico:** identificar o monitor por identidade é o mínimo em multi-monitor.
- **Impacto:** dock/undock ou reboot mudam a tela da ilha e o usuário reconfigura.
- **Mínimo viável:** salvar o nome do dispositivo (`MONITORINFOEX.szDevice`) e resolver por identidade, com fallback para o primário.

## B27 — O seletor "Display" não faz nada quando a ilha foi arrastada
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** trocar a tela no combo e ver a ilha mudar na hora.
- **Hoje:** com posição personalizada (qualquer arraste) `Reposition` retorna antes de ler `MonitorIndex` (`src/Island.App/Shell/IslandWindow.xaml.cs:559-577`), mas o combo continua habilitado (`src/Island.App/Shell/SettingsWindow.xaml:335-341`) e sem aviso.
- **Por que é básico:** controle que não responde é pior do que controle ausente.
- **Impacto:** o usuário acha o seletor quebrado e não sabe que precisa resetar posição.
- **Mínimo viável:** ao trocar Display, limpar a posição personalizada e reposicionar; ou desabilitar o combo com texto "Volte a ilha ao centro para escolher a tela".

## B28 — O encaixe só existe nas laterais
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** encaixar em qualquer borda/canto, como os painéis do Windows.
- **Hoje:** `DockEdge` só tem None/Left/Right (`src/Island.Core/Configuration/IslandSettings.cs:3-9`) e o detector testa apenas as duas bordas laterais (`src/Island.App/Shell/IslandWindow.xaml.cs:618-629`); a margem do encaixe é fixa em 8 DIP (`:75`).
- **Por que é básico:** barra de tarefas no topo/inferior e monitores em retrato mudam a melhor borda.
- **Impacto:** quem usa a ilha embaixo não tem opção; o encaixe lateral é a única alternativa.
- **Mínimo viável:** `DockEdge.Bottom` + margem configurável no cartão POSITION.

## B29 — Não há atalho global para abrir a estante (ou mostrar/ocultar a ilha)
- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** invocar a ilha pelo teclado, sem mirar numa cápsula de 124 px.
- **Hoje:** existem atalhos só para clipboard (V, `src/Island.App/App.xaml.cs:327`), captura (P/R, `src/Island.App/Shell/CaptureHotkeys.cs:13-14`), notas (N, `App.xaml.cs:120`) e notas do jogo (G, `src/Island.App/Shell/GameNotesHotkeyController.cs:38`); não há tecla para expandir a estante nem para mostrar/ocultar a ilha (nenhum outro `new GlobalHotkey`).
- **Por que é básico:** utilitário de produtividade é usado pelo teclado; mouse em overlay é o caminho mais lento.
- **Impacto:** usuários de teclado não abrem a estante; o fluxo é interrompido.
- **Mínimo viável:** Ctrl+Alt+D (configurável) alternando estante, registrado como os demais e com estado de conflito visível.

## B30 — Esc não fecha a estante, o clipboard nem a personalização
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** Esc fecha o painel aberto, como em qualquer overlay.
- **Hoje:** estante e clipboard fecham só com clique fora (`OutsideClickWatcher`, `src/Island.App/Shell/IslandWindow.xaml.cs:522-536`); o Esc global só é capturado na tela de transição do Pomodoro (`EscapeKeyWatcher` usado em `src/Island.App/Shell/PomodoroTransitionWindow.xaml.cs:26,91`), e a ilha não ativa (`src/Island.App/Shell/IslandWindow.xaml:14-15`), então o Esc vai para o app anterior.
- **Por que é básico:** Esc é o "fechar" universal de painéis.
- **Impacto:** o usuário tenta Esc, nada acontece e a ilha fica aberta por cima do trabalho.
- **Mínimo viável:** armar o `EscapeKeyWatcher` também em Expanded/Clipboard/Customize e fechar no Esc.

## B31 — Abrir o app uma segunda vez não faz nada e não avisa
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** trazer Settings/estante para frente, ou pelo menos "já está em execução".
- **Hoje:** a segunda instância falha no mutex e chama `Shutdown()` em silêncio, sem mensagem nem IPC (`src/Island.App/App.xaml.cs:80-84`; `src/Island.Windows/Shell/OverlayWindowService.cs:89-97`).
- **Por que é básico:** o usuário abre pelo menu Iniciar quando "não vê" a ilha.
- **Impacto:** acha que o app não abre; clica várias vezes ou reinstala.
- **Mínimo viável:** evento nomeado/IPC mínimo: a instância viva abre Settings (ou a estante) quando recebe um segundo lançamento.

## B32 — Personalizar a estante só é possível pelo clique direito na ilha
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** encontrar "editar estante" na própria estante.
- **Hoje:** só o menu de contexto oferece "Customize Shelf" (`src/Island.App/Shell/IslandContextMenu.xaml:75-88`), e a estante não tem botão de editar/engrenagem (`src/Island.App/Views/ShelfView.xaml:1-116`); o modo de edição tem check/cancelar (`:41-77`), mas não explica arrastar/remover.
- **Por que é básico:** personalização é o recurso central da estante e fica atrás de um clique direito invisível.
- **Impacto:** o usuário não descobre os 16 widgets do catálogo e acha a estante fixa.
- **Mínimo viável:** botão "Editar" discreto na estante e dica de primeira vez "clique direito para personalizar".

## B33 — A troca de linha da estante só funciona pela roda do mouse
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** setas ou pontos clicáveis e indicação de que existem mais linhas.
- **Hoje:** a troca de linha é só pela roda, com debounce de 400 ms (`src/Island.App/Shell/IslandWindow.xaml.cs:417-456`); os pontos existem, mas são `IsHitTestVisible="False"` (`src/Island.App/Views/ShelfView.xaml:107-114`) e só aparecem com mais de uma linha.
- **Por que é básico:** trackpad em modo de rolagem horizontal/touch e mouses sem roda ficam sem navegação.
- **Impacto:** os widgets da segunda linha são praticamente inacessíveis.
- **Mínimo viável:** setas ‹ › clicáveis ao lado dos pontos e suporte a gesto horizontal.

## B34 — Widgets que não cabem na linha somem sem aviso
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** saber que a linha atingiu o limite e que um widget salvo não está sendo mostrado.
- **Hoje:** a linha é limitada a 1080 DIP e os excedentes são descartados em memória (`src/Island.App/Animations/IslandTransitions.cs:451-472`, aplicado em `src/Island.App/Views/ShelfView.xaml.cs:108,126,136`) sem mensagem; na edição, um widget que não cabe fica 40% opaco, cursor "No" e sem texto explicando (`ShelfView.xaml.cs:519,528-581`).
- **Por que é básico:** o que está salvo deveria ser o que aparece, ou o app deve dizer o porquê.
- **Impacto:** widget "desaparece" e o usuário não entende que precisa de outra linha.
- **Mínimo viável:** tooltip "Não cabe nesta linha — solte na linha 2" e aviso ao sair da edição quando houver widget oculto.

## B35 — File Tray perde os arquivos ao fechar o app
- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** os arquivos separados na estante continuem lá depois de fechar/reabrir (ou um aviso claro de que é temporário).
- **Hoje:** a bandeja é só memória (`FileTray`, "In-memory list", `src/Island.Core/Shelf/FileTray.cs:3-9`) e o botão "Limpar" apaga sem confirmação (`src/Island.App/Views/Widgets/FileTrayWidget.xaml.cs:57`); nada é persistido em `IslandSettings`.
- **Por que é básico:** bandeja é área de preparação de arquivos; perder no restart quebra o fluxo.
- **Impacto:** a seleção de arquivos preparada é perdida; um clique em "Limpar" apaga sem volta.
- **Mínimo viável:** persistir caminhos em JSON (validando existência) ou avisar "a bandeja é temporária"; confirmar "Limpar".

## B36 — Não há layouts/perfis salvos da estante
- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** salvar 2–3 arranjos (trabalho, jogo, estudo) e alternar.
- **Hoje:** existe um único layout (`ShelfWidgets`/`ShelfRows`, `src/Island.Core/Configuration/IslandSettings.cs:42-44`) editado no Customize; não há perfis nomeados e o "Reset to defaults" não restaura o layout (`src/Island.App/ViewModels/SettingsViewModel.cs:414-434`).
- **Por que é básico:** com 16 widgets, o arranjo útil muda por atividade.
- **Impacto:** re-arrastar widgets toda vez desestimula o uso da estante.
- **Mínimo viável:** "Salvar layout atual como…" + combo para aplicar, guardado no JSON.

## B37 — A busca do clipboard é um botão decorativo
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** digitar e filtrar os itens do histórico.
- **Hoje:** o botão de lupa é explicitamente decorativo (comentário "search and square are decorative", `src/Island.App/Views/ClipboardView.xaml:13-21`) e não há `TextBox`/handler de busca; o painel só lista os mais recentes (`src/Island.App/Views/ClipboardView.xaml.cs:118-162`).
- **Por que é básico:** o histórico chega a 50 itens; sem busca ele fica inútil.
- **Impacto:** o usuário rola às cegas e desiste de usar o clipboard do app.
- **Mínimo viável:** campo de busca no topo filtrando por texto (case-insensitive), com Esc limpando.

## B38 — "Clear history" apaga tudo sem confirmação
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar ou desfazer a limpeza de todo o histórico.
- **Hoje:** o botão limpa na hora, sem diálogo (`src/Island.App/Views/ClipboardView.xaml.cs:35-39`; menu inline em `src/Island.App/Views/ClipboardView.xaml:67-79`).
- **Por que é básico:** ação destrutiva sobre dado útil, a um clique de distância.
- **Impacto:** perda acidental de todo o histórico, sem undo.
- **Mínimo viável:** confirmação ou "Desfazer" por 5 s no lugar do painel.

## B39 — Clipboard sem favoritos/pin
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fixar itens frequentes (e-mail, trecho, endereço) para não sumirem.
- **Hoje:** `ClipboardItem` só tem Id/Kind/Text/Image/At (`src/Island.Core/Clipboard/ClipboardItem.cs:6`) e o histórico descarta o mais antigo ao passar do limite (`src/Island.Core/Clipboard/ClipboardHistory.cs:44-49`); não existe pin/ordem manual.
- **Por que é básico:** favoritar é o que transforma histórico em ferramenta.
- **Impacto:** itens frequentes somem com o uso e o usuário volta a digitar tudo.
- **Mínimo viável:** botão "Fixar" no card; fixos não entram no limite e ficam à esquerda.

## B40 — Pausar a captura e ajustar o limite do clipboard não tem controle na UI
- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** parar de capturar (ex.: ao usar senha/banco) e escolher quantos itens guardar.
- **Hoje:** `ClipboardEnabled` e `ClipboardMaxItems` existem (`src/Island.Core/Configuration/IslandSettings.cs:58-59`), mas não há controle em Settings; `App.xaml.cs:314` só lê o bool e o menu da ilha apenas esconde "Open Clipboard" (`src/Island.App/Shell/IslandWindow.xaml.cs:485`).
- **Por que é básico:** privacidade e uso de memória; 50 itens fixos não servem para todos.
- **Impacto:** o usuário não consegue pausar/desligar a captura sem editar JSON e copia conteúdo sensível.
- **Mínimo viável:** cartão "Clipboard" com switch "Capturar" e "Itens a manter (10–200)", aplicando na hora.

## B41 — Sem lista de apps excluídos da captura
- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** excluir apps específicos (banco, gerenciador de senhas, chat privado) do histórico.
- **Hoje:** só são ignorados os formatos de privacidade do Windows e o conteúdo copiado de dentro do próprio app (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:241-256`); não há lista configurável de processos/janelas.
- **Por que é básico:** exclusão por app é recurso padrão de clipboard manager.
- **Impacto:** conteúdo sensível entra no histórico e o usuário desliga a feature inteira.
- **Mínimo viável:** lista "Apps ignorados" (nome do processo) checada no dono do clipboard/janela em foco.

## B42 — O histórico do clipboard é só da sessão
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** o histórico sobreviva a reinícios ou ao menos ser avisado de que é temporário.
- **Hoje:** `ClipboardHistory` é explicitamente "In-memory only (never persisted, for privacy)" (`src/Island.Core/Clipboard/ClipboardHistory.cs:6`), sem opção de retenção e sem aviso ao fechar o app.
- **Por que é básico:** o valor do histórico é a última hora/semana, não a sessão atual.
- **Impacto:** o recurso perde valor e o usuário não sabe que o que copiou já não está mais lá.
- **Mínimo viável:** opção "Salvar por N horas" (arquivo protegido por DPAPI, limpeza automática), desligada por padrão.

## B43 — Escolher um item copia, mas não cola no app em foco
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** escolher o item e ele ser colado no app de onde veio (ou ao menos Enter/Ctrl+V).
- **Hoje:** o card só copia para o clipboard (`copy` em `src/Island.App/Views/ClipboardView.xaml.cs:171-186`; clique em `src/Island.App/Views/ClipboardCard.cs:107-108`), e o usuário precisa voltar e apertar Ctrl+V; a ilha não ativa (`src/Island.App/Shell/IslandWindow.xaml:14-15`) e não há navegação por teclado; o app não simula colagem em lugar nenhum (grep sem `SendInput`).
- **Por que é básico:** o fluxo "abrir clipboard → escolher → colar" deve ser um passo.
- **Impacto:** dois passos extras a cada uso; o recurso perde para o Win+V do Windows.
- **Mínimo viável:** opção "colar ao escolher" (copia + `SendInput` Ctrl+V), com fallback quando o alvo não aceitar.

## B44 — Arquivos, HTML e rich text copiados são ignorados
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** copiar um arquivo no Explorer (ou texto formatado) e vê-lo no histórico.
- **Hoje:** o serviço só lê `CF_UNICODETEXT` e imagem DIB/BMP (`src/Island.Windows/Clipboard/WindowsClipboardService.cs:218-234,303-330`); listas de arquivos, HTML e RTF são descartados sem aviso.
- **Por que é básico:** copiar arquivos é uso diário no Windows.
- **Impacto:** boa parte dos "copiar" do dia não aparece e o app parece não funcionar.
- **Mínimo viável:** suporte a `CF_HDROP` como tipo "Arquivos" (lista de caminhos) e HTML com fallback para texto.

## B45 — O clima é sempre em °C
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher °C ou °F.
- **Hoje:** a cápsula sempre formata `{(int)Math.Round(snapshot.TemperatureC)}°C` (`src/Island.App/ViewModels/IdlePresenter.cs:122`) e o snapshot só guarda Celsius; não há preferência em Settings.
- **Por que é básico:** °F é o padrão de parte dos usuários (e do público de apps em inglês).
- **Impacto:** número sem sentido para quem usa °F; estranheza no primeiro uso.
- **Mínimo viável:** toggle °C/°F no cartão "Cápsula ociosa", convertendo na exibição e na descrição acessível.

## B46 — A localização por IP não tem consentimento nem indicação de origem
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber que o IP é enviado a um serviço e poder usar cidade manual sem geolocalização.
- **Hoje:** com cidade vazia o app consulta `ipwho.is` automaticamente (`src/Island.Windows/Weather/OpenMeteoWeatherService.cs:17,50-54`), e o único aviso é a descrição do campo (`src/Island.App/Shell/SettingsWindow.xaml:636-638`); não há opt-in, indicador de origem (IP vs cidade) nem "detectar minha localização".
- **Por que é básico:** privacidade e previsibilidade de rede são requisitos de utilitário maduro.
- **Impacto:** o usuário descobre o envio do IP depois; em VPN, a cidade fica errada sem explicação.
- **Mínimo viável:** "Localização: Automática (IP) / Manual", com aviso de privacidade no primeiro uso e o nome da cidade efetiva exibido.

## B47 — Falha de clima e cidade inválida não geram nenhum feedback
- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** "sem conexão" ou "cidade não encontrada" e como corrigir.
- **Hoje:** erros de rede e cidade desconhecida são engolidos (`src/Island.Core/Idle/WeatherRefresher.cs:138-142` diz que é "never an error the island shows"; `src/Island.Windows/Weather/OpenMeteoWeatherService.cs:59,64` só loga); a leitura velha apenas esmaece (`src/Island.App/ViewModels/IdlePresenter.cs:124-127`) e desaparece após 3 h (`src/Island.Core/Idle/WeatherRefreshPolicy.cs:17-22`).
- **Por que é básico:** sem mensagem o usuário conclui que a função quebrou.
- **Impacto:** não sabe que digitou a cidade errado nem que está sem rede; clima "sumiu".
- **Mínimo viável:** estado "Sem conexão — última leitura há X" e "Cidade não encontrada" no cartão de clima, com "Tentar de novo".

## B48 — Sem previsão horária, mín/máx ou detalhes do clima
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** mín/máx do dia ou próximas horas, como em qualquer widget de clima.
- **Hoje:** a consulta pede só `current` + `hourly=precipitation_probability` de 1 dia (`src/Island.Windows/Weather/OpenMeteoWeatherService.cs:74-75`), e a cápsula mostra a temperatura atual e a maior chance de chuva das 3 próximas horas (`src/Island.Windows/Weather/OpenMeteoParser.cs:12-13,49-59`).
- **Por que é básico:** a pergunta real ("vai chover? que casaco?") exige mais que um número.
- **Impacto:** informação pouco útil; o usuário abre o site ou outro app.
- **Mínimo viável:** tooltip/expansão com mín/máx e as próximas 3 h (mesma resposta, `daily=temperature_2m_min,max`).

## B49 — O download do update não mostra progresso nem permite cancelar
- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** percentual/barra durante o download e um botão cancelar.
- **Hoje:** `InstallAsync` aceita `IProgress<double>` (`src/Island.Core/Abstractions/IUpdateService.cs:20-22`) e o serviço reporta (`src/Island.Windows/Updates/GitHubUpdateService.cs:194-207`), mas o app chama sem progresso (`src/Island.App/App.xaml.cs:438-444`) e mostra só um toast fixo "Downloading {tag}"; nenhum `CancellationToken` é passado.
- **Por que é básico:** download de um pacote de vários MB em rede lenta, sem sinal, parece travado.
- **Impacto:** o usuário força o Quit no meio e a instalação fica pela metade.
- **Mínimo viável:** ligar o `Progress<double>` já existente em um toast com %/MB e botão Cancelar.

## B50 — Sem release notes antes de instalar a atualização
- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver o que muda e um link para a Release.
- **Hoje:** `UpdateInfo` carrega só Version/Tag/DownloadUrl/Sha256 (`src/Island.Core/Models/UpdateInfo.cs:4`), e `UpdateRules.Evaluate` lê `tag_name`, assets e digest, ignorando `body`/`html_url` (`src/Island.Windows/Updates/UpdateRules.cs:53-77`); o menu mostra apenas "Install update (vX)" (`src/Island.App/Shell/IslandContextMenu.xaml.cs:60-65`).
- **Por que é básico:** atualização sem changelog é um "sim" às cegas, ainda mais com auto-update.
- **Impacto:** o usuário adia updates por medo e perde correções.
- **Mínimo viável:** coletar `body`/`html_url` e mostrar 3–5 linhas + "Ver no GitHub" no toast/menu.

## B51 — Falha de update vira um aviso genérico em inglês, sem ação
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** entender o motivo e tentar de novo.
- **Hoje:** a falha mostra "Update failed / See the logs" (`src/Island.App/App.xaml.cs:447-451`), e o status de "Check for updates" também é em inglês e sem ação (`:499-512`; `src/Island.App/Shell/SettingsWindow.xaml:709-716`); não abre log nem oferece retry.
- **Por que é básico:** o auto-update é a única via de instalação (não há MSIX), então falhar bem importa.
- **Impacto:** o usuário fica preso em versão antiga sem saber o que fazer.
- **Mínimo viável:** mensagem em PT com motivo resumido + "Tentar de novo" e "Abrir update-install.log".

## B52 — Sem canal beta, sem rollback e sem "ignorar esta versão"
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** opção de testar pré-release, pular uma versão e voltar à anterior se algo quebrar.
- **Hoje:** o app consulta `releases/latest` (`src/Island.Windows/Updates/GitHubUpdateService.cs:127`), que ignora pré-releases; `TryParseTag` rejeita tag com sufixo (`src/Island.Windows/Updates/UpdateRules.cs:9-20`); não há "ignorar versão", canal nem download de versão anterior (`src/Island.App/Shell/IslandContextMenu.xaml:139-152` só oferece a mais nova).
- **Por que é básico:** com auto-update por zip, rollback é a rede de segurança — o README registra uma versão que quebrou o app (v0.4.12).
- **Impacto:** uma versão ruim exige reinstalação manual do zip antigo.
- **Mínimo viável:** "Ignorar esta versão", canal beta (lista `/releases` em vez de `/latest`) e link para versões anteriores.

## B53 — A verificação automática de updates não pode ser desligada
- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** controlar se e quando o app consulta o GitHub.
- **Hoje:** o serviço sempre inicia e consulta após 30 s e a cada 6 h (`StartupDelay`/`CheckInterval`, `src/Island.Windows/Updates/GitHubUpdateService.cs:20-21`; `updates.Start()` incondicional em `src/Island.App/App.xaml.cs:324`); Settings tem só "Check for updates" manual (`src/Island.App/Shell/SettingsWindow.xaml:700-719`).
- **Por que é básico:** tráfego de rede periódico é preferência padrão, e redes limitadas/corporativas podem restringir.
- **Impacto:** consultas indesejadas; alertas de rede; o usuário não controla nada.
- **Mínimo viável:** switch "Verificar automaticamente" + intervalo (diário/semanal) no cartão UPDATES.

## B54 — Uma atualização disponível não é anunciada na ilha
- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber que há atualização sem abrir o menu de contexto.
- **Hoje:** a checagem só guarda `_available` para o menu (`src/Island.Windows/Updates/GitHubUpdateService.cs:143-146`; `src/Island.App/Shell/IslandContextMenu.xaml.cs:59-65`) e registra em log; nenhum aviso/indicador é emitido quando uma Release nova aparece.
- **Por que é básico:** a maioria dos usuários nunca abre o clique direito da ilha, então o auto-update nunca acontece.
- **Impacto:** o app fica desatualizado indefinidamente, com correções sem chegar.
- **Mínimo viável:** aviso não urgente "Atualização vX disponível" (respeitando pausa/tela cheia) e ponto no ícone da bandeja.

## B55 — O menu da bandeja é mínimo e não abre o produto
- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** abrir a ilha/estante, ver estado, atualizar e sair pelo ícone.
- **Hoje:** o menu tem só "Settings…", "Pause island" e "Quit" (`src/Island.App/Shell/TrayIconService.cs:38-51`); clique simples não faz nada, duplo abre Settings (`:59`) e o tooltip é fixo "Dynamic Island" (`:56`), sem versão ou estado.
- **Por que é básico:** a bandeja é o ponto de entrada padrão de utilitários Windows.
- **Impacto:** o usuário procura o app na bandeja e só encontra configurações/sair; não vê pausado nem versão.
- **Mínimo viável:** "Abrir ilha", "Pausar por…", "Check for updates", "Sobre" e tooltip com versão/estado.

## B56 — Não é possível ocultar o ícone da bandeja
- **Severidade:** Baixa
- **Tipo:** Lacuna de configuração
- **Usuário espera:** esconder o ícone (ou movê-lo para a bandeja secundária) em setups minimalistas.
- **Hoje:** o `NotifyIcon` é criado com `Visible = true` e nunca é ocultável (`src/Island.App/Shell/TrayIconService.cs:53-60`); não há campo em `IslandSettings` nem controle em Settings.
- **Por que é básico:** "mostrar/ocultar ícone da bandeja" é convenção em apps de utilidade.
- **Impacto:** a única saída é a configuração de ícones da própria bandeja do Windows, que muda de versão para versão; o app não tem controle.
- **Mínimo viável:** switch "Mostrar ícone na bandeja", avisando que sem ele o único ponto de entrada visível passa a ser a própria ilha.

## B57 — A ilha não é operável por teclado nem por leitor de tela
- **Severidade:** Alta
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** abrir a estante e navegar com Tab/Enter, e ter os conteúdos anunciados.
- **Hoje:** a janela é `ShowActivated="False"` e `Focusable="False"` (`src/Island.App/Shell/IslandWindow.xaml:14-15`) e recebe `WS_EX_NOACTIVATE` (`src/Island.App/Shell/IslandWindow.xaml.cs:267-274`); a estante e os widgets são operados por mouse (`src/Island.App/Views/ShelfView.xaml.cs:646-660`), então não há foco, Tab nem navegação por teclado dentro da ilha.
- **Por que é básico:** acessibilidade e uso sem mouse são requisitos de app desktop maduro.
- **Impacto:** usuários de teclado/leitor de tela não conseguem usar nenhuma função da ilha.
- **Mínimo viável:** atalho "modo focado" que abre uma janela de estante acessível (Tab + AutomationProperties) ou navegação por teclado via hook global.

## B58 — Configurações removem o indicador de foco do teclado
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ver onde está o foco ao navegar com Tab.
- **Hoje:** os estilos custom de Settings zeram `FocusVisualStyle` em sliders, switches, combos, itens e botões (`src/Island.App/Shell/SettingsWindow.xaml:112,144,208,235,281`) e não há trigger de foco; o Tab percorre os controles, mas nada indica onde está.
- **Por que é básico:** sem foco visível, navegação por teclado é inutilizável (WCAG 2.4.7).
- **Impacto:** usuário de teclado se perde e pode alterar a configuração errada, inclusive o reset.
- **Mínimo viável:** anel/borda de 2 px no `IsKeyboardFocused` dos cinco estilos e do `ComboBoxItem`.

## B59 — Tiles do Customize e cards do clipboard não têm nome acessível
- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** o leitor de tela anunciar "Widget Pomodoro", "Copiar item", "Limpar histórico".
- **Hoje:** os tiles do Customize e os cards do clipboard são `Border`/`Grid` com eventos de mouse e sem `AutomationProperties.Name` (`src/Island.App/Views/ShelfView.xaml.cs:528-581`; `src/Island.App/Views/ClipboardCard.cs:50-123`), enquanto outros widgets do app já usam `AutomationProperties` (por exemplo `src/Island.App/Views/Widgets/CounterWidget.xaml:30-75`).
- **Por que é básico:** mesmo quando a estante está aberta, não há o que o leitor de tela anuncie.
- **Impacto:** a personalização e o clipboard são inacessíveis para leitor de tela.
- **Mínimo viável:** `AutomationProperties.Name` nos tiles/cards e nos botões de limpar/excluir, com `HelpText` explicando o gesto.
