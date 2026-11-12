# Patch Android v1.7 — Blindagem Fase 1 (`com.example.focusapp`)

**Autor:** agente `android-hardening` (mesmo time do Lead) · **Data:** 2026-11-11
**App-alvo:** `com.example.focusapp` ("Foco & Bem-Estar", Kotlin/Compose, `minSdk 30`, `targetSdk 35`)
**Base:** working tree v1.6 do repo `C:\Users\israe\OneDrive\Desktop\Projetos\app` + relatórios
`01-android-codigo.md` (estático) e `02-android-adb.md` (operacional), incorporados abaixo.

> **Staging:** este pacote NÃO foi compilado nem instalado por mim (o Lead aplica/builda com permissão
> elevada; o repo Android é somente leitura para o agente). Os arquivos de `files/` são conteúdo COMPLETO
> dos arquivos novos/alterados, prontos para copiar por cima.

---

## 1. Aplicar

```powershell
# dry-run primeiro (não escreve nada)
pwsh -NoProfile -File .\apply.ps1 -DryRun

# aplicar de verdade (faz backup .bak-<timestamp> de cada arquivo alterado)
pwsh -NoProfile -File .\apply.ps1
# ou: pwsh -NoProfile -File .\apply.ps1 -RepoPath 'D:\caminho\do\repo'
```

O `apply.ps1` só copia os arquivos listados em `files/`; não apaga nada, ignora arquivos idênticos
(idempotente) e valida que o destino é o repo Android. Para reverter: copiar de volta cada `.bak-*`
listado no resumo, ou `git checkout -- .` no repo.

---

## 2. O que mudou (arquivos)

### Novos

| Arquivo | Papel |
|---|---|
| `guard/AccessibilityGuard.kt` | Religar acessibilidade via `Settings.Secure` preservando a lista + polling/resultado com motivo; admin; ADB ligar/desligar/restaurar; usuários/perfis; `bootCount`. **Não assume `WRITE_SECURE_SETTINGS`** — checa e devolve `Failed(no_write_secure_settings)`. |
| `guard/FocusEnforcementState.kt` | Heartbeat real do serviço de acessibilidade (`enabled ≠ bound`) — A-03. |
| `guard/GuardPolicy.kt` | Regras puras (intervalos 10 s/60 s, throttle de alertas, nível 0/1/2, ADB, perfis). Testável sem Android. |
| `guard/GuardState.kt` | Snapshot do que está ativo (adulto/pomodoro/diário/seletivo) + flags de ADB + âncoras monotônicas do bloqueio do dia + throttle. |
| `guard/GuardNotifier.kt` | Canais/notificações: status persistente do watchdog e alertas de alta prioridade ("BLOQUEIO NÃO APLICADO", permissão ADB, admin, perfis, relógio, ADB off). |
| `guard/GuardianBridge.kt` | Contrato da Fase 2: detecta/inicia `com.example.focusapp.guardian`; reporta `guardian:false` no ack quando ausente. |
| `guard/ProtectionStateProvider.kt` | Provider `content://com.example.focusapp.protection/state` (permissão `signature`) com o JSON de estado para o guardião da Fase 2. |
| `guard/ReviveService.kt` | Serviço exportado (permissão `signature`, action `com.example.focusapp.action.REVIVE`): sobe o watchdog, garante acessibilidade, tenta reativar admin, reagenda alarme, manda ack `reason=revive` e para (`START_NOT_STICKY`). É a porta que o guardião usa para limpar o estado *stopped*. |
| `service/GuardWatchdogService.kt` | Foreground service `specialUse`, `START_STICKY`, loop 10 s (bloqueio ativo) / 60 s (ocioso). |
| `push/AckPayload.kt` | Tópico ntfy (`di-ack-` + 32 hex de SHA-256) + JSON do ack. Puro/testável. |
| `push/AckClient.kt` | `POST` LAN (`ackUrl` do FCM) → fallback `https://ntfy.sh/<tópico>`; thread própria, timeouts, nunca lança. |
| `push/AckPayloads.kt` | Monta o ack a partir do estado REAL (snapshot + heartbeat). |

### Alterados

| Arquivo | Mudança |
|---|---|
| `service/FocusAccessibilityService.kt` | Heartbeat 20 s; inicia o watchdog; anti-tamper casando **os três rótulos** (`app_name`, `accessibility_service_name` = "Serviço de Foco e Proteção", package) + className (Accessibility/DeviceAdmin/App info do próprio pacote); descoberta de navegadores em runtime; varredura genérica de URL adulta em qualquer app (conservadora); `onUnbind` manda ack `a11y_off` e acorda o watchdog. |
| `service/FocusBlockRules.kt` | Navegadores fixos ampliados (Fenix, Firefox Beta, Focus, DuckDuckGo, Vivaldi, Kiwi, Yandex, UC, Opera GX/Mini, Chrome beta/dev/canary, Chromium, Ecosia, Bromite) + `discoverBrowserPackages(ACTION_VIEW http/https)`; hosts adultos ampliados (brasileirinhas, sexlog, missav, javdb, fatalmodel, coelhinhas etc.); `containsAdultDomain`/`firstAdultDomainIn` (só host, nunca palavra solta); `shouldScanAnyAppPackage`; `isProtectionTamperScreen`. |
| `push/PomodoroPushService.kt` | Garante acessibilidade antes de considerar aplicado, acorda watchdog, ack `applied/failed` + notificação de alta prioridade quando falha, `gapMs` se o PC mandar `sentAtMs`. |
| `schedule/DailyBlockPolicy.kt` | `PomodoroBlockRecord` ganhou âncora monotônica (`endElapsedRealtime`/`startElapsedRealtime`); `shouldBlockAt`/`nextBoundaryMs`/`isAdultBlockActive`/`shouldShowPomodoroOverlay` usam a âncora quando existe e continuam aceitando os mesmos argumentos de antes (defaults) — C-01. |
| `schedule/DailyBlockStore.kt` | Grava âncora monotônica do pomodoro e do adulto com `boot_count` (âncora inválida após reboot); `reanchorPomodoro`/`reanchorAdult`. |
| `schedule/DailyBlockController.kt` | Detecção de salto de relógio (wall × monotônico) → aviso + ack + reancoragem; bloqueio do dia ancorado em `elapsedRealtime` para não terminar por relógio adiantado; inicia o watchdog quando um bloqueio começa. |
| `schedule/DailyBlockScheduler.kt` | Alarme de fim do pomodoro calculado pelo relógio monotônico quando disponível. |
| `receiver/BootReceiver.kt` | No boot e em `MY_PACKAGE_REPLACED` inicia o watchdog **e** garante a acessibilidade em background com ack (A-07). |
| `MainActivity.kt` | Inicia o watchdog no `onResume`; pede isenção de otimização de bateria uma vez por processo (sem ela Doze atrasa o loop). |
| `ui/DailyBlockCard.kt` | A-03: "Ativo" só com heartbeat; A-01: **senha mestra para desligar o Modo seletivo**; texto de adulto/pomodoro usa a âncora monotônica. |
| `ui/DashboardViewModel.kt` | `hasFocusAccessibility` = `FocusEnforcementState.isEnforcing` (enabled ≠ bound). |
| `ui/DashboardPermissions.kt` | A-06: promessa da UI corrigida — admin **não** bloqueia desinstalação fora de Device Owner (Nível 3). |
| `ui/FocusDashboardScreen.kt` | D-03: senha para **subir** o limite diário e para **zerar** a dívida (reduzir/pagar continuam livres). |
| `AndroidManifest.xml` | `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`; `<permission> PROTECTION_CONTROL (signature)`; `uses-permission` `GUARDIAN_CONTROL`; `GuardWatchdogService`; `ReviveService` exportado + intent-filter `com.example.focusapp.action.REVIVE`; provider do estado. |
| `app/build.gradle.kts` | `versionCode 8`, `versionName "1.7"`. **Nenhuma dependência Gradle nova.** |

Testes novos (`app/src/test/java/...`): `guard/GuardPolicyTest.kt`, `push/AckPayloadTest.kt`,
`service/FocusBlockRulesHardeningTest.kt`, `schedule/ClockJumpPolicyTest.kt`. Os testes existentes
(`FocusBlockRulesTest`, `SelectiveBlockModeTest`, `DailyBlockPolicyTest`, ...) continuam válidos — as
assinaturas antigas foram preservadas com parâmetros default.

---

## 3. Contratos (para o PC e para o guardião da Fase 2)

### Ack do celular (`AckClient`)

Tópico ntfy: **`di-ack-` + os 32 primeiros hex** de `SHA-256("dynamic-island-ack-v1:" + tokenFCM)`.
O token é o que a ilha já recebe/guarda; o tópico é segredo de capacidade (não exponha em texto/UI).
Corpo publicado em `https://ntfy.sh/<tópico>` (corpo cru = a string JSON; o PC faz o parse da mensagem):

```json
{"v":1,"id":"<blockId|messageId|heartbeat>","applied":true,"a11y":true,"admin":true,
 "level":1,"endMs":1234567890123,"ts":1234567890456,"gapMs":123,
 "hb":false,"reason":"fcm","adb":false,"users":1,"profiles":1,"guardian":false}
```

- `applied` = heartbeat do serviço de acessibilidade fresco **e** `level > 0`.
- `level`: `0` = nada ativo; `1` = modo seletivo/bloqueio adulto; `2` = pomodoro/horário de tela cheia.
- `hb:true` = heartbeat a cada ~60 s enquanto a proteção é esperada.
- `reason`: `fcm`, `heartbeat`, `a11y_off`, `a11y_restored`, `admin_off`, `admin_on`, `adb_off`,
  `adb_restored`, `boot`, `package_replaced`, `clock_change`, `profiles`, `revive`.
- `adb`/`users`/`profiles`/`guardian` são diagnósticos extras (PC antigo pode ignorar).
- Se o FCM trouxer `ackUrl` (LAN, ex.: `http://192.168.0.10:8787/phone-ack`), o app tenta primeiro esse
  POST; ntfy é fallback. Não há HMAC nesta fase (o tópico derivado do token é o segredo de capacidade).

Derivar o tópico no PC (PowerShell):

```powershell
$token = '<token FCM do celular>'
$alg = [Security.Cryptography.SHA256]::Create()
$hash = ($alg.ComputeHash([Text.Encoding]::UTF8.GetBytes("dynamic-island-ack-v1:$token")) |
         ForEach-Object { $_.ToString('x2') }) -join ''
$topic = 'di-ack-' + $hash.Substring(0,32); $topic
# assinar: curl.exe -N "https://ntfy.sh/$topic/sse"
```

O PC deve mostrar "Celular bloqueado ✓" só com ack **fresco (< 90 s)** e `a11y && applied`.

### Provider para o guardião (Fase 2)

- authority `com.example.focusapp.protection`, URI `content://com.example.focusapp.protection/state`;
- `query` → 1 linha, coluna String `json`:
  `{"v":1,"protectionActive":bool,"a11yEnabled":bool,"adminActive":bool,"fcmToken":"<token>","blockEndMs":<long|null>}`;
- protegido por `android:permission="com.example.focusapp.permission.PROTECTION_CONTROL"` (signature).
- `ReviveService`: exportado com a mesma permissão, action `com.example.focusapp.action.REVIVE`.
  Ordem do guardião: **launch explícito do app/ReviveService (limpa *stopped*) → escrever settings →
  overlay de fallback**. O ReviveService também sobe o watchdog, tenta admin, reagenda alarme e manda ack.
- O app principal, no watchdog, tenta acordar o guardião via
  `Intent().setClassName("com.example.focusapp.guardian","com.example.focusapp.guardian.GuardianService").setAction("com.example.focusapp.guardian.action.START")`
  (exige `<uses-permission android:name="com.example.focusapp.guardian.permission.GUARDIAN_CONTROL"/>`, já no manifest)
  e reporta `guardian:false`/`true` no ack, além de checar `pm.getPackageInfo("com.example.focusapp.guardian")`.

---

## 4. Build e instalação (o que o Lead deve rodar)

```powershell
$env:JAVA_HOME = "$HOME\.gradle\jdks\eclipse_adoptium-17-amd64-windows.2"
cd C:\Users\israe\OneDrive\Desktop\Projetos\app

# 1) testes unitários (novos + antigos)
.\gradlew.bat :app:testDebugUnitTest

# 2) APK de produção (release = NÃO debuggable; assinado com debug.keystore como na v1.6,
#    para o auto-update continuar aceitando)
.\gradlew.bat :app:assembleRelease

# 3) instalar (NÃO desinstalar: mantém prefs/senha/admin)
adb -s ZF523QVD4J install -r app\build\outputs\apk\release\app-release.apk

# 4) ⚠️ OBRIGATÓRIO para o watchdog religar acessibilidade/ADB (hoje a permissão NÃO está concedida):
adb -s ZF523QVD4J shell pm grant com.example.focusapp android.permission.WRITE_SECURE_SETTINGS
adb -s ZF523QVD4J shell dumpsys package com.example.focusapp | Select-String WRITE_SECURE_SETTINGS

# 5) conferir que o release é não-debuggable (esperado: sem DEBUGGABLE)
adb -s ZF523QVD4J shell dumpsys package com.example.focusapp | Select-String 'flags='

# 6) abrir o app 1x para iniciar o watchdog (MainActivity.start) e conferir:
adb -s ZF523QVD4J shell dumpsys activity services com.example.focusapp | Select-String GuardWatchdog
adb -s ZF523QVD4J shell dumpsys notification --noredact | Select-String 'Watchdog'
```

Sem o grant do passo 4 o app **compila e roda**, mas o watchdog só consegue *detectar e notificar*
(ack `a11y=false` + alerta "Permissão ADB necessária"); ele não religa a acessibilidade nem desliga ADB.
O restart do serviço após o update pode exigir abrir o app uma vez (o `MY_PACKAGE_REPLACED` tenta
religar em background, mas depende do grant acima e da política do Android).

---

## 5. Roteiro de aceitação (no Moto G20)

Pré: app instalado, aberto uma vez, acessibilidade ligada e bound, admin ativo, ADB ligado.

| # | Teste | Como | Aceite |
|---|---|---|---|
| F1 | **Watchdog religa acessibilidade (app vivo)** | `adb shell settings put secure enabled_accessibility_services ''` (o sistema zera `accessibility_enabled`) | Em **≤ 15 s** a acessibilidade volta a `enabled=1` e `bounded`, notificação do watchdog muda para "Proteção ativa", log sem crash. Sem o grant do §4 o aceite é: alerta "Permissão ADB necessária" + ack `a11y=false` |
| F1b | **Lista preservada** | Antes, habilitar TalkBack (ou qualquer outro serviço) e rodar o F1 | O `enabled_accessibility_services` final contém o outro serviço **e** o Focus (não sobrescreve a lista) |
| F2 | **Force-stop: limite honesto Fase 1** | `adb shell am force-stop com.example.focusapp` | O serviço **não** volta sozinho (o sistema apaga o setting e recusa bind de pacote *stopped*). Não é regressão: Fase 2. Ao abrir o app, o watchdog sobe e o `ReviveService`/acessibilidade religa |
| F2b | **ReviveService (ponte da Fase 2)** | Só o guardião (mesma assinatura) consegue chamar. Via adb o shell **não tem** a permissão signature — não usar `am startservice` como teste. Testar quando o APK guardião existir | Após force-stop, o guardião chama o revive → heartbeat/ack `reason=revive` e a11y bound |
| F3 | **Tela de Acessibilidade bloqueada** | Nível 2 ligado, sem senha/tolerância: abrir Ajustes → Acessibilidade → Serviço de Foco e Proteção | O app volta ao HOME e abre o MainActivity; nada de "desligar" sem antes autenticar (a janela por senha abre as configurações legitimamente e **não** deve ser bloqueada) |
| F3b | **Admin/Detalhes do app bloqueados** | Ajustes → Segurança → Apps de administração; Ajustes → Apps → Foco & Bem-Estar | Mesma reação (HOME + app). Ficha de **outros** apps **não** pode ser bloqueada |
| F4 | **Release não-debuggable** | `adb shell run-as com.example.focusapp ls` | Erro "not debuggable" (fecha o bypass de `run-as` do relatório 02) |
| F5 | **Ack real (ntfy)** | Calcular o tópico (§3) e escutar `curl.exe -N "https://ntfy.sh/<tópico>/sse"`; mandar FCM do PC | Chega `applied:true,a11y:true` em ≤ 20 s e heartbeat `hb:true` a cada ~60 s enquanto houver bloqueio; com acessibilidade off, chega `applied:false` + motivo |
| F6 | **Depuração USB preservada** | Com bloqueio ativo: `adb shell settings get global adb_enabled` | Continua `1`; o app **não altera mais** ADB (anti-ADB removido em 2026-11-11: derrubava a sessão ADB+IA do dono e, por bug, o modo seletivo o disparava em loop) |
| F7 | **Relógio adiantado (C-01)** | Com bloqueio adulto/pomodoro ativo, adiantar data/hora | O bloqueio **continua** (âncora monotônica); notificação "Relógio alterado"; ack `reason=clock_change`; ao voltar o relógio, os fins de parede são reancorados |
| F8 | **Percentual/UI honesta (A-03)** | Desligar a acessibilidade pelo toggle (com o app vivo) | O card e o hub de permissões deixam de dizer "Ativo" imediatamente (heartbeat) e o watchdog religa em ≤ 15 s |
| F9 | **Perfis (D-01/D-02)** | Criar usuário/perfil de trabalho e trocar | Enquanto houver bloqueio ativo, notificação "Perfil sem proteção" e ack `reason=profiles`. Não repetir o teste de `pm create-user` do relatório 02 (derrubou o `system_server`) |
| F10 | **Senha no Modo seletivo (A-01)** | Card "Bloqueio por horário" → desligar "Modo seletivo" | Abre diálogo de senha; sem senha correta não desliga (e o bloqueio adulto continua) |
| F11 | **D-03** | Dashboard → subir limite diário / zerar dívida | Abre diálogo de senha; reduzir limite e pagar dívida continuam livres |

Smoke mínimo de fumaça: `adb shell dumpsys accessibility` mostra o serviço `FOCUS_BOUND`; `dumpsys
activity services` mostra `GuardWatchdogService`; `adb logcat -s FocusAccessibility GuardWatchdogService AckClient`.

---

## 6. Decisões e riscos (honestos)

1. **`WRITE_SECURE_SETTINGS` não estava concedida** (achado do relatório 02, contrariando o briefing).
   Todo o reparo de acessibilidade/ADB depende dela. O código **checa antes** e reporta o motivo; o Lead
   precisa do `pm grant` do §4. Sem ela: o app detecta, notifica e manda ack — mas não religa nada.
2. **`force-stop` não é recuperável dentro do próprio app** (teste T4 do relatório 02: o Android apaga o
   setting e recusa bind de pacote *stopped*; `am start` sozinho não religa). A Fase 1 entrega a ponte
   (`ReviveService` + provider + guardião iniciável) e o watchdog para "app vivo". A recuperação real
   pós-force-stop é a Fase 2 (segundo pacote) — a limitação está explícita aqui e no ack (`guardian:false`).
3. **Reativar admin programaticamente** (`dpm.setActiveAdmin`) costuma ser recusado pelo sistema em
   builds de produção. O código tenta em try/catch e, ao falhar, mantém o alerta/ack e o caminho de UI
   (`ACTION_ADD_DEVICE_ADMIN`). Não é regressão: antes nem tentava.
4. **Anti-ADB desliga a depuração durante o bloqueio.** Isso derruba a sessão adb do próprio dono
   enquanto durar o bloqueio (por design do usuário: "anti-ADB durante bloqueio"). O valor anterior é
   restaurado ao fim; ⚠️ se o processo morrer no meio, o `onDestroy`/próximo start restaura, mas o teste
   de aceitação deve usar bloqueio curto.
5. **Âncora monotônica ≠ a prova de reboot:** `elapsedRealtime` zera no reboot; por isso a âncora grava
   `boot_count` e é descartada quando o boot muda (nesse caso vale o wall-clock, que após reboot está
   correto). Relógio adiantado **não** encerra o bloqueio ancorado; relógio atrasado pode alongá-lo
   (fallback), o que é o lado seguro.
6. **Notificações podem estar mutadas pelo usuário** (observado: canais mutados no Moto G20). Por isso
   toda falha também vai no ack e nos prefs; a notificação é a camada visível, não a única.
7. **Doze/bateria:** o loop de 10 s pode atrasar com tela apagada/Doze; a isenção de bateria é pedida no
   `MainActivity` (uma vez). Atraso é degradação, não bypass (eventos de janela/tela ligam reavaliam).
8. **ntfy.sh é serviço público de terceiros.** O tópico é derivado do token FCM via SHA-256 (128 bits
   efetivos) e não é exibido na UI; nenhum conteúdo sensível vai no ack. Se preferir, o PC pode mandar
   `ackUrl` LAN no FCM e o app tenta primeiro.
9. **Provider expõe o token FCM** — por isso é `signature` (`PROTECTION_CONTROL`). Não remova a permissão
   nem torne o provider legível por terceiros.
10. **Falsos positivos do anti-tamper**: o guard bloqueia as telas de Acessibilidade/Admin/Detalhes do
    app quando o nível é 2+ e o texto/classe casa; a janela de tolerância por senha (2 min) libera o uso
    legítimo. A ficha de apps alheios não é bloqueada.
11. **Varredura genérica de URL adulta é conservadora** (só host da lista; sem palavra solta). Não cobre
    imagens de busca, vídeo em canal do Telegram ou content local — riscos residuais do relatório 01
    (B-03/B-04/B-05), cuja mitigação é decisão de produto/rede, não da Fase 1.
12. **Build release** continua assinado com `debug.keystore` versionado (mantido de propósito para o
    auto-update da v1.6). O ganho imediato da Fase 1 é `debuggable=false` (fecha `run-as`); trocar a
    chave/keystore fica para a decisão de higiene (D-04) do Lead.
13. **Nada de segredos** foi adicionado; nenhum token/chave aparece no patch ou em log.

## 7. Pendências / Fase 2 (não implementado aqui)

- APK guardião `com.example.focusapp.guardian` (FGS + alarme + overlay de fallback + launch explícito);
  contrato já pronto neste app (`ReviveService`, `ProtectionStateProvider`, `GuardianBridge`).
- Gate de instalação de APK/Play Store (B-01/B-09), decisão sobre apps de mensagem (B-03), camada de
  rede/DNS (B-04/B-05/B-07), Device Owner para `setUninstallBlocked`/`DISALLOW_*` (A-04/A-06/D-01/D-02).
- Teste real do relaunch pós-force-stop pelo guardião (recomendação nº1 do relatório 02).

