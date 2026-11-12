package com.example.focusapp.guardian

import android.content.Context
import android.content.Intent
import android.util.Log

/** Como o guardião chama o app principal (sempre por componente explícito, nunca por intent implícito). */
internal object MainAppControl {

    private const val TAG = "GuardianMainApp"

    enum class PackageAvailability { INSTALLED, DISABLED, MISSING }

    fun availability(context: Context): PackageAvailability = try {
        val info = context.packageManager.getApplicationInfo(ProtectionContract.MAIN_PACKAGE, 0)
        if (info.enabled) PackageAvailability.INSTALLED else PackageAvailability.DISABLED
    } catch (error: Exception) {
        PackageAvailability.MISSING
    }

    /**
     * Start explícito no ReviveService do app principal: é o que limpa o estado "stopped" deixado pelo
     * `am force-stop` (um serviço/receiver explícito conta como início do pacote; settings sozinhos não).
     */
    fun startReviveService(context: Context): Boolean = try {
        val intent = Intent()
            .setClassName(ProtectionContract.MAIN_PACKAGE, ProtectionContract.MAIN_REVIVE_SERVICE)
            .setAction(ProtectionContract.ACTION_REVIVE)
        val started = context.startForegroundService(intent)
        if (started == null) {
            Log.w(TAG, "ReviveService não iniciou (retorno nulo)")
            false
        } else {
            Log.i(TAG, "ReviveService iniciado explicitamente: $started")
            true
        }
    } catch (error: Exception) {
        // ReviveService ausente (app principal sem o patch da Fase 1) ou permissão signature ainda não
        // concedida: o fallback do guardião é lançar a MainActivity.
        Log.w(TAG, "falha ao iniciar ReviveService: ${error.javaClass.simpleName}: ${error.message}")
        false
    }

    /**
     * Último recurso: lançar a MainActivity do principal (abrir o app também limpa o "stopped").
     * Em Android 12+ isto depende da isenção de background activity start (SYSTEM_ALERT_WINDOW +
     * janela visível do overlay); por isso o guardião mostra o overlay ANTES.
     */
    fun launchMainActivity(context: Context): Boolean = try {
        val intent = Intent(Intent.ACTION_MAIN)
            .setClassName(ProtectionContract.MAIN_PACKAGE, ProtectionContract.MAIN_ACTIVITY)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED)
        context.startActivity(intent)
        Log.i(TAG, "MainActivity lançada para limpar o estado stopped")
        true
    } catch (error: Exception) {
        Log.w(TAG, "falha ao lançar MainActivity: ${error.javaClass.simpleName}: ${error.message}")
        false
    }
}
