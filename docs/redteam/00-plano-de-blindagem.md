# Plano de blindagem (Lead) — v1 (rascunho de trabalho)

> Este documento é o plano de implementação. Será atualizado com os IDs/severidades dos relatórios
> `01-android-codigo.md`, `02-android-adb.md` e `03-pc.md`.
> Regra: cada defesa abaixo tem um **teste de aceitação** verificável no aparelho/máquina real.

## Fatos já comprovados nos testes (13:47–13:54)

| # | Experimento | Resultado | Consequência para o design |
|---|---|---|---|
| F1 | `settings put secure enabled_accessibility_services ''` + `accessibility_enabled 0` | app **não reage**: processo segue vivo (pid 6968), serviço unbound por 60 s+, nenhum aviso/religa | watchdog próprio resolve essa via (app continua vivo) |
| F2 | `am force-stop com.example.focusapp` | processo morre, serviço **não volta** (77 s) | só um **segundo pacote** (guardião) pode reviver; um serviço do próprio app não roda morto |
| F3 | com app force-stopped, reescrever `enabled_accessibility_services` + `accessibility_enabled=1` via adb | `enabled=1` mas `bound=NO` por 87 s; dumpsys mostra `Crashed services: {FocusAccessibilityService}` e `Enabled services:{}` | escrever o setting **não basta**: é preciso limpar o estado de force-stop com um **componente explícito** (activity/serviço exportado). O guardião terá esse componente. |
| F4 | app é **debuggable** (`run-as` funcionou) e `ALLOW_CLEAR_USER_DATA` | `run-as com.example.focusapp` lê/escreve prefs (ex.: apagar `adult_end_ms`) | instalar build **release não-debuggable** e assumir que adb ainda pode `pm clear` |
| F5 | FCM payload só grava; `PomodoroPushService` não checa acessibilidade | ilha mostra "Celular bloqueado" só com HTTP 2xx do FCM | ack real por ntfy + LAN; a ilha só confirma "bloqueado" com ack |
| F6 | Angry é só memória + `angry-resume.json` gravado **apenas no update** | matar processo pelo Gerenciador de Tarefas perde a sessão e o guarda | gravar estado vivo periodicamente + guardião secundário no PC + integração com SysGuard |
| F7 | SysGuard: serviço SYSTEM reaplica hosts/DNS/políticas a cada 3 s; `--force-uninstall --silent` existe fora do fluxo de senha | admin consegue desproteger (UAC); o flag é um bypass de senha documentado | exigir senha no `--force-uninstall`; recovery do serviço; bloqueio adicional enquanto Angry ativo |

## Android — Pacote 1 (app `com.example.focusapp`, v1.7 / versionCode 8)

1. **`guard/AccessibilityGuard.kt`** (lógica pura testável)
   - `isServiceEnabled(ctx)` (já existe `FocusAccessibilityService.isEnabled`, centralizar)
   - `ensureServiceEnabled(ctx): Result` — se ausente: preservar a lista atual + componente, `Settings.Secure.putString(ENABLED_ACCESSIBILITY_SERVICES)`, `putInt(ACCESSIBILITY_ENABLED,1)`; polling de até 5 s (`AccessibilityManager`); devolver motivo da falha (sem permissão / setting recusado / não vinculou).
   - `isAdminActive`, `tryReactivateAdmin` (se o sistema permitir via `dpm.setActiveAdmin`; se não, avisar).
   - `adbEnabled`, `disableAdbWhileProtected`, `restoreAdb`.
2. **`service/GuardWatchdogService`** (foreground, `specialUse`, `START_STICKY`)
   - iniciado por `BootReceiver`, `MainActivity`, `PomodoroPushService` e `DailyBlockController` quando um bloqueio começa.
   - loop 10 s (modo protegido) / 60 s (ocioso): acessibilidade ligada? admin ativo? ADB desligado durante bloqueio ativo? Se acessibilidade caiu e o app está vivo → **religa**. Se falhar → notificação “BLOQUEIO NÃO APLICADO — toque para corrigir” + ack de falha.
   - heartbeat de estado para o PC a cada 60 s enquanto houver bloqueio ativo.
3. **`push/PomodoroPushService`** — no recebimento: gravar (como hoje) → garantir acessibilidade → `applied = a11y && overlay` → **ack**; se não aplicado: notificação de falha e ack `applied=false` com motivo.
4. **`push/AckClient`** — `POST https://ntfy.sh/<tópico>` com JSON `{v,id,applied,a11y,admin,level,endMs,ts,gapMs}`; tópico = `di-ack-` + SHA-256(`dynamic-island-ack-v1:` + token FCM) (PC deriva o mesmo do token que já tem); fallback LAN se o FCM trouxer `ackUrl`.
5. **`FocusAccessibilityService`** — (a) iniciar/assegurar o watchdog; (b) ampliar o anti-tamper de Settings: bloquear telas de **Acessibilidade**, **Admin do dispositivo** e **App info** do próprio pacote (por `className`/texto), com a janela de graça por senha já existente; (c) ack/heartbeat enquanto o serviço estiver bound (funciona mesmo sem FCM).
6. **`FocusBlockRules`** — ampliar navegadores conhecidos + varredura genérica de **URLs de domínio adulto** em qualquer app (pega WebView/custom tabs de apps fora da lista) com regra conservadora (host bloqueado, não palavra solta).
7. **`AndroidManifest`** — serviço do watchdog, permissões (`FOREGROUND_SERVICE_SPECIAL_USE`, `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`), provider/`ReviveService` exportados com permissão `signature` (para o guardião do Pacote 2).
8. **Build**: `versionCode 8`, `versionName 1.7`; instalar **`assembleRelease`** (não debuggable, assinado com debug.keystore como o release atual) — fecha `run-as`.
9. **Testes**: unit tests para tópico/payload do ack, regras de URL ampliadas, política de proteção; `testDebugUnitTest` no build.

### Pacote 2 (só se os testes confirmarem necessidade): guardião em segundo pacote
- APK mínimo `com.example.focusapp.guardian`, mesma assinatura (debug.keystore), `WRITE_SECURE_SETTINGS` + `SYSTEM_ALERT_WINDOW` concedidos uma vez por adb, admin de dispositivo próprio, foreground service + alarme exato, receptor de boot.
- Loop 10 s: consulta `ProtectionStateProvider` do app principal (permissão `signature`); se proteção esperada e acessibilidade ausente → reescreve setting **e** inicia `ReviveService` explícito (limpa force-stop) → re-checa; notifica falha.
- **Aceite**: `am force-stop com.example.focusapp` → em ≤ 20 s o serviço de acessibilidade volta a bound e o bloqueio volta a valer **sem** abrir o app.
- Mútuo: app principal tenta reerguer o guardião (e vice-versa) quando um some.
- Limite honesto: `adb shell am force-stop` nos **dois** pacotes + revogar permissões ainda vence; documentar e detectar (gap reportado no próximo ack).

## PC — Dynamic Island (v0.4.41)

1. **Estado vivo do Angry** (`angry-live.json`, escrito a cada mudança + 5 s): `{savedAt, active, locked, timer, phrase, scheduled}`. Restaurar no startup normal quando válido (hoje só existe no caminho de update, F6).
2. **Guardião no mesmo exe** (`DynamicIsland.exe --guardian <pid> <stateFile>`): processo oculto lançado ao travar o Angry, encerrado ao fim da sessão; (a) se a ilha morrer com sessão travada e `end>now` → reabre a ilha; (b) enquanto a ilha estiver morta, aplica ele mesmo a guarda de títulos (message loop + `SetWinEventHook` + minimizar) — a regra `BlockedSites` é reaproveitada; (c) watchdog de si mesmo por auto-relançamento da ilha (se o guardião também morrer, o SysGuard cobre).
3. **Confirmação real do celular**: `PhoneBlockAckListener` (ntfy SSE derivado do token). A ilha: envia FCM → mostra “aguardando confirmação”; ack `applied && a11y` → “Celular bloqueado ✓ HH:mm”; ack falho/timeout (20 s) → “Bloqueio NÃO aplicado no celular: <motivo>”; heartbeat a cada 60 s mantém o selo.
4. **Integração SysGuard** (quando instalado): ilha grava `angry-live.json`; serviço lê (por usuário em `C:\Users\*\AppData\Local\DynamicIsland\angry-live.json`) e, enquanto ativo, (a) acrescenta hosts das distrações (youtube/x/instagram/reddit) com marcador, (b) reaplica ao terminar. Tray aplica guarda de títulos aos domínios do Angry. Resultado: matar a ilha **não** desliga o bloqueio de rede.
5. **SysGuard** (`tools/SysGuard`): `--force-uninstall` passa a exigir senha (script de teste atualizado); recovery do serviço (`ChangeServiceConfig2` com ações de reinício) + watchdog mútuo leve; hosts das distrações enquanto Angry ativo; testes atualizados.
6. **Testes**: `dotnet test DynamicIsland.slnx` + `dotnet test` do SysGuard; `--smoke-test` do build publicado; teste real de `taskkill` durante sessão simulada (gancho de teste no `--demo`/`--soak` já existente).

## Ordem de execução
1. Pacote 1 Android (maior impacto no pedido do usuário) → build release → instalar no Moto G20 → aceite F1/F3/F4/F5.
2. PC itens 1–3 (estado vivo, guardião no exe, ack) → testes → smoke.
3. Pacote 2 Android (guardião) → aceite F2.
4. SysGuard itens 4–5 → build do exe → selftest (sem instalar serviço: exige UAC do usuário).
5. Re-teste adversarial (`android-live` + `pc-redteam`) e relatório final.

## Riscos e limites (honestidade para o relatório)
- Quem é admin dos próprios aparelhos sempre vence (adb/root/device-owner/UAC/safe mode). O objetivo é: fechar o caminho **fácil e impulsivo**, detectar e avisar nos caminhos difíceis.
- ntfy.sh é serviço público de terceiros: tópico é segredo de capacidade (128 bits), sem conteúdo sensível; se preferir, dá para trocar por LAN pura depois.
- Nada disso justifica bloquear sites de saúde/educação; listas e heurísticas erram para os dois lados.
