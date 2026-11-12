package com.example.focusapp.guardian

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log

/**
 * Alarme "cinto e suspensório": START_STICKY sozinho não garante processo vivo em Doze/OEM agressivo.
 * A cada ~10 min o Android devolve o controle; o receiver re-agenda e garante o foreground service.
 */
internal object GuardianAlarmScheduler {

    const val ACTION_GUARDIAN_ALARM = "com.example.focusapp.guardian.action.ALARM_TICK"
    private const val TAG = "GuardianAlarm"
    private const val INTERVAL_MS = 10 * 60 * 1000L
    private const val REQUEST_CODE = 73

    fun schedule(context: Context) {
        val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val pendingIntent = PendingIntent.getBroadcast(
            context,
            REQUEST_CODE,
            Intent(context, GuardianAlarmReceiver::class.java).setAction(ACTION_GUARDIAN_ALARM),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val triggerAt = System.currentTimeMillis() + INTERVAL_MS
        try {
            val exactAllowed = Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarmManager.canScheduleExactAlarms()
            if (exactAllowed) {
                alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAt, pendingIntent)
                Log.i(TAG, "alarme exato de 10 min agendado")
            } else {
                // Android 12+ sem o acesso especial de alarmes exatos: usa o alarme permitido em Doze.
                alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAt, pendingIntent)
                Log.i(TAG, "alarme inexato de 10 min agendado (canScheduleExactAlarms=false)")
            }
        } catch (error: Exception) {
            Log.w(TAG, "falha ao agendar alarme exato: ${error.javaClass.simpleName}; tentando inexato")
            try {
                alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAt, pendingIntent)
            } catch (fallbackError: Exception) {
                Log.e(TAG, "alarme inexato também falhou: ${fallbackError.javaClass.simpleName}")
            }
        }
    }
}
