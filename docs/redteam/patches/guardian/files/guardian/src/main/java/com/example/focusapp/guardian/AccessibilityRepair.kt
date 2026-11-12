package com.example.focusapp.guardian

import android.accessibilityservice.AccessibilityServiceInfo
import android.content.Context
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import android.view.accessibility.AccessibilityManager

/**
 * Reparo do serviço de acessibilidade do app principal por WRITE_SECURE_SETTINGS.
 *
 * Limite comprovado no red-team (T4): reescrever o setting com o pacote force-stopped NÃO religa o
 * serviço — o AccessibilityManagerService recusa o bind de pacote "stopped". Por isso o guardião
 * primeiro faz um start explícito no ReviveService do principal (limpa o stopped) e só depois reescreve.
 */
internal object AccessibilityRepair {

    private const val TAG = "GuardianRepair"
    private const val SETTING_SEPARATOR = ":"

    /** Componente no formato flatten aceito em enabled_accessibility_services. */
    private val mainComponent =
        "${ProtectionContract.MAIN_PACKAGE}/${ProtectionContract.MAIN_ACCESSIBILITY_SERVICE}"

    /**
     * O AccessibilityManager lista os serviços habilitados nos settings. Cuidado: um pacote force-stopped
     * pode continuar "listado" sem bind (foi o caso do teste T4), por isso esta checagem sozinha não
     * prova saúde — o guardião só considera o principal saudável quando o provider responde E isto é true.
     */
    @Suppress("DEPRECATION")
    fun isMainServiceListed(context: Context): Boolean = try {
        val manager = context.getSystemService(Context.ACCESSIBILITY_SERVICE) as AccessibilityManager
        manager.getEnabledAccessibilityServiceList(AccessibilityServiceInfo.FEEDBACK_ALL_MASK).any { info ->
            val serviceInfo = info.resolveInfo?.serviceInfo
            serviceInfo != null &&
                serviceInfo.packageName == ProtectionContract.MAIN_PACKAGE &&
                serviceInfo.name == ProtectionContract.MAIN_ACCESSIBILITY_SERVICE
        }
    } catch (error: Exception) {
        Log.w(TAG, "não foi possível ler a lista de acessibilidade: ${error.javaClass.simpleName}")
        false
    }

    /**
     * Preserva a lista atual (outros serviços de acessibilidade do usuário continuam valendo), garante o
     * componente do app principal e liga accessibility_enabled. Devolve false se faltar
     * WRITE_SECURE_SETTINGS ou se o sistema recusar a escrita.
     */
    fun ensureMainServiceEnabled(context: Context): Boolean {
        if (!GuardianPermissions.hasWriteSecureSettings(context)) {
            Log.w(TAG, "sem WRITE_SECURE_SETTINGS: não é possível religar a acessibilidade")
            return false
        }
        val resolver = context.contentResolver
        return try {
            val current = Settings.Secure.getString(
                resolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            ).orEmpty()
            val services = current.split(SETTING_SEPARATOR).filter { it.isNotBlank() }.toMutableList()
            if (services.none { matchesMainComponent(it) }) {
                services.add(mainComponent)
                Log.i(TAG, "acrescentando o serviço de acessibilidade do app principal à lista")
            }
            Settings.Secure.putString(
                resolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                services.joinToString(SETTING_SEPARATOR),
            )
            Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)
            Log.i(TAG, "settings de acessibilidade reescritos (total de serviços=${services.size})")
            true
        } catch (error: Exception) {
            Log.e(TAG, "falha ao reescrever a acessibilidade: ${error.javaClass.simpleName}: ${error.message}")
            false
        }
    }

    private fun matchesMainComponent(entry: String): Boolean {
        val slash = entry.indexOf('/')
        if (slash <= 0) return false
        val packageName = entry.substring(0, slash)
        if (packageName != ProtectionContract.MAIN_PACKAGE) return false
        val className = entry.substring(slash + 1)
        val fullClassName = if (className.startsWith(".")) packageName + className else className
        return fullClassName == ProtectionContract.MAIN_ACCESSIBILITY_SERVICE
    }

    /**
     * Toggle "de verdade" (desliga por ~1,8 s e religa). Teste real no Moto G20: depois de um
     * force-stop, reescrever o setting sem o toggle NÃO rebinda o serviço; com o toggle, o
     * AccessibilityManagerService refaz o bind em ~2 s. É a peça que faltava no ciclo de revive.
     */
    fun forceToggleMainService(context: Context): Boolean {
        if (!GuardianPermissions.hasWriteSecureSettings(context)) return false
        val resolver = context.contentResolver
        return try {
            val current = Settings.Secure.getString(
                resolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            ).orEmpty()
            val others = current.split(SETTING_SEPARATOR)
                .map { it.trim() }
                .filter { it.isNotEmpty() && !matchesMainComponent(it) }
            Settings.Secure.putString(
                resolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                others.joinToString(SETTING_SEPARATOR),
            )
            Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 0)
            Log.i(TAG, "toggle: acessibilidade desligada temporariamente (outros=${others.size})")
            SystemClock.sleep(1800L)
            val listOk = Settings.Secure.putString(
                resolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                (others + mainComponent).joinToString(SETTING_SEPARATOR),
            )
            val enabledOk = Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)
            Log.i(TAG, "toggle: acessibilidade religada (listOk=$listOk enabledOk=$enabledOk)")
            listOk && enabledOk
        } catch (error: Exception) {
            Log.e(TAG, "falha no toggle da acessibilidade: ${error.javaClass.simpleName}: ${error.message}")
            false
        }
    }
}
