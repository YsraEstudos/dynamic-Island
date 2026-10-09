# Dynamic Island for Windows

Ilha flutuante inspirada na Dynamic Island, nativa (C# / .NET 10 / WPF), centralizada no topo do monitor.
Mostra volume, música em reprodução (qualquer player que exponha sessão de mídia ao Windows) e controles.

Também expande suavemente para avisar quando o Caps Lock é ativado/desativado e quando dispositivos Bluetooth
ou USB conectam/desconectam. O aviso mostra um ícone e o nome do dispositivo por 3 segundos (configuração
`noticeSeconds`), depois recolhe. A enumeração inicial é silenciosa e interfaces do mesmo dispositivo são agrupadas
para evitar avisos repetidos. Bluetooth pareado sem conexão não gera aviso. Como os demais avisos normais, esses
eventos respeitam pausa, tela cheia, Mini e painéis abertos; a opção de reduzir animações continua sendo respeitada.
O modo `--demo` mostra exemplos desses avisos sem alterar teclado ou dispositivos.

## Executar
```
dotnet run --project src/Island.App            # integração real (mídia + volume)
dotnet run --project src/Island.App -- --demo  # serviços simulados (sem tocar música)
dotnet run --project src/Island.App -- --soak=10  # teste de estabilidade (10 min, eventos simulados); resultado em %LocalAppData%\DynamicIsland\logs
dotnet test
```
Configurações: ícone da bandeja → Settings. Arquivo: `%LocalAppData%\DynamicIsland\settings.json`. Logs em `%LocalAppData%\DynamicIsland\logs`.

## Instalar e atualizar (auto-update)
O app se atualiza sozinho pelas Releases do GitHub (`YsraEstudos/dynamic-Island`). Não há MSIX, certificado nem senha: cada Release tem um único arquivo `DynamicIsland-X.Y.Z-win-x64.zip` com o app compilado.

**Instalar (uma vez):** instale o .NET 10 Desktop Runtime, baixe o `.zip` da última Release em https://github.com/YsraEstudos/dynamic-Island/releases, extraia em `%LocalAppData%\Programs\DynamicIsland` (uma pasta onde o seu usuário pode gravar; não use Program Files) e abra `DynamicIsland.exe`. Opcional: um atalho no menu Iniciar e a chave "Start with Windows" em Settings.

**Como o app se atualiza:**
1. Ao iniciar (30 s depois) e a cada 6 h, ele consulta `https://api.github.com/repos/YsraEstudos/dynamic-Island/releases/latest`.
2. Se a tag (ex.: `v0.4.2`) for maior que a versão em execução e a Release tiver um asset `.zip`, o clique direito da ilha mostra **Install update (vX.Y.Z)**. **Check for updates** (no menu e em Settings → UPDATES) consulta na hora.
3. Install baixa o zip para `%LocalAppData%\DynamicIsland\updates` (só de HTTPS em github.com ou githubusercontent.com), descompacta e fecha o app. Um PowerShell oculto espera o app sair, copia os arquivos sobre a pasta de instalação e abre o app de novo. O resultado fica em `%LocalAppData%\DynamicIsland\logs\update-install.log`; as sobras do download são apagadas na próxima abertura.
4. Com o Pomodoro raivoso travado, Install pede a frase de desbloqueio, como o Quit.
O repositório pode ser trocado pela chave `updateRepository` do `settings.json`. A versão em execução vem de `<Version>` em `Directory.Build.props`.

**Publicar uma versão nova** (uma linha; quem usa o Claude Code só pede "publique uma versão nova", ver `CLAUDE.md`):
```
.\release.ps1                  # sobe o último número (0.4.1 -> 0.4.2)
.\release.ps1 -Version 1.0.0   # ou escolhe o número
```
O script sobe `<Version>`, roda `dotnet publish` (win-x64, dependente de framework), gera o zip em `release-out\`, faz commit + tag `vX.Y.Z` + push e cria a Release com o zip (`gh release create`). Precisa de `dotnet`, `git` e `gh` (GitHub CLI) logado em uma conta com acesso ao repositório. As instalações existentes veem a Release na próxima verificação.

**Se algo falhar:** veja `%LocalAppData%\DynamicIsland\logs\update-install.log` (instalação) e `island-AAAAMMDD.log` (verificação/download). Causas comuns: o app instalado numa pasta sem permissão de escrita; a Release sem `.zip`, marcada como rascunho ou pré-release (`releases/latest` ignora esses); tag que não é `vX.Y.Z`.

**Armadilhas do auto-update (não repetir):** em v0.4.8 e v0.4.9 a ilha ficou fechada depois de "Install update". A causa estava no próprio app, não no processo de publicação: uma instância travava ao fechar e segurava os arquivos, a cópia falhava e o script não reabria o app. Regras para quem mexer nisso:
- Ao fechar, o app solta a captura do mouse antes de destruir janelas, e `OnExit` tem um watchdog que força a saída após 5 s (`src/Island.App/App.xaml.cs`). Não remova nenhum dos dois.
- O script de update (`UpdateRules.BuildUpdateScript`) precisa parar toda instância ainda aberta na pasta de instalação antes de copiar, e reabrir o app mesmo se a cópia falhar. Os testes em `UpdateRulesTests` cobrem essa ordem.
- O script que roda é o da versão **instalada**. Uma correção no script só vale para a atualização seguinte à versão que a contém; a passagem para ela ainda usa o script antigo.
- Antes de culpar o update, leia `update-install.log`. `robocopy failed with code 8` ou `9` significa arquivos em uso, e quase sempre há uma instância antiga aberta.
- Não publique sem testar a atualização de verdade: instale a versão anterior, rode "Install update" e confirme que o app volta na nova.
- **v0.4.12 foi publicada com o app que não abre.** `QuickNotesWindowManager` pedia um `Func<bool>` que ninguém registrou na injeção de dependência (`ServiceRegistration`). Compila, os testes passavam, e o app fechava no `OnStartup`: o `DispatcherUnhandledException` engole o erro, então nem aparece janela de falha, só `Unable to resolve service for type ...` no `island-AAAAMMDD.log`. Quem foi o culpado não foi o auto-update: o build já estava quebrado. Regras para não repetir:
  - Ao mexer em construtor de classe registrada em `ServiceRegistration.Configure`, registre também cada dependência (use `s.AddSingleton(sp => new X(...))` quando o construtor pede delegates, `Func<>` ou valores simples). `ServiceRegistrationTests` valida o grafo inteiro (`ValidateOnBuild`) em modo demo e real: se ele falhar, o app não abre.
  - `release.ps1` agora abre o build **publicado** com `DynamicIsland.exe --smoke-test` (startup real com serviços falsos, ao lado da instância aberta, sai com 0 após ~3 s ou com 1 na primeira exceção) e **aborta antes do commit, tag e Release** se não abrir. Não remova esse passo nem publique zip gerado por outro caminho.
  - Teste a abertura de qualquer build antes de publicar: `dotnet publish src/Island.App/Island.App.csproj -c Release -r win-x64 --self-contained false -o $env:TEMP\di-teste`, depois `& $env:TEMP\di-teste\DynamicIsland.exe --smoke-test; $LASTEXITCODE` (precisa imprimir 0).
  - Se o app instalado "não abre", leia primeiro o `island-AAAAMMDD.log` (e `island-AAAAMMDD_001.log`): uma exceção `Unhandled UI exception` perto da hora da abertura é o build, não o update. Para voltar, reinstale o zip da Release anterior em `%LocalAppData%\Programs\DynamicIsland`.

## Estrutura
- `Island.Core` – regras (IslandCoordinator, reducer, prioridades). Sem WPF/Win32.
- `Island.Windows` – adaptadores (Windows.Media.Control, Core Audio/NAudio, monitores, janela overlay, JSON de configurações).
- `Island.App` – WPF: janela, views, molas (springs), tray, configurações.
Ver `docs/architecture.md`.

## Estante (shelf)
Clique na ilha para abrir. Widgets: Now Playing, Pomodoro, Calendar, Notas, Orçamento IA, File Tray e os widgets para jogo (Captura, Desempenho, Mixer, Notas do Jogo; veja "Widgets para jogo"). Clique direito: Customize Shelf, Open Clipboard
(atalho global Ctrl+Alt+V), Check for updates / Install update (este só aparece quando há uma Release mais nova com `.zip` no GitHub), Open Settings, Quit. Histórico do clipboard fica só na memória (itens de gerenciadores de senha são ignorados).
Não implementado: Weather e conversão de arquivos.

## Widgets para jogo
Quatro widgets da estante pensados para quem joga com a ilha em outra tela ou num canto. Adicione-os em Customize Shelf. Nenhum lê a memória nem injeta código em outro processo (nada que um anticheat deva marcar). Em `--demo` todos usam serviços falsos. A ilha só amostra dados enquanto o widget está visível (ou, no Desempenho, enquanto o alerta está ligado): em repouso não há timer ligado.

### Captura (`capture`)
- **Print** (Ctrl+Alt+P ou botão): PNG do monitor em primeiro plano em `%UserProfile%\Pictures\DynamicIsland\Capturas`. A miniatura do último print aparece no widget; clicar abre o arquivo, e há botão para copiar a imagem.
- **Gravação** (Ctrl+Alt+R ou botão): MP4 do monitor, com ponto vermelho pulsando e cronômetro mm:ss. A ilha avisa quando o print é salvo e quando a gravação começa/termina.
- Implementação: GDI + Media Foundation (Sink Writer), sem dependência extra. 30 fps, no máximo 1920x1080, bitrate de 2 a 12 Mbps. Código em `Island.Core/Capture`, `Island.Windows/Capture` e `Views/Widgets/CaptureWidget`.
- **Limites:** jogo em **tela cheia exclusiva (DirectX exclusivo) sai preto**, tanto no print quanto no vídeo; use janela sem bordas (borderless). Sem áudio, sem cursor, e o overlay da ilha aparece na captura. Atalhos fixos no código; se estiverem em uso por outro app o widget avisa. O próximo passo natural é migrar a captura para a Windows.Graphics.Capture.

### Desempenho (`performance`)
- Mostra CPU %, GPU %, temperaturas de CPU e GPU, RAM e VRAM, com barras animadas e um histórico (sparkline) de 60 amostras.
- **Alerta de temperatura:** a ilha expande com "GPU 87 °C" (ou CPU) quando passa do limite. Padrão 85 °C (CPU) e 83 °C (GPU), com histerese de 5 °C para rearmar e no mínimo 60 s entre avisos do mesmo sensor. No widget: botão **Alerta**, ‹ › ajustam ±1 °C, a roda do mouse ±1 (Shift ±5). É um aviso normal: respeita pausa e tela cheia. Configuração em `%LocalAppData%\DynamicIsland\performance.json`.
- Fontes (sem admin e sem driver próprio, de propósito: o WinRing0 do LibreHardwareMonitor é bloqueado pelo Defender e marcado por anticheats): CPU por `GetSystemTimes`, RAM por `GlobalMemoryStatusEx`, GPU NVIDIA por NVML (`nvml.dll` do driver; carga, temperatura e VRAM), GPU Intel/AMD pelos contadores "GPU Engine" via PDH (só carga), temperatura de CPU pelos contadores "Thermal Zone Information" (só zonas com "CPU" no nome).
- **Limites:** sem sensor disponível o widget mostra "—", nunca falha. Em muitos PCs a zona térmica de CPU não existe, então a temperatura de CPU some. A temperatura de GPU só existe em NVIDIA. A leitura de temperatura de CPU e de GPU NVIDIA ainda não foi validada em hardware real. Com o alerta ligado (padrão) o app lê ~840 contadores de GPU a cada 3 s (custo de CPU não medido). O aviso não tem ícone.

### Mixer (`mixer`)
- Volume por aplicativo: ícone, nome, **slider**, **mudo** e **medidor de pico** animado. Ordem: app em primeiro plano, depois quem está tocando, depois sessões inativas (esmaecidas). Três linhas por vez, rolagem com a roda do mouse. O botão Mutar/Ligar Discord só aparece enquanto o Discord tem uma sessão de áudio.
- Orientado a eventos (sessões novas, mudança de volume, troca do dispositivo de saída padrão). Todo o COM roda numa thread MTA própria; o medidor de pico (40 ms) só roda com o widget visível e som tocando.
- **Limites:** sem seletor de dispositivo de saída (exigiria a API não documentada PolicyConfig) e sem volume preferido por app. A ordem pelo app em primeiro plano só atualiza quando algo muda (não há hook de foco). Código em `Island.Core/Audio`, `Island.Windows/Audio/WindowsAudioMixerService` e `Views/Widgets/MixerWidget`.

### Notas (`quicknotes`)
- O widget (340 DIP) tem uma **barra de captura**, as duas notas mais recentes (clique numa para abri-la no app) e **Abrir app**. Os dois botões abrem janelas que **crescem a partir do widget** (`WindowMorph`: o cartão sai do retângulo do widget na tela e se expande; ao fechar, encolhe de volta).
- **Captura rápida:** a ilha não ativa (WS_EX_NOACTIVATE), então não recebe teclado; a barra abre a mini-janela `QuickCaptureWindow` (Enter salva, Shift+Enter quebra linha, Ctrl+Enter continua no app, Esc fecha; cor e fixar). O atalho global **Ctrl+Alt+N** abre a mesma janela.
- **App de notas** (`QuickNotesWindow`): lista com cor, prévia, etiquetas, progresso do checklist e idade; abas Notas/Arquivo/Lixeira com contagem; busca (Ctrl+F); fixar, arquivar, lixeira, restaurar, esvaziar lixeira; checklist (Enter adiciona item); autosave com "Tentar salvar de novo". Ctrl+N cria, Esc fecha, Delete manda para a lixeira.
- O editor abre assim que você pede uma nota nova; a nota só passa a existir quando tem conteúdo (rascunho vazio não é salvo). Dados em `%LocalAppData%\DynamicIsland
otes.json`.

### Notas do Jogo (`gamenotes`)
- Notas (checklist com marcar, fixar e apagar) amarradas ao jogo em primeiro plano; voltando ao jogo, as notas dele reaparecem. O widget lembra o último jogo, então continua mostrando as notas dele quando você dá alt-tab para a ilha. ‹ › navegam entre jogos com notas e dá para fixar um jogo à mão se a detecção errar.
- **Como digitar:** a ilha não ativa (WS_EX_NOACTIVATE), então não recebe teclado. O botão **+** e o atalho global **Ctrl+Alt+G** abrem uma janela própria (Enter adiciona, Esc fecha), a mesma solução das Notas. Se o atalho estiver em uso o widget avisa e o botão + continua funcionando.
- **Detecção do jogo:** um jogo em `GameProcesses` (Settings) sempre vale; depois são descartados navegadores, editores, Office, shell e a própria ilha; depois vale processo em tela cheia ou borderless. **Um jogo em janela comum só é detectado se estiver em `GameProcesses` ou se for fixado à mão.** A chave do jogo é o nome do processo (sem `.exe`, minúsculo).
- Dados em `%LocalAppData%\DynamicIsland\gamenotes.json` (escrita atômica; arquivo corrompido vai para `.bad`). Limites: 200 notas por jogo, 500 caracteres por nota. Apagar é definitivo.

## Orçamento IA
Widget da estante (adicione em Customize Shelf) que divide a porcentagem restante de cada IA pelos dias úteis até o reinício e mostra "Pode usar hoje" (sempre arredondado para baixo) e "Sobra no fim do dia". Botões **+1%** / **+5%** registram uso, ‹ › trocam de IA e **Abrir** abre a janela de edição (período, hora de reinício, dias sem uso, total, modo Consumido/Restante). Dados em `%LocalAppData%\DynamicIslandudgets.json`. Código: `Island.Core/Budgets` (cálculo e `BudgetBook`), `Island.Windows/Budgets` (JSON), `Island.App/Budgets` (janela) e `Views/Widgets/BudgetWidget`.

## Pomodoro com ciclos
No widget Pomodoro, escolha a duração do foco (pílula **Focus** + régua) e do descanso (pílula **Break** + régua) e a quantidade de pomodoros na pílula **×N** (clique soma 1, a roda do mouse sobe/desce; 1 a 12). **Play** com o foco intacto inicia o plano: foco → descanso → foco … → descanso, tudo automático. A cada troca a ilha abre um aviso ("Hora do descanso" / "Volte ao foco" / "Sessão concluída"), mesmo se estiver recolhida ou com a estante aberta, e toca um som se o botão de som estiver ligado. Durante o plano a pílula mostra o progresso (2/3); Pause mantém o plano e Reset o cancela. Os pomodoros e descansos são contados com o relógio do sistema, sem deriva.

A pílula **Pré** é um preparo fixo de 5 minutos para se organizar antes de estudar. Ela roda sozinha, fora do plano: ao terminar, a ilha avisa "Hora de estudar" e deixa o foco pronto para o Play.

## Pomodoro raivoso
A pílula **Angry** inicia uma sessão de foco que não pode ser pausada, reiniciada ou editada. Enquanto ela roda, janelas de navegador cujo título mostra YouTube, X (Twitter), Instagram, Reddit ou conteúdo adulto (sites e termos pornográficos conhecidos) são minimizadas e aparece um aviso.
Só há duas saídas: o timer de foco terminar, ou digitar uma frase longa em português que não pode ser colada. A mesma frase é pedida ao escolher Quit no menu da ilha ou na bandeja enquanto a sessão estiver travada.

### Bloqueio do celular
Com **Block phone during Angry** ligado em Settings, iniciar o Angry também bloqueia o celular (app Foco & Bem-Estar, "Bloqueio por horário") pelo tempo que restar do timer. Uma única mensagem FCM por sessão; nada roda nem fica conectado fora disso, e o celular não faz polling.
- Sair do Angry antes da hora **não** libera o celular: ele só se libera digitando a frase no próprio aparelho, ou ao fim do tempo. Iniciar outro Angry com o celular ainda bloqueado só estende o bloqueio, nunca encurta.
- Sem internet, token vencido ou chave inválida, o Angry segue normal no PC e a ilha avisa "Celular não bloqueado". Falhas de rede são repetidas (5 s, 15 s, 45 s) enquanto restar tempo.
- Configuração única: crie um projeto Firebase, baixe a chave da service account e salve em `%LocalAppData%\DynamicIsland\fcm-service-account.json`. No app do celular, copie o token (Bloqueio por horário → Copiar token do celular) e cole em Settings → Phone. "Send 1-minute test block" confere tudo.
- A chave fica só nesse arquivo (nunca em `settings.json` nem nos logs).

Limites: a detecção é pelo **título da janela ou pelo título acessível da aba ativa** do navegador (Chrome, Edge, Firefox, Brave, Opera, Vivaldi...). Janelas nomeadas no Edge também são verificadas pelo título acessível. A leitura roda fora da interface; se o provedor de acessibilidade do navegador travar, essa leitura aguarda sua resposta, enquanto a ilha e a detecção pelo título normal continuam disponíveis. Um site cujo título não cita o nome ainda pode escapar. Não é um bloqueio de rede: outros apps não são cobertos, e encerrar o processo no Gerenciador de Tarefas não é impedido.
