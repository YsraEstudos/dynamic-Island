# SysGuard — bloqueio de conteúdo adulto para Windows

App standalone (fora do Dynamic Island) para bloquear pornografia no Windows inteiro, sem depender
do navegador. É um único `SysGuard.exe` self-contained (não precisa de .NET instalado): você copia
para o notebook, executa uma vez, cria a senha, e ele se instala e passa a subir junto com o Windows.

A bandeja usa um ícone neutro (escudo) que fica nos **ícones ocultos** (seta para cima ao lado do
relógio). O menu permite desativar, alterar a senha ou desinstalar — tudo com senha. Não existe
janela principal nem nenhum texto sobre o conteúdo bloqueado na interface.

**Versão 1.1.0:** o filtro de DNS passou de CleanBrowsing *Family* para *Adult* e a política
`ForceYouTubeRestrict` foi removida. O modo restrito do YouTube que a 1.0 forçava (e que escondia
resultados de vídeo) não é mais aplicado; valores antigos são apagados do registro na atualização.
SafeSearch de **busca** do Google/Bing continua forçado — ele não restringe o YouTube.

## Como o bloqueio funciona (três camadas + reforços)

1. **DNS de bloqueio adulto (principal):** todos os adaptadores com rota padrão passam a usar o
   CleanBrowsing **Adult** (`185.228.168.10` / `185.228.169.11` + IPv6 `2a0d:2a00:1::1` /
   `2a0d:2a00:2::1`), que bloqueia pornografia no DNS e **não** força o modo restrito do YouTube.
   O SafeSearch de busca do Google/Bing continua sendo imposto pelo próprio DNS do filtro Adult.
   O DNS original é guardado e restaurado ao desativar; ao aplicar/restaurar o cache de DNS é
   limpo (equivalente a `ipconfig /flushdns`).
2. **Arquivo hosts (reforço):** lista embutida com ~4.200 domínios adultos apontando para
   `0.0.0.0`/`::`. Cobre o caso de o DNS ser contornado por algum app. Enquanto protegido, o mesmo
   bloco também relaciona os **nomes dos resolvedores DNS-over-HTTPS conhecidos** (`dns.google`,
   `cloudflare-dns.com`, `mozilla.cloudflare-dns.com`, `dns.quad9.net`, `doh.opendns.com`, …): o
   navegador não consegue cair num DoH fora do filtro só por nome. Isso **não** bloqueia DoH por IP
   fixo (`https://1.1.1.1/dns-query`), VPN ou proxy — ver "Limitações conhecidas".
3. **Políticas de navegador:** desliga DNS-over-HTTPS e força SafeSearch de busca (Google/Bing) no
   Chrome, Edge, Firefox, Brave, Chromium e Vivaldi (sem isso os navegadores poderiam ignorar o DNS
   do sistema). **Não** força mais o YouTube Restricted Mode: a política `ForceYouTubeRestrict` foi
   removida de propósito na 1.1. Também minimiza janelas de navegador cujo título denuncia conteúdo
   adulto.

**Reforço do Pomodoro Angry (Dynamic Island):** durante uma sessão Angry (foco e intervalo), o
serviço acrescenta ao hosts (com marcadores próprios, removíveis) os sites de distração
`youtube.com`/`youtu.be`, `twitter.com`/`x.com`, `instagram.com` e `reddit.com` (variantes `www`/`m`
incluídas) e a bandeja minimiza janelas de navegador cujo título cita esses sites. A exceção é a
janela final de 60 s de um intervalo rodando: só Twitter/X e Instagram ficam liberados; YouTube,
Reddit e o filtro adulto continuam valendo. Ao terminar a sessão o bloco é removido. Detalhes em
"Integração com o Pomodoro Angry".

O serviço `SysGuardSvc` (roda como SYSTEM) reaplica as camadas a cada poucos segundos se algo for
alterado por fora. Um segundo serviço pequeno, `SysGuardWatchdogSvc`, vigia o principal e o religa
se ele parar; o principal também vigia o vigia. Os dois serviços têm **recovery actions** do SCM
(restart em 5 s / 10 s / 30 s, contador reiniciando a cada 24 h) para quedas.

Parar/desinstalar pela bandeja exige senha. Parar o serviço pelo Gerenciador de Serviços (admin)
restaura o sistema (é a válvula de segurança) — mas, com o vigia, a proteção volta sozinha em poucos
segundos, a menos que os **dois** serviços sejam parados. Documentado como limite: quem é
administrador da máquina sempre vence.

## Instalação (no notebook)

1. Copie `SysGuard.exe` para o notebook.
2. Execute com duplo clique. O UAC pede permissão de administrador.
3. Crie a senha (mínimo 6 caracteres) e confirme. **Anote-a**: não há recuperação automática.
4. O exe se copia para `C:\ProgramData\SysGuard`, registra **os dois serviços** (principal + vigia),
   inicia o bloqueio e inicia a bandeja. O ícone aparece nos ícones ocultos da barra de tarefas.
5. Reinicie o Windows uma vez para conferir o início automático (serviços + bandeja).

A partir daí: `Windows + login` → navegação filtrada; clique no ícone oculto → menu.

## Atualizar uma instalação existente (1.0 → 1.1)

Não é preciso desinstalar nem perder a senha: o exe novo se atualiza por cima.

1. Copie o `SysGuard.exe` novo para o notebook (pode ser em qualquer pasta, menos em cima do exe
   instalado em `C:\ProgramData\SysGuard`).
2. Dê duplo clique. O exe detecta a instalação existente e pergunta se quer **atualizar/reparar**
   ("Já existe uma instalação… Atualizar/reparar com esta versão? A senha é mantida."). Responda
   **Sim** e aproveite o UAC.
   - Se o serviço não estiver respondendo, ele oferece **Reparar**; o menu da bandeja também tem
     **Reparar instalação (administrador)…**.
3. O reparo para os dois serviços, copia o exe novo, mantém a senha e religa a proteção. Na
   primeira aplicação ele já faz a migração:
   - DNS dos adaptadores passa do CleanBrowsing **Family** para o **Adult** (IPs `185.228.168.10/…`);
   - os valores antigos `ForceYouTubeRestrict` são **apagados** do registro (HKLM Chrome/Edge e
     HKCU do usuário), além das políticas de SafeSearch/DoH continuarem ativas;
   - o hosts é recomposto pelo novo serviço (blocos de adulto/anti-DoH/Angry recalculados).
4. **Feche e reabra os navegadores** (Chrome, Edge, Firefox, Brave…). Eles leem as políticas do
   registro só ao iniciar; o modo restrito do YouTube some na próxima abertura.
5. Confira: `chrome://policy` / `edge://policy` (sem `ForceYouTubeRestrict`) e YouTube com os
   resultados normais. O bloqueio de pornografia continua pelo DNS Adult + hosts.

Se precisar conferir a versão instalada, abra `C:\ProgramData\SysGuard\state\install.dat` (registra a
versão da última instalação) — a 1.1.0 é a primeira com o filtro Adult.

## Menu da bandeja

- **Proteção ativa / desativada** — status (e aviso de VPN/túnel ativo, quando houver).
- **Desativar proteção…** — pede a senha; restaurar DNS/hosts/políticas até o próximo reinício.
- **Ativar proteção** — reaplica tudo.
- **Alterar senha…** — pede a senha atual e a nova.
- **Desinstalar…** — pede a senha, restaura o sistema e agenda a remoção dos serviços/pasta.

Desbloquear vale só para a sessão atual: ao reiniciar o Windows, o serviço volta protegido.

## Se o serviço não responder

O instalador só conclui depois que o serviço responde ao canal de controle. Se falhar, ele reverte
a instalação sozinho e guarda os logs em `%TEMP%\SysGuard.log`, `%TEMP%\SysGuard-service.log` e
`%TEMP%\SysGuard-watchdog.log`. Para testar só o canal (não instala nada):
`SysGuard.exe --pipetest` (mostra uma caixa com o resultado) ou `SysGuard.exe --pipetest --silent`
(código de saída 0 = OK).

O menu mostra **Reparar instalação (administrador)…**, que reaplica sem trocar a senha (UAC).

Recuperação manual (se algo quebrar), em um prompt **administrador**:

```powershell
sc stop SysGuardWatchdogSvc
sc stop SysGuardSvc          # parar o principal já restaura hosts/DNS/políticas
sc delete SysGuardWatchdogSvc
sc delete SysGuardSvc
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v SysGuard /f
Remove-Item -Recurse -Force C:\ProgramData\SysGuard
```

O mesmo `sc stop` funciona no Gerenciador de Serviços (services.msc): parar um serviço só não
desliga a proteção, porque o outro o religa; parar os dois desliga.

A recuperação pelo próprio exe continua disponível **com o serviço fora** e agora exige a senha:

```powershell
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('sua-senha'))
SysGuard.exe --force-uninstall --silent --password $b64   # terminal elevado
```

Sem senha (ou com senha errada) ele recusa com código de saída 5 e **não** restaura nem apaga nada.
Se o arquivo de senha estiver ausente/corrompido, use a recuperação manual acima — é a saída
documentada para esse caso (quem é administrador consegue remover a pasta de qualquer forma).

## Integração com o Pomodoro Angry (Dynamic Island)

A ilha grava o estado vivo da sessão em
`C:\Users\<usuário>\AppData\Local\DynamicIsland\angry-live.json` (um arquivo por perfil, escrito a
cada mudança e a cada ≤5 s enquanto ativa). O serviço SYSTEM procura esse arquivo em **todos os
perfis de usuário**, no máximo uma vez a cada 3 s (cache), e a bandeja lê o do usuário logado.

Formato lido (tolerante a camelCase/PascalCase e a variações): `active`, `locked`, `endMs`/`endUtc`
(fim estimado da sessão), `savedAtMs`/`savedAtUtc`, e o objeto `timer` (`phase`, `remainingMs`,
`running`). O bloqueio extra vale enquanto `active && endMs > agora` — foco e intervalo:

- **Hosts:** bloco `# >>> DynamicIsland Angry: temporary distraction block >>>` com YouTube (e
  `youtu.be`), Twitter/X, Instagram e Reddit, em IPv4 e IPv6. Fica aplicado durante a sessão inteira;
  é removido quando a sessão termina. Na janela final de 60 s de um intervalo rodando
  (`locked=false`, `timer.phase="Break"`, `timer.running=true`, faltando ≤60 s), o bloco continua no
  hosts mas **sem** as linhas de Twitter/X e Instagram — é a única liberação. YouTube, Reddit e as
  camadas de conteúdo adulto continuam bloqueados. Intervalo parado esperando o Start manual
  (`running=false`) não libera nada.
- **Bandeja:** minimiza janelas de navegador cujo título cita esses sites (mesma heurística de
  títulos do Dynamic Island: `YouTube`, `Twitter`, `Home / X`, `Nome on X:`, `Instagram`, `Reddit`,
  `r/sub`). Nos 60 s finais do intervalo, só Twitter/X e Instagram deixam de ser minimizados.

Matar a ilha durante o Angry **não** desliga o bloqueio de rede até o fim previsto da sessão: o
serviço lê o arquivo em disco, não a memória da ilha. A janela de liberação também é recalculada a
cada ciclo a partir de `savedAtMs` + `timer.remainingMs`: um arquivo parado (ilha morta) libera no
máximo o que faltava dos 60 s e volta a bloquear sozinho, sem depender de nenhuma flag gravada.

## VPN/túnel e DoH (limites honestos)

O serviço detecta adaptadores de túnel ativos (Cloudflare WARP, Tailscale, WireGuard, OpenVPN…) e
DNS apontando para proxy local (ex.: `127.0.2.2/3`, típico do WARP). Ele **não derruba a VPN**: isso
quebraria o uso e exige decisões do dono da máquina. O que ele faz é avisar no `--status`
(sufixo `VPN`), no log (`%ProgramData%\SysGuard\logs\service.log`) e na bandeja: com VPN ativa, a
camada de DNS pode não valer para o tráfego dela.

Limitações conhecidas:

- **administrador da máquina vence**: `sc stop` dos dois serviços, take-ownership, Safe Mode,
  trocar o binário, editar o hosts como admin etc. O objetivo é fechar o caminho fácil e impulsivo.
- **VPN/proxy/WARP/Tailscale**, navegador portátil com DoH próprio ou DoH por IP fixo furam a camada
  de DNS; o hosts só bloqueia nomes. Uma rede com DNS interno corporativo também conflita com o DNS
  do filtro adulto; nesse caso desative a proteção com a senha e reative fora da rede.
- **`--force-uninstall`/`--reset`** exigem a senha atual/do gate local — é atrito, não barreira:
  `sc delete` e services.msc contornam de qualquer forma.
- **recovery actions** cobrem falha/crash, não parada limpa: é o vigia (serviço gêmeo) que cobre o
  `sc stop`. Parar os dois serviços ainda desliga.
- Não bloqueia conteúdo adulto em aplicativos que não sejam navegador (ex.: um app de mensagens);
  as camadas de rede/hosts ainda valem para os domínios bloqueados.
- **Filtro Adult x Family:** o Adult bloqueia pornografia no DNS e mantém SafeSearch de busca do
  Google/Bing, mas é menos rigoroso com conteúdo adulto "mixed" e não força modo restrito no
  YouTube (era exatamente o que escondia resultados). A lista de hosts cobre os domínios adultos
  conhecidos como reforço.
- SafeSearch de **busca** (Google/Bing) continua forçado — é busca, não o player do YouTube.

## Desenvolvimento

Projetos em `tools/` (fora da solução do Dynamic Island, de propósito):

- `tools/SysGuard/` — app (WinForms mínimo + dois hosts de serviço + P/Invoke).
- `tools/SysGuard.Tests/` — testes unitários; roda com `dotnet test` fora do sandbox, ou
  `dotnet run --project tools/SysGuard.Tests` (runner próprio para ambientes restritos).

Publicar o exe:

```powershell
pwsh -NoProfile -File tools/SysGuard/build.ps1
# ambiente sem internet/SSL, usando o cache local do NuGet:
pwsh -NoProfile -File tools/SysGuard/build.ps1 -Offline
```

O script roda os testes, publica `win-x64` self-contained single-file, roda o selftest do exe e
copia o resultado para `release-out/SysGuard/SysGuard.exe`. `SysGuard.exe --selftest` valida lista,
hosts (inclusive os blocos anti-DoH e do Angry), senha, troca de senha local, remoção forçada,
políticas (sem YouTube Restricted Mode e com limpeza dos valores legados), DNS Adult, títulos,
detectores de VPN, recuperação do SCM, vigia mútuo e o pipe de controle (este último é pulado em
processos de integridade baixa, como sandbox de desenvolvimento).

### Teste ponta a ponta (máquina real, terminal elevado)

`verify-install.ps1` instala em silêncio com uma senha de teste, confere os dois serviços, recovery,
hosts, DNS, políticas, autostart e pipe; verifica que **remoção forçada e `--reset` sem a senha são
recusados** e que **parar o serviço principal (`sc stop`) não desliga a proteção** (o vigia religa);
e desinstala no final:

```powershell
pwsh -NoProfile -File tools/SysGuard/verify-install.ps1 -Password "teste-123456"
```

Ele mexe de verdade no Windows da máquina onde roda (é o teste de instalação). Em caso de falha no
meio, o comando no bloco `.DESCRIPTION` do script limpa a máquina com a senha de teste.

Modos de automação do exe (sem interface; requerem administrador para instalar/forçar remoção):

- `--setup --silent --password <base64>` — instala com senha nova (recusa se já existe senha).
- `--setup --silent --password <base64> --reset --current-password <base64>` — troca a senha
  existente; **exige a senha atual** (sem ela, código 5 e nada muda).
- `--uninstall --silent --password <base64>` — desinstala pelo pipe (serviço no ar).
- `--force-uninstall --silent --password <base64>` — recuperação com o serviço fora; valida a senha
  direto no `config.dat` antes de qualquer mudança.
- `--status --silent` — códigos: 0 protegido, 5 degradado, 3 desprotegido, 4 indisponível.
- `--pipetest --silent` — valida o canal de controle.
- `--selftest` — diagnóstico interno do exe.
