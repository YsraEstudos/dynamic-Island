package com.example.focusapp.push

import android.util.Log
import com.example.focusapp.guard.AccessibilityGuard
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.guard.GuardNotifier
import com.example.focusapp.schedule.DailyBlockScheduler
import com.example.focusapp.schedule.DailyBlockStore
import com.example.focusapp.service.GuardWatchdogService
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage

/**
 * Recebe do PC o bloqueio do pomodoro Angry. Só roda quando chega uma mensagem (o Android acorda o app por
 * alguns segundos): grava o slot do pomodoro e reagenda o alarme do fim. O DailyBlockController
 * (acessibilidade) reage à mudança nas preferências e mostra o overlay.
 *
 * A partir da v1.7 ele **confirma** para o PC o que foi realmente aplicado: garante a acessibilidade
 * (sem assumir WRITE_SECURE_SETTINGS), acorda o watchdog e manda um ack via [AckClient]. Se a
 * acessibilidade não estiver sendo aplicada, dispara notificação de alta prioridade ("BLOQUEIO NÃO
 * APLICADO") — a ilha não deve mostrar "Celular bloqueado" só porque o FCM respondeu 2xx (C-03).
 *
 * O ack também carrega a âncora monotônica gravada por [DailyBlockStore.saveAdult]/savePomodoro: um
 * bloqueio ativo nunca é encerrado por adiantar a data/hora do aparelho (C-01).
 */
class PomodoroPushService : FirebaseMessagingService() {

    companion object {
        private const val TAG = "PomodoroPushService"
    }

    override fun onMessageReceived(message: RemoteMessage) {
        val data = message.data
        val store = DailyBlockStore(this)
        // Bloqueio adulto é independente do pomodoro: vale mesmo que o pomodoro seja duplicado ou inválido.
        data["adultEndMs"]?.toLongOrNull()?.let { adultEndMs -> store.saveAdult(adultEndMs) }
        val id = data["id"]
        val durationMs = data["durationMs"]?.toLongOrNull()
        val endMs = data["endMs"]?.toLongOrNull()
        // Payload inválido ou duplicado é ignorado pela política (acceptPomodoro).
        if (id != null && durationMs != null && endMs != null) {
            if (store.savePomodoro(id, endMs, durationMs)) DailyBlockScheduler.refresh(this)
        }

        // O watchdog religa a acessibilidade se ela caiu e mantém o heartbeat durante o bloqueio (não mexe no ADB).
        GuardWatchdogService.start(this)

        val blockId = id ?: message.messageId ?: "fcm"
        val ackUrl = data["ackUrl"] ?: data["ack_url"]
        val sentAtMs = (data["sentAtMs"] ?: data["sent_at_ms"])?.toLongOrNull()

        // Garantir acessibilidade faz polling de até ~5 s: NUNCA na main thread do FCM.
        val worker = Thread({
            try {
                val ensure = AccessibilityGuard.ensureServiceEnabled(this)
                val enforcing = FocusEnforcementState.isEnforcing(this)
                val reason = when {
                    enforcing -> "fcm"
                    else -> ensure.reason
                }
                val gapMs = sentAtMs?.let { System.currentTimeMillis() - it }
                val payload = AckPayloads.current(
                    this,
                    id = blockId,
                    reason = if (enforcing) reason else ensure.reason,
                    gapMs = gapMs
                )
                AckClient.send(this, payload, ackUrl)
                if (!enforcing) {
                    GuardNotifier.notifyBlockNotApplied(this, ensure.reason)
                }
            } catch (error: Exception) {
                Log.e(TAG, "Falha ao confirmar a aplicação do bloqueio", error)
            }
        }, "focus-fcm-ack").apply { isDaemon = false }
        worker.start()
    }

    override fun onNewToken(token: String) {
        DailyBlockStore(this).pushToken = token
    }
}

