package com.example.focusapp.guard

import android.Manifest
import android.accessibilityservice.AccessibilityServiceInfo
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.os.SystemClock
import android.os.UserManager
import android.provider.Settings
import android.util.Log
import android.view.accessibility.AccessibilityManager
import com.example.focusapp.admin.AdminPolicyReceiver
import com.example.focusapp.service.FocusAccessibilityService

/**
 * Operações de integridade que o watchdog, o [ReviveService] e o guardião (Fase 2) compartilham:
 * religar a acessibilidade via `Settings.Secure`, checar/reativar o admin de dispositivo, ler o estado
 * da depuração USB e detectar multiusuário/perfis. (O app NÃO desliga mais o ADB — decisão do dono.)
 *
 * **IMPORTANTE:** `WRITE_SECURE_SETTINGS` é permissão de assinatura/privada — NUNCA é assumida. O
 * aparelho-alvo (Moto G20) ainda **não** a tem concedida; sem ela, [ensureServiceEnabled] devolve
 * [EnsureResult.Failed] com motivo e o chamador deve notificar o dono e reportar `a11y=false` no ack.
 * O README do patch traz o comando de concessão (`adb shell pm grant ... WRITE_SECURE_SETTINGS`).
 *
 * Os métodos que fazem polling ([ensureServiceEnabled], [forceRebind]) bloqueiam até ~5 s e devem
 * ser chamados de thread de background (nunca da main thread).
 */
internal object AccessibilityGuard {
    private const val TAG = "AccessibilityGuard"

    /** Códigos de motivo para ack/notificação; mantidos curtos e estáveis para o PC. */
    const val REASON_ALREADY_ENABLED = "already_enabled"
    const val REASON_ENABLED = "enabled"
    const val REASON_NO_PERMISSION = "no_write_secure_settings"
    const val REASON_WRITE_REJECTED = "settings_write_rejected"
    const val REASON_NOT_BOUND = "not_bound"
    const val REASON_ENABLED_NOT_BOUND = "enabled_not_bound"

    /** Chave literal: `Settings.Global.ADB_WIFI_ENABLED` só existe como constante desde a API 31. */
    private const val KEY_ADB_WIFI_ENABLED = "adb_wifi_enabled"

    /** Intervalo de polling do binding; o teste real mediu religação em ~1,1 s. */
    private const val POLL_STEP_MS = 250L

    /** Resultado de uma tentativa de garantir a acessibilidade. */
    internal sealed class EnsureResult(val ok: Boolean, val reason: String) {
        object AlreadyEnabled : EnsureResult(true, REASON_ALREADY_ENABLED)
        object Enabled : EnsureResult(true, REASON_ENABLED)
        data class Failed(private val failure: String) : EnsureResult(false, failure)
    }

    fun serviceComponent(context: Context): ComponentName =
        ComponentName(context.packageName, FocusAccessibilityService::class.java.name)

    fun componentId(context: Context): String = serviceComponent(context).flattenToString()

    fun hasWriteSecureSettings(context: Context): Boolean =
        context.checkSelfPermission(Manifest.permission.WRITE_SECURE_SETTINGS) == PackageManager.PERMISSION_GRANTED

    /**
     * "Habilitado" = aparece em `getEnabledAccessibilityServiceList`. Isso NÃO garante que o serviço
     * esteja vinculado (crash/force-stop deixa habilitado e morto) — para liveness real use
     * [FocusEnforcementState.isEnforcing].
     */
    fun isEnabled(context: Context): Boolean = runCatching {
        val manager = context.getSystemService(Context.ACCESSIBILITY_SERVICE) as AccessibilityManager
        manager.getEnabledAccessibilityServiceList(AccessibilityServiceInfo.FEEDBACK_ALL_MASK).any { service ->
            val info = service.resolveInfo?.serviceInfo ?: return@any false
            info.packageName == context.packageName &&
                info.name == FocusAccessibilityService::class.java.name
        }
    }.getOrDefault(false)

    /** O componente está escrito em `enabled_accessibility_services` (pode estar habilitado e morto). */
    fun isComponentListed(context: Context): Boolean = runCatching {
        val current = Settings.Secure.getString(
            context.contentResolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
        ).orEmpty()
        current.split(':').any { it.trim() == componentId(context) }
    }.getOrDefault(false)

    /**
     * Garante o serviço de acessibilidade habilitado. É **idempotente** e preserva os outros serviços
     * já habilitados (nunca sobrescreve a lista com um único item). Se o componente já está listado
     * mas o serviço não vinculou (crash), tenta um toggle único do componente antes de desistir.
     *
     * Não vence `force-stop`: o Android marca o pacote como *stopped* e recusa o bind; nesse caso
     * devolve [EnsureResult.Failed] com [REASON_NOT_BOUND] e o guardião (Fase 2) precisa fazer o
     * launch explícito do app antes. Documentado no README.
     */
    fun ensureServiceEnabled(context: Context, timeoutMs: Long = 5_000L): EnsureResult {
        if (isEnabled(context) && FocusEnforcementState.isEnforcing(context)) {
            return EnsureResult.AlreadyEnabled
        }
        if (!hasWriteSecureSettings(context)) {
            Log.w(TAG, "WRITE_SECURE_SETTINGS não concedida: não é possível religar a acessibilidade")
            return EnsureResult.Failed(REASON_NO_PERMISSION)
        }
        val wrote = runCatching { writeServiceEnabled(context) }.getOrElse { error ->
            Log.w(TAG, "Falha ao escrever enabled_accessibility_services", error)
            false
        }
        if (!wrote) return EnsureResult.Failed(REASON_WRITE_REJECTED)
        if (pollUntilEnabled(context, timeoutMs)) return EnsureResult.Enabled

        // Habilitado no setting mas não vinculado (crash): um toggle explícito às vezes força o rebind.
        val toggled = runCatching { toggleComponent(context) }.getOrDefault(false)
        if (toggled && pollUntilEnabled(context, timeoutMs)) return EnsureResult.Enabled
        return EnsureResult.Failed(if (isComponentListed(context)) REASON_ENABLED_NOT_BOUND else REASON_NOT_BOUND)
    }

    /** Inclui o componente na lista e liga `accessibility_enabled`. Preserva os demais serviços. */
    private fun writeServiceEnabled(context: Context): Boolean {
        val resolver = context.contentResolver
        val component = componentId(context)
        val current = Settings.Secure.getString(
            resolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
        ).orEmpty()
        val services = current.split(':').map { it.trim() }.filter { it.isNotEmpty() }.toMutableList()
        if (services.none { it == component }) services.add(component)
        val listOk = Settings.Secure.putString(
            resolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            services.joinToString(":")
        )
        val enabledOk = Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)
        return listOk && enabledOk
    }

    /**
     * Remove e re-adiciona o componente (toggle) para tentar limpar o estado "Crashed services".
     * Bloqueia a thread por ~150 ms — chamar só em background.
     */
    fun forceRebind(context: Context, timeoutMs: Long = 5_000L): EnsureResult {
        if (isEnabled(context) && FocusEnforcementState.isEnforcing(context)) {
            return EnsureResult.AlreadyEnabled
        }
        if (!hasWriteSecureSettings(context)) return EnsureResult.Failed(REASON_NO_PERMISSION)
        val toggled = runCatching { toggleComponent(context) }.getOrElse { error ->
            Log.w(TAG, "Falha no toggle do componente de acessibilidade", error)
            false
        }
        if (!toggled) return EnsureResult.Failed(REASON_WRITE_REJECTED)
        return if (pollUntilEnabled(context, timeoutMs)) EnsureResult.Enabled
        else EnsureResult.Failed(REASON_ENABLED_NOT_BOUND)
    }

    private fun toggleComponent(context: Context): Boolean {
        val resolver = context.contentResolver
        val component = componentId(context)
        val current = Settings.Secure.getString(
            resolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
        ).orEmpty()
        val others = current.split(':').map { it.trim() }.filter { it.isNotEmpty() && it != component }
        Settings.Secure.putString(
            resolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            others.joinToString(":")
        )
        // Teste no Moto G20 (Android 11): depois de force-stop/queda, o AMS só refaz o bind se enxergar
        // o "desligado" por tempo suficiente; 150 ms não bastava. 1,8 s foi o valor que rebindou.
        Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 0)
        SystemClock.sleep(1800L)
        val listOk = Settings.Secure.putString(
            resolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            (others + component).joinToString(":")
        )
        val enabledOk = Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)
        return listOk && enabledOk
    }

    private fun pollUntilEnabled(context: Context, timeoutMs: Long): Boolean {
        val deadline = SystemClock.elapsedRealtime() + timeoutMs.coerceAtLeast(POLL_STEP_MS)
        while (SystemClock.elapsedRealtime() < deadline) {
            if (isEnabled(context)) return true
            SystemClock.sleep(POLL_STEP_MS)
        }
        return isEnabled(context)
    }

    fun isAdminActive(context: Context): Boolean = runCatching {
        AdminPolicyReceiver.isAdminActive(context)
    }.getOrDefault(false)

    /**
     * Tenta reativar o admin de dispositivo programaticamente. Em Android moderno isso costuma falhar
     * (o fluxo normal é `ACTION_ADD_DEVICE_ADMIN` com confirmação do usuário) — o teste real mostrou
     * que `pm disable-user` remove o admin e `pm enable` não o devolve. Devolve false e o chamador
     * notifica/abre o app para o dono reativar. Nunca lança.
     */
    fun tryReactivateAdmin(context: Context): Boolean {
        return runCatching {
            val dpm = context.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
            val component = AdminPolicyReceiver.getComponentName(context)
            if (dpm.isAdminActive(component)) return@runCatching true
            // `setActiveAdmin` não faz parte do SDK público do compileSdk 36. Em Android moderno o
            // fluxo normal é a confirmação do usuário; tentamos por reflexão e, se o sistema recusar,
            // devolvemos false para o chamador notificar o dono a reativar em Ajustes.
            val method = dpm.javaClass.getMethod(
                "setActiveAdmin",
                ComponentName::class.java,
                java.lang.Boolean.TYPE
            )
            method.invoke(dpm, component, true)
            dpm.isAdminActive(component)
        }.getOrElse { error ->
            Log.w(TAG, "Reativação programática do admin recusada pelo sistema", error)
            false
        }
    }

    fun isAdbEnabled(context: Context): Boolean =
        readGlobalInt(context, Settings.Global.ADB_ENABLED, 0) == 1

    fun isAdbWifiEnabled(context: Context): Boolean =
        readGlobalInt(context, KEY_ADB_WIFI_ENABLED, 0) == 1

    fun isDeveloperOptionsEnabled(context: Context): Boolean =
        readGlobalInt(context, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) == 1

    /**
     * Desliga a depuração USB (e a sem fio) **enquanto um bloqueio está ativo**, guardando o estado
     * anterior para restaurar depois. Isso é o anti-ADB da Fase 1: atrito contra o bypass mais rápido
     * (`adb shell settings put ...`). Não previne um usuário decidido (ele reabre as Opções do
     * desenvolvedor ou reinicia), mas derruba o ATACANTE preguiçoso e pode ser reaplicado a cada 10 s.
     * Nunca lança; devolve false sem permissão.
     */
    fun disableAdbWhileProtected(context: Context): Boolean {
        if (!hasWriteSecureSettings(context)) return false
        return runCatching {
            val resolver = context.contentResolver
            val previousUsb = readGlobalInt(context, Settings.Global.ADB_ENABLED, -1)
            val previousWifi = readGlobalInt(context, KEY_ADB_WIFI_ENABLED, -1)
            val usbOk = Settings.Global.putInt(resolver, Settings.Global.ADB_ENABLED, 0)
            val wifiOk = Settings.Global.putInt(resolver, KEY_ADB_WIFI_ENABLED, 0)
            if (usbOk || wifiOk) {
                GuardState.recordAdbDisabledByGuard(context, previousUsb, previousWifi)
            }
            usbOk && wifiOk
        }.getOrElse { error ->
            Log.w(TAG, "Falha ao desligar a depuração USB", error)
            false
        }
    }

    /**
     * Restaura a depuração exatamente como estava **somente** se foi o guardião que a desligou e o
     * bloqueio terminou. Sem permissão devolve false (e o estado permanece marcado para nova tentativa).
     */
    fun restoreAdb(context: Context): Boolean {
        if (!hasWriteSecureSettings(context)) return false
        return runCatching {
            val previousUsb = GuardState.adbPreviousEnabled(context)
            val previousWifi = GuardState.adbPreviousWifi(context)
            val resolver = context.contentResolver
            var ok = true
            if (previousUsb >= 0) {
                ok = Settings.Global.putInt(resolver, Settings.Global.ADB_ENABLED, previousUsb) && ok
            }
            if (previousWifi >= 0) {
                ok = Settings.Global.putInt(resolver, KEY_ADB_WIFI_ENABLED, previousWifi) && ok
            }
            if (ok) GuardState.clearAdbAutoDisabled(context)
            ok
        }.getOrElse { error ->
            Log.w(TAG, "Falha ao restaurar a depuração USB", error)
            false
        }
    }

    /** `boot_count`: âncora monotônica só vale dentro do mesmo boot (elapsedRealtime zera no reboot). */
    fun bootCount(context: Context): Int = readGlobalInt(context, "boot_count", -1)

    fun userCount(context: Context): Int = runCatching {
        val manager = context.getSystemService(Context.USER_SERVICE) as UserManager
        manager.userCount
    }.getOrDefault(1)

    /** Inclui o próprio usuário; > 1 significa perfil de trabalho/segundo usuário (sem a11y lá). */
    fun profileCount(context: Context): Int = runCatching {
        val manager = context.getSystemService(Context.USER_SERVICE) as UserManager
        manager.userProfiles.size
    }.getOrDefault(1)

    private fun readGlobalInt(context: Context, key: String, defaultValue: Int): Int = runCatching {
        Settings.Global.getInt(context.contentResolver, key, defaultValue)
    }.getOrDefault(defaultValue)
}

