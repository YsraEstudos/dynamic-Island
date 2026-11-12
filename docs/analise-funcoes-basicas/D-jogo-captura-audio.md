# Auditoria — Jogo, captura, mixer e desempenho

Escopo: widgets para jogo (Captura, Desempenho, Mixer e Notas do Jogo), backend em `src/Island.Core`/`src/Island.Windows` e UI em `src/Island.App`, comparados ao que ferramentas maduras de captura, overlay, áudio e monitoramento oferecem. Cada ausência foi lida no código e confirmada por grep antes de ser afirmada; limitações do README (`README.md:82-113`) foram conferidas na implementação. Total: 52 itens — 16 de captura, 12 de desempenho, 12 de mixer e 12 de notas do jogo; itens transversais (i18n, acessibilidade global, instalador, backup geral) ficaram fora.

## D01 — Captura só do monitor do app em primeiro plano, sem região ou janela

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** escolher uma região (killfeed, dano, mapa) ou a janela do jogo antes do print/vídeo.
- **Hoje:** o único recorte é `ScreenGrabber.TryGetForegroundMonitor` (`src/Island.Windows/Capture/ScreenGrabber.cs:17-35`) e o serviço grava sempre o monitor inteiro (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:72-79`); não existe overlay de seleção nem HWND alvo.
- **Por que é básico:** Snipping Tool, Xbox Game Bar, ShareX e OBS sempre perguntam o que capturar; jogador quer recorte e privacidade.
- **Impacto:** prints/vídeos com 80–90% de tela irrelevante, incluindo chat e notificações; impossível documentar um detalhe do jogo.
- **Mínimo viável:** overlay de seleção por arrasto e opção "janela ativa", reusando `ScreenGrabber.Grab` com um `ScreenRect` arbitrário.

## D02 — Sem cursor no print nem na gravação

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver o ponteiro no vídeo (ou poder ligar/desligar isso).
- **Hoje:** `DibFrame.CopyFromScreen` usa `StretchBlt` com `SRCCOPY|CAPTUREBLT` e não compõe o cursor (`src/Island.Windows/Capture/DibFrame.cs:59-68`); não há uso de `GetCursorInfo`/`DrawIcon` no código; `README.md:89` lista "sem cursor".
- **Por que é básico:** tutorial/gameplay sem cursor não mostra onde o jogador clica; OBS e Game Bar têm cursor por padrão.
- **Impacto:** gravação instrutiva inútil para demonstrar menus/builds; passo a passo sem o ponteiro.
- **Mínimo viável:** opção de desenhar o cursor com `GetCursorInfo`+`DrawIconEx` no frame, ou migrar para Windows.Graphics.Capture (inclui cursor).

## D03 — Gravação sem áudio do sistema e sem microfone

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** vídeo com som do jogo e/ou microfone, com escolha de fonte e mix.
- **Hoje:** `Mp4VideoWriter` cria um único stream H.264 (`src/Island.Windows/Capture/Mp4/Mp4VideoWriter.cs:36-55`) e não há captura WASAPI loopback/mic no projeto (grep sem `AudioClient`/`Loopback` fora do áudio de foco do Pomodoro).
- **Por que é básico:** clipe de jogo sem áudio perde o momento; qualquer gravador maduro captura sistema + mic.
- **Impacto:** vídeos precisam de pós-produção em outro app; memos de voz e reações do time ficam de fora.
- **Mínimo viável:** `WasapiLoopbackCapture` + stream de áudio (AAC) no Sink Writer e opção "sistema / microfone / ambos".

## D04 — Gravação em 30 fps, 1080p e bitrate fixos, sem configuração

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher fps (30/60), resolução (720p/1080p/nativa) e qualidade/bitrate, com a escolha salva.
- **Hoje:** `Fps = 30` (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:15`), teto 1920x1080 e bitrate calculado entre 2 e 12 Mbps (`src/Island.Core/Capture/CaptureSize.cs:6-7,21-22`); não há store nem UI de captura (grep de "Capture" em `src/Island.Core/Configuration` não retorna nada).
- **Por que é básico:** 60 fps e bitrate controlado são requisito mínimo de clip de gameplay; 1080p decepciona em monitor QHD/4K.
- **Impacto:** vídeo borrado em movimento e sem opção de arquivo leve; aparência de ferramenta "de brinquedo".
- **Mínimo viável:** três preferências (fps, altura máxima, Mbps) em JSON de captura, repassadas a `CaptureSize.Fit`/`BitrateFor`.

## D05 — Pasta e nome dos arquivos fixos; sem "abrir pasta"

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher pasta de destino, modelo de nome e abrir a pasta após salvar.
- **Hoje:** pasta fixa `%UserProfile%\Pictures\DynamicIsland\Capturas` (`src/Island.Windows/Capture/CaptureLibrary.cs:9-10`, injetada em `src/Island.App/Composition/ServiceRegistration.cs:177-180`); nomes fixos `Captura-*`/`Gravacao-*` (`src/Island.Core/Capture/CaptureNaming.cs:10-13`); o aviso "Pasta Capturas" não é clicável (`src/Island.Core/Capture/CaptureController.cs:108,170`).
- **Por que é básico:** quem grava clips tem pasta de acervo (SSD de jogos, OneDrive); a notificação deveria levar ao arquivo.
- **Impacto:** arquivos escondidos em Pictures; usuário não sabe onde estão nem como mudar; não há "Abrir pasta" no tray/settings.
- **Mínimo viável:** caminho configurável com "Restaurar padrão", prefixo opcional e ação "Abrir pasta de capturas".

## D06 — Sem galeria/histórico: só o último print é acessível e nenhum vídeo

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** ver as últimas capturas/vídeos, abrir, copiar ou apagar.
- **Hoje:** `CaptureController` guarda apenas `_lastScreenshot` e mantém o caminho do vídeo privado (`src/Island.Core/Capture/CaptureController.cs:26-27,80-87`); `CaptureLibrary.LatestScreenshot` lista só o PNG mais novo (`CaptureLibrary.cs:13-25`) e o widget mostra uma miniatura só (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:129`).
- **Por que é básico:** qualquer ferramenta tem histórico/bandeja de capturas; "gravei e quero rever/enviar" é uso diário.
- **Impacto:** o print seguinte substitui a referência; o vídeo fica "perdido" no Explorer e nem dá para conferir se saiu bom.
- **Mínimo viável:** grade das últimas N capturas (prints e vídeos) com abrir/copiar/apagar e botão "Abrir no Explorer".

## D07 — Copiar print é manual e só do último arquivo

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** print direto na área de transferência por atalho, ou opção "copiar ao salvar".
- **Hoje:** o print sempre grava PNG primeiro (`src/Island.Core/Capture/CaptureController.cs:90-110`); copiar exige abrir a estante e clicar "Copiar" (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:64-67,202-218`); o atalho global só salva (`README.md:86`).
- **Por que é básico:** colar print no Discord/chat é o destino mais comum; Snipping Tool e Game Bar fazem isso por padrão.
- **Impacto:** dois cliques e abrir a ilha para uma tarefa que deveria ser um atalho; se o arquivo sumir, o botão não faz nada.
- **Mínimo viável:** preferência/atalho "print para clipboard (salvar opcional)" e feedback de cópia no aviso.

## D08 — Gravação não pausa nem retoma

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** pausar (intervalo, loading) e continuar no mesmo arquivo.
- **Hoje:** a máquina de estados só tem Idle→Starting→Recording→Stopping (`src/Island.Core/Capture/RecordingStateMachine.cs:3-9`) e a interface só expõe iniciar/parar (`src/Island.Core/Abstractions/IScreenCaptureService.cs:15-22`).
- **Por que é básico:** pausar e retomar é controle padrão de gravador; evita cortar o vídeo em vários arquivos.
- **Impacto:** clips com intervalo precisam ser gravados inteiros e cortados depois, ou ficam em vários MP4s.
- **Mínimo viável:** fase `Paused` no estado e no loop do `ScreenRecorder` (não escrever frames, manter o timeline) com botão/atalho.

## D09 — Indicador de gravação só existe com a estante aberta no widget

- **Severidade:** Alta
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber que está gravando (e conseguir parar) com a ilha recolhida ou em outra tela.
- **Hoje:** o "Gravando" com ponto vermelho e cronômetro vive só dentro do `CaptureWidget` (`src/Island.App/Views/Widgets/CaptureWidget.xaml.cs:99-131`); o tray tem apenas Settings/Pause/Quit (`src/Island.App/Shell/TrayIconService.cs:38-51`); a cápsula não consome `RecordingPhase` (grep só acha uso no widget).
- **Por que é básico:** gravação "invisível" gera horas de vídeo acidental e esquecimento de parar; OBS/Game Bar mostram indicador persistente.
- **Impacto:** usuário esquece gravando; disco enche; risco de privacidade (capturou reunião sem querer).
- **Mínimo viável:** estado no tray ou item "Parar gravação" no menu + ponto vermelho na cápsula recolhida.

## D10 — Atalhos fixos (Ctrl+Alt+P/R) sem configuração nem alternativa

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** trocar/desativar atalhos, principalmente quando outro app (Discord, OBS, Steam) usa a combinação.
- **Hoje:** teclas constantes (`src/Island.App/Shell/CaptureHotkeys.cs:13-14`) e modificadores fixos `ModControl|ModAlt` (`src/Island.App/Composition/ServiceRegistration.cs:181-184`); o conflito só gera texto no widget (`CaptureWidget.xaml.cs:192-200`), sem remapear.
- **Por que é básico:** atalho global conflitante é comum; ferramenta madura permite gravar a combinação.
- **Impacto:** usuário com Ctrl+Alt+P/R ocupado fica sem captura global para sempre (só clicando na ilha).
- **Mínimo viável:** editor de atalhos em Settings com validação de conflito e combinação alternativa.

## D11 — Sem replay buffer (salvar os últimos N segundos)

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** "salvar os últimos 30 s" depois que algo aconteceu no jogo.
- **Hoje:** `StartRecordingAsync` abre o MP4 e grava desde o início (`src/Island.Windows/Capture/ScreenRecorder.cs:44-58`); não há buffer circular de frames nem gravação contínua descartável (grep por "replay" só acha um comentário de animação do widget de timer).
- **Por que é básico:** é o recurso-assinatura de game capture (Instant Replay do ShadowPlay/Game Bar); sem ele, o jogador precisa gravar a partida toda.
- **Impacto:** perde o clipe do momento; gravações longas e pesadas; dependência de software de terceiros.
- **Mínimo viável:** buffer circular de N segundos em arquivo temporário e atalho "Salvar últimos N s" com tamanho configurável.

## D12 — Tela cheia exclusiva (DirectX) sai preta

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** capturar o jogo rodando em tela cheia exclusiva.
- **Hoje:** a captura é GDI do desktop (`src/Island.Windows/Capture/ScreenGrabber.cs:10-13`) e o serviço assume a limitação no comentário de classe (`src/Island.Windows/Capture/WindowsScreenCaptureService.cs:7-12`); não há Windows.Graphics.Capture/D3D no projeto; `README.md:89` confirma.
- **Por que é básico:** a maioria dos jogos ainda oferece "tela cheia" exclusiva; preto é falha total da função para esses títulos.
- **Impacto:** print/vídeo inútil justamente no jogo; usuário precisa mudar o jogo para borderless.
- **Mínimo viável:** migrar o backend para Windows.Graphics.Capture (WGC), que captura exclusive fullscreen via D3D, mantendo GDI como fallback.

## D13 — A própria ilha aparece no print e no vídeo

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** captura limpa, sem o overlay do próprio app.
- **Hoje:** `StretchBlt` com `CAPTUREBLT` inclui janelas em camadas (`src/Island.Windows/Capture/DibFrame.cs:58-66`) e não há `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` no projeto (grep sem resultados); `README.md:89` lista o efeito.
- **Por que é básico:** a ilha fica no topo da tela; toda captura carrega a pílula no meio do frame.
- **Impacto:** prints/vídeos com o overlay alheio; refazer captura ou recortar manualmente.
- **Mínimo viável:** esconder a ilha durante a captura ou marcá-la com `WDA_EXCLUDEFROMCAPTURE` (efeito em WGC).

## D14 — Sem limite de duração/tamanho nem aviso de disco cheio

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** limite de gravação (ex.: 10 min) ou aviso/parada quando o disco enche.
- **Hoje:** o loop do `ScreenRecorder` não checa tempo nem espaço (`src/Island.Windows/Capture/ScreenRecorder.cs:69-83`); não há `DriveInfo`/checagem de espaço livre no projeto (grep) e falha de escrita só vira "Gravação interrompida" (`src/Island.Core/Capture/CaptureController.cs:192-204`).
- **Por que é básico:** gravação esquecida enche o disco; ferramenta madura avisa ou limita.
- **Impacto:** MP4 corrompido/curto e disco cheio derrubando outros apps, sem causa clara.
- **Mínimo viável:** limite configurável de duração e checagem periódica de espaço livre com aviso e parada graciosa (finaliza o MP4).

## D15 — Erro de captura sem diagnóstico acionável

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber por que falhou (permissão, pasta, monitor, encoder) e o que fazer.
- **Hoje:** falhas viram `Notice("Print não salvo", "Veja os logs")`/`"Gravação não iniciou"` (`src/Island.Core/Capture/CaptureController.cs:99-109,146-148,203`); `Notice` não tem ação/clique (`src/Island.Core/Models/Notice.cs:7`) e não há item de menu para abrir os logs, guardados em `%LocalAppData%\DynamicIsland\logs` (`src/Island.App/App.xaml.cs:91-94`).
- **Por que é básico:** quando a função principal falha, o usuário precisa de causa e saída, não de "veja os logs".
- **Impacto:** suporte por tentativa e erro; usuário conclui que o widget "não funciona".
- **Mínimo viável:** mensagem com causa provável, ação "Abrir pasta de logs" e, no print preto, orientar "use borderless".

## D16 — Sem atraso/countdown para print e gravação

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** "print em 3 s" para abrir um menu/tooltip, ou contagem 3-2-1 antes de gravar.
- **Hoje:** `TakeScreenshotAsync` e `StartRecordingAsync` executam na hora (`src/Island.Core/Capture/CaptureController.cs:90-150`); não há timer/contagem no fluxo (grep por "countdown" só acha o widget de timer).
- **Por que é básico:** atraso é padrão em captura de tela; evita precisar da mão em dois lugares ao mesmo tempo.
- **Impacto:** impossível capturar menus que somem ao clicar; início de gravação com a barra de controles visível.
- **Mínimo viável:** atraso configurável (0/3/5/10 s) com contagem discreta no widget/cápsula antes de executar.

## D17 — Sem FPS/frametime nem 1% low

- **Severidade:** Crítica
- **Tipo:** Lacuna de dados
- **Usuário espera:** FPS ao vivo (e frametime/1% low) no jogo, como MSI Afterburner, Game Bar ou Steam.
- **Hoje:** `PerformanceSnapshot` só tem CPU, GPU, temperaturas, RAM e VRAM (`src/Island.Core/Performance/PerformanceSnapshot.cs:7-15`) e o sampler só lê essas fontes (`src/Island.Windows/Performance/WindowsPerformanceSampler.cs:20-38`); grep por `Fps/PresentMon/ETW/FrameRate` só acha o encoder de captura.
- **Por que é básico:** FPS é a métrica nº 1 de quem joga; um widget "de desempenho" sem FPS mede o PC, não o jogo.
- **Impacto:** usuário não vê stutter nem quedas; mantém overlay de terceiros (RTSS/Afterburner).
- **Mínimo viável:** FPS por ETW (PresentMon) ou contador de apresentação por processo, com média e 1% low no sparkline.

## D18 — Sem HUD/overlay de desempenho sobre o jogo

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** métricas pequenas no canto do jogo, sem alt-tab nem segunda tela.
- **Hoje:** o widget é um cartão da estante (`src/Island.App/Views/Widgets/PerformanceWidget.xaml:1-8`) e a ilha se esconde em fullscreen por padrão (`src/Island.Core/Configuration/IslandSettings.cs:21`; `src/Island.Core/Application/IslandStateReducer.cs:29`); não existe HUD de métricas sobre o jogo — o cartão de desempenho só existe na estante.
- **Por que é básico:** o caso de uso é durante a partida; abrir a estante interrompe a experiência e a ilha fica escondida.
- **Impacto:** desempenho só é visto em outra tela (como o README assume) ou desligando a proteção de fullscreen; nada compacto sobre o jogo.
- **Mínimo viável:** HUD opcional topmost pequeno preso a um canto, com métricas escolhidas e transparência.

## D19 — Temperatura cobre pouco hardware: GPU só NVIDIA e CPU só zonas ACPI "CPU"

- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** temperatura de CPU e GPU em qualquer placa (AMD, Intel, NVIDIA).
- **Hoje:** GPU térmica só via NVML, primeira GPU NVIDIA (`src/Island.Windows/Performance/NvmlGpuReader.cs:20-23,44-47`); os contadores de GPU dão carga mas não temperatura (`src/Island.Windows/Performance/CounterGpuReader.cs:36-38`); CPU só de zonas térmicas com "CPU" no nome (`src/Island.Windows/Performance/ThermalZoneReader.cs:43`); `README.md:95` admite.
- **Por que é básico:** o alerta de temperatura é a função mais elaborada do widget e não funciona em boa parte do hardware (GPU AMD/Intel, muitos notebooks).
- **Impacto:** AMD/Intel e muitos notebooks mostram "—" e nunca alertam; o recurso central vira decorativo.
- **Mínimo viável:** temperatura de CPU por contadores/WMI adicionais e GPU AMD/Intel por ADL/Level Zero, com aviso claro de indisponível.

## D20 — Sem CPU por núcleo nem frequência/clock

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver se um núcleo está saturado e a frequência atual (throttling).
- **Hoje:** CPU vem de `GetSystemTimes`, só o total da máquina (`src/Island.Windows/Performance/CpuUsageReader.cs:5-7,18-25`); não há campos de núcleos nem MHz no snapshot (`PerformanceSnapshot.cs:7-15`) nem leitor de clock (grep por `MHz/Frequency` vazio).
- **Por que é básico:** stutter por um núcleo e throttle são diagnosticados por núcleo/frequência; o Gerenciador de Tarefas já mostra.
- **Impacto:** usuário não entende gargalo de CPU nem queda de clock; o widget parece raso.
- **Mínimo viável:** uso por núcleo via PDH `Processor Information`/`NtQuerySystemInformation` e clock via `CallNtPowerInformation`.

## D21 — Sem rede (download/upload)

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver throughput/latência enquanto joga ou baixa atualização.
- **Hoje:** o snapshot não tem campos de rede (`src/Island.Core/Performance/PerformanceSnapshot.cs:7-15`) e o sampler não lê `NetworkInterface`/PDH (grep vazio); `README.md:94` lista só CPU/RAM/GPU e temperaturas.
- **Por que é básico:** lag de rede é tão sentido quanto FPS; overlays de jogo mostram rede.
- **Impacto:** jogador não sabe se o problema é ping ou FPS; precisa de outro app.
- **Mínimo viável:** bytes/s por adaptador ativo (`NetworkInterface.GetIPv4Statistics`) a 1 Hz e um painel/linha opcional no widget.

## D22 — Sem disco (uso/espaço/IO)

- **Severidade:** Baixa
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver se o disco está a 100% ou sem espaço no SSD do jogo.
- **Hoje:** não há campos nem leitor de disco (`PerformanceSnapshot.cs:7-15`; `WindowsPerformanceSampler.cs:14-17` só CPU/NVML/contadores/térmica); grep sem leitor de disco/`DriveInfo`.
- **Por que é básico:** stutter por disco em 100% é comum em PC gamer; espaço livre importa para gravações.
- **Impacto:** diagnóstico incompleto; usuário abre o Gerenciador de Tarefas.
- **Mínimo viável:** PDH `PhysicalDisk`/`LogicalDisk` (uso/fila) e espaço livre do volume das capturas, com painel opcional.

## D23 — Sem ventoinhas nem consumo de energia

- **Severidade:** Baixa
- **Tipo:** Lacuna de dados
- **Usuário espera:** RPM de fans e watts da CPU/GPU para saber se o PC está no limite.
- **Hoje:** nenhum leitor de fan/watt no projeto (`WindowsPerformanceSampler.cs:14-17`; grep `Fan/Power/Watt` só acha eventos de energia do Windows).
- **Por que é básico:** junto com temperatura, é o trio de monitoramento de hardware de ferramentas maduras (HWiNFO/Afterburner).
- **Impacto:** usuário não vê fan parada ou consumo anormal; o widget fica atrás dos concorrentes.
- **Mínimo viável:** NVML (`NvmlDeviceGetFanSpeed`/`GetPowerUsage`) e, para CPU, PDH/WMI de consumo quando disponível; exibir só se houver dado.

## D24 — Histórico não persiste e não há exportação/CSV

- **Severidade:** Alta
- **Tipo:** Lacuna de dados
- **Usuário espera:** rever o gráfico da sessão, comparar antes/depois e exportar os dados.
- **Hoje:** o histórico é um anel de 60 amostras em memória (`src/Island.Core/Performance/PerformanceHistory.cs:6-7`), zerado ao fechar; o único JSON de desempenho salva limites de alerta (`src/Island.Windows/Performance/JsonPerformanceSettingsStore.cs:25-39`); não há export de métricas (o CSV do projeto existe só na suíte de arquivos).
- **Por que é básico:** para investigar queda de FPS/temperatura, precisa do histórico da partida, não do último minuto visível.
- **Impacto:** nada para mostrar em suporte/benchmark; comparação impossível; reinício perde tudo.
- **Mínimo viável:** gravar amostras em CSV/JSONL rotativo enquanto o widget estiver visível, com "Exportar última sessão" e abrir a pasta.

## D25 — Alerta só de temperatura; sem limite de uso de CPU/GPU/RAM

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** alertar "GPU 99%", "RAM 95%" ou "FPS abaixo de X", com limites próprios.
- **Hoje:** `PerformanceAlertSettings` só tem dois limites em °C (`src/Island.Core/Performance/PerformanceAlertSettings.cs:4,11`) e a política avalia apenas temperaturas (`src/Island.Core/Performance/TemperatureAlertPolicy.cs:42-45`); não há alerta de uso/FPS.
- **Por que é básico:** uso travado em 100% e RAM estourando são sinais de problema; só temperatura cobre parte.
- **Impacto:** usuário só descobre gargalo abrindo o widget; sem aviso enquanto joga.
- **Mínimo viável:** limites configuráveis de CPU%, GPU%, RAM% e FPS mínimo, reusando a histerese/cooldown existentes.

## D26 — Intervalo de amostragem fixo (1 s visível, 3 s com alerta)

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** amostrar mais rápido para picos (500 ms) ou menos para economizar (5 s).
- **Hoje:** constantes `VisibleInterval = 1 s` e `AlertInterval = 3 s` (`src/Island.Core/Performance/PerformanceSamplingPolicy.cs:6-11`); nenhuma preferência no `performance.json` (só alertas).
- **Por que é básico:** picos de GPU/RAM somem entre amostras de 1 s; o usuário não controla o custo de leitura.
- **Impacto:** gráfico impreciso em stutter curto; impossível reduzir o custo de CPU (~840 contadores de GPU a cada 3 s, `README.md:95`).
- **Mínimo viável:** intervalo configurável (0,5–5 s) com aviso de custo e opção de desligar os contadores de GPU.

## D27 — Métricas e tamanho fixos: não escolhe sensores nem painéis

- **Severidade:** Média
- **Tipo:** Lacuna de configuração
- **Usuário espera:** escolher o que ver (ex.: só FPS+GPU, ou CPU+RAM) e ter um modo compacto.
- **Hoje:** o widget sempre mostra dois painéis CPU/GPU fixos (`src/Island.App/Views/Widgets/PerformanceWidget.xaml.cs:93-101`) em 300x152 fixos (`PerformanceWidget.xaml:6-7`); não há configuração de layout/sensores.
- **Por que é básico:** cada jogo/PC precisa de métricas diferentes; cartão fixo ocupa espaço sem mostrar RAM/rede.
- **Impacto:** informação irrelevante polui; não dá para deixar um HUD enxuto.
- **Mínimo viável:** seleção de 2–4 métricas e modo compacto de uma linha, persistidos.

## D28 — "—" sem explicação para métricas ausentes

- **Severidade:** Baixa
- **Tipo:** Problema básico de UX
- **Usuário espera:** saber por que falta o dado e como resolver (sensor ausente, driver sem suporte, precisa de admin).
- **Hoje:** só a temperatura tem `ToolTip` explicativo (`src/Island.App/Views/Widgets/PerformanceMetricPanel.cs:106-119`); CPU/GPU/RAM sem valor mostram "—" seco; `README.md:95` reconhece.
- **Por que é básico:** "—" persistente parece bug; ferramentas maduras dizem "sensor indisponível".
- **Impacto:** usuário acha que o app quebrou e desinstala; suporte sem pista.
- **Mínimo viável:** tooltip/estado por métrica ("sem sensor nesta máquina", "driver NVIDIA ausente") com link para a limitação.

## D29 — Sem seletor de dispositivo de saída (nem nome do atual)

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** alternar entre fone, caixa e HDMI, ver qual está ativo e ter os apps do dispositivo escolhido.
- **Hoje:** o serviço fixa `GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` (`src/Island.Windows/Audio/WindowsAudioMixerService.cs:139`) e a interface só tem volume/mute por sessão (`src/Island.Core/Abstractions/IAudioMixerService.cs:20-24`); não lista dispositivos nem mostra o nome do atual; `README.md:100` admite.
- **Por que é básico:** mixer de ferramenta madura (EarTrumpet/Volume2) troca o dispositivo; jogador alterna headset/caixa toda hora.
- **Impacto:** usuário precisa ir às configurações do Windows; apps aparecem "no dispositivo errado" sem explicação.
- **Mínimo viável:** enumerar endpoints de renderização (`MMDeviceEnumerator.EnumerateAudioEndPoints`), seletor no cabeçalho e sessões do endpoint escolhido.

## D30 — Volume/mudo por app não é lembrado

- **Severidade:** Alta
- **Tipo:** Lacuna de configuração
- **Usuário espera:** "Chrome sempre em 30%, Discord em 70%" restaurado quando o app abre.
- **Hoje:** `SetVolume`/`SetMuted` só chamam a sessão viva (`WindowsAudioMixerService.cs:67-84`) e não existe store nem preferência de mixer (grep sem store de áudio); ao reiniciar o app ou trocar de dispositivo as sessões são recriadas (`:132-151,190-230`).
- **Por que é básico:** ajustar volume de cada app todo dia é o uso típico; persistência é o diferencial do EarTrumpet.
- **Impacto:** retrabalho diário; o valor do mixer cai para "volume do Windows".
- **Mínimo viável:** JSON de mixer (processo/executável → volume/mudo) aplicado na criação de cada sessão.

## D31 — Sem controle de microfone/entrada

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** volume e mudo do microfone e dos dispositivos de entrada, além dos apps.
- **Hoje:** o mixer só usa `DataFlow.Render` (`src/Island.Windows/Audio/WindowsAudioMixerService.cs:139`) e `IAudioMixerService` não tem APIs de entrada; nenhum `DataFlow.Capture` no projeto (grep).
- **Por que é básico:** quem joga em call precisa abaixar/mutar o mic; mixers maduros mostram "Microfone" e "Sons do sistema".
- **Impacto:** usuário abre o painel do Discord/Windows; não há mute global de mic.
- **Mínimo viável:** endpoint de captura padrão com slider/mudo e atalho "mudo o mic" (ver D39).

## D32 — Sem volume master nem "mutar todos" no mixer

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** um slider/ícone no topo do mixer para o volume geral e um botão para silenciar tudo de uma vez.
- **Hoje:** o mixer só oferece controles por sessão (`IAudioMixerService.cs:20-24`; `src/Island.App/Views/Widgets/MixerWidget.xaml:50-63` sem controle mestre); o volume master existe apenas no fluxo de volume separado (`WindowsVolumeService`), fora do mixer.
- **Por que é básico:** controles globais são a primeira linha do misturador do Windows; mutar todos resolve "alguém chamou no Discord".
- **Impacto:** usuário precisa de outro lugar para o volume geral; não há mute geral de apps.
- **Mínimo viável:** master (volume/mudo do endpoint) no cabeçalho + "Mutar todos" que guarda o estado anterior para desfazer.

## D33 — Sessões do mesmo app aparecem como linhas separadas (sem agrupamento)

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** um app = uma linha, com o volume valendo para o app inteiro (como no mixer do Windows).
- **Hoje:** cada session instance id vira uma linha (`WindowsAudioMixerService.cs:190-230`; `src/Island.App/Views/Widgets/MixerWidget.cs:119-139`), sem agrupar por processo/executável; `OnGroupingParamChanged` é ignorado (`src/Island.Windows/Audio/MixerSessionHandle.cs:245-248`).
- **Por que é básico:** navegadores e jogos com vários processos de áudio geram duplicatas; ajustar volume duas vezes é confuso.
- **Impacto:** lista poluída e volume "que não obedece" (outra linha do mesmo app continua alta).
- **Mínimo viável:** agrupar por caminho do executável, espelhando volume/mudo em todas as sessões do grupo.

## D34 — Primeiro plano na ordem só atualiza quando algo muda

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** o app em foco ir para o topo da lista imediatamente ao alt-tab.
- **Hoje:** a ordem usa `ForegroundProcess.CurrentId()` só dentro de `Refresh` (`src/Island.App/Views/Widgets/MixerWidget.cs:104`), disparado por eventos do mixer; o serviço de áudio não tem hook de foco (só consulta pontual a `GetForegroundWindow` em `src/Island.Windows/Audio/ForegroundProcess.cs:9-16`); `README.md:100` admite.
- **Por que é básico:** a ordenação é o principal atalho para achar o app do jogo; desatualizada, anula o recurso.
- **Impacto:** o app recém-focado não sobe; usuário caça a linha na lista paginada.
- **Mínimo viável:** `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` no serviço (já usado em GameNotes) reordenando sem reabrir.

## D35 — Sem fixar nem ocultar apps na lista

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** fixar o app do jogo/chat no topo e esconder apps que nunca ajusta.
- **Hoje:** a ordem é só regra fixa (`src/Island.Core/Audio/AudioSessionOrdering.cs:12-23`) e o widget renderiza todas as sessões (`MixerWidget.cs:126-139`); não existe estado de fixar/ocultar por app.
- **Por que é básico:** mixers maduros deixam fixar (pin) e filtrar; com paginação de 3 linhas, a ordem importa.
- **Impacto:** app importante pode ficar na página 2; lista cheia de apps irrelevantes.
- **Mínimo viável:** pin/filtro por executável em JSON de mixer, aplicado na ordenação e na paginação.

## D36 — Não abre/foca o app a partir da linha

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** clique/duplo clique ou menu para trazer a janela do app que está tocando.
- **Hoje:** a linha tem só slider, mudo e medidor (`src/Island.App/Views/Widgets/MixerRow.cs:53-156`); `ExecutablePath` é usado apenas para o ícone (`MixerRow.cs:167-174`).
- **Por que é básico:** o mixer do Windows tem menu "Abrir volume do app"; aqui a linha não leva a lugar nenhum.
- **Impacto:** usuário precisa achar a janela no Alt+Tab; o recurso fica só de "controle".
- **Mínimo viável:** menu de contexto "Abrir app" (o caminho do executável já está no modelo).

## D37 — Sem boost acima de 100% nem equalizador

- **Severidade:** Baixa
- **Tipo:** Função ausente
- **Usuário espera:** passar de 100% em áudio baixo e ajustar graves/agudos (ou pelo menos um "reforço de volume").
- **Hoje:** o volume é limitado a 0–100 (`src/Island.Core/Audio/AudioVolumeMath.cs:6-11`) e o scalar do Core Audio vai até 1.0 (`src/Island.Windows/Audio/MixerSessionHandle.cs:142-157`); não há processamento de áudio por app (grep sem EQ/DSP).
- **Por que é básico:** jogos com mix baixo se beneficiam de boost; é diferencial comum (Volume2/EarTrumpet com add-ons).
- **Impacto:** usuário não resolve áudio baixo dentro do app; recorre a outro software.
- **Mínimo viável:** boost 100–200% via processamento próprio/APO; equalizador pode ficar para depois.

## D38 — Mute rápido hardcoded só para o Discord

- **Severidade:** Baixa
- **Tipo:** Função parcial
- **Usuário espera:** um botão de pânico para o app de voz que usa (Discord, Teams, Steam).
- **Hoje:** `DiscordProcess = "Discord"` fixo (`src/Island.App/Views/Widgets/MixerWidget.cs:21`) e só esse botão aparece (`:212-226`); outros apps dependem de achar a linha e clicar no mudo.
- **Por que é básico:** mutar a call é a ação mais comum no meio da partida; a solução deveria ser configurável, não um nome de processo no código.
- **Impacto:** usuário de Teams/Steam Chat não tem o atalho; usuário de Discord tem.
- **Mínimo viável:** escolher o app do botão em Settings (ou um por linha fixada), mantendo os dois rótulos atuais.

## D39 — Sem atalho global de mute geral/mic

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** uma tecla para mutar tudo (ou o mic) sem abrir a ilha.
- **Hoje:** os únicos atalhos globais são Ctrl+Alt+V (clipboard), N (nota rápida), G (nota do jogo) e P/R (captura) (`src/Island.App/App.xaml.cs:120,327`; `src/Island.App/Shell/GameNotesHotkeyController.cs:15`; `src/Island.App/Shell/CaptureHotkeys.cs:13-14`); o mixer não registra atalho.
- **Por que é básico:** "mutar tudo" no meio da partida é o caso de uso clássico de hotkey de áudio.
- **Impacto:** usuário abre a estante (ou o mixer do Windows) no meio do jogo; perde tempo.
- **Mínimo viável:** hotkey configurável de mute geral (endpoint) e de mic, com aviso na cápsula.

## D40 — Slider de volume só responde ao mouse

- **Severidade:** Média
- **Tipo:** Lacuna de acessibilidade
- **Usuário espera:** ajustar volume pelo teclado (setas/Home/End) e por leitor de tela, como um slider padrão.
- **Hoje:** `VolumeSlider` é `FrameworkElement` desenhado, trata só eventos de mouse e não tem `AutomationPeer`/teclas (`src/Island.App/Mixer/VolumeSlider.cs:16,32-35,68-113`); só o botão de mudo tem nome de automação (`MixerRow.cs:149`).
- **Por que é básico:** é o único controle contínuo do widget; sem teclado, quem não usa mouse (ou usa leitor de tela) não ajusta volume.
- **Impacto:** usuário de teclado/leitor de tela fica sem o controle principal; quebra o padrão WPF de acessibilidade.
- **Mínimo viável:** implementar `IRangeValueProvider` e `OnKeyDown` (setas, PageUp/Down), preservando o desenho fino.

## D41 — Não há lista completa/editor das notas do jogo

- **Severidade:** Crítica
- **Tipo:** Função ausente
- **Usuário espera:** abrir uma janela com todas as notas do jogo, marcar, editar e apagar.
- **Hoje:** o widget mostra no máximo 3 linhas e a quarta em diante vira um `TextBlock` "+N notas" não clicável (`src/Island.App/Views/Widgets/GameNotesWidget.xaml.cs:26,194-209`); a única janela (`GameNotesWindow`) só adiciona (`src/Island.App/Shell/GameNotesWindow.xaml.cs:86-113`) e o gerenciador a mantém apenas para captura (`src/Island.App/Shell/GameNotesWindowManager.cs:5-9`).
- **Por que é básico:** com 4+ notas não há como ler ou agir na nota 5; é o mínimo de um widget de notas.
- **Impacto:** notas antigas ficam inacessíveis na prática; usuário volta para bloco de notas/planilha.
- **Mínimo viável:** janela "Notas do jogo" com lista completa (rolagem), checkbox, editar, apagar e filtro por jogo.

## D42 — Nota não pode ser editada depois de criada

- **Severidade:** Alta
- **Tipo:** Função ausente
- **Usuário espera:** corrigir texto (typo, preço, build) e complementar detalhes.
- **Hoje:** `GameNotesService` expõe só adicionar, alternar concluída, fixar e apagar (`src/Island.Core/GameNotes/GameNotesService.cs:132-152`); `GameNotesBook` idem (`src/Island.Core/GameNotes/GameNotesBook.cs:77-95`); a janela captura apenas texto novo.
- **Por que é básico:** nota de jogo envelhece (patch muda build/nomes); sem editar, a saída é apagar e recriar.
- **Impacto:** notas erradas/obsoletas permanecem; apagar e recriar perde o histórico e o pin.
- **Mínimo viável:** `UpdateNoteTextAsync` no serviço + edição inline na lista (Enter salva, Esc cancela).

## D43 — Adicionar/remover jogo manualmente exige editar settings.json na mão

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** "Adicionar jogo" na interface (executável/nome do processo) e "Remover jogo".
- **Hoje:** `GameProcesses` existe em `src/Island.Core/Configuration/IslandSettings.cs:34` e é lido por `src/Island.App/Composition/ServiceRegistration.cs:101,137`, mas nenhuma tela de Settings o edita (grep sem "GameProcesses" em `SettingsWindow.xaml`/`SettingsViewModel`) e nenhum código altera essa lista (só leitura em `src/Island.Windows/Configuration/JsonSettingsStore.cs:125`); o botão "Fixar" só age no jogo já detectado (`GameNotesService.cs:126-130`).
- **Por que é básico:** sem detectar jogo em janela, o usuário não tem como cadastrá-lo pela interface; `README.md:112` sugere "Settings", que não oferece isso.
- **Impacto:** jogo em janela/com launcher não é detectado e não há saída; notas ficam sem jogo.
- **Mínimo viável:** lista editável em Settings (adicionar pelo executável aberto ou digitando o processo; remover), gravando `GameProcesses`.

## D44 — Detecção não cobre jogo em janela nem UWP/Game Pass

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** notas funcionarem no jogo em janela e nos jogos da Store/Xbox.
- **Hoje:** só fullscreen/borderless vale (`src/Island.Core/GameNotes/GameDetectionRules.cs:49`) e a lista de ignorados contém `applicationframehost` (`GameDetectionRules.cs:23-33`), que hospeda apps UWP; jogo em janela precisa estar em `GameProcesses` (sem UI, ver D43).
- **Por que é básico:** muitos jogadores usam janela/borderless; Game Pass é comum no Windows.
- **Impacto:** notas "não aparecem" nesses jogos; usuário conclui que a detecção é aleatória.
- **Mínimo viável:** detectar UWP pelo processo real do jogo (AppUserModelId/ApplicationFrameHost) e oferecer "este jogo" quando a janela em primeiro plano não for ignorada.

## D45 — Jogo fixado não sobrevive ao reinício

- **Severidade:** Média
- **Tipo:** Função parcial
- **Usuário espera:** o jogo fixado à mão continuar fixado depois de fechar/abrir o app.
- **Hoje:** o pin vive só em memória, em `GameNotesSelection.PinnedKey` (`src/Island.Core/GameNotes/GameNotesSelection.cs:9-16`), e o arquivo de notas não tem campo de pin de jogo (`src/Island.Windows/GameNotes/JsonGameNotesStore.cs:180-188`); ao reiniciar, volta a valer o último detectado.
- **Por que é básico:** fixar é a correção para detecção errada; se some, o usuário refaz todo dia.
- **Impacto:** correção manual repetida; notas do jogo errado aparecem após reiniciar.
- **Mínimo viável:** persistir `pinnedKey`/`browsedKey` em `gamenotes.json` (ou settings) e restaurar ao carregar.

## D46 — Sem data/hora nem contexto de sessão

- **Severidade:** Média
- **Tipo:** Lacuna de dados
- **Usuário espera:** ver quando anotou (e "da última sessão"/"da run anterior") para saber o que já é obsoleto.
- **Hoje:** `GameNote.CreatedAt` é gravado (`src/Island.Core/GameNotes/GameNote.cs:4-9`), mas a linha do widget mostra só o texto (`GameNotesWidget.xaml.cs:258-268`); não há sessão/partida no modelo (`GameNotesBook.cs:4-10`).
- **Por que é básico:** notas de jogo são por run/patch; sem data, não se sabe o que foi resolvido.
- **Impacto:** notas velhas poluem; impossível saber "o que anotei ontem".
- **Mínimo viável:** data relativa na linha (tooltip com data completa) e agrupamento opcional por dia/sessão.

## D47 — Sem busca/filtro nas notas

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** buscar "chave", "boss", "build" dentro das notas do jogo.
- **Hoje:** `GameNotesService` só entrega `NotesFor(key)` (`src/Island.Core/GameNotes/GameNotesService.cs:62-66`) e o widget exibe um recorte fixo (`src/Island.App/Views/Widgets/GameNotesWidget.xaml:60-65`); não há campo de busca (o widget de Notas rápidas tem busca, mas não se aplica ao jogo).
- **Por que é básico:** com 10–200 notas, achar uma é o uso principal; notas rápidas já têm busca.
- **Impacto:** usuário rola/apanha visualmente; nota "some".
- **Mínimo viável:** busca no widget/janela completa (substring, sem acento) com contagem e navegação.

## D48 — Não exporta nem compartilha as notas

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** copiar todas as notas (texto) ou exportar para arquivo/backup.
- **Hoje:** não há método de exportação no serviço (`GameNotesService.cs:132-152`) nem UI; o dado só existe no JSON interno (`JsonGameNotesStore.cs:30-36`); `GameNotesFormat` formata apenas contagem/vazio/overflow (`src/Island.Core/GameNotes/GameNotesFormat.cs:6-17`).
- **Por que é básico:** guias de jogo circulam em texto/Discord; backup é proteção contra perder a run.
- **Impacto:** compartilhar exige abrir o JSON; migração/reinstalação arriscada.
- **Mínimo viável:** "Copiar notas do jogo" (markdown/lista) e "Exportar .txt/.md" com diálogo de salvar.

## D49 — Nenhuma forma de consultar as notas dentro do jogo

- **Severidade:** Alta
- **Tipo:** Função parcial
- **Usuário espera:** ver um checklist/HUD enquanto joga, sem alt-tab para a ilha.
- **Hoje:** a consulta só existe na estante/`GameNotesWidget`; o Ctrl+Alt+G abre uma janela topmost que só adiciona (`src/Island.App/Shell/GameNotesWindow.xaml.cs:86-113`) e a ilha se esconde em fullscreen (`src/Island.Core/Application/IslandStateReducer.cs:29`); não há HUD com a lista.
- **Por que é básico:** notas de jogo existem para serem consultadas durante a partida; esconder a ilha anula o widget.
- **Impacto:** o usuário faz alt-tab ou imprime/cola em outro lugar; o recurso perde a função principal.
- **Mínimo viável:** janela/overlay topmost de checklist (abre com atalho, fecha com Esc) com as notas abertas/fixadas.

## D50 — Apagar nota é definitivo, sem confirmação nem desfazer

- **Severidade:** Média
- **Tipo:** Problema básico de UX
- **Usuário espera:** confirmar ou poder desfazer a exclusão.
- **Hoje:** o X na linha chama `RemoveNoteAsync` direto (`src/Island.App/Views/Widgets/GameNotesWidget.xaml.cs:296-320`) e o registro é removido na hora (`src/Island.Core/GameNotes/GameNotesBook.cs:86-95`); `README.md:113` diz "Apagar é definitivo"; não há lixeira/undo.
- **Por que é básico:** o X fica ao lado do pin, num widget pequeno; clique errado custa a nota.
- **Impacto:** perda de dados sem recuperação; usuário evita usar o X ou perde notas valiosas.
- **Mínimo viável:** snackbar "Nota apagada — Desfazer" por alguns segundos, ou confirmação.

## D51 — Não dá para limpar as notas do jogo em lote

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** "limpar concluídas" ou "apagar todas as notas deste jogo" ao terminar a run.
- **Hoje:** só existe remoção por nota (`src/Island.Core/GameNotes/GameNotesService.cs:151-152`; `GameNotesBook.cs:86-95`); não há método em lote nem seleção múltipla; o limite é 200 notas/jogo (`GameNotesBook.cs:18`), e o jogo só some da lista quando a última nota é apagada.
- **Por que é básico:** terminar a run/jogo é comum; apagar 20 notas uma a uma inviabiliza a limpeza.
- **Impacto:** notas e jogos antigos acumulam; dados obsoletos atrapalham a busca.
- **Mínimo viável:** "Apagar concluídas" e "Apagar todas do jogo" (com confirmação) no menu do jogo.

## D52 — Prints/screenshots não se ligam ao jogo

- **Severidade:** Média
- **Tipo:** Função ausente
- **Usuário espera:** apertar print e a imagem aparecer anexada ao jogo/nota em que estava.
- **Hoje:** a captura vai para a pasta fixa com nome por data (`src/Island.Windows/Capture/CaptureLibrary.cs:9-10`; `src/Island.Core/Capture/CaptureNaming.cs:17-21`) e `GameNotesGame` não tem mídia/print (`src/Island.Core/GameNotes/GameNotesBook.cs:4-10`); os dois widgets não se conhecem — a nota é só texto (`src/Island.Core/GameNotes/GameNote.cs:4-9`).
- **Por que é básico:** capturas e notas do jogo andam juntas (mapa, build, bug); ferramentas maduras anexam print à nota.
- **Impacto:** usuário não encontra o print daquele jogo; notas sem prova visual.
- **Mínimo viável:** "Print para esta nota/jogo" salvando o caminho no registro, com miniatura na lista.
