package com.example.focusapp.guard

import android.content.Context
import android.os.SystemClock
import com.example.focusapp.schedule.DailyBlockPolicy
import com.example.focusapp.schedule.DailyBlockStore
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Estado persistido do guardião: flags de ADB, âncoras monotônicas do bloqueio do dia, marcações de
 * "já avisei" (throttle de notificação) e o snapshot do que está realmente ativo agora.
 *
 * Fica separado de [com.example.focusapp.schedule.DailyBlockStore] (prefs do bloqueio em si) para não
 * misturar dados que o PC controla com dados operacionais do watchdog. Nada aqui contém segredo.
 */
internal object GuardState {
    private const val PREFS = "focus_guard_prefs"

    private const val KEY_ADB_AUTO_DISABLED = "adb_auto_disabled"
    private const val KEY_ADB_PREVIOUS_USB = "adb_previous_usb"
    private const val KEY_ADB_PREVIOUS_WIFI = "adb_previous_wifi"
    private const val KEY_LAST_FAILURE_NOTIFY_MS = "last_failure_notify_ms"
    private const val KEY_LAST_PROFILE_WARN_MS = "last_profile_warn_ms"
    private const val KEY_LAST_CLOCK_WARN_MS = "last_clock_warn_ms"
    private const val KEY_LAST_ACK_AT_MS = "last_ack_at_ms"
    private const val KEY_LAST_ACK_OK_MS = "last_ack_ok_ms"
    private const val KEY_LAST_GUARDIAN_CHECK_MS = "last_guardian_check_ms"
    private const val KEY_LAST_REVIVE_AT_MS = "last_revive_at_ms"
    private const val KEY_DAILY_ANCHOR_ID = "daily_anchor_id"
    private const val KEY_DAILY_ANCHOR_END_ELAPSED = "daily_anchor_end_elapsed"
    private const val KEY_DAILY_ANCHOR_BOOT = "daily_anchor_boot"

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Snapshot único usado pelo watchdog, pelo ack e pelo provider da Fase 2. */
    fun snapshot(context: Context): ProtectionSnapshot {
        val store = DailyBlockStore(context)
        val nowMs = System.currentTimeMillis()
        val nowElapsed = SystemClock.elapsedRealtime()
        val now = LocalDateTime.now()

        val adultEndMs = store.loadAdultEnd()
        val adultMonoEndMs = store.loadAdultMonoEnd()
        val adultActive = DailyBlockPolicy.isAdultBlockActive(adultEndMs, nowMs, adultMonoEndMs, nowElapsed)

        val pomodoro = store.loadPomodoro()
        val pomodoroActive = DailyBlockPolicy.shouldBlockAt(pomodoro, nowMs, nowElapsed)

        val dailyRecord = store.load(now.toLocalDate())
        val dailyWallActive = DailyBlockPolicy.shouldBlock(dailyRecord, now)
        val anchor = loadDailyAnchor(context)
        // Salto de relógio que passou da meia-noite: o record ancorado pode ser o de ontem.
        val anchoredRecord = when {
            anchor == null -> null
            dailyRecord?.id == anchor.first -> dailyRecord
            else -> store.load(now.toLocalDate().minusDays(1))?.takeIf { it.id == anchor.first }
        }
        val dailyAnchoredActive = DailyBlockPolicy.shouldBlockAnchored(
            anchoredRecord,
            anchor?.first,
            anchor?.second ?: 0L,
            nowElapsed
        )
        val dailyActive = dailyWallActive || dailyAnchoredActive

        val selective = store.loadSelective()
        val level = GuardPolicy.protectionLevel(selective, adultActive, pomodoroActive, dailyActive)
        val expected = GuardPolicy.isProtectionExpected(selective, adultActive, pomodoroActive, dailyActive)

        val dailyEndMs = when {
            dailyAnchoredActive -> nowMs + ((anchor?.second ?: 0L) - nowElapsed).coerceAtLeast(0L)
            dailyWallActive && dailyRecord != null ->
                dailyRecord.end().atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
            else -> null
        }
        val blockEndMs = GuardPolicy.latestEndMs(
            if (adultActive) adultEndMs else null,
            if (pomodoroActive) pomodoro?.endMs else null,
            dailyEndMs
        )
        return ProtectionSnapshot(
            protectionExpected = expected,
            blockActive = level > 0,
            blockLevel = level,
            blockEndMs = blockEndMs,
            selective = selective,
            adultActive = adultActive,
            pomodoroActive = pomodoroActive,
            dailyActive = dailyActive
        )
    }

    fun isBlockActive(context: Context): Boolean = snapshot(context).blockActive

    fun protectionExpected(context: Context): Boolean = snapshot(context).protectionExpected

    // --- ADB: só desliga durante bloqueio e restaura exatamente como estava ---------------------------------

    fun adbAutoDisabled(context: Context): Boolean =
        prefs(context).getBoolean(KEY_ADB_AUTO_DISABLED, false)

    fun adbPreviousEnabled(context: Context): Int =
        prefs(context).getInt(KEY_ADB_PREVIOUS_USB, -1)

    fun adbPreviousWifi(context: Context): Int =
        prefs(context).getInt(KEY_ADB_PREVIOUS_WIFI, -1)

    fun recordAdbDisabledByGuard(context: Context, previousUsb: Int, previousWifi: Int) {
        prefs(context).edit()
            .putBoolean(KEY_ADB_AUTO_DISABLED, true)
            .putInt(KEY_ADB_PREVIOUS_USB, previousUsb)
            .putInt(KEY_ADB_PREVIOUS_WIFI, previousWifi)
            .commit()
    }

    fun clearAdbAutoDisabled(context: Context) {
        prefs(context).edit()
            .putBoolean(KEY_ADB_AUTO_DISABLED, false)
            .remove(KEY_ADB_PREVIOUS_USB)
            .remove(KEY_ADB_PREVIOUS_WIFI)
            .commit()
    }

    // --- Throttle de alertas e ack -------------------------------------------------------------------------

    fun lastFailureNotifyMs(context: Context): Long =
        prefs(context).getLong(KEY_LAST_FAILURE_NOTIFY_MS, 0L)

    fun markFailureNotified(context: Context, nowMs: Long = System.currentTimeMillis()) {
        prefs(context).edit().putLong(KEY_LAST_FAILURE_NOTIFY_MS, nowMs).apply()
    }

    fun lastProfileWarnMs(context: Context): Long =
        prefs(context).getLong(KEY_LAST_PROFILE_WARN_MS, 0L)

    fun markProfileWarned(context: Context, nowMs: Long = System.currentTimeMillis()) {
        prefs(context).edit().putLong(KEY_LAST_PROFILE_WARN_MS, nowMs).apply()
    }

    fun lastClockWarnMs(context: Context): Long =
        prefs(context).getLong(KEY_LAST_CLOCK_WARN_MS, 0L)

    fun markClockWarned(context: Context, nowMs: Long = System.currentTimeMillis()) {
        prefs(context).edit().putLong(KEY_LAST_CLOCK_WARN_MS, nowMs).apply()
    }

    fun lastAckAtMs(context: Context): Long = prefs(context).getLong(KEY_LAST_ACK_AT_MS, 0L)

    fun lastAckOkMs(context: Context): Long = prefs(context).getLong(KEY_LAST_ACK_OK_MS, 0L)

    fun recordAck(context: Context, ok: Boolean, nowMs: Long = System.currentTimeMillis()) {
        val editor = prefs(context).edit().putLong(KEY_LAST_ACK_AT_MS, nowMs)
        if (ok) editor.putLong(KEY_LAST_ACK_OK_MS, nowMs)
        editor.apply()
    }

    fun lastGuardianCheckMs(context: Context): Long =
        prefs(context).getLong(KEY_LAST_GUARDIAN_CHECK_MS, 0L)

    fun markGuardianChecked(context: Context, nowMs: Long = System.currentTimeMillis()) {
        prefs(context).edit().putLong(KEY_LAST_GUARDIAN_CHECK_MS, nowMs).apply()
    }

    fun lastReviveAtMs(context: Context): Long = prefs(context).getLong(KEY_LAST_REVIVE_AT_MS, 0L)

    fun recordRevive(context: Context) {
        prefs(context).edit().putLong(KEY_LAST_REVIVE_AT_MS, System.currentTimeMillis()).apply()
    }

    // --- Âncora monotônica do bloqueio por horário (C-01) --------------------------------------------------

    /**
     * Âncora do bloqueio diário ativo: (id do record, fim em elapsedRealtime). Serve para um bloqueio
     * que já estava valendo não terminar antes da hora se o relógio de parede for adiantado. O boot é
     * gravado porque `elapsedRealtime` zera no reboot.
     */
    fun loadDailyAnchor(context: Context): Pair<String, Long>? {
        val stored = prefs(context)
        val id = stored.getString(KEY_DAILY_ANCHOR_ID, null) ?: return null
        val endElapsed = stored.getLong(KEY_DAILY_ANCHOR_END_ELAPSED, 0L)
        if (endElapsed <= 0L) return null
        val anchorBoot = stored.getInt(KEY_DAILY_ANCHOR_BOOT, -1)
        val currentBoot = AccessibilityGuard.bootCount(context)
        if (anchorBoot >= 0 && currentBoot >= 0 && anchorBoot != currentBoot) return null
        return id to endElapsed
    }

    fun saveDailyAnchor(context: Context, recordId: String, endElapsedRealtime: Long) {
        if (recordId.isBlank() || endElapsedRealtime <= 0L) return
        prefs(context).edit()
            .putString(KEY_DAILY_ANCHOR_ID, recordId)
            .putLong(KEY_DAILY_ANCHOR_END_ELAPSED, endElapsedRealtime)
            .putInt(KEY_DAILY_ANCHOR_BOOT, AccessibilityGuard.bootCount(context))
            .commit()
    }

    fun clearDailyAnchor(context: Context) {
        prefs(context).edit()
            .remove(KEY_DAILY_ANCHOR_ID)
            .remove(KEY_DAILY_ANCHOR_END_ELAPSED)
            .remove(KEY_DAILY_ANCHOR_BOOT)
            .apply()
    }
}
