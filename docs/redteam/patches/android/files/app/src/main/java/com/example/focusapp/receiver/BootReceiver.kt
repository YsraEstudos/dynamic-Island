package com.example.focusapp.receiver

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.example.focusapp.guard.AccessibilityGuard
import com.example.focusapp.push.AckClient
import com.example.focusapp.push.AckPayloads
import com.example.focusapp.service.GuardWatchdogService
import com.example.focusapp.service.ScreenTimeTrackerService
import com.example.focusapp.schedule.DailyBlockScheduler

/**
 * Receptor de inicialização e atualização do pacote para garantir
 * persistência 24/7 após reinicialização do dispositivo (Samsung One UI).
 *
 * v1.7 (A-07): no boot e, principalmente, em `MY_PACKAGE_REPLACED` o Android costuma deixar o serviço
 * de acessibilidade desligado. O watchdog é acordado e a acessibilidade é garantida em background
 * (via WRITE_SECURE_SETTINGS, se concedida), com ack para o PC.
 */
class BootReceiver : BroadcastReceiver() {

    companion object {
        private const val TAG = "BootReceiver"
    }

    override fun onReceive(context: Context?, intent: Intent?) {
        if (context == null || intent == null) return

        when (intent.action) {
            Intent.ACTION_BOOT_COMPLETED,
            Intent.ACTION_MY_PACKAGE_REPLACED,
            "android.intent.action.QUICKBOOT_POWERON" -> {
                Log.i(TAG, "Dispositivo reiniciado ou pacote atualizado. Restaurando serviços de foco...")
                val appContext = context.applicationContext
                ScreenTimeTrackerService.start(appContext)
                DailyBlockScheduler.refresh(appContext)
                GuardWatchdogService.start(appContext)

                // O receiver tem ~10 s: garante a acessibilidade e manda ack em background.
                val pending = goAsync()
                val replaced = intent.action == Intent.ACTION_MY_PACKAGE_REPLACED
                Thread({
                    try {
                        AccessibilityGuard.ensureServiceEnabled(appContext, 6_000L)
                        AckClient.send(
                            appContext,
                            AckPayloads.current(
                                appContext,
                                id = if (replaced) "package_replaced" else "boot",
                                reason = if (replaced) "package_replaced" else "boot"
                            )
                        )
                    } catch (error: Exception) {
                        Log.w(TAG, "Falha ao restaurar a acessibilidade no boot/update", error)
                    } finally {
                        pending.finish()
                    }
                }, "focus-boot-revive").start()
            }
        }
    }
}
