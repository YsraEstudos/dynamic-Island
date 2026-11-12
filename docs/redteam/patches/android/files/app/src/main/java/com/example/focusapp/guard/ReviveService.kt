package com.example.focusapp.guard

import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.IBinder
import android.util.Log
import com.example.focusapp.push.AckClient
import com.example.focusapp.push.AckPayloads
import com.example.focusapp.schedule.DailyBlockScheduler
import com.example.focusapp.service.GuardWatchdogService

/**
 * Serviço de "reviver a proteção", **exportado com permissão de assinatura** para o guardião da Fase 2.
 *
 * Ordem do guardião: (1) launch explícito deste serviço/atividade para limpar o estado *stopped* de um
 * force-stop; (2) escrever os settings de acessibilidade; (3) overlay de fallback. Por isso este
 * serviço é autossuficiente: ao ser iniciado, inclusive de fora, ele:
 *  - sobe o [GuardWatchdogService];
 *  - garante a acessibilidade via [AccessibilityGuard] (sem assumir WRITE_SECURE_SETTINGS — checa);
 *  - tenta reativar o admin de dispositivo (fallback: notificação);
 *  - reagenda o alarme do bloqueio (`force-stop` cancela alarmes);
 *  - manda um ack `reason="revive"` para o PC;
 *  - para (`START_NOT_STICKY`).
 *
 * O serviço é iniciado como foreground (`startForegroundService`) para respeitar o limite de execução
 * em background; o trabalho roda em thread separada e o serviço se encerra no fim.
 */
class ReviveService : Service() {

    companion object {
        const val ACTION_REVIVE = "com.example.focusapp.action.REVIVE"
        private const val TAG = "ReviveService"

        fun intent(context: Context): Intent =
            Intent(context, ReviveService::class.java).setAction(ACTION_REVIVE)
    }

    @Volatile
    private var workThread: Thread? = null

    override fun onCreate() {
        super.onCreate()
        GuardNotifier.createChannels(this)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        GuardNotifier.startForegroundCompat(
            this,
            GuardNotifier.NOTIFICATION_REVIVE,
            GuardNotifier.buildRevive(this)
        )
        if (workThread?.isAlive == true) return START_NOT_STICKY
        val thread = Thread({
            try {
                revive()
            } catch (error: Exception) {
                Log.e(TAG, "Falha no revive", error)
            } finally {
                stopSelf()
            }
        }, "focus-revive").apply { isDaemon = false }
        workThread = thread
        thread.start()
        return START_NOT_STICKY
    }

    private fun revive() {
        GuardWatchdogService.start(this)

        val ensure = AccessibilityGuard.ensureServiceEnabled(this)
        val a11y = AccessibilityGuard.isEnabled(this)
        var admin = AccessibilityGuard.isAdminActive(this)
        if (!admin) {
            // Em Android moderno setActiveAdmin costuma ser recusado; o fallback é notificar o dono.
            admin = AccessibilityGuard.tryReactivateAdmin(this)
        }
        GuardState.recordRevive(this)

        // force-stop/disable-user cancelam os alarmes exatos: reage o fim do bloqueio.
        runCatching { DailyBlockScheduler.refresh(this) }
            .onFailure { Log.w(TAG, "Falha ao reagendar o alarme de bloqueio", it) }

        // Ack de revive: o PC passa a saber que houve recuperação e em que estado o celular ficou.
        AckClient.send(
            this,
            AckPayloads.current(this, id = "revive", reason = "revive", heartbeat = false)
        )

        if (!a11y) GuardNotifier.notifyAccessibilityLost(this, ensure.reason)
        if (!admin) GuardNotifier.notifyAdminMissing(this)
    }

    override fun onDestroy() {
        super.onDestroy()
        // A thread termina sozinha; stopSelf() não interrompe trabalho em andamento.
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
