package com.example.focusapp.guard

import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Ponte com o pacote guardião da Fase 2 (`com.example.focusapp.guardian`), assinado com a mesma chave.
 *
 * O guardião é o único capaz de recuperar de `force-stop`: ele faz launch explícito do app principal
 * (limpa o estado *stopped*), chama o [ReviveService], reescreve os settings e mostra overlay de
 * fallback. Enquanto ele não existir, o watchdog apenas reporta `guardian=false` no ack — é a
 * limitação honesta da Fase 1 (documentada no README).
 */
internal object GuardianBridge {
    private const val TAG = "GuardianBridge"

    const val GUARDIAN_PACKAGE = "com.example.focusapp.guardian"
    const val GUARDIAN_SERVICE = "com.example.focusapp.guardian.GuardianService"
    private const val GUARDIAN_ACTION = "com.example.focusapp.guardian.action.START"

    /** `pm.getPackageInfo` sem lançar: guardião é opcional na Fase 1. */
    fun isInstalled(context: Context): Boolean = runCatching {
        context.packageManager.getPackageInfo(GUARDIAN_PACKAGE, 0)
        true
    }.getOrDefault(false)

    /**
     * Tenta acordar o guardião. Pode falhar sem instalar/permissão — sempre em try/catch. Nosso app
     * tem um foreground service rodando, então a restrição de iniciar FGS em background não se aplica.
     */
    fun start(context: Context): Boolean {
        if (!isInstalled(context)) return false
        return runCatching {
            val intent = Intent()
                .setClassName(GUARDIAN_PACKAGE, GUARDIAN_SERVICE)
                .setAction(GUARDIAN_ACTION)
            context.startForegroundService(intent)
            true
        }.getOrElse { error ->
            Log.w(TAG, "Não foi possível iniciar o guardião da Fase 2", error)
            false
        }
    }
}
