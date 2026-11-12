# Guardião Android (Fase 2) — segundo pacote contra `force-stop`

**Status:** implementação em STAGING (não compilada/instalada por mim; o Lead aplica, builda e testa no Moto G20).
**Alvo:** `com.example.focusapp.guardian` (APK separado), Android 11 / minSdk 30 / targetSdk 35, assinado com o
mesmo `app/debug.keystore` do app principal.
**Escopo:** `docs/redteam/patches/guardian/` (nada do app principal foi alterado aqui).

---

## 1. Por que este pacote existe (o que ele resolve)

O red-team provou (T3/T4 de `docs/redteam/02-android-adb.md`, F2/F3 do plano de blindagem):

- `adb shell am force-stop com.example.focusapp` mata o processo e o sistema **apaga** o setting de acessibilidade;
- reescrever `enabled_accessibility_services` com o pacote *stopped* **não religa** (87 s sem bind, serviço
  marcado "Crashed") — o `AccessibilityManagerService` recusa o bind de pacote stopped;
- a única recuperação sem Device Owner é um **segundo pacote vivo** que (1) faça um *start* explícito em um
  componente do principal (limpa o `stopped`), (2) reescreva os settings e (3) re-cheque.

Este guardião é exatamente isso: um foreground service independente, com alarme exato, boot receiver e admin de
dispositivo próprio, que também publica ack/heartbeat no ntfy para o PC não mentir "Celular bloqueado".

**Aceite:** `adb shell am force-stop com.example.focusapp` → acessibilidade do principal **bound em ≤ 30 s**,
sem abrir o app manualmente; o guardião registra `revive: recuperado` no logcat.

---

## 2. Contrato entre os dois APKs (NÃO alterar um lado sozinho)

Tudo isso está centralizado em `guardian/.../ProtectionContract.kt`.

| Lado | Item | Valor exato |
|---|---|---|
| principal | permission (signature) | `com.example.focusapp.permission.PROTECTION_CONTROL` |
| principal | provider | `com.example.focusapp.guard.ProtectionStateProvider`, authority `com.example.focusapp.protection` |
| principal | query de estado | `content://com.example.focusapp.protection/state` → 1 linha, coluna String `json` |
| principal | JSON do estado | `{"v":1,"protectionActive":bool,"a11yEnabled":bool,"adminActive":bool,"fcmToken":"...","blockEndMs":number\|null}` |
| principal | serviço de revive | `com.example.focusapp.guard.ReviveService`, action `com.example.focusapp.action.REVIVE`, permission `PROTECTION_CONTROL` |
| principal | activity | `com.example.focusapp.MainActivity` (exported) |
| principal | a11y service | `com.example.focusapp.service.FocusAccessibilityService` |
| guardião | applicationId | `com.example.focusapp.guardian` |
| guardião | permission (signature) | `com.example.focusapp.guardian.permission.GUARDIAN_CONTROL` |
| guardião | serviço | `com.example.focusapp.guardian.GuardianService`, action `com.example.focusapp.guardian.action.START`, `foregroundServiceType=specialUse`, `START_STICKY` |
| guardião | admin | `com.example.focusapp.guardian.GuardianAdminReceiver` |
| guardião | ack ntfy | tópico `di-ack-` + 32 primeiros hex de `SHA-256("dynamic-island-ack-v1:" + fcmToken)` (token que o provider publica) |

Assinatura: os dois módulos usam `app/debug.keystore` (debug **e** release). Sem a mesma assinatura as permissions
`signature` não são concedidas e o guardião não consegue chamar o `ReviveService` do principal.

---

## 3. O que o guardião faz

`GuardianService` (foreground service, canal de notificação próprio, `START_STICKY`):

1. **Loop de 10 s** (thread `guardian-check`, nunca na main thread) + gatilhos `SCREEN_ON`/`USER_PRESENT` + boot +
   alarme exato de ~10 min. Também é iniciado pelo app principal quando ele percebe o guardião fora do ar.
2. Consulta o provider do principal (`ProtectionContract.query`). Se o provider não responde (app force-stopped)
   **ou** `protectionActive=true` e a acessibilidade do principal está ausente:
   - (a) `startForegroundService` explícito no `ReviveService` do principal com action `REVIVE` (limpa o `stopped`);
   - (b) espera ~2 s;
   - (c) reescreve `enabled_accessibility_services` **preservando a lista atual** + `accessibility_enabled=1`
     (só com `WRITE_SECURE_SETTINGS`; se faltar, notifica o dono e loga);
   - (d) espera ~3 s e re-checa `AccessibilityManager` + provider;
   - (e) se ainda ausente: mostra **overlay de aviso** (exige `SYSTEM_ALERT_WINDOW`; antes da atividade porque no
     Android 14 a isenção de *background activity start* por SAW exige janela visível) e lança a `MainActivity`
     do principal; re-checa de novo; última tentativa de settings;
   - (f) publica ack ntfy `{... applied:false, reason:"guardian-revive", a11y, admin, ts, blockEndMs ...}`;
3. Quando está tudo ok e há bloqueio ativo: **heartbeat de 60 s** com `applied:true` (e `a11y:true`).
4. Sem bloqueio ativo (`protectionActive=false`), fica em espera: não força nem alarma; só mantém o FGS.
5. Alarme exato de 10 min (`setExactAndAllowWhileIdle`; em Android 12+ sem o acesso especial cai para
   `setAndAllowWhileIdle`), receiver que re-agenda e garante o serviço. Boot/`MY_PACKAGE_REPLACED` idem.
6. Tela mínima de status (`GuardianStatusActivity`, **sem launcher**) para setup sem terminal: iniciar o guardião,
   permitir sobreposição, ignorar otimização de bateria, ativar admin, notificações (Android 13+) e abrir o principal.
   É também o que limpa o estado *stopped* de pacote recém-instalado.

**Limite honesto:** `force-stop` **dos dois** pacotes, `pm disable-user` de qualquer um, desinstalação ou revogação
das permissões por ADB ainda vencem. `reboot` restaura (boot receiver). O objetivo é a janela de fuga impulsiva,
não vencer o dono do aparelho com ADB/root.

---

## 4. Arquivos desta staging

```
docs/redteam/patches/guardian/
├─ apply-guardian.ps1                      # aplica no repo Android (backup .bak, idempotente)
├─ README.md                               # este arquivo
└─ files/                                  # caminhos relativos à raiz do repo Android
   ├─ settings.gradle.kts                  # + include(":guardian") (aplicado por edição mínima)
   └─ guardian/
      ├─ build.gradle.kts                  # minSdk 30, compileSdk 36, target 35, assinatura debug.keystore
      └─ src/main/
         ├─ AndroidManifest.xml
         ├─ res/values/strings.xml
         ├─ res/xml/guardian_device_admin.xml
         ├─ res/drawable/ic_guardian.xml
         └─ java/com/example/focusapp/guardian/
            ├─ GuardianService.kt          # FGS + loop 10 s + ciclo de revive + acks + notificações/overlay
            ├─ ProtectionContract.kt       # contrato (nomes/actions/authority/JSON) + query do provider
            ├─ AccessibilityRepair.kt      # checagem + reescrita de settings preservando a lista
            ├─ MainAppControl.kt           # start explícito do ReviveService / launch da MainActivity
            ├─ AckPublisher.kt             # tópico ntfy (SHA-256) + payload + POST HTTP
            ├─ GuardianAlertOverlay.kt     # overlay TYPE_APPLICATION_OVERLAY de aviso (só em falha)
            ├─ GuardianAlarmScheduler.kt   # alarme exato de 10 min
            ├─ GuardianAlarmReceiver.kt
            ├─ GuardianBootReceiver.kt     # BOOT_COMPLETED / MY_PACKAGE_REPLACED / QUICKBOOT
            ├─ GuardianAdminReceiver.kt    # DeviceAdminReceiver sem políticas
            ├─ GuardianStatusActivity.kt   # diagnóstico/setup sem Compose
            └─ GuardianPermissions.kt      # WRITE_SECURE_SETTINGS / overlay / bateria
```

O módulo **não** usa Compose, Firebase, Coroutines ou androidx: só APIs nativas do Android (`minSdk 30`), o que
reduz dependências novas a zero. O APK do guardião é pequeno e independente do app principal em runtime.

**Validação feita na staging (sem Gradle):** os 12 arquivos Kotlin compilam com o Kotlin 2.3.20 contra o
`android-36/android.jar` (0 erros) e os recursos + manifest passam no `aapt2 compile/link` (build-tools 37.0.0).
Isso não substitui o build real: **o Lead deve rodar o Gradle**.

---

## 5. Aplicar no repositório Android

O repo Android fica fora do workspace (`C:\Users\israe\OneDrive\Desktop\Projetos\app`), então o patch é aplicado
por script — sempre com backup `.bak` na primeira alteração e **idempotente** (rodar 2× não muda nada).

```powershell
# dry-run: mostra criados/atualizados/backups e o que faria no settings.gradle.kts
pwsh -NoProfile -File .\apply-guardian.ps1 -DryRun

# aplicar (repo padrão = C:\Users\israe\OneDrive\Desktop\Projetos\app)
pwsh -NoProfile -File .\apply-guardian.ps1

# repo em outro caminho
pwsh -NoProfile -File .\apply-guardian.ps1 -RepoPath 'D:\caminho\app'
```

Comportamento:

- copia os 17 arquivos do módulo guardião preservando bytes (LF) e criando diretórios;
- `settings.gradle.kts`: faz **apenas** a inserção de `include(":guardian")` após `include(":app")` (comentário
  incluído), para não sobrescrever mudanças paralelas; se o include já existe, não toca no arquivo;
- `-ForceSettingsCopy` substitui o `settings.gradle.kts` pelo conteúdo completo da staging (último recurso);
- avisa com `WARNING` se o **patch da Fase 1 do app principal** ainda não foi aplicado (faltam
  `guard/ProtectionStateProvider.kt` e `guard/ReviveService.kt`): o guardião compila, mas cai no fallback
  (settings + MainActivity) e não tem token FCM para ack.

Para desfazer: os arquivos alterados têm backup `<arquivo>.bak` (o original); o módulo `guardian/` pode ser apagado
e o `include(":guardian")` removido do `settings.gradle.kts` (ou `git checkout -- settings.gradle.kts`).

---

## 6. Build

Requisitos: JDK 17+ (toolchain), Android SDK com platform 36 (o repo já compila `:app` assim).
Assinatura: o mesmo `app/debug.keystore` (o módulo usa `file("../app/debug.keystore")`) — **não** troque a chave
de um dos APKs.

```powershell
cd C:\Users\israe\OneDrive\Desktop\Projetos\app

# app principal (Fase 1) e guardião (Fase 2). Ordem não importa para o build.
.\gradlew.bat :app:assembleRelease :guardian:assembleRelease

# se o Gradle estiver com configuration cache estranho após o novo módulo:
.\gradlew.bat --stop
```

Saídas esperadas:

- `app\build\outputs\apk\release\app-release.apk`
- `guardian\build\outputs\apk\release\guardian-release.apk`

Sanidade de assinatura (os dois devem ter o **mesmo** certificado):

```powershell
$apksigner = "$env:LOCALAPPDATA\Android\Sdk\build-tools\36.0.0\apksigner.bat"
& $apksigner verify --print-certs .\app\build\outputs\apk\release\app-release.apk
& $apksigner verify --print-certs .\guardian\build\outputs\apk\release\guardian-release.apk
```

Se o `:guardian` falhar por lint de release, o módulo já traz `lint { checkReleaseBuilds = false; abortOnError = false }`
(APK interno, nunca vai para loja).

---

## 7. Instalação e permissões (uma vez)

`adb` no PATH ou `C:\Users\israe\Documents\Codex\AndroidPlatformTools\platform-tools\adb.exe`. Aparelho de teste:
Moto G20 `ZF523QVD4J` (Android 11). Substitua `<serial>` por `-s ZF523QVD4J` se houver mais de um aparelho.

> **Ordem importa:** instale o **app principal primeiro** (é ele que define a permission `PROTECTION_CONTROL`), depois
> o guardião. Se instalar o guardião antes, reinstale-o (`adb install -r`) depois do principal para a permission
> signature ser concedida.

```powershell
$adb = 'adb'
$app = '.\app\build\outputs\apk\release\app-release.apk'
$guard = '.\guardian\build\outputs\apk\release\guardian-release.apk'

# 1) APKs (instalação por cima, preservando dados/permissões)
& $adb install -r $app
& $adb install -r $guard

# 2) permissões protegidas (concedidas por ADB; sem elas o guardião não religa nada)
& $adb shell pm grant com.example.focusapp android.permission.WRITE_SECURE_SETTINGS
& $adb shell pm grant com.example.focusapp.guardian android.permission.WRITE_SECURE_SETTINGS

# 3) overlay do guardião (aviso de falha + isenção de BAL/FGS em Android 12+)
& $adb shell appops set com.example.focusapp.guardian SYSTEM_ALERT_WINDOW allow

# 4) admin de dispositivo do guardião (atrito contra desinstalação/desativação acidental)
& $adb shell dpm set-active-admin com.example.focusapp.guardian/.GuardianAdminReceiver

# 5) abrir a tela de status: inicia o guardião e limpa o estado "stopped" do pacote recém-instalado
#    (sem isso o BOOT_COMPLETED pode não ser entregue e o alarme não é agendado)
& $adb shell am start -n com.example.focusapp.guardian/.GuardianStatusActivity
```

Opcional por versão do Android:

```powershell
# Android 13+: permissão de notificações (sem ela o FGS continua, mas o alerta pode ficar oculto)
& $adb shell pm grant com.example.focusapp.guardian android.permission.POST_NOTIFICATIONS

# Android 12+: alarmes exatos (sem isso o guardião cai para alarme inexato; a recuperação principal é o loop de 10 s)
& $adb shell cmd appops set com.example.focusapp.guardian SCHEDULE_EXACT_ALARM allow

# Doze/bateria: coloca o guardião na whitelist (recomendado)
& $adb shell cmd deviceidle whitelist +com.example.focusapp.guardian
```

Verificações rápidas:

```powershell
# permission signature concedida? deve aparecer granted=true para PROTECTION_CONTROL
& $adb shell dumpsys package com.example.focusapp.guardian | Select-String 'PROTECTION_CONTROL' -Context 0,2
# e a permissão do próprio guardião (GUARDIAN_CONTROL) definida com protectionLevel=signature
& $adb shell dumpsys package com.example.focusapp.guardian | Select-String 'GUARDIAN_CONTROL' -Context 0,1

# FGS rodando?
& $adb shell dumpsys activity services com.example.focusapp.guardian | Select-String 'GuardianService'

# logs do guardião (deixe rodando em outra janela durante os testes)
& $adb logcat -s GuardianService GuardianRepair GuardianAck GuardianAlarm GuardianBoot GuardianAdmin GuardianOverlay GuardianMainApp GuardianStatus GuardianContract
```

---

## 8. Teste de aceite — `force-stop` do app principal

Preparação: acessibilidade do principal ligada/bound e, de preferência, um bloqueio ativo (`protectionActive=true`)
para o guardião ter o que proteger. Deixe o logcat do guardião rodando.

```powershell
# 1) limpa o log para medir a recuperação
& $adb logcat -c

# 2) o ataque
& $adb shell am force-stop com.example.focusapp

# 3) mede quanto tempo até o serviço de acessibilidade voltar a ficar BOUND
$t0 = Get-Date
do {
    Start-Sleep -Milliseconds 500
    $dump = & $adb shell dumpsys accessibility
    $bound = ($dump | Select-String 'Bound services').Line
    $elapsed = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
    Write-Host ("{0,5}s  {1}" -f $elapsed, $bound)
} while ($elapsed -lt 40 -and $bound -notmatch 'FocusAccessibilityService')
Write-Host "A11y de volta em ${elapsed}s (aceite: <= 30 s)"
```

O que deve aparecer:

- logcat: `GuardianService: provider do app principal indisponível…` → `revive: iniciando ciclo…` →
  `revive: recuperado` em ~10–18 s (loop de 10 s + ciclo de ~5–9 s);
- `dumpsys accessibility`: `Bound services:{...FocusAccessibilityService...}` em ≤ 30 s;
- `dumpsys package com.example.focusapp | Select-String 'stopped'`: `stopped=false` (o `stopped` foi limpo);
- notificação persistente "Guardião Foco" e, se o bloqueio estava ativo, heartbeat ntfy com `applied:true`.

Também vale testar (regressões das defesas da Fase 1, agora com o guardião de rede):

```powershell
# a) apagar o setting de acessibilidade com o app VIVO (guardião + watchdog da Fase 1 devem religar em <= 20 s)
& $adb shell settings put secure enabled_accessibility_services '""'
& $adb shell settings put secure accessibility_enabled 0
# restaurar manualmente se necessário:
#   & $adb shell settings put secure enabled_accessibility_services com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService
#   & $adb shell settings put secure accessibility_enabled 1

# b) matar só o guardião (FGS): o app principal deve reerguê-lo (GuardianBridge) e o alarme também
& $adb shell am force-stop com.example.focusapp.guardian
# em <= 10 min (alarme) ou no próximo gatilho do principal o serviço deve voltar

# c) reboot: os dois voltam sozinhos (BOOT_COMPLETED). Depois verifique a11y bound + admin ativo
& $adb shell dumpsys accessibility | Select-String 'FocusAccessibilityService'
& $adb shell dumpsys device_policy | Select-String 'GuardianAdminReceiver'
```

**Se o teste (3) não recuperar:** olhe o logcat do guardião. Causas prováveis:
`WRITE_SECURE_SETTINGS` não concedida (`sem WRITE_SECURE_SETTINGS…`), `SYSTEM_ALERT_WINDOW` negada
(`sem SYSTEM_ALERT_WINDOW…`), permission de assinatura `PROTECTION_CONTROL` não concedida
(`permission signature PROTECTION_CONTROL ausente…` → reinstale o guardião **depois** do principal),
app principal sem o patch da Fase 1 (`falha ao iniciar ReviveService…`, `provider … indisponível`,
`ReviveService` sem `startForeground`), ou o guardião foi morto junto (force-stop dos dois).

---

## 9. Riscos e limites (honestidade para o relatório)

| Risco | Efeito | Mitigação/observação |
|---|---|---|
| `am force-stop` nos **dois** pacotes | derrota tudo; nada roda para recuperar | limite estrutural sem Device Owner; documentar no relatório |
| `pm disable-user` do principal ou do guardião | derrota; sem DO não há recuperação automática | guardião alerta "app principal indisponível"; principal reporta `guardian=false` no ack |
| Desinstalar o principal | guardião não revive (só alerta) | admin do principal + senha na UI da Fase 1 atrasam |
| Revogar `WRITE_SECURE_SETTINGS`/`SYSTEM_ALERT_WINDOW` por ADB | guardião degrada: não reescreve a11y e/ou não mostra overlay | alerta + log; a Fase 1 ainda tenta por dentro |
| Doze/OEM agressivo | loop de 10 s pode pausar; alarme pode atrasar | FGS + alarme exato + whitelist de bateria + `SCREEN_ON/USER_PRESENT`; ack com gap revela no PC |
| Android 12+ (BAL/FGS/alarme exato/notificações) | `startActivity` em background pode ser bloqueado; alarme cai para inexato | overlay mostrado antes da atividade (isenção SAW); fallback documentado; alvo principal é Android 11 |
| `ntfy.sh` é serviço público de terceiros | tópico é segredo de capacidade derivado do token; sem conteúdo sensível | PC precisa estar assinando o tópico; sem token FCM não há ack |
| **Parser atual do PC só consome ack cujo `id` == id da mensagem FCM pendente** (`PhoneBlockAckTracker.Observe`) | o guardião **não conhece** esse id (o provider não o expõe); o ack do guardião é útil como observabilidade/inspeção, mas **não confirma o selo "Celular bloqueado"** no PC atual | **recomendação ao Lead:** (a) estender o JSON do provider com o último `lastFcmMessageId` e o guardião ecoar em `id`, ou (b) o PC aceitar acks `reason=guardian-*` com `applied && a11y`. O payload do guardião já manda `id`, `applied`, `a11y`, `admin`, `ts`, `endMs`/`blockEndMs`, `hb` e `source:"guardian"` para viabilizar (b) |
| Overlay de aviso cobre o topo da tela | incômodo visual quando a proteção cai | só aparece em falha, some ao recuperar, toque abre o app principal, dismissível |
| `GuardianStatusActivity` exportada (só para o ADB abrir) | qualquer app pode abri-la | ela só inicia o guardião/abre ajustes do sistema; não expõe dados |
| Admin do guardião é **non-testOnly** (como o do principal) | `dpm remove-active-admin` falha (SecurityException, T7); `adb uninstall` pode ser recusado | desativar em Ajustes → Segurança → Administração do dispositivo antes de desinstalar; é atrito intencional |
| Pacote recém-instalado fica em estado *stopped* | BOOT_COMPLETED não é entregue até o primeiro launch explícito | o passo 5 de instalação abre a `GuardianStatusActivity` uma vez (faz parte do roteiro) |
| Dois canais de notificação + FGS permanente | notificação persistente | é o preço de sobreviver a force-stop no Android; `IMPORTANCE_LOW` no status, `HIGH` só em alerta |

---

## 10. Rollback / desinstalação limpa

```powershell
# 1) parar o guardião e desativar o admin pela UI do sistema (não há como via adb para admin non-test)
& $adb shell am force-stop com.example.focusapp.guardian
& $adb shell am start -a android.settings.DEVICE_ADMIN_SETTINGS   # desative "Guardião Foco"

# 2) desinstalar o guardião (se o admin ainda estiver ativo, o uninstall pode falhar: desative antes)
& $adb uninstall com.example.focusapp.guardian

# 3) reverter os arquivos do repo (backups .bak da primeira aplicação e remover o módulo/include)
#    git -C C:\Users\israe\OneDrive\Desktop\Projetos\app checkout -- settings.gradle.kts
#    git -C ... status  # e remover guardian/ do working tree, se desejar
```

O app principal continua funcionando sem o guardião (Fase 1), apenas perde a recuperação de `force-stop`.

---

## 11. Checklist para o Lead (ordem sugerida)

1. [ ] aplicar patch da Fase 1 do app principal e **este** patch (`apply-guardian.ps1`) no repo Android;
2. [ ] `.\gradlew.bat :app:assembleRelease :guardian:assembleRelease`;
3. [ ] conferir assinatura dos dois APKs (`apksigner verify --print-certs` → mesmo certificado);
4. [ ] `adb install -r` app principal, depois guardião;
5. [ ] grants/`appops`/`dpm set-active-admin` do passo 7; abrir a tela de status uma vez;
6. [ ] conferir logcat + FGS + permission `PROTECTION_CONTROL` `granted=true`;
7. [ ] teste de aceite do passo 8 (`force-stop` do principal → a11y bound ≤ 30 s);
8. [ ] teste `force-stop` do guardião (principal deve reerguer) e reboot;
9. [ ] reportar no relatório final os limites do passo 9 (inclusive a pendência do `id` do ack no PC).
