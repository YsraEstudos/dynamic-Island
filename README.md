# Dynamic Island for Windows

Ilha flutuante inspirada na Dynamic Island, nativa (C# / .NET 10 / WPF), centralizada no topo do monitor.
Mostra volume, música em reprodução (qualquer player que exponha sessão de mídia ao Windows) e controles.

## Executar
```
dotnet run --project src/Island.App            # integração real (mídia + volume)
dotnet run --project src/Island.App -- --demo  # serviços simulados (sem tocar música)
dotnet run --project src/Island.App -- --soak=10  # teste de estabilidade (10 min, eventos simulados); resultado em %LocalAppData%\DynamicIsland\logs
dotnet test
```
Configurações: ícone da bandeja → Settings. Arquivo: `%LocalAppData%\DynamicIsland\settings.json`. Logs em `%LocalAppData%\DynamicIsland\logs`.

## Empacotar (MSIX)
Gera um pacote MSIX com `packaging/build-msix.ps1` (publica o app em win-x64, empacota com `makeappx` e assina com `signtool`; exige o Windows SDK). Antes, crie o certificado de desenvolvimento com `packaging/create-dev-cert.ps1` e confie no `.cer` como administrador. O resultado sai em `packaging\out\`. Passos completos, avisos sobre dados virtualizados e autostart estão em `packaging/README.md`.

## Atualizações
Ao iniciar (30 s depois) e a cada 6 h, o app consulta a última Release publicada em `YsraEstudos/dynamic-Island`. Se ela for mais nova que a versão em execução e tiver um asset `.msix`, o clique direito da ilha mostra **Install update (vX.Y.Z)**. **Check for updates** (no menu, enquanto não há atualização, e em Settings → UPDATES) consulta na hora e avisa se há versão nova. Install baixa o `.msix` para `%LocalAppData%\DynamicIsland\updates` e executa `Add-AppxPackage` em um PowerShell oculto, separado do app; ao terminar, o app novo é reaberto sozinho. O resultado fica em `%LocalAppData%\DynamicIsland\logs\update-install.log`. Downloads só saem de HTTPS em github.com ou githubusercontent.com. O repositório pode ser trocado pela chave `updateRepository` do `settings.json`. Para publicar uma versão, veja `packaging/README.md`.

## Estrutura
- `Island.Core` – regras (IslandCoordinator, reducer, prioridades). Sem WPF/Win32.
- `Island.Windows` – adaptadores (Windows.Media.Control, Core Audio/NAudio, monitores, janela overlay, JSON de configurações).
- `Island.App` – WPF: janela, views, molas (springs), tray, configurações.
Ver `docs/architecture.md`.

## Estante (shelf)
Clique na ilha para abrir. Widgets: Now Playing, Pomodoro, Calendar, File Tray. Clique direito: Customize Shelf, Open Clipboard
(atalho global Ctrl+Alt+V), Check for updates / Install update (este só aparece quando há uma Release mais nova com `.msix` no GitHub), Open Settings, Quit. Histórico do clipboard fica só na memória (itens de gerenciadores de senha são ignorados).
Não implementado: Weather e conversão de arquivos.

## Pomodoro raivoso
A pílula **Angry** inicia uma sessão de foco que não pode ser pausada, reiniciada ou editada. Enquanto ela roda, janelas de navegador cujo título mostra YouTube, X (Twitter), Instagram ou Reddit são minimizadas e aparece um aviso.
Só há duas saídas: o timer de foco terminar, ou digitar uma frase longa em português que não pode ser colada. A mesma frase é pedida ao escolher Quit no menu da ilha ou na bandeja enquanto a sessão estiver travada.

### Bloqueio do celular
Com **Block phone during Angry** ligado em Settings, iniciar o Angry também bloqueia o celular (app Foco & Bem-Estar, "Bloqueio por horário") pelo tempo que restar do timer. Uma única mensagem FCM por sessão; nada roda nem fica conectado fora disso, e o celular não faz polling.
- Sair do Angry antes da hora **não** libera o celular: ele só se libera digitando a frase no próprio aparelho, ou ao fim do tempo. Iniciar outro Angry com o celular ainda bloqueado só estende o bloqueio, nunca encurta.
- Sem internet, token vencido ou chave inválida, o Angry segue normal no PC e a ilha avisa "Celular não bloqueado". Falhas de rede são repetidas (5 s, 15 s, 45 s) enquanto restar tempo.
- Configuração única: crie um projeto Firebase, baixe a chave da service account e salve em `%LocalAppData%\DynamicIsland\fcm-service-account.json`. No app do celular, copie o token (Bloqueio por horário → Copiar token do celular) e cole em Settings → Phone. "Send 1-minute test block" confere tudo.
- A chave fica só nesse arquivo (nunca em `settings.json` nem nos logs).

Limites: a detecção é pelo **título da janela** do navegador (Chrome, Edge, Firefox, Brave, Opera, Vivaldi...). Um site cujo título não cita o nome também escapa. Não é um bloqueio de rede: outros apps não são cobertos, e encerrar o processo no Gerenciador de Tarefas não é impedido.
