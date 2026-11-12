# Patch proposto — android-codigo (documento textual, não aplicado)

O repositório do Android (`C:\Users\israe\OneDrive\Desktop\Projetos\app`) é somente leitura para este agente.
Este arquivo consolida as mudanças propostas no relatório `docs/redteam/01-android-codigo.md` em forma de patch
textual (pseudo-`git diff`), para o dono/Lead aplicarem manualmente. Contexto/hunks seguem o working tree v1.6.

Arquivos novos + alterados (ordem de prioridade):

1. `app/src/main/java/com/example/focusapp/service/FocusEnforcementState.kt` (novo) — heartbeat/liveness.
2. `app/src/main/java/com/example/focusapp/service/AccessibilityWatchdog.kt` (novo) — reparo via WRITE_SECURE_SETTINGS.
3. `app/src/main/java/com/example/focusapp/service/FocusAccessibilityService.kt` — heartbeat + guard de rótulos.
4. `app/src/main/java/com/example/focusapp/service/ScreenTimeTrackerService.kt` — chamar o watchdog.
5. `app/src/main/java/com/example/focusapp/push/PomodoroPushService.kt` — ack de aplicação.
6. `app/src/main/java/com/example/focusapp/push/BlockStatusReporter.kt` (novo) — ack LAN HTTP + HMAC (fallback ntfy).
7. `app/src/main/java/com/example/focusapp/ui/DailyBlockCard.kt` — senha ao desligar "Modo seletivo".
8. `app/src/main/java/com/example/focusapp/ui/FocusDashboardScreen.kt` — senha ao subir limite/zerar dívida.
9. `app/src/main/java/com/example/focusapp/schedule/DailyBlockPolicy.kt` — âncora monotônica anti-relógio.
10. `app/src/main/java/com/example/focusapp/service/FocusBlockRules.kt` — descoberta de navegadores em runtime.
11. `app/build.gradle.kts` — release não debuggable e keystore fora do repo (higiene).

Os corpos completos dos esboços estão no §7 do relatório (seções 7.1–7.9). Abaixo apenas os diffs resumidos
para os arquivos existentes.

---

## 1) FocusAccessibilityService.kt — heartbeat de liveness

```diff
 class FocusAccessibilityService : AccessibilityService() {
+    private val heartbeatHandler = android.os.Handler(android.os.Looper.getMainLooper())
+    private val heartbeatTick = object : Runnable {
+        override fun run() {
+            FocusEnforcementState.beat(this@FocusAccessibilityService)
+            heartbeatHandler.postDelayed(this, 30_000L)
+        }
+    }
 
     override fun onServiceConnected() {
         super.onServiceConnected()
+        FocusEnforcementState.beat(this)
+        heartbeatHandler.removeCallbacks(heartbeatTick)
+        heartbeatHandler.postDelayed(heartbeatTick, 30_000L)
         windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager
@@
     override fun onDestroy() {
         super.onDestroy()
+        heartbeatHandler.removeCallbacksAndMessages(null)
         dailyBlockController?.close()
```

## 2) FocusAccessibilityService.kt — guard de Ajustes com o rótulo CERTO

```diff
-        val ourAppNodes = rootNode.findAccessibilityNodeInfosByText(getString(R.string.app_name))
-                .ifEmpty { rootNode.findAccessibilityNodeInfosByText("com.example.focusapp") }
-
-        if (ourAppNodes.isNotEmpty()) {
+        // A tela de Acessibilidade mostra @string/accessibility_service_name
+        // ("Serviço de Foco e Proteção"), não o nome do app.
+        val labels = listOf(
+            getString(R.string.app_name),
+            getString(R.string.accessibility_service_name),
+            packageName
+        )
+        val hit = labels.any { label ->
+            rootNode.findAccessibilityNodeInfosByText(label).orEmpty().isNotEmpty()
+        }
+        if (hit) {
             performGlobalAction(GLOBAL_ACTION_HOME)
             startActivity(intent)
         }
```

## 3) ScreenTimeTrackerService.kt — watchdog a cada tick/SCREEN_ON

```diff
+    private val accessibilityWatchdog by lazy { AccessibilityWatchdog(this) }
@@ onScreenTurnedOn():
+        accessibilityWatchdog.checkAndRepair()
         if (usageInitialized && tickerJob?.isActive != true) {
@@ dentro do while(isActive && isScreenInteractive), no início da iteração:
+                accessibilityWatchdog.checkAndRepair()
```

## 4) PomodoroPushService.kt — ack do que foi aplicado

```diff
     override fun onMessageReceived(message: RemoteMessage) {
         val data = message.data
         val store = DailyBlockStore(this)
         data["adultEndMs"]?.toLongOrNull()?.let { adultEndMs -> store.saveAdult(adultEndMs) }
         val id = data["id"] ?: return
         val durationMs = data["durationMs"]?.toLongOrNull() ?: return
         val endMs = data["endMs"]?.toLongOrNull() ?: return
-        if (store.savePomodoro(id, endMs, durationMs)) DailyBlockScheduler.refresh(this)
+        val accepted = store.savePomodoro(id, endMs, durationMs)
+        if (accepted) DailyBlockScheduler.refresh(this)
+        // O PC só deve marcar "Celular bloqueado" quando receber este ack com a11yAlive=true.
+        BlockStatusReporter.reportAsync(this, reason = "fcm", blockId = id, nonce = data["nonce"])
     }
```

## 5) DailyBlockCard.kt — senha ao desligar o Modo seletivo (buraco A-01)

```diff
                 Switch(
                     checked = selective,
                     onCheckedChange = { enabled ->
-                        store.saveSelective(enabled)
-                        version++
-                        message = if (enabled) "Modo seletivo ativado." else "Modo seletivo desativado."
+                        if (!enabled && manager.isPasswordSet()) {
+                            // abre PasswordConfirmDialog; só aplica no callback verificado:
+                            pendingSelectiveOff = true
+                        } else {
+                            store.saveSelective(enabled)
+                            version++
+                        }
                     },
```
(Adicionar estado `pendingSelectiveOff` + `PasswordConfirmDialog` igual ao usado para reduzir nível em
`FocusDashboardScreen.kt:330-340`; no sucesso: `store.saveSelective(false); version++`.)

## 6) FocusDashboardScreen.kt — senha para subir limite e zerar dívida

```diff
-                    DashboardModule.DAILY_LIMIT -> DailyLimitPickerSection(
-                        currentLimitMillis = dailyLimitMillis,
-                        onSelectLimit = { millis, label -> viewModel.setDailyLimit(millis, label) }
-                    )
+                    DashboardModule.DAILY_LIMIT -> DailyLimitPickerSection(
+                        currentLimitMillis = dailyLimitMillis,
+                        onSelectLimit = { millis, label ->
+                            // subir limite exige senha (reduzir é livre)
+                            if (millis > dailyLimitMillis) pendingLimitIncrease = millis to label
+                            else viewModel.setDailyLimit(millis, label)
+                        }
+                    )
```
(Análogo para `resetMoneyOwed`/`subtractMoneyOwed` em `:300-305`.)

## 7) build.gradle.kts — release de verdade (higiene D-04)

```diff
-    signingConfigs {
-        getByName("debug") {
-            storeFile = file("debug.keystore")
-            storePassword = "android"
-            keyAlias = "androiddebugkey"
-            keyPassword = "android"
-        }
-    }
+    signingConfigs {
+        create("release") {
+            // caminho/token vêm de variáveis de ambiente/keystore.properties FORA do repo (nunca versionar)
+            storeFile = file(System.getenv("FOCUS_RELEASE_KEYSTORE") ?: "release.keystore")
+            storePassword = System.getenv("FOCUS_RELEASE_STORE_PASSWORD")
+            keyAlias = System.getenv("FOCUS_RELEASE_KEY_ALIAS")
+            keyPassword = System.getenv("FOCUS_RELEASE_KEY_PASSWORD")
+        }
+    }
@@ release {
         isMinifyEnabled = true
         isShrinkResources = true
-        signingConfig = signingConfigs.getByName("debug")
+        signingConfig = signingConfigs.getByName("release")
     }
```
+ remover `app/debug.keystore` do git (se já não estiver no histórico público, só parar de versionar).

## 8) FocusBlockRules.kt — descoberta de navegadores (buraco B-01)

```diff
+    fun discoverBrowserPackages(context: android.content.Context): Set<String> {
+        val intent = android.content.Intent(
+            android.content.Intent.ACTION_VIEW, android.net.Uri.parse("https://example.com")
+        )
+        return context.packageManager
+            .queryIntentActivities(intent, android.content.pm.PackageManager.MATCH_ALL)
+            .map { it.activityInfo.packageName }
+            .toSet()
+    }
```
Uso em `onServiceConnected`:
```diff
-                (FocusBlockRules.monitoredPackages + listOfNotNull(homePackage)).toTypedArray()
+                (FocusBlockRules.monitoredPackages +
+                    FocusBlockRules.discoverBrowserPackages(this) +
+                    listOfNotNull(homePackage)).toTypedArray()
```

## 9) DailyBlockPolicy.kt — âncora monotônica (buraco C-01)

```diff
 internal data class PomodoroBlockRecord(
     val id: String,
     val startMs: Long,
     val endMs: Long,
-    val released: Boolean = false
+    val released: Boolean = false,
+    val endElapsedRealtime: Long = 0L
 ) {
+    fun remainingMs(nowMs: Long, nowElapsed: Long): Long {
+        val byWall = endMs - nowMs
+        val byMono = if (endElapsedRealtime > 0L) endElapsedRealtime - nowElapsed else Long.MIN_VALUE
+        return maxOf(byWall, byMono).coerceAtLeast(0L)
+    }
 }
```
+ `acceptPomodoro`/`savePomodoro` gravam `endElapsedRealtime = SystemClock.elapsedRealtime() + (end - nowMs)`;
+ `shouldBlockAt(record, nowMs, nowElapsed)` passa a usar `record.remainingMs(...) > 0`.
(Para o bloqueio diário, que é hora de parede por definição: manter o comportamento, mas detectar
`ACTION_TIME_CHANGED` durante bloqueio ativo e avisar/репortar ao PC — §7.6 do relatório.)

## 10) Novo: FocusEnforcementState.kt / AccessibilityWatchdog.kt / BlockStatusReporter.kt

Corpos completos no §7.1, §7.2 e §7.4 do relatório `01-android-codigo.md`. Pontos de integração:

- `FocusAccessibilityService.onServiceConnected/onDestroy` → heartbeat.
- `ScreenTimeTrackerService` (FGS) → `AccessibilityWatchdog.checkAndRepair()` no tick e em `SCREEN_ON/USER_PRESENT`.
- `PomodoroPushService.onMessageReceived` → `BlockStatusReporter.reportAsync(...)`.
- `ui/DashboardViewModel.kt:104` e `ui/DailyBlockCard.kt:51,83,135` → trocar
  `FocusAccessibilityService.isEnabled(context)` por `FocusEnforcementState.isEnforcing(context)`.
- Manifest: nenhuma permissão nova no app principal (`WRITE_SECURE_SETTINGS` já concedido via ADB);
  o pacote guardião (§7.3) exige um segundo módulo `app-guard` com `SYSTEM_ALERT_WINDOW` + FGS + permissão
  `signature` própria e `WRITE_SECURE_SETTINGS` concedido por ADB uma vez.
```
