package com.example.focusapp.guardian

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log

/** Boot e atualização do próprio APK: o guardião precisa voltar sem depender de ninguém abrir o app. */
class GuardianBootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        Log.i(TAG, "evento ${intent?.action}: iniciando guardião e alarme")
        GuardianAlarmScheduler.schedule(context)
        GuardianService.start(context)
    }

    private companion object {
        const val TAG = "GuardianBoot"
    }
}
