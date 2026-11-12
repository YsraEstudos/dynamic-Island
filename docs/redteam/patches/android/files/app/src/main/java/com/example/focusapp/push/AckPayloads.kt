package com.example.focusapp.push

import android.content.Context
import com.example.focusapp.guard.AccessibilityGuard
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.guard.GuardState
import com.example.focusapp.guard.GuardianBridge

/**
 * Monta o [AckPayload] a partir do estado REAL do aparelho (snapshot do guardião + heartbeat do
 * serviço de acessibilidade). É o único lugar que decide `applied` para os acks — assim o PC recebe
 * a mesma verdade venha o evento do FCM, do watchdog ou do [com.example.focusapp.guard.ReviveService].
 */
internal object AckPayloads {

    fun current(
        context: Context,
        id: String,
        reason: String,
        heartbeat: Boolean = false,
        gapMs: Long? = null
    ): AckPayload {
        val snapshot = runCatching { GuardState.snapshot(context) }
            .getOrDefault(com.example.focusapp.guard.ProtectionSnapshot.NONE)
        val a11y = FocusEnforcementState.isEnforcing(context)
        val admin = AccessibilityGuard.isAdminActive(context)
        return AckPayload(
            id = id.ifBlank { "state" },
            applied = a11y && snapshot.blockLevel > 0,
            a11y = a11y,
            admin = admin,
            level = snapshot.blockLevel,
            endMs = snapshot.blockEndMs,
            tsMs = System.currentTimeMillis(),
            gapMs = gapMs,
            heartbeat = heartbeat,
            reason = reason,
            adb = AccessibilityGuard.isAdbEnabled(context),
            users = AccessibilityGuard.userCount(context),
            profiles = AccessibilityGuard.profileCount(context),
            guardian = GuardianBridge.isInstalled(context)
        )
    }
}
