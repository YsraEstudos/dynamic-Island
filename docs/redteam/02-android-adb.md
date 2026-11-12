# 02 — Red-team OPERACIONAL via ADB: Moto G20 (Android 11) + `com.example.focusapp` v1.6

**Agente:** `android-live` (único autorizado a mutar o celular)
**Data:** 2026-11-11, 13:45–14:15 (America/Sao_Paulo)
**Alvo:** Moto G20 `ZF523QVD4J`, Android 11 (SDK 30), build `RTAS31.68-66-3` (user), app `com.example.focusapp` v1.6
(versionCode 7, targetSdk 35). Aparelho do próprio usuário; autorização explícita no `BRIEFING.md`.
**Escopo:** testes reais com `adb -s ZF523QVD4J ...`, cada mutação restaurada e verificada. Evidências brutas em
`docs/redteam/evidence/android-live/`.

> **Regra seguida:** toda mutação teve comando de restauração anotado antes; ao final o celular foi verificado e
> restaurado (seção "Estado final"). Nada foi desinstalado/limpo; ADB nunca foi desligado; PIN/tela de bloqueio não
> foram tocados (o aparelho estava desbloqueado em parte da sessão — ver Limitações).

---

## Veredito (TL;DR)

1. **O bloqueio do celular é derrotável em segundos por qualquer pessoa com o cabo/ADB**, e o app **não detecta nem
   reage** a nada disso. O caso realista de uso (celular na mão, sem PC) resiste melhor: desligar a acessibilidade
   pelos Ajustes exige ~5–10 s de toques, mas **não foi observado nenhum bloqueio/aviso** dessa tela.
2. **`am force-stop` vence tudo o que o app tem hoje**: o sistema apaga `enabled_accessibility_services`, cancela
   alarmes, bloqueia FCM e o serviço de acessibilidade **não volta** (77 s observados; precisa reabrir o app e
   reativar o toggle). **O teste-chave do design falhou**: escrever os settings via ADB com o app force-stopped
   **não** religa o serviço (AMS recusa bind de pacote *stopped*).
3. **`pm disable-user` é pior**: além de matar o bloqueio, **remove o admin de dispositivo** de forma persistente
   (`pm enable` não devolve o admin). Só `dpm set-active-admin` (ADB) ou a UI do app restauram.
4. **APK instalado é DEBUG (`DEBUGGABLE`)** → `adb shell run-as com.example.focusapp` dá leitura/escrita totais nos
   dados privados (prefs do bloqueio). É um bypass CRÍTICO que some publicando build release.
5. **O status "Celular bloqueado" no PC é não verificado** por construção: o celular não envia nenhum ack; ficou
   ~20 min com acessibilidade desligada e bloqueio zero sem que nada avisasse o PC. Q5 exige canal de ack
   (recomendo ntfy/LAN — seção Pergunta 5).
6. **O que já resiste bem:** reboot (acessibilidade volta sozinha), `am kill` (no-op), `dpm remove-active-admin`
   (falha: admin não-testOnly), appops de overlay (irrelevante para o overlay, que é `TYPE_ACCESSIBILITY_OVERLAY`).
7. **Custo colateral documentado:** `pm create-user` + `am switch-user` funciona (e o usuário novo não tem o app),
   mas **derrubou o `system_server`** (DeadSystemException; restart de userspace). O aparelho se recuperou sozinho
   ao user 0.

---

## Matriz de testes executados (com evidência real)

Os IDs são usados nas seções seguintes. Formatos: [TESTADO] = rodado no aparelho com saída colada; [ESTÁTICO] =
análise de código (`arquivo:linha`); [NÃO TESTADO] = preciso de condição indisponível (explicada).

### T1 — Desligar acessibilidade por `settings put secure` [TESTADO]

Snapshot: `enabled_accessibility_services=com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService`,
`accessibility_enabled=1`, serviço *bound*.

Comando (uma escrita já basta; o sistema ajusta o resto):
```bash
adb -s ZF523QVD4J shell settings put secure enabled_accessibility_services '""'
```
Observado (timeline em `evidence/android-live/01a-lista-vazia-timeline.txt`):
```
T0  enabled=1  list=com.example...FocusAccessibilityService  pid=6968  bound=FOCUS_BOUND
A0  enabled=0  list=''                                        pid=6968  bound=NONE
A+1s..A+60s  enabled=0  list=''  pid=6968  bound=NONE   (nada mudou em 60 s)
```
- `accessibility_enabled` virou **0 automaticamente** (não é preciso escrevê-lo).
- Processo continuou vivo (pid 6968); **nenhuma tentativa de religar**; **nenhuma notificação nova**; nenhuma
  janela/overlay de aviso (`dumpsys notification`, `dumpsys window windows` vazios para o app).
- Restauração: 2 writes e o serviço voltou **bound em ≤1,1 s** (medido em `01b-restauracao-dumpsys.txt`).
- **Severidade: CRÍTICO** (ADB; ~2–5 s, sem senha, sem rastro no app).

### T2 — `am kill` [TESTADO]

```bash
adb -s ZF523QVD4J shell am kill com.example.focusapp
```
Resultado: **no-op**. Saída vazia, `pid=6968` e `bound=YES` 1,4 s depois (`02a-amkill-timeline.txt`). O app está
protegido porque o binding de acessibilidade/serviço o mantém em estado não-"background killable".
**Severidade: BAIXO** (não é vetor).

### T3 — `am force-stop` [TESTADO] — o vetor mais forte

```bash
adb -s ZF523QVD4J shell am force-stop com.example.focusapp
```
Logcat (`02b-forcestop-logcat.txt`):
```
I/ActivityManager: Force stopping com.example.focusapp appid=10271 user=0: from pid 15398
I/ActivityManager: Killing 6968:com.example.focusapp/u0a271 (adj 100): stop ...
```
Estado imediatamente depois e por 77 s (`02b-forcestop-timeline.txt`):
```
Bound services:{}                       ← acessibilidade caída
Enabled services:{}                     ← o SISTEMA apagou o setting
Crashed services:{...FocusAccessibilityService}
enabled_accessibility_services = 'null'
accessibility_enabled = '0'
pkg: stopped=true                       (persistente)
alarme REFRESH_DAILY_BLOCK: AUSENTE      (cancelado pelo force-stop)
```
- **Não volta sozinho** em 77 s. FCM/alarmes/jobs do app ficam mortos enquanto `stopped=true`
  (entrega de FCM a pacote *stopped* é bloqueada pelo Android; não foi possível enviar FCM real por falta de
  credencial do servidor — ver Limitações).
- Abrir o app limpa o estado *stopped*, mas **não religa a acessibilidade sozinho**: após
  `am start -n com.example.focusapp/.MainActivity` o processo subiu (`pid=19178`) e o serviço continuou
  **"Crashed"**, bound=vazio por 55 s (`06b`); só um **toggle real** dos settings religou (bound em +1,1 s).
- **Severidade: CRÍTICO** (ADB; ~2 s). No UI: Ajustes → App → Forçar parada (~10 s, desbloqueado).

### T4 — TESTE-CHAVE do design: settings + app force-stopped [TESTADO — FALHOU]

Sequência: `am force-stop` → (`enabled_accessibility_services` já estava `null` pelo próprio sistema) → escrever
de novo o componente + `accessibility_enabled=1` **sem abrir o app**:
```bash
adb -s ZF523QVD4J shell settings put secure enabled_accessibility_services com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService
adb -s ZF523QVD4J shell settings put secure accessibility_enabled 1
```
Resultado (`06-keytest-force-stopped-a11y-timeline.txt`):
```
+6.1s  enabled=1  pid=none  bound=NO  STOPPED
+86.9s enabled=1  pid=none  bound=NO  STOPPED
dumpsys accessibility: Enabled services:{...FocusAccessibilityService}
                       Bound services:{}
                       Crashed services:{...FocusAccessibilityService}
```
**Conclusão dura: um watchdog que apenas reescreve `enabled_accessibility_services` NÃO vence `force-stop`.**
O `AccessibilityManagerService` recusa/ignora o bind de um pacote em estado *stopped* — e o próprio force-stop já
tinha apagado o setting. O que funciona é a ordem: **(1) launch explícito do app para limpar `stopped`
(`am start`/activity), (2) reescrever o setting**. Isso só é possível de dentro do celular por **outro pacote**
(ver "Design validado por experimento").

### T5 — `pm disable-user` / `pm enable` [TESTADO] — mata o bloqueio E o admin

```bash
adb -s ZF523QVD4J shell pm disable-user --user 0 com.example.focusapp     # → "new state: disabled-user"
adb -s ZF523QVD4J shell pm enable com.example.focusapp                    # → "new state: enabled"
```
Observado (`03-disable-user-logcat.txt`, `03-disable-user-timeline.txt`):
```
Package com.example.focusapp new state: disabled-user
pid=none ; enabled=3 ; a11y: Bound={} / Crashed=Focus ; accessibility_enabled=0
alarme REFRESH_DAILY_BLOCK: AUSENTE
Enabled Device Admins (User 0): <VAZIO>     ← ADMIN REMOVIDO pelo disable-user
```
- `pm enable` devolve o pacote (`enabled=1`) mas **não devolve o admin** e **não religa a acessibilidade**.
- Isolamento feito depois: `am force-stop` **mantém** o admin; **é o `pm disable-user` que o remove**
  (evidence `05`/`99`; restauração com `dpm set-active-admin com.example.focusapp/.admin.AdminPolicyReceiver`
  → `Success: Active admin set to component {...}`).
- Consequência prática: depois de um `disable-user`/`enable`, o app fica **sem a proteção anti-desinstalação**
  (admin) até o usuário reativar pela UI dele. **Severidade: CRÍTICO** (ADB; 1 comando). Agravante: persistente.

### T6 — Permissões: `WRITE_SECURE_SETTINGS`, overlay appop, `USAGE_STATS` [TESTADO + correção do briefing]

- **O briefing está errado:** a permissão `WRITE_SECURE_SETTINGS` **NÃO estava concedida**. `dumpsys package` mostra
  o app em `requested permissions:` mas **não** em `install permissions: granted=true`
  (`snapshots/package-before.txt` linhas 155–180 e `evidence/android-live/04-package-dump.txt`). O app inclusive pede
  esse grant via ADB na própria UI (`ui/DashboardPermissions.kt:135-146`, “Permissão WRITE_SECURE_SETTINGS via ADB”).
  Sem ela, o recurso de escala de cinza não funciona (`display/GrayscaleController.kt:31-57`).
- `adb shell pm grant/revoke com.example.focusapp android.permission.WRITE_SECURE_SETTINGS` **funcionam** (verifiquei
  o grant entrando/saindo de `install permissions`) — é assim que o usuário concederia. Não derrubam acessibilidade
  nem admin; o app **não detecta** (sem logcat, sem notificação). Voltou ao estado original (não concedida).
- `appops set ... SYSTEM_ALERT_WINDOW deny` → irrelevante para o bloqueio: o overlay de tela cheia é
  `TYPE_ACCESSIBILITY_OVERLAY` (`service/FocusAccessibilityService.kt:275-280`, `schedule/DailyBlockController.kt:205-207`),
  que **não usa** a permissão de overlay. Restaurado para `default` (estado do snapshot).
- `appops set ... GET_USAGE_STATS deny` → aceito; quebra o contador de tempo de tela (não observei crash/aviso).
  Restaurado para `allow`.
- **Severidade: MÉDIO** (exige ADB).

### T7 — `dpm remove-active-admin` [TESTADO — defesa funciona]

```bash
adb -s ZF523QVD4J shell dpm remove-active-admin com.example.focusapp/.admin.AdminPolicyReceiver
```
Saída (resumida):
```
java.lang.SecurityException: Attempt to remove non-test admin
  ComponentInfo{com.example.focusapp/com.example.focusapp.admin.AdminPolicyReceiver} 0
  at android.app.admin.IDevicePolicyManager$Stub$Proxy.forceRemoveActiveAdmin(...)
```
O admin (`testOnlyAdmin=false`) **continua ativo**. **Severidade: improcedente** por esse caminho — mas `pm disable-user`
contorna (T5). **Correção:** manter `testOnly=false` (já está) e proteger contra disable (DO, ver recomendações).

### T8 — `run-as` por APK DEBUG [TESTADO] — bypass CRÍTICO esquecido

`dumpsys package` do app: `flags=[ DEBUGGABLE HAS_CODE ALLOW_CLEAR_USER_DATA ALLOW_BACKUP ]`
(`evidence/android-live/04-package-dump.txt:138`). Experimento reversível (probe criado, lido e apagado):
```bash
adb -s ZF523QVD4J shell "echo redteam-probe | run-as com.example.focusapp tee files/redteam_probe.txt"  # ok
adb -s ZF523QVD4J shell run-as com.example.focusapp cat files/redteam_probe.txt                          # → redteam-probe
adb -s ZF523QVD4J shell run-as com.example.focusapp rm -f files/redteam_probe.txt                        # apagado
```
- Leitura confirma estado interno: `shared_prefs/focus_daily_block.xml` guarda o bloqueio
  (`pomodoro={"id":...,"start":...,"end":...,"released":false}`, `adult_end_ms`, `selective_block`, token FCM),
  `focus_security_prefs.xml` a senha (PBKDF2 310k). Ler/editar = desarmar o bloqueio sem root.
- O app **não detecta** edição externa de prefs em processo vivo (o listener é in-process
  `schedule/DailyBlockController.kt:49-59`); mas num restart ele carrega o estado adulterado.
- **Severidade: CRÍTICO** com ADB. **Correção de 1 linha:** publicar build **release** (`debuggable false`).
  Este é o achado que o briefing não previa.

### T9 — Tela de Ajustes da acessibilidade: o app bloqueia? [TESTADO — não bloqueou; código tem anti-tamper]

Com a acessibilidade **LIGADA** e o app rodando:
```bash
adb -s ZF523QVD4J shell am start -a android.settings.ACCESSIBILITY_SETTINGS
```
- Resultado: `mResumedActivity = com.android.settings/.Settings$AccessibilitySettingsActivity`; **nenhum** HOME
  forçado, **nenhum overlay** do app, **nenhum toast/notificação** durante 10 s. O usuário chega ao toggle e desliga.
- O código **tem** anti-tamper (`service/FocusAccessibilityService.kt:238-268`: acha texto do app na tela de
  Ajustes → `performGlobalAction(GLOBAL_ACTION_HOME)` + abre o app), mas ele só roda em nível reforçado
  (`reinforced = level >= 2`, `FocusAccessibilityService.kt:126-131`). O nível efetivo aqui **é** 2
  (`tracker/TrackerPrefs.kt`: default `LEVEL_REINFORCED`, e `focus_tracker_prefs.xml` não tem `key_security_level`)
  — então o gatilho **deveria** ter agido. Indício forte de que a heurística de texto **não casa** com o rótulo
  exibido pelo Ajustes (o serviço aparece como “Serviço de Foco e Proteç…” / app “Foco & Bem-Estar”, não como
  `com.example.focusapp`). **Severidade: CRÍTICO se confirmado** — **Correção:** casar por
  `packageName` do serviço + rótulos (`app_name`, `service_label`) e varrer por "acessibilidade"/"Foco"; validar
  com o aparelho **desbloqueado** e screenshot/`dumpsys window`.
- **Nota:** o app não tem um "pânico" (volume keys) configurado; o atalho de acessibilidade do sistema aponta para
  TalkBack (snapshot), não para o Focus.

### T10 — Janela secundária de usuário [TESTADO — funciona, mas com side effect grave]

```bash
adb -s ZF523QVD4J shell pm create-user rt_teste        # → Success: created user id 10
adb -s ZF523QVD4J shell am switch-user 10              # → current-user: 10
adb -s ZF523QVD4J shell pm list packages --user 10 | grep focusapp   # → VAZIO (app não instalado no user 10)
adb -s ZF523QVD4J shell pm list packages --user 10 | grep -E 'chrome|facebook|youtube'  # → com.android.chrome, com.facebook.katana, youtube...
adb -s ZF523QVD4J shell am switch-user 0 ; adb -s ZF523QVD4J shell pm remove-user 10   # → de volta ao 0, removido
```
- **O usuário novo não tem o app nem a acessibilidade** (estado de a11y é por usuário): bloqueio **inexistente** lá,
  e Chrome/Facebook/YouTube disponíveis. Escape por "pular de perfil" funciona.
- **Porém**: durante o retorno ao user 0 o **`system_server` morreu** (`DeadSystemException` em vários apps,
  `logcat -b crash`; PID do system_server 965 → 26233, restart de userspace). O aparelho voltou sozinho ao user 0,
  estado íntegro. Reprodução arriscada — **não repetir em produção**; documentar como vetor de alto custo.
- **Severidade: ALTO** (requer múltiplos toques/Ajustes; com ADB é trivial, mas instável). **Correção:** Device Owner
  com `DISALLOW_ADD_USER`/políticas de usuário; sem DO, não há API para um app comum impedir.

### T11 — Alarme/job/reboot como recuperação [TESTADO + ESTÁTICO]

- Alarme `*walarm*:com.example.focusapp.action.REFRESH_DAILY_BLOCK` existe enquanto há bloco/fim agendado; o app
  o reagenda no `DailyBlockController.connect()` (`schedule/DailyBlockController.kt:85`) e no listener de prefs
  (`:49-59`). **`force-stop`/`disable-user` cancelam esse alarme** — ele **não** é um mecanismo de recuperação
  contra esses vetores (só serve para o fim do bloco).
- **Reboot:** a acessibilidade **volta sozinha** no boot (setting persiste; observado após o restart de userspace:
  `Bound services` focado, `accessibility_enabled=1`, admin ativo). Ou seja: **reiniciar o celular não fura o
  bloqueio** — bom. (Não testamos force-stop + reboot; o estado *stopped* é persistente no Android, então
  provavelmente continua furado até abrir o app — a confirmar.)
- **Severidade: BAIXO** como bypass.

### T12 — "Nada" quando a acessibilidade cai: status do PC fica mentiroso [TESTADO]

Durante o T1 (60 s com acessibilidade off) e no teste de Settings (T9):
- **Zero** notificação; **zero** overlay; **zero** logcat de erro do app; o app continuou vivo e silencioso.
- A única notificação ativa do app é a de "Tempo de Tela Restante" (`id=1001`,
  `android.title="Tempo de Tela Restante"`, `android.text="01h 54m restantes"`) — **não** avisa de acessibilidade.
  Além disso, **todos os canais do app estão mutados pelo usuário** (`post_frequency{...,muted=48/48}` etc.
  em `dumpsys notification`), então aviso por notificação seria invisível de qualquer forma.
- O PC não recebe ack (o `PomodoroPushService` só **recebe** FCM; não há envio de status —
  `push/PomodoroPushService.kt:14-28`). **Conclusão: "Celular bloqueado" na ilha não tem confirmação real e pode
  mentir por tempo indeterminado.** (Ver Q5.)

### T13 — Tempo de reação do overlay [NÃO MEDIDO ao vivo — análise de código]

Não foi possível medir ao vivo porque o overlay é **suprimido quando a tela está bloqueada** por design
(`schedule/DailyBlockController.kt:164-167`: `if (active != null && interactive && !keyguard && ...)`), e não havia
bloco ativo (o último pomodoro terminou 13:28:50; `selective_block=false`). Por código:
- Reação a mudança de prefs (FCM): listener → `DailyBlockScheduler.refresh` + `evaluate()` (`:49-59`) → overlay
  `addView` imediato (`:186-215`) — na casa de ms após o FCM chegar.
- Reação ao usuário abrir app bloqueado: evento `TYPE_WINDOW_STATE_CHANGED` (`FocusAccessibilityService.kt:88-110`)
  → `shouldBlockApp` → `performGlobalAction(HOME)` + overlay (`:146-153`); o `accessibility_service_config` usa
  `notificationTimeout=300` ms; deep-scan de navegador limitado a 1 varredura/700 ms (`:225`).
- Ao desbloquear com bloco ativo: `ACTION_USER_PRESENT` → `evaluate()` (`:75-77`) → overlay imediato.
- **Números honestos:** latência típica esperada ≈ **≤300–400 ms** por evento de janela (até ~1 s na primeira
  varredura do navegador). **Não confundir** com a recuperação **após force-stop**, que é manual (segundos a
  minutos) — é aí que "parece falha" quando o app foi morto e ninguém reabriu.
- **Para medir de verdade:** com o celular desbloqueado e um bloco ativo (FCM do PC ou edição controlada de prefs),
  cronometrar `dumpsys window` (aparecimento de `Window{...focusapp}` `TYPE_ACCESSIBILITY_OVERLAY`) — deixei o
  roteiro no fim deste relatório.

---

## Tabela de vetores (formato do BRIEFING)

| ID | Vetor | Como se faz | Pré-requisitos | Resultado | Severidade | Por que a defesa atual falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|
| V1 | Desligar acessibilidade via ADB | `settings put secure enabled_accessibility_services '""'` | ADB autorizado | **TESTADO**: serviço cai em <1 s, `accessibility_enabled=0` automático, app não reage em 60 s | CRÍTICO | Não existe watchdog; app não checa acessibilidade | Watchdog 2º pacote + ack; ver D1/D3 | ADB sempre pode |
| V2 | `am force-stop` | `am force-stop com.example.focusapp` | ADB | **TESTADO**: a11y não volta (77 s); sistema apaga setting; alarmes/FCM mortos; `am start` não religa sem toggle | CRÍTICO | Serviço só liga por toggle do sistema; sistema recusa bind de pacote stopped | DO + `setUserControlDisabledPackages` (se disponível) / 2º pacote com BAL | Nenhuma defesa sem DO |
| V3 | `pm disable-user` | `pm disable-user --user 0 ...` | ADB | **TESTADO**: mata o app, **remove o admin** (persistente); enable não restaura admin | CRÍTICO | Nada roda para se defender; DPM remove admin de pacote desabilitado | DO; ack para avisar; re-ativar admin pela UI | Admin pode ficar órfão |
| V4 | `run-as` (APK DEBUG) | `adb shell run-as com.example.focusapp ...` | ADB | **TESTADO**: leitura/escrita em dados privados (prefs do bloqueio) | CRÍTICO | Build instalado é DEBUG (`DEBUGGABLE`) | Publicar **release** (`debuggable=false`) | — |
| V5 | Desligar acessibilidade pelos Ajustes | Ajustes → Acessibilidade → toggle | celular desbloqueado | **TESTADO**: tela abre e toggle fica acessível; app não interveio | CRÍTICO* | Anti-tamper existe mas não disparou (heurística de texto/label) | Corrigir match por label/pacote (D2) | *confirmar desbloqueado |
| V6 | Usuário secundário | `pm create-user` + `am switch-user` | ADB (ou Ajustes/desbloqueado) | **TESTADO**: funciona; user novo sem app/sem bloqueio; **causou crash do system_server** | ALTO | A11y e instalação são por usuário; sem API comum p/ impedir | DO `DISALLOW_ADD_USER` | instabilidade do aparelho |
| V7 | Revogar `WRITE_SECURE_SETTINGS` | `pm revoke ...` | ADB, se concedida | **TESTADO**: grant/revoke funcionam; app não detecta | MÉDIO | Sem watchdog de permissão | Conceder e usar no watchdog de a11y (D3) | requer adb 1x |
| V8 | `appops` overlay deny | `appops set ... SYSTEM_ALERT_WINDOW deny` | ADB | **TESTADO**: sem efeito (overlay é `TYPE_ACCESSIBILITY_OVERLAY`) | BAIXO | — | Nenhuma | — |
| V9 | `appops` USAGE_STATS deny | `appops set ... GET_USAGE_STATS deny` | ADB | **TESTADO**: aceito; contador de tempo deixa de medir | MÉDIO | App não detecta | Guardar/revalidar; ack | — |
| V10 | `dpm remove-active-admin` | `dpm remove-active-admin ...` | ADB | **TESTADO**: **falha** (`SecurityException: non-test admin`) | improcedente | Defesa funciona | Manter `testOnly=false` | — |
| V11 | `am kill` | `am kill ...` | ADB | **TESTADO**: no-op | BAIXO | — | — | — |
| V12 | ADB como porta de entrada | plugar cabo/USB debugging | PC desbloqueado + chave já autorizada | **TESTADO** (implícito em tudo): todos os vetores acima em <1 min | CRÍTICO | Depuração USB ligada, dev. options ligadas, chave do PC autorizada | Desligar Depuração USB no uso diário; revogar chaves (`adb kill-server`, remover `~/.android/adbkey`); DO pode aplicar restrições | celular na mão + PC = fuga |

\* Severidade "CRÍTICO*" para V5: a rota existe e é curta; a confirmação de que o anti-tamper é inócuo merece um
reteste desbloqueado (o teste foi feito com o aparelho aparentemente desbloqueado, mas sem screenshot da tela).

---

## Design validado por experimento (perguntas do Lead)

### D1. Watchdog por `WRITE_SECURE_SETTINGS` funciona? **Parcialmente — e NÃO contra force-stop.**

- **Funciona** para o cenário "o atacante só removeu o serviço da lista de acessibilidade" com o app **vivo**:
  escrever `enabled_accessibility_services` de volta religa o serviço em **≤1,1 s** (medido na restauração do T1).
- **Não funciona se o app estiver force-stopped**: o sistema já zerou o setting e recusa o bind de pacote *stopped*
  (T4, 87 s sem bind, serviço marcado "Crashed").
- **Não funciona se o app estiver `disable-user`** (nada roda; e o admin cai).
- Para force-stop, a recuperação exige **(a) launch explícito** (limpa `stopped`) **e (b) write dos settings**.
  Um app em background **não pode** lançar a activity de outro pacote sem isenção de *background activity launch*
  (Android 10+); a isenção prática é ter **`SYSTEM_ALERT_WINDOW`** (concedido pelo usuário) ou ser
  sistema/DO. Daí o **segundo pacote**.

### D2. Segundo pacote é necessário? **Sim, para force-stop/disable-user e para a janela de fuga real.**

- Guardião separado (não debug, nome discreto) com: `WRITE_SECURE_SETTINGS` (grant ADB 1x), `SYSTEM_ALERT_WINDOW`,
  `SCHEDULE_EXACT_ALARM` + foreground service; a cada 1–5 min (e em `USER_PRESENT`/`SCREEN_ON`):
  1) verifica se o serviço de acessibilidade do app principal está habilitado/bound
  (`AccessibilityManager.getEnabledAccessibilityServiceList`) → se não, **abre a activity do app principal**
  (isenção BAL pelo overlay) e **reescreve os settings**; 2) confirma o estado e emite ack.
- Dois pacotes que se vigiam mutuamente cobrem o force-stop de um só; force-stop **dos dois** (ou do guardião
  "invisível" se o usuário souber o nome) ainda fura. Para *disable-user* não há recuperação por app comum —
  só **Device Owner**.

### D3. `force-stop` derrota tudo? **Sim, hoje derrota o bloqueio no celular.** E `disable-user` derrota + apaga admin.

- Único caminho de recuperação pós-force-stop: launch explícito por outro pacote com BAL exemption (não testado ao
  vivo neste aparelho — não havia 2º pacote e instalar não estava autorizado; é o teste recomendado nº 1 da próxima
  rodada: instalar um APK guardião assinado igual, conceder as 2 permissões, force-stop o principal e medir o
  relaunch).
- **Device Owner** é a única defesa de sistema: em Android 11 o framework já expõe
  `mUserControlDisabledPackages` (visto no `dumpsys device_policy`), que um DO pode usar para **impedir o usuário
  de forçar parada/limpar dados** do app e aplicar `DISALLOW_ADD_USER` etc. Exige (re)provisionar o aparelho
  (reset/conta sem contas) — decisão do usuário, não do app.
- `pm create-user`/`am switch-user` vale como vetor real, mas com custo: funciona, cria perfil sem app/bloqueio,
  **e derrubou o system_server** (DeadSystemException) no retorno. Na prática: escape que "quebra o celular" —
  documentado, não recomendado repetir.

### D4. Janela de fuga máxima (tempo)

| Cenário | Tempo até o bloqueio deixar de valer | Recuperação hoje |
|---|---|---|
| ADB conectado | **2–5 s** (1 comando) | nenhuma |
| Ajustes → acessibilidade (desbloqueado) | **~5–10 s** (3–4 toques) | nenhuma (anti-tamper não disparou) |
| Forçar parada pelo menu do app | **~10 s** | manual (reabrir app + toggle) = **minutos** |
| `disable-user` (ADB/Ajustes→Desativar) | **~2 s** | manual + reativar admin (UI do app) |
| Usuário secundário | ~15–30 s (+ reboot do sistema ~1–2 min) | manual, inexistente hoje |
| Reboot do celular | **0** (acessibilidade volta sozinha) | — (defesa funciona) |

---

## Respostas às perguntas centrais do BRIEFING

### Q1. Dá para fugir do bloqueio hoje? Por quais caminhos?

**Sim, e por muitos. TESTADO:** ADB (desligar a acessibilidade = 1 comando; `force-stop`; `disable-user`;
`run-as` para reescrever prefs; `pm create-user`+`switch-user`). **TESTADO no próprio aparelho sem PC**:
a tela de Acessibilidade do Ajustes abre e o toggle está disponível (o anti-tamper do app não interveio);
"Forçar parada" nas informações do app e "Desativar" também são caminhos curtos (mesmos efeitos de T3/T5 —
não cliquei nos toques, mas os comandos equivalentes foram testados). **Outros vetores de navegador
(Chrome/Facebook/YouTube in-app browser)** existem, mas o bloqueio deles depende de acessibilidade ligada — com
a11y off, qualquer navegador passa; com a11y on, o bloqueio é só de URL/texto (Firefox instalado como alternativa
não é tratado? `FocusBlockRules.isBrowser` — ver 01-android-codigo; aqui o teste live mostrou Chrome **desabilitado
pelo próprio usuário** (`enabled=3`) e **Firefox instalado e habilitado** — vetor de navegador alternativo real a
validar com a11y on). **VPN/DoH:** não muda o bloqueio local por acessibilidade (ele lê a URL na tela), mas pode
burlar bloqueio por DNS/rede — não é o mecanismo atual. **Outro usuário:** funciona (T10), com o custo de crash.

### Q2. Dá para impedir o usuário de desligar a acessibilidade? De que formas funcionam no Android 11?

**Parcialmente.** (a) **Watchdog que religa** — funciona se o app estiver vivo (≤1,1 s), **não** contra force-stop/
disable-user. (b) **2º pacote guardião** — necessário para force-stop; não testado ao vivo (sem instalar);
tecnicamente viável com `SYSTEM_ALERT_WINDOW` (BAL exemption) + `WRITE_SECURE_SETTINGS` + alarme exato.
(c) **Device Owner** — único que impede de verdade force-stop/disable/desinstalação
(`setUserControlDisabledPackages`, restrições de usuário); exige reprovisionamento. (d) **Bloqueio da tela de
Ajustes** — o app tenta via acessibilidade (`GLOBAL_ACTION_HOME`), mas **hoje não disparou**; corrigir o matching
por label/pacote deve funcionar para a tela de acessibilidade (a11y só precisa estar ligada). (e) **Detecção de ADB**
— um app comum **não** pode desligar a depuração USB; DO pode aplicar restrições; o prático é o usuário desligar
"Depuração USB" e revogar chaves do PC, e o celular mandar ack de saúde (D/ack). **Mito:** "app consegue se
proteger de force-stop sem DO" — não; o próprio Android apaga o setting de acessibilidade.

### Q3. O app sobrevive a `force-stop`/Gerenciador de Tarefas? O que é possível e o que é mito?

**Não sobrevive.** Testado: serviço não volta (77 s), setting apagado pelo sistema, alarmes cancelados, FCM morto
até reabrir. **Mito:** "acessibilidade religa sozinha depois de force-stop", "alarme reagenda o app", "FCM acorda app
force-stopped", "watchdog de settings resolve". **Possível (sem DO):** detectar e reagir via **2º pacote** (launch
explícito + settings), e **avisar o PC** via ack para o status parar de mentir. **Possível (com DO):** impedir
force-stop/clear/desinstalação e restringir usuários/ADB. **Gerenciador de Tarefas** (limpar recentes) não é
force-stop no Android moderno — o serviço de acessibilidade continua; o ataque real é "Forçar parada".

### Q5. Como o celular pode confirmar (ack) para o PC que está bloqueado?

Hoje **não confirma nada** (TESTADO: nada é enviado; só recebe FCM). Canais possíveis e recomendação:

| Canal | Como | Prós | Contras |
|---|---|---|---|
| **LAN HTTP** (celular→PC) | app envia POST `{accessibility_bound, block_active, block_end_ms, ts}` para `http://PC:porta/api/ack` + heartbeat 30–60 s; descoberta por mDNS ou IP fixo | instantâneo, sem nuvem, sem segredo; PC já é o servidor | só funciona no Wi-Fi de casa |
| **ntfy** (recomendado como principal/fallback) | app publica em tópico secreto; PC assina (HTTP/WebSocket) | funciona em 4G, sem servidor próprio, simples | terceiro confiável; tópico é segredo |
| **FCM upstream** | app→servidor próprio→PC | usa a infra atual | exige **service account/segredo no servidor**; app cliente não pode publicar sozinho; complexo |
| **ADB poll** | PC roda `adb shell dumpsys accessibility` quando plugado | verdade absoluta | só com cabo/USB; não é o caso de uso |
| **Notificação/servidor local no celular** | app expõe estado local + PC consulta | — | NAT/IP dinâmico; mesmo problema do LAN |

**Recomendação:** ack por **ntfy** com heartbeat e `block_active` assinado pelo estado real do app; o PC só mostra
"Celular bloqueado" se o ack for **fresco (<90 s)** e `accessibility_bound && block_active` (ou
`released_by_phrase`) forem verdadeiros; se o ack sumir, mostrar "SEM CONFIRMAÇÃO"/alarme. O app já tem a base:
`FocusAccessibilityService.isEnabled()` (`service/FocusAccessibilityService.kt:228-235`) e
`DailyBlockController.evaluate()` sabem o estado real — basta publicar. Também emitir ack em transições
(ligou/desligou a acessibilidade, bloco aplicado/liberado) e no `USER_PRESENT` (revalidação pós-unlock).

---

## Defesas recomendadas (ordem de impacto × implementabilidade)

1. **[ALTO impacto, BAIXO esforço] Publicar build RELEASE** (`debuggable=false`) do app. Elimina o `run-as`
   (V4) e o vazamento de prefs. *Verificar em `app/build.gradle.kts` qual variante foi instalada.*
2. **[ALTO, BAIXO] Corrigir o anti-tamper de Ajustes** (`FocusAccessibilityService.checkSettingsTampering`):
   casar por `packageName` do serviço + rótulos reais (`R.string.app_name`, label do serviço) e também por
   "acessibilidade"/"Acessibilidade"; varrer a árvore das telas `Settings$AccessibilitySettingsActivity` e
   `Settings$AppInfoActivity` (botão Desativar/Forçar parada); testar **desbloqueado** com screenshot. Hoje não
   interveio (T9) — é a defesa mais barata e de maior retorno para o cenário "celular na mão".
3. **[ALTO, MÉDIO] Ack real celular→PC (ntfy ou LAN HTTP)** com heartbeat + estado; PC só diz "bloqueado" com ack
   fresco. Resolve o status mentiroso (T12/Q5) e vira sensor de "alguém mexeu" (se o ack sumir, alarmar).
4. **[ALTO, ALTO] 2º pacote guardião** com `WRITE_SECURE_SETTINGS`+`SYSTEM_ALERT_WINDOW`+alarme exato:
   religa a acessibilidade (settings) e **relança o app principal** após force-stop; vigia o principal e é vigiado.
   *Antes de implementar: validar BAL exemption e o relaunch num teste com APK de teste assinado igual.*
5. **[MÁXIMO, CUSTO ALTO] Device Owner** (requer reset/provisionamento sem contas): `setUserControlDisabledPackages`
   (impede force-stop/limpar dados), `DISALLOW_ADD_USER` (mata T10), restrições de depuração. Só o DO resolve
   force-stop/disable-user de verdade; é decisão do usuário.
6. **[ALTO, BAIXO operacional] Higiene de ADB no uso diário:** desligar "Depuração USB" (e opções de
   desenvolvedor) quando não estiver depurando; no PC, `adb kill-server` e remover `%USERPROFILE%\.android\adbkey`
   (chave autorizada dá acesso sem confirmação); evitar deixar o cabo conectado.
7. **[MÉDIO, BAIXO] Revalidar a11y em `USER_PRESENT`/`SCREEN_ON`** e ao abrir o app: se `isEnabled() == false`,
   mostrar tela de aviso **forte** (actividade em fullscreen/overlay) em vez de silêncio — não notificação (canais
   mutados) — e **emitir ack de falha** para o PC.
8. **[MÉDIO, BAIXO] Desmutar/limpar os canais de notificação** do app (estão 100% mutados, `muted=48/48`): sem
   isso, qualquer aviso via notificação é inútil.
9. **[BAIXO] Documentar que reboot NÃO fura o bloqueio** (a11y volta) — já é uma defesa que funciona; e que
   `am kill`/`dpm remove-active-admin` também não são vetores (defesas atuais OK).
10. **[BAIXO] Backup do estado** (`shared_prefs`) e verificação de integridade: um checksum/assinatura do bloco
    detectaria edição externa via run-as num build debug (inútil no release, mas barato).

---

## Estado final do celular (obrigatório — verificado às 14:11)

```
current_user: 0 (único usuário; user 10 de teste removido)
accessibility_enabled: 1
enabled_accessibility_services: com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService
dumpsys accessibility: Bound services: Serviço de Foco e Proteç… (FOCUS_BOUND) ; Crashed services: {}
admin: com.example.focusapp/.admin.AdminPolicyReceiver — testOnlyAdmin=false (ativo)
package: installed=true stopped=false enabled=0 (default = habilitado; estado original do snapshot)
adb_enabled: 1 ; development_settings_enabled: 1 (inalterados)
appops: WRITE_CLIPBOARD/WAKE_LOCK/GET_USAGE_STATS/BIND_ACCESSIBILITY_SERVICE/START_FOREGROUND/ACCESS_ACCESSIBILITY
        allow (igual ao snapshot); SYSTEM_ALERT_WINDOW=default (inalterado)
WRITE_SECURE_SETTINGS: NÃO concedida (igual ao estado original — não estava concedida)
usuários: apenas UserInfo{0:Israel}
tela: apagada (Dozing/Display OFF) após o teste — keyguard volta a aparecer ao acordar; PIN não foi tocado
restaurações verificadas por snapshot: enabled 0←(pm default-state), admin ←(dpm set-active-admin "Success"),
  a11y ←(toggle + bound), user 10 ←(am switch-user 0 + pm remove-user), prefs do app NÃO foram editadas
  (só lidas; probe removido; conferido `ls files/`)
```

---

## Evidências (brutas, em `docs/redteam/evidence/android-live/`)

- `00-baseline-*` — estado no início da rodada (a11y/list, package, device_policy, alarm, jobscheduler, users, appops).
- `01a-lista-vazia-timeline.txt`, `01a-lista-vazia-dumpsys.txt`, `01a-lista-vazia-logcat.txt`, `01b-restauracao-*`
  — T1 (desligar/religar acessibilidade, 60 s de observação).
- `02a-amkill-timeline.txt` — T2 (`am kill` no-op). `02b-forcestop-timeline.txt`, `02b-forcestop-accessibility.txt`,
  `02b-forcestop-logcat.txt` — T3. `02c-package-dump.txt` — permissões.
- `03-disable-user-timeline.txt`, `03-disable-user-logcat.txt` — T5 (+admin removido).
- `04-package-dump.txt`, `04b-package-dump-pos-restore.txt`, `04c-package-dump-pos-revoke.txt` — T6 grants.
- `05-pref-*.xml.txt` — estado do app (token FCM e hash/salt **redigidos**), `05-runas-sharedprefs-ls.txt`.
- `06-keytest-force-stopped-a11y-timeline.txt`, `06-keytest-logcat.txt`, `06b-restore-logcat.txt` — **T4 (teste-chave)**.
- `09-a11y-off-app-aberto-logcat.txt`, `09-notif-a11y-on.txt` — T9/T12.
- `99-estado-final.txt`, `99-estado-final-verificado.txt` — verificação final.
- `08-systemserver-*` — tentativa de forense do restart do system_server.

---

## Limitações e discrepâncias honestas

1. **`WRITE_SECURE_SETTINGS` não estava concedida** (briefing dizia que sim). Evidência: `dumpsys package`, bloco
   `install permissions` sem ela, e o próprio app pede via ADB na UI. Com isso, (a) o recurso de grayscale do app
   não funciona hoje; (b) um watchdog que use essa permissão **precisa ser concedido uma vez via ADB**.
2. **Modo seletivo estava DESLIGADO** (`selective_block=false`) — diverge do "padrão ligado" do briefing.
3. **Chrome está desabilitado pelo usuário** (`enabled=3`) — não é obra dos testes; Firefox instalado e habilitado.
4. **FCM real não foi testado** (sem credencial de servidor/PC). O que foi testado: o app force-stopped **não**
   roda/recebe (estado *stopped* + alarmes cancelados; política documentada do Android). O agente do PC deve
   validar o caminho completo FCM→bloco.
5. **Latência do overlay não medida ao vivo** (sem bloco ativo e keyguard oculta o overlay por design — T13);
   números acima são bounds de código.
6. **`system_server` crashou** durante T10 (restart de userspace; estado recuperado). O dropbox/crash buffer não
   guardou o stack do system_server (só `DeadSystemException` nos apps) — recomendo não repetir T10 sem supervisão.
7. **Aparelho estava desbloqueado em parte da sessão** (após o restart de userspace o `isKeyguardShowing=false`;
   o briefing dizia bloqueado). Não desbloqueei/ digitei PIN; ao final apaguei a tela para o keyguard voltar.
   Os testes que dependem de UI (T9) devem ser repetidos com o aparelho desbloqueado e captura de tela.
8. **Instalar o 2º pacote guardião não estava autorizado** — a recomendação D2/D4 fica como design + roteiro para a
   próxima rodada.
