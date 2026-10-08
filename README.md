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

## Estrutura
- `Island.Core` – regras (IslandCoordinator, reducer, prioridades). Sem WPF/Win32.
- `Island.Windows` – adaptadores (Windows.Media.Control, Core Audio/NAudio, monitores, janela overlay, JSON de configurações).
- `Island.App` – WPF: janela, views, molas (springs), tray, configurações.
Ver `docs/architecture.md`.

## Estante (shelf)
Clique na ilha para abrir. Widgets: Now Playing, Pomodoro, Calendar, File Tray. Clique direito: Customize Shelf, Open Clipboard
(atalho global Ctrl+Alt+V), Check for updates / Install update (este só aparece quando há uma Release mais nova com `.zip` no GitHub), Open Settings, Quit. Histórico do clipboard fica só na memória (itens de gerenciadores de senha são ignorados).
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
