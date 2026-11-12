package com.example.focusapp.guard

import android.content.Context
import android.os.SystemClock

/**
 * Liveness REAL do serviço de acessibilidade (A-03 do red-team).
 *
 * `isEnabled()` (lista de serviços habilitados) **não** garante que o serviço esteja vinculado: uma
 * queda/force-stop deixa o serviço "habilitado" na configuração e morto na prática — foi observado no
 * Moto G20 (`Bound services:{}`, `Crashed services:{...}`) com o card do app dizendo “Ativo”.
 *
 * O próprio [com.example.focusapp.service.FocusAccessibilityService] grava um heartbeat no
 * `onServiceConnected` e a cada ~20 s enquanto vinculado. A UI e o watchdog só consideram a proteção
 * "de verdade" ([isEnforcing]) com heartbeat recente.
 */
internal object FocusEnforcementState {
    private const val PREFS = "focus_enforcement_state"
    private const val KEY_LAST_HEARTBEAT = "a11y_last_heartbeat_elapsed_ms"
    private const val KEY_LAST_BLOCK_EVENT = "a11y_last_block_event_elapsed_ms"

    /** O serviço bate a cada 20 s; 90 s tolera Doze curto sem acusar falso-negativo. */
    const val HEARTBEAT_TIMEOUT_MS = 90_000L

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Chamado pelo serviço de acessibilidade (imediato em onServiceConnected e a cada 20 s). */
    fun beat(context: Context) {
        prefs(context).edit()
            .putLong(KEY_LAST_HEARTBEAT, SystemClock.elapsedRealtime())
            .apply()
    }

    /** Marca que o serviço aplicou um bloqueio de verdade (usado só para diagnóstico). */
    fun beatBlockedEvent(context: Context) {
        prefs(context).edit()
            .putLong(KEY_LAST_HEARTBEAT, SystemClock.elapsedRealtime())
            .putLong(KEY_LAST_BLOCK_EVENT, SystemClock.elapsedRealtime())
            .apply()
    }

    fun lastHeartbeatElapsed(context: Context): Long =
        prefs(context).getLong(KEY_LAST_HEARTBEAT, 0L)

    /** Acessibilidade habilitada E com heartbeat recente = bloqueio realmente sendo aplicado. */
    fun isEnforcing(context: Context): Boolean {
        if (!AccessibilityGuard.isEnabled(context)) return false
        val last = lastHeartbeatElapsed(context)
        return last > 0L && SystemClock.elapsedRealtime() - last < HEARTBEAT_TIMEOUT_MS
    }

    /** Chamado quando o serviço foi desvinculado/desligado: a UI não pode mentir "Ativo". */
    fun clear(context: Context) {
        prefs(context).edit().remove(KEY_LAST_HEARTBEAT).apply()
    }
}
