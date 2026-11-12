package com.example.focusapp.schedule

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock
import android.util.Log
import java.time.LocalDateTime
import java.time.ZoneId

internal object DailyBlockScheduler {
    const val ACTION_REFRESH = "com.example.focusapp.action.REFRESH_DAILY_BLOCK"

    fun canSchedule(context: Context): Boolean = Build.VERSION.SDK_INT < 31 ||
        context.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()

    fun refresh(context: Context) {
        val alarmManager = context.getSystemService(AlarmManager::class.java)
        val now = LocalDateTime.now()
        val store = DailyBlockStore(context)
        val record = store.load(now.toLocalDate())
        val pomodoro = store.loadPomodoro()
        val nowMs = System.currentTimeMillis()
        val nowElapsed = SystemClock.elapsedRealtime()
        // 0/1: início e fim do horário do dia. 2: fim do bloqueio do pomodoro (o início é sempre imediato).
        listOf(0, 1, 2).forEach { boundary ->
            val pending = PendingIntent.getBroadcast(context, boundary,
                Intent(context, DailyBlockAlarmReceiver::class.java).setAction(ACTION_REFRESH),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            alarmManager.cancel(pending)
            val triggerMs: Long? = when (boundary) {
                0, 1 -> {
                    val time = if (boundary == 0) record?.start() else record?.end()
                    if (record != null && !record.released && time != null && time.isAfter(now))
                        time.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli() else null
                }
                // Fim do pomodoro: com âncora monotônica (v1.7) o fim é calculado por ela, para uma
                // mudança de data/hora não despertar no instante errado (C-01).
                else -> pomodoro?.takeIf { !it.released }?.let { active ->
                    val remaining = active.remainingMs(nowMs, nowElapsed)
                    if (remaining > 0L) nowMs + remaining else null
                }
            }
            if (triggerMs != null && canSchedule(context)) {
                try {
                    alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerMs, pending)
                } catch (error: SecurityException) {
                    Log.w("DailyBlock", "Permissão de alarme revogada", error)
                }
            }
        }
        context.sendBroadcast(Intent(ACTION_REFRESH).setPackage(context.packageName))
    }
}

/** Explicit, non-exported receiver. It signals re-evaluation, never grants access. */
class DailyBlockAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        context.sendBroadcast(Intent(DailyBlockScheduler.ACTION_REFRESH).setPackage(context.packageName))
    }
}
