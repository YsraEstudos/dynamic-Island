# Relatório final — red-team ofensivo e blindagem dos bloqueios

**Data:** 2026-11-11, 17:10 (America/Sao_Paulo)
**Autorização:** o usuário é dono de todos os aparelhos, contas e códigos abaixo e pediu explicitamente, neste
sessão, que subagentes "tentassem quebrar os bloqueios de todas as formas possíveis e inimagináveis" e que
"procurassem várias formas de bloquear a acessibilidade". Todo o trabalho foi feito **para defesa** do próprio
sistema de autocontrole (bloqueio de pornografia/redes sociais). Nenhum alvo de terceiros foi tocado.

## 0. Resumo em uma frase

Em 2–5 segundos, com um comando ADB ou três toques, o bloqueio do celular caía **silenciosamente** e o app
continuava dizendo "Ativo"; no PC, matar o `DynamicIsland.exe` no Gerenciador de Tarefas desligava o Pomodoro
Angry inteiro; o `SysGuard.exe` tinha `--force-uninstall` como bypass de senha e o filtro DNS Family restringia
o YouTube. **Nesta rodada** os subagentes provaram tudo isso com evidência, implementamos as defesas viáveis
(watchdog + guardião de segundo pacote no Android, estado vivo + guardião no exe do PC, ack real celular→PC,
SysGuard 1.1.0 sem o modo restrito) e re-testamos no aparelho real — com um limite honesto: quem é admin do
próprio aparelho (ADB/UAC) sempre consegue vencer; o objetivo é fechar o caminho fácil e impulsivo.

## 1. Resposta direta ao que foi pedido

| Pergunta do usuário | Resposta | Status |
|---|---|---|
| Dá para fugir do bloqueio do celular? | Sim, e era fácil: desligar a acessibilidade (2–5 s), `am force-stop` (2 s), `pm disable-user` (2 s), `run-as` (APK debug), navegador fora da lista, WebView interno (Telegram/Discord/Facebook), mudar a data/hora, segundo usuário | **Antes: sim. Depois: as vias fáceis foram fechadas ou detidas** |
| Dá para impedir o usuário de desligar a acessibilidade? | Não de forma absoluta sem Device Owner. Com o app vivo: **sim** (watchdog religa em ≤2 s). Depois de `force-stop`: **agora sim com o guardião** (religou em 16 s no teste real) | **Implementado e testado** |
| Dá para o app sobreviver a `force-stop`/Gerenciador de Tarefas? | Não dentro do mesmo pacote (o Android apaga o setting e recusa bind de pacote stopped). **Com o segundo pacote guardião: sim** (detecta + recupera; não impede o force-stop) | **Implementado e testado** |
| Dá para o `DynamicIsland.exe` sobreviver ao Task Manager? | Antes não: o Angry/timer/frase eram 100% em memória. **Agora**: estado vivo persistido a cada 5 s + processo guardião no mesmo exe que reabre a ilha e aplica a guarda de títulos enquanto ela está morta; se o `SysGuard` estiver instalado, ele mantém o bloqueio de rede mesmo com a ilha morta | **Código implementado, testes e publish+smoke OK** (não publicado/instalado no PC do usuário nesta rodada) |
| O `.exe` do PC resiste a Gerenciador de Tarefas/serviços/senha? | Resiste a matar a bandeja e a reboot; **antes** caia com `sc stop` (admin), `--force-uninstall --silent`, `--setup --reset`. **Agora** force-uninstall e reset exigem senha; segundo serviço `SysGuardWatchdogSvc` religa o principal se ele for parado | **SysGuard 1.1.0 gerado e testado** (exe pronto para o notebook) |
| A ilha só deve dizer "Celular bloqueado" com confirmação real? | Antes: só o HTTP 2xx do FCM. **Agora**: ack real do celular por ntfy (tópico derivado do token), ack com `applied/a11y/admin/level/endMs`; sem ack em 20 s mostra "NÃO aplicado"; heartbeat de 60 s mantém o selo | **Implementado (C#) e o ack foi validado no aparelho real** |

## 2. O que o red-team encontrou (relatórios completos em `docs/redteam/`)

### 2.1 Celular — Moto G20, Android 11 (`01-android-codigo.md` + `02-android-adb.md`)
- **A-01 (CRÍTICO):** 1 toque no interruptor "Modo seletivo" (sem senha) desligava o bloqueio de conteúdo adulto.
- **A-02 (CRÍTICO):** Ajustes → Acessibilidade → desligar "Serviço de Foco e Proteção" passava pelo anti-tamper
  (o guard procurava o nome "Foco & Bem-Estar"; a tela mostra o label do serviço) — **testado ao vivo: 2–5 s**.
- **A-03 (CRÍTICO):** `force-stop` deixava o serviço "enabled" mas **não bound**, sem watchdog e com o card
  dizendo "Ativo"; `enabled=1` era apagado pelo sistema. **Testado: não voltou em 77 s.**
- **A-05/D-04 (CRÍTICO):** APK instalado era **DEBUGGABLE** → `run-as` permitia ler/editar as preferências
  (apagar bloqueio, mudar senha/nível) com um comando ADB.
- **B-01/B-02/B-03 (CRÍTICO):** o bloqueio era "por pacote de 6 navegadores + texto de URL". Qualquer navegador
  fora da lista (Play Store), o navegador interno de Telegram/Discord/Facebook e mídia de canais do Telegram
  abriam pornografia sem nenhum evento inspecionado. WebViews/custom tabs não eram vistos.
- **C-01 (CRÍTICO):** mudar data/hora (página dos Ajustes sem o nome do app) encerrava o bloqueio adulto do PC,
  o bloqueio diário e zerava o lockout da senha.
- **C-03 (ALTO):** arquitetura 100% unidirecional: a ilha mostrava "Celular bloqueado" sem nenhuma confirmação.
- **D-01/D-02 (ALTO):** segundo usuário/perfil de trabalho não têm o app → bloqueio inexistente ali. **Testado:
  criar usuário funcionou, mas derrubou o `system_server` desta ROM (ver §7).**
- **A-06 (ALTO):** admin de dispositivo **não** bloqueia desinstalação fora de Device Owner (a UI prometia).
- **A-07:** atualização do APK desliga a acessibilidade e nada religava.
- **O que já resistia:** push monotônico (nunca encurta/libera bloqueio), adulto sem frase de liberação, bloqueio
  diário sobrevive a reboot, PBKDF2 310k + lockout, backup exclui prefs sensíveis, componentes perigosos não
  exportados, updater valida assinatura/versionCode.

### 2.2 PC — Dynamic Island/Angry + SysGuard (`03-pc.md`)
- **PC-01 (CRÍTICO):** matar `DynamicIsland.exe` no Gerenciador de Tarefas/`taskkill /f` desligava o Angry
  (timer, frase, guarda de títulos) — tudo em memória; sem serviço/watchdog.
- **PC-02 (CRÍTICO):** "Block phone during Angry" e "Reset to defaults" mudavam sem senha; `settings.json` editável.
- **PC-04/05 (CRÍTICO/ALTO):** navegador portátil renomeado/fora da lista de 15 processos e WebView2/Electron
  (Steam, VS Code, etc.) não eram cobertos; janela elevada (UIPI) é limite.
- **PC-13/14 (CRÍTICO):** SysGuard caía com `sc stop`/services.msc (admin/UAC) sem senha e
  `--force-uninstall --silent`/`--setup --reset` ignoravam a senha.
- **PC-17/18 (ALTO):** WARP/Tailscale rodando, DNS do Wi-Fi via WARP, DoH/proxy/IP direto/safe mode/WSL/VM furam
  a camada de rede.
- **O que já resistia:** saída pelo menu exige frase longa (mas a frase está na tela), fechar janela não encerra,
  instância única, SysGuard reaplica hosts+políticas a cada 3 s e DNS a ~15 s, resiste a reboot e a matar a bandeja.

## 3. O que foi implementado

### 3.1 Android — app `com.example.focusapp` v1.7.1 (versionCode 9) + guardião v1.0.1 — **INSTALADOS no Moto G20**
Principais defesas (staging em `docs/redteam/patches/android/` e `docs/redteam/patches/guardian/`):
1. **Watchdog `GuardWatchdogService`** (FGS specialUse, loop 10 s/60 s): religa a acessibilidade via
   `WRITE_SECURE_SETTINGS` (com polling e **toggle off→1,8 s→on**, que é o que rebinda depois de crash/force-stop),
   reativa admin quando possível, **não altera a depuração USB (anti-ADB removido em 2026-11-11 a pedido do dono: ele usa ADB+IA no PC)**,
   detecta multiusuário/perfis, manda heartbeat/acks e corrige o card "Ativo" (heartbeat real, não lista de enabled).
2. **Ack real celular→PC** (`AckClient`): POST em `ntfy.sh` com tópico `di-ack-<sha256("dynamic-island-ack-v1:"+token)[:32]>`
   e payload `{v,id,applied,a11y,admin,level,endMs,ts,gapMs,hb,reason,adb,users,profiles,guardian}`.
   O FCM agora confere a acessibilidade antes de considerar "aplicado" e notifica falha.
3. **Anti-tamper dos Ajustes corrigido** (A-02): casa os 3 rótulos + `className` de Acessibilidade/Admin/App info
   e bloqueia a tela, respeitando a janela de graça por senha.
4. **Senha mestra** para desligar o Modo seletivo, subir limite diário e zerar dívida (A-01/D-03).
5. **Âncora monotônica anti-relógio** (`elapsedRealtime` + boot_count) no bloqueio do PC/adulto/diário; detecta salto
   de relógio e mantém o bloqueio pelo tempo real (C-01).
6. **Detecção de conteúdo ampliada**: navegadores descobertos em runtime por `queryIntentActivities(VIEW http/https)`
   + lista ampliada; varredura conservadora de **host adulto em qualquer app** (Telegram/Discord/Facebook/WebView),
   sem bloquear por palavra solta (B-01/B-02).
7. **Build release não-debuggable** (fecha o `run-as`) e correção da promessa de desinstalação (só Nível 3/Device Owner).
8. **Guardião (segundo APK `com.example.focusapp.guardian`)**: overvive ao force-stop do principal; FGS + alarme
   exato + boot + admin próprio; consulta o estado real do principal via provider (permission signature), faz
   `startForegroundService` no `ReviveService`, reescreve o setting e, se preciso, **toggle de acessibilidade**;
   overlay de aviso e ack de falha; heartbeat. Também é acordado pelo app principal (mútuo).

- **Regra de produto (bloqueio de tela):** o overlay de tela cheia + frase só é aplicado no **Galaxy S23 Ultra** (SM-S918*); no **moto g(20)** nunca — implementado em `schedule/ScreenLockDevicePolicy.kt` com teste unitário e documentado em `docs/bloqueio-diario.md` do app.

### 3.2 PC — Dynamic Island (código no workspace, testado e com publish+smoke OK; **não publicado/instalado**)
- `AngryLiveState`/`AngryLiveStateStore`/`AngryLiveStateWriter`: `%LocalAppData%\DynamicIsland\angry-live.json`
  gravado a cada mudança + 5 s; retomada no startup normal (≤10 min, timer consistente).
- **Modo guardião no mesmo exe** (`DynamicIsland.exe --guardian <pidPai> <estado>`): processo oculto, reabre a ilha se
  ela morrer com sessão ativa e aplica ele mesmo a guarda de títulos (hooks Win32) enquanto ela está morta.
- **Confirmação real do celular**: `PhoneBlockAckListener` (ntfy streaming, tópico derivado do token) +
  `PhoneBlockTracker` (Awaiting→Confirmed/Failed, timeout 20 s, heartbeat 60 s). A ilha só mostra
  "Celular bloqueado ✓ HH:mm" com ack `applied && a11y` do id certo (ou heartbeat `guardian*`); falha → "NÃO aplicado: motivo".
- **PC-02/04/05**: settings de segurança travados durante o foco; matar a ilha + editar `settings.json` não desliga
  o bloqueio na retomada; detecção de navegador por **classe de janela** (Chromium/Gecko/IE) + msedgewebview2/steamwebhelper/electron.
- Validação: `dotnet test DynamicIsland.slnx` → **Core 1226/1226; Windows 474/477** (3 falhas
  `NamedPipeProgressFeedTests` são do sandbox DSH — `UnauthorizedAccessException` em `NamedPipeClientStream.Connect`,
  comprovadamente ambientais; ver evidência); publish Release + `--smoke-test` → **exit 0**.

### 3.3 SysGuard 1.1.0 — exe novo pronto para o notebook (`release-out/SysGuard/SysGuard.exe`)
- `--force-uninstall` agora exige senha (valida localmente, funciona com o serviço fora); `--setup --reset` exige a senha atual.
- Recovery do SCM (`restart 5 s/10 s/30 s`, reset diário) + **segundo serviço `SysGuardWatchdogSvc`** que religa o principal a cada 5 s.
- Integração com o Angry: lê `angry-live.json` de todos os perfis e, durante o Angry, acrescenta ao hosts
  youtube/twitter-x/instagram/reddit (bloco marcado removível) e o WindowGuard minimiza esses títulos.
- Anti-DoH no hosts (dns.google, cloudflare-dns.com, quad9, opendns, AdGuard/NextDNS/Mullvad…), detecção de VPN/Tunnel no STATUS.
- Testes: **168/168**; `build.ps1 -Offline` OK; `SysGuard.exe --selftest` **exit 0** (v1.1.0).

## 4. Testes de aceitação reais (evidências em `docs/redteam/evidence/`)

| # | Teste | Resultado |
|---|---|---|
| F1 | Desligar acessibilidade com o app vivo (`settings put secure ... ''`) | **religada em ~2 s** pelo watchdog (antes: silêncio total por 60 s+) |
| F2 | `adb shell am force-stop com.example.focusapp` com bloqueio ativo | **acessibilidade bound de novo em 16 s** pelo guardião (antes: não voltava) |
| F3 | `run-as com.example.focusapp` no APK release | **falha: "package not debuggable"** (antes: acesso total às prefs) |
| F4 | Depuração USB durante bloqueio | O **anti-ADB foi removido** (bug do modo seletivo derrubava o ADB em loop no S23). Agora `adb_enabled` continua `1` durante bloqueios |
| F5 | FCM real → ack ntfy | ack recebido: `{"v":1,"id":"0:…","applied":true,"a11y":true,"admin":true,"level":1,…}` |
| F6 | `pm disable-user` / revogar permissões | documentado: ainda **vence** (só Device Owner previne); ack reporta a falha |
| F7 | App release não-debuggable + versão | `versionName=1.7.1`, flags sem `DEBUGGABLE` |
| F8 | Estado final do celular | a11y **ligada e bound**, app + guardião admins **ativos**, user 0 único, ADB ligado, guardião rodando |

## 5. Correção do problema do YouTube no notebook (pedido desta rodada)

**Causa:** o SysGuard 1.0 usava o filtro DNS **CleanBrowsing Family** — que bloqueia pornografia **e força
YouTube Restricted Mode/SafeSearch** — e gravava `ForceYouTubeRestrict=2` no Chrome/Edge. Por isso o YouTube
ficou restrito e "vários resultados" sumiram.

**Correção no SysGuard 1.1.0:** filtro **CleanBrowsing Adult** (185.228.168.10 / 185.228.169.11; IPv6
2a0d:2a00:1::1 / 2a0d:2a00:2::1) — bloqueia pornografia, **não restringe o YouTube**; as políticas
`ForceYouTubeRestrict` foram removidas do Chrome/Edge e os valores antigos são **apagados do registro** na
atualização/restauração. `ForceGoogleSafeSearch`/`ForceBingSafeSearch` e o DoH desligado continuam.

**Passo a passo no notebook (o exe novo está em `release-out/SysGuard/SysGuard.exe`, SHA-256
`C86C66D4C35466514543836700A22BC53636C0374DF6766F2947B1DDF19379A3`):**
1. Copie o exe novo para o notebook (não sobrescreva o de `C:\ProgramData\SysGuard`).
2. Duplo clique → se aparecer "Já existe uma instalação… Atualizar/reparar? A senha é mantida" → **Sim** → aceite o UAC → **Reparar**.
3. Feche e reabra Chrome/Edge/Firefox (eles leem políticas ao iniciar). O YouTube volta ao normal; pornografia
   continua bloqueada por DNS Adult + hosts.
4. Confira em `chrome://policy` / `edge://policy`: `ForceYouTubeRestrict` não deve mais existir. Reinicie o PC
   para a bandeja nova subir também.

## 6. Achado operacional importante (token FCM da ilha)

O token configurado hoje em Settings → Phone na ilha **não é o token atual do Moto G20** (o token que estava nas
preferências do app é diferente do que está no `settings.json` da ilha — provavelmente é o do S23 Ultra ou um token
antigo). Na prática: **o bloqueio disparado pelo PC pode não estar chegando no Moto G20**. Para bloquear o Moto G20:
abrir o app → card *Bloqueio por horário* → **Renovar e copiar token do celular** → colar em Settings → Phone na ilha.
(Sem expor os valores: os prefixos dos dois tokens são diferentes.)

## 7. Incidente durante o red-team (transparência)

No teste do vetor "segundo usuário", `pm create-user`/`am switch-user` **derrubou o `system_server` desta ROM**
(`java.lang.IllegalStateException: No UserInfo for 10`, framework reiniciou). O aparelho se recuperou sozinho,
**não houve reboot completo nem perda de dados**, e o estado final foi verificado: user 0 único, a11y ligada+bound,
admins ativos. Não recomendamos repetir esse teste; o vetor é real, mas instável nesta ROM.

## 8. Riscos residuais (o que ainda vence)

| Vetor | Ainda funciona? | Por quê |
|---|---|---|
| ADB/root do dono | **Sim** | `pm clear`, `pm disable-user`, revogar `WRITE_SECURE_SETTINGS`, force-stop dos **dois** pacotes. O ack reporta a falha, mas não previne |
| UAC/admin no PC | **Sim** | `sc delete`, parar os dois serviços, safe mode, alterar registro. Recomendação: usar conta diária **não-admin** |
| Device Owner | **Único que previne de verdade** no Android | exige provisionamento/reprovisionamento (sem contas); decisão do usuário |
| Navegador com DoH/VPN/proxy, IP direto, WebView canvas, Telegram/MTProto | **Parcial** | rede/hosts/DNS ajudam, mas conteúdo servido por CDN/cripto/app não é filtrável localmente |
| Janela elevada (UIPI) | **Sim, no PC** | app não-admin não consegue ler/minimizar janela elevada; SysGuard (SYSTEM) cobre parte |
| Segundo usuário/perfil de trabalho | **Sim** | acessibilidade é por usuário; sem Device Owner não há como impedir a criação |
| Relógio (bloqueio diário manual) | **Mitigado** | âncora monotônica cobre os bloqueios do PC/adulto/diário; o "horário do dia" ainda é conceito de relógio de parede |
| Notificações mutadas | **Sim** | os canais do app estavam 100% mutados; por isso a falha também vai no ack (a ilha alerta) e o guardião usa overlay |

## 9. Recomendações práticas

1. **Ligar o "Modo seletivo"** no app (com a nova senha): hoje ele está **desligado** no Moto G20, e é ele que
   mantém Twitter/X + conteúdo adulto bloqueados 24 h, independente do PC. Agora desligá-lo exige senha.
2. **Renovar/colar o token do Moto G20** na ilha (§6) se quiser que o Angry bloqueie esse aparelho.
3. **Decidir sobre Device Owner** no Moto G20/S23 (única forma de impedir force-stop/disable/desinstalação de
   verdade; exige reprovisionar o aparelho sem contas).
4. No PC: usar uma **conta diária não-administradora** (a maior alavanca), remover WARP/Tailscale do uso diário,
   e instalar o SysGuard atualizado se quiser a camada de rede.
5. **Publicar a nova versão da ilha** quando quiser (o repositório já tem o código; basta pedir "publique uma
   versão nova" — o fluxo do CLAUDE.md roda o `release.ps1`, com testes e smoke). No Android, se quiser distribuir,
   subir `versionName`/tag do repo `app` e publicar a Release (v1.7.1 já está instalada localmente).

## 10. Artefatos

| Artefato | Caminho | SHA-256 |
|---|---|---|
| App Android 1.7.1 (release, não-debuggable) | `release-out/redteam/Foco-BemEstar-1.7.1-release.apk` | `8AA9DD08F37AD7FEA8099D5D6BB9ACE170B0C4B0D761248F83A9386AF3B09568` |
| Guardião Android 1.0.1 | `release-out/redteam/Foco-Guardiao-1.0.1-release.apk` | `2015D4341BB94AF5756EAEF1B338AB8B29509DC6A78EAC4C47B478F9925F9BC0` |
| SysGuard 1.1.0 (notebook) | `release-out/SysGuard/SysGuard.exe` | `C86C66D4C35466514543836700A22BC53636C0374DF6766F2947B1DDF19379A3` |
| Relatórios | `docs/redteam/01-android-codigo.md`, `02-android-adb.md`, `03-pc.md` | — |
| Plano e patches | `docs/redteam/00-plano-de-blindagem.md`, `patches/android/`, `patches/guardian/` | — |
| Evidências de aceite | `docs/redteam/evidence/{acceptance-android,guardian-force-stop,fcm-e2e-android}.txt` | — |

**Reverter o Android:** os arquivos alterados no repo `app` têm backups `*.bak-<timestamp>` ao lado; o APK antigo
v1.6 está em `%TEMP%\dsh-redteam\focusapp-v1.6-base.apk` (SHA-256 `D7188B6A…C7E571`). O guardião pode ser
desinstalado com `adb uninstall com.example.focusapp.guardian`.

## 11. Relatórios por agente (para leitura detalhada)
- `docs/redteam/01-android-codigo.md` — 30 vetores, análise estática com `arquivo:linha`, respostas Q1–Q3, propostas Kotlin.
- `docs/redteam/02-android-adb.md` — 13 testes reais no Moto G20, teste-chave do force-stop, design validado, tempos medidos.
- `docs/redteam/03-pc.md` — 25 vetores no PC, SysGuard, FCM/ack, blindagem por camada.



## 12. Atualizações desta rodada (2026-11-11, pós-entrega)

- **Regra de produto registrada e implementada:** o bloqueio de tela (overlay + frase) só é aplicado no **Galaxy S23 Ultra** (SM-S918*); no **moto g(20)** nunca. Documentado em `Projetos\app\docs\bloqueio-diario.md` e `README.md` do app; código em `schedule/ScreenLockDevicePolicy.kt` com teste `ScreenLockDevicePolicyTest`.
- **Anti-ADB removido:** decisão do dono (usa ADB + IA no PC). O watchdog não escreve mais em `ADB_ENABLED`/`ADB_WIFI_ENABLED`. Havia um bug: o "Modo seletivo" ligado contava como bloqueio ativo e o anti-ADB desligava a depuração em loop (aconteceu no S23 após o update). A correção saiu na **v1.7.3**.
- **S23 Ultra atualizado para a v1.7.3 + guardião 1.0.1** (instalado e configurado nesta sessão): acessibilidade ligada (preservando Bitwarden), admins ativos, notificações concedidas, ADB permanece ligado. Testes no S23: watchdog religou a acessibilidade em **1 s** e o guardião reviveu após `force-stop` em **3 s**.
- **Token FCM da ilha está expirado:** o token configurado hoje responde **404 Not Found** no FCM (não é o token atual do S23 nem do Moto G20). Para o PC voltar a bloquear um celular: no app → "Renovar e copiar token do celular" e colar em Settings → Phone da ilha.
- **Moto G20:** continua na v1.7.1 (instalada antes desta rodada). Falta instalar a v1.7.3 nele para remover o anti-ADB e aplicar a regra de bloqueio de tela (o APK está em `release-out/redteam/Foco-BemEstar-1.7.3-release.apk`). Precisa reconectar USB ou parear a depuração sem fio.

### 12.1 Correção do acesso aos dados de uso (2026-11-11, S23)

- **Bug:** o anti-tamper de Ajustes casava o nome do app em qualquer tela de Settings; a tela "Acesso aos dados de uso" (className genérica ``SubSettings``, mostra "Foco & Bem-Estar") era tratada como tamper e o app expulsava o usuário para a launcher.
- **Correção (v1.7.5):** allowlist por título de telas legítimas de permissão ("Acesso aos dados de uso", "Usage access", "Acesso especial", "Special access", "Otimização da bateria", "Battery optimization", "Notificações do app") antes do match por rótulo, com testes de regressão em ``FocusBlockRulesHardeningTest``. A defesa de Acessibilidade/Admin/Ficha do app continua valendo.
- **Extras concedidos por ADB no S23:** ``GET_USAGE_STATS=allow``, ``SCHEDULE_EXACT_ALARM=allow`` e whitelist de bateria (além de WRITE_SECURE_SETTINGS, overlay, notificações e admins).
- **S23:** app agora na **v1.7.5** (instalada) + guardião 1.0.1; a11y ligada preservando Bitwarden; ADB permanece ligado.
- **Moto G20:** continua na v1.7.1 — falta instalar a v1.7.5 (``release-out/redteam/Foco-BemEstar-1.7.5-release.apk``) quando reconectado.

- **v1.7.6 (2026-11-11):** correção definitiva do bug de "Acesso aos dados de uso": o anti-tamper parou de usar o **nome do app** como gatilho (ele aparece em várias telas legítimas de permissão). Agora só bloqueia com evidência forte: rótulo do serviço "Serviço de Foco e Proteção", classe de Acessibilidade/Admin, títulos de Admin ou ficha do app com ações destrutivas. Teste com tela acesa no S23: `ResumedActivity = Settings$UsageAccessSettingsActivity` permaneceu por vários segundos (antes voltava para a launcher em ~0,7 s).
- **S23 atualizado para v1.7.6** (com testes de regressão para a tela de dados de uso/bateria). `GET_USAGE_STATS=allow`, `SCHEDULE_EXACT_ALARM=allow`, whitelist de bateria, a11y ligada (Bitwarden preservado), ADB ligado.
- **Moto G20:** ainda na v1.7.1; instalar a **v1.7.6** quando reconectado (``release-out/redteam/Foco-BemEstar-1.7.6-release.apk``).

### 12.2 Incidente 2026-11-12: Angry no PC não bloqueava o S23 (token FCM morto) — resolvido

- **Sintoma:** sessão ativa na ilha do PC e o celular (S23) não bloqueava. O log da ilha mostrava `FCM answered 404` (08:33, 08:53, 09:16, 09:26): o token em Settings → Phone apontava para um registro que o Google aceitava em formato mas devolvia `404 NotRegistered / UNREGISTERED` — token de instalação antiga/restaurado de backup, que não entrega no aparelho atual.
- **Diagnóstico:** leitura do estado real do app no S23 (`content://com.example.focusapp.protection/state`, campo `fcmToken`) com um APK auxiliar assinado com a mesma chave; envio direto ao FCM confirmou o `404 UNREGISTERED`.
- **Correção aplicada:** no S23, "Renovar e copiar token do celular" (`deleteToken` + `getToken` → token novo; `onNewToken` registrado). O token novo foi gravado no `settings.json` da ilha com DPAPI (backups `settings.json.bak-20261112` e `settings.json.bak2-20261112`) e o app foi reiniciado. A ilha passou a enviar **HTTP 200** e o S23 respondeu ack `applied=true, a11y=true, level=2` (evidência `docs/redteam/evidence/fcm-webhook-check-20261112.txt`; sondagens em `fcm-token-refresh-20261112.txt`, `fcm-block-e2e-20261112.txt`, `fcm-delivery-check-20261112.txt`).
- **Modo seletivo (atenção):** o S23 está com **Modo seletivo ligado** — por design, o Angry do PC **não trava a tela**; ele só mantém Twitter/X + conteúdo adulto (que o modo seletivo já bloqueia 24 h). Para o Angry voltar a travar a tela/Instagram/Reddit é preciso desligar o Modo seletivo no app (exige a senha mestra).
- **ntfy 429:** durante o diagnóstico o ntfy.sh devolveu `429` (cota diária de mensagens por IP) para acks/heartbeats; o bloqueio em si não depende do ack. Na próxima versão do PC, o `ackUrl` LAN deve ser o canal primário do listener de ack.
- **Segurança (achado):** o APK release do app Android é assinado com a **debug keystore pública** (`app/debug.keystore`, SHA-256 `17289d37…5bcf`); por isso a permissão `signature` `PROTECTION_CONTROL` não isola o provider — qualquer app assinado com a debug key lê o `fcmToken` (foi assim que o diagnóstico leu o estado). Recomendado criar keystore de release própria; a troca exige reinstalar app + guardião e reativar os admins.
