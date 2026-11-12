package com.example.focusapp.guardian

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log

/** Recebe o alarme de 10 min: re-agenda e garante que o Guardião volte a rodar. */
class GuardianAlarmReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        Log.i(TAG, "alarme recebido: re-agendando e garantindo o foreground service")
        GuardianAlarmScheduler.schedule(context)
        GuardianService.start(context)
    }

    private companion object {
        const val TAG = "GuardianAlarm"
    }
}
