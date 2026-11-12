package com.example.focusapp.guardian

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import android.util.Log
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.ThreadFactory
import java.util.concurrent.TimeUnit

/**
 * Guardião (Fase 2 do plano de blindagem): foreground service do SEGUNDO pacote.
 *
 * Por que existe: `adb shell am force-stop com.example.focusapp` mata o principal e o Android apaga o
 * setting de acessibilidade; reescrever o setting com o pacote "stopped" NÃO religa (teste T4, 87 s sem
 * bind). A recuperação validada exige um componente explícito de outro pacote: (1) start no
 * ReviveService do principal (limpa o "stopped"), (2) reescrever enabled_accessibility_services, e só
 * então (3) re-checar. Este serviço faz isso a cada 10 s, em SCREEN_ON/USER_PRESENT, no boot e por um
 * alarme exato de 10 min; o app principal reergue este serviço quando ele cai (mútuo).
 *
 * O guardião também publica ack/heartbeat no ntfy para o PC não mentir "celular bloqueado".
 */
class GuardianService : Service() {

    private var scheduler: ScheduledExecutorService? = null
    private var ackPublisher: AckPublisher? = null
    private var alertOverlay: GuardianAlertOverlay? = null
    private var screenReceiver: BroadcastReceiver? = null

    @Volatile
    private var stopped = false

    // Estados de throttle/telemetria (todos lidos/escritos só na thread "guardian-check", exceto stopped).
    private var lastCheckStartedElapsed = 0L
    private var lastReviveElapsed = 0L
    private var lastHeartbeatElapsed = 0L
    private var lastReviveAckElapsed = 0L
    private var lastPermissionAlertElapsed = 0L
    private var lastProtectionPermissionAlertElapsed = 0L
    private var lastMainUnavailableAlertElapsed = 0L
    private var lastStatusText: String? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        running = true
        createNotificationChannels()
        startForegroundSafely()
        ackPublisher = AckPublisher()
        alertOverlay = GuardianAlertOverlay(this)
        val executor = Executors.newSingleThreadScheduledExecutor(
            ThreadFactory { runnable -> Thread(runnable, "guardian-check").apply { isDaemon = true } },
        )
        scheduler = executor
        registerScreenReceiver()
        GuardianAlarmScheduler.schedule(this)
        // Confere imediatamente (o app principal pode ter sido force-stopped enquanto subíamos).
        submitCheck()
        executor.scheduleWithFixedDelay(
            { safeCheckOnce() },
            CHECK_INTERVAL_MS,
            CHECK_INTERVAL_MS,
            TimeUnit.MILLISECONDS,
        )
        Log.i(TAG, "guardião criado: FGS + canal + alarme + loop de $CHECK_INTERVAL_MS ms")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Log.i(TAG, "start recebido (action=${intent?.action}); garantindo FGS e conferindo já")
        startForegroundSafely()
        GuardianAlarmScheduler.schedule(this)
        submitCheck()
        return START_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // A tela de status foi descartada: mantém o alarme vivo de qualquer forma.
        GuardianAlarmScheduler.schedule(this)
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        stopped = true
        running = false
        unregisterScreenReceiver()
        alertOverlay?.hide()
        alertOverlay = null
        ackPublisher?.shutdown()
        ackPublisher = null
        scheduler?.shutdownNow()
        scheduler = null
        // Não cancelamos o alarme de propósito: se o serviço morrer, ele deve ser reerguido.
        Log.w(TAG, "guardião destruído (START_STICKY + alarme devem reerguer)")
        super.onDestroy()
    }

    // ---------------------------------------------------------------------------------------------
    // Loop de verificação e recuperação
    // ---------------------------------------------------------------------------------------------

    private fun submitCheck() {
        val executor = scheduler ?: return
        try {
            executor.execute { safeCheckOnce() }
        } catch (_: RejectedExecutionException) {
            // Executor encerrado (onDestroy): ignora.
        }
    }

    private fun safeCheckOnce() {
        try {
            checkOnce()
        } catch (throwable: Throwable) {
            Log.e(TAG, "checagem falhou: ${throwable.javaClass.simpleName}: ${throwable.message}", throwable)
        }
    }

    private fun checkOnce() {
        if (stopped) return
        val now = SystemClock.elapsedRealtime()
        if (now - lastCheckStartedElapsed < MIN_CHECK_GAP_MS) return
        lastCheckStartedElapsed = now

        if (!GuardianPermissions.hasProtectionControl(this)) {
            // Sem a permission signature (guardião instalado antes do principal) é impossível consultar o
            // provider ou chamar o ReviveService: avisa em vez de tentar um revive que sempre falharia.
            reportMissingProtectionPermission()
            return
        }

        val state = ProtectionContract.query(this)
        val a11yListed = AccessibilityRepair.isMainServiceListed(this)

        if (state == null) {
            // Provider inaccessible = pacote do principal force-stopped (ou ainda subindo após o revive).
            Log.w(TAG, "provider do app principal indisponível; disparando ciclo de revive")
            updateStatusNotification("Guardião ativo — app principal parado, reparando")
            runReviveCycle()
            return
        }

        val healthy = state.protectionActive && a11yListed && state.a11yEnabled
        if (healthy) {
            hideFailureUi()
            updateStatusNotification("Guardião ativo — proteção em dia")
            publishHeartbeatIfDue(state)
            return
        }

        if (!state.protectionActive) {
            // Bloqueio não está ativo: o principal pode estar fechado de propósito; não forçamos nada.
            hideFailureUi()
            updateStatusNotification("Guardião ativo — sem bloqueio ativo")
            return
        }

        // protectionActive=true e acessibilidade ausente dos dois pontos de vista.
        Log.w(TAG, "proteção ativa, mas acessibilidade ausente (listado=$a11yListed reportado=${state.a11yEnabled})")
        updateStatusNotification("Guardião ativo — reparando a proteção")
        runReviveCycle()
    }

    /**
     * Ordem validada pelo red-team (F3/T4):
     *  (a) start explícito no ReviveService do principal -> limpa o estado "stopped" do force-stop;
     *  (b) ~2 s de espera;
     *  (c) reescrever enabled_accessibility_services (preservando a lista) + accessibility_enabled=1;
     *  (d) ~3 s e re-checar pelo AccessibilityManager + provider;
     *  (e) se ainda ausente: overlay de aviso + MainActivity do principal;
     *  (f) ack no ntfy (applied=false, reason=guardian-revive) e, quando volta, heartbeat applied=true.
     */
    private fun runReviveCycle() {
        if (stopped) return
        val availability = MainAppControl.availability(this)
        if (availability != MainAppControl.PackageAvailability.INSTALLED) {
            notifyMainUnavailable(availability)
            return
        }

        val now = SystemClock.elapsedRealtime()
        if (now - lastReviveElapsed < REVIVE_COOLDOWN_MS) return
        lastReviveElapsed = now
        Log.i(TAG, "revive: iniciando ciclo (ReviveService -> settings -> recheck -> activity)")

        val reviveStarted = MainAppControl.startReviveService(this)
        sleepQuietly(CLEAR_STOPPED_WAIT_MS)

        val settingsWritten = AccessibilityRepair.ensureMainServiceEnabled(this)
        if (!settingsWritten) reportMissingWritePermission()
        sleepQuietly(BIND_WAIT_MS)

        var state = ProtectionContract.query(this)
        var a11yListed = AccessibilityRepair.isMainServiceListed(this)
        var healthy = state != null && state.a11yEnabled && a11yListed

        if (!healthy) {
            // Teste real no Moto G20: depois de force-stop, só reescrever o setting NÃO rebinda; o
            // toggle (desliga ~1,8 s e religa) limpa o estado "crashed" do AMS e o serviço volta.
            val toggled = AccessibilityRepair.forceToggleMainService(this)
            sleepQuietly(TOGGLE_WAIT_MS)
            state = ProtectionContract.query(this)
            a11yListed = AccessibilityRepair.isMainServiceListed(this)
            healthy = state != null && state.a11yEnabled && a11yListed
            if (healthy) Log.i(TAG, "revive: recuperado via toggle (toggled=$toggled)")
            else Log.w(TAG, "revive: toggle não rebindou (toggled=$toggled listado=$a11yListed)")
        }

        if (!healthy) {
            Log.w(
                TAG,
                "revive: ainda ausente (reviveStarted=$reviveStarted settingsWritten=$settingsWritten " +
                    "listado=$a11yListed provider=${state != null}); overlay + MainActivity",
            )
            // Overlay ANTES da atividade: no Android 14 a isenção de BAL por SYSTEM_ALERT_WINDOW
            // exige uma janela visível do app; no Android 11 a ordem é irrelevante.
            alertOverlay?.show()
            MainAppControl.launchMainActivity(this)
            sleepQuietly(AFTER_LAUNCH_WAIT_MS)
            state = ProtectionContract.query(this)
            a11yListed = AccessibilityRepair.isMainServiceListed(this)
            if (!a11yListed) {
                // Última tentativa: a atividade explícita pode ter limpado o "stopped" só agora.
                AccessibilityRepair.ensureMainServiceEnabled(this)
                sleepQuietly(AFTER_LAST_SETTINGS_WAIT_MS)
                state = ProtectionContract.query(this)
                a11yListed = AccessibilityRepair.isMainServiceListed(this)
            }
            healthy = state != null && state.a11yEnabled && a11yListed
        }

        when {
            healthy -> {
                hideFailureUi()
                Log.i(TAG, "revive: recuperado; acessibilidade do principal de volta")
                updateStatusNotification("Guardião ativo — proteção em dia")
                state?.let { publishReviveResult(it, applied = true, a11yListed = true) }
            }

            state != null && !state.protectionActive -> {
                // O app principal voltou, mas o bloqueio terminou durante a recuperação: nada a forçar.
                hideFailureUi()
                Log.i(TAG, "revive: principal voltou, porém sem bloqueio ativo")
                updateStatusNotification("Guardião ativo — sem bloqueio ativo")
            }

            else -> {
                Log.e(TAG, "revive: FALHOU; bloqueio continua desligado (overlay/notificação exibidos)")
                showAlertNotification("O bloqueio caiu e o guardião não conseguiu religar a acessibilidade.")
                alertOverlay?.show()
                updateStatusNotification("Guardião ativo — bloqueio NÃO aplicado")
                state?.let { publishReviveResult(it, applied = false, a11yListed = a11yListed) }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Acks para o PC (ntfy) — heartbeat 60 s e resultado do revive
    // ---------------------------------------------------------------------------------------------

    private fun publishHeartbeatIfDue(state: ProtectionContract.MainState) {
        val now = SystemClock.elapsedRealtime()
        if (now - lastHeartbeatElapsed < HEARTBEAT_INTERVAL_MS) return
        lastHeartbeatElapsed = now
        ackPublisher?.publish(
            state.fcmToken,
            AckPayload.heartbeat(a11y = true, admin = state.adminActive, blockEndMs = state.blockEndMs),
        )
    }

    private fun publishReviveResult(state: ProtectionContract.MainState, applied: Boolean, a11yListed: Boolean) {
        val now = SystemClock.elapsedRealtime()
        val minGap = if (applied) RECOVERY_ACK_MIN_GAP_MS else FAILURE_ACK_MIN_GAP_MS
        if (now - lastReviveAckElapsed < minGap) return
        lastReviveAckElapsed = now
        ackPublisher?.publish(
            state.fcmToken,
            AckPayload.revive(
                applied = applied,
                a11y = a11yListed,
                admin = state.adminActive,
                blockEndMs = state.blockEndMs,
            ),
        )
    }

    // ---------------------------------------------------------------------------------------------
    // Notificações e overlay
    // ---------------------------------------------------------------------------------------------

    private fun createNotificationChannels() {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_STATUS, "Status do guardião", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Serviço que mantém a proteção do app Foco de pé"
                setShowBadge(false)
            },
        )
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ALERTS, "Alertas de proteção", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Avisos de que o bloqueio caiu e precisa ser reativado"
                enableVibration(true)
            },
        )
    }

    private fun startForegroundSafely() {
        try {
            startForeground(NOTIFICATION_ID_STATUS, buildStatusNotification("Guardião ativo"))
        } catch (error: Exception) {
            Log.e(TAG, "startForeground falhou: ${error.javaClass.simpleName}: ${error.message}")
        }
    }

    private fun updateStatusNotification(text: String) {
        if (text == lastStatusText) return
        lastStatusText = text
        try {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            manager.notify(NOTIFICATION_ID_STATUS, buildStatusNotification(text))
        } catch (error: Exception) {
            Log.w(TAG, "falha ao atualizar a notificação de status: ${error.javaClass.simpleName}")
        }
    }

    private fun buildStatusNotification(text: String): Notification =
        Notification.Builder(this, CHANNEL_STATUS)
            .setSmallIcon(R.drawable.ic_guardian)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(text)
            .setContentIntent(statusActivityPendingIntent())
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setCategory(Notification.CATEGORY_SERVICE)
            .setShowWhen(false)
            .build()

    private fun showAlertNotification(text: String) {
        try {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            val notification = Notification.Builder(this, CHANNEL_ALERTS)
                .setSmallIcon(R.drawable.ic_guardian)
                .setContentTitle("Proteção desligada")
                .setContentText(text)
                .setContentIntent(statusActivityPendingIntent())
                .setAutoCancel(true)
                .setCategory(Notification.CATEGORY_ERROR)
                .build()
            manager.notify(NOTIFICATION_ID_ALERT, notification)
        } catch (error: Exception) {
            Log.w(TAG, "falha ao publicar alerta: ${error.javaClass.simpleName}")
        }
    }

    private fun statusActivityPendingIntent(): PendingIntent = PendingIntent.getActivity(
        this,
        REQUEST_STATUS_ACTIVITY,
        Intent(this, GuardianStatusActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )

    private fun hideFailureUi() {
        alertOverlay?.hide()
        try {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            manager.cancel(NOTIFICATION_ID_ALERT)
        } catch (error: Exception) {
            Log.w(TAG, "falha ao limpar alerta: ${error.javaClass.simpleName}")
        }
    }

    private fun reportMissingWritePermission() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastPermissionAlertElapsed < PERMISSION_ALERT_GAP_MS) return
        lastPermissionAlertElapsed = now
        Log.e(TAG, "WRITE_SECURE_SETTINGS ausente: o guardião não consegue religar a acessibilidade")
        showAlertNotification("Guardião sem WRITE_SECURE_SETTINGS. Conceda por ADB (ver README).")
    }

    private fun reportMissingProtectionPermission() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastProtectionPermissionAlertElapsed < PERMISSION_ALERT_GAP_MS) return
        lastProtectionPermissionAlertElapsed = now
        Log.e(TAG, "permission signature PROTECTION_CONTROL ausente: reinstale o guardião DEPOIS do app principal")
        updateStatusNotification("Guardião ativo — sem acesso ao app principal")
        showAlertNotification(
            "Reinstale o guardião depois do app principal: falta a permissão de assinatura PROTECTION_CONTROL.",
        )
    }

    private fun notifyMainUnavailable(availability: MainAppControl.PackageAvailability) {
        val now = SystemClock.elapsedRealtime()
        if (now - lastMainUnavailableAlertElapsed < MAIN_UNAVAILABLE_ALERT_GAP_MS) return
        lastMainUnavailableAlertElapsed = now
        val detail = when (availability) {
            MainAppControl.PackageAvailability.DISABLED ->
                "O app Foco está desativado (pm disable-user). Reative em Ajustes para o bloqueio voltar."
            else ->
                "O app Foco não está instalado. Instale o APK principal para o bloqueio valer."
        }
        Log.e(TAG, detail)
        updateStatusNotification("Guardião ativo — app principal indisponível")
        showAlertNotification(detail)
    }

    // ---------------------------------------------------------------------------------------------
    // Gatilhos de tela (SCREEN_ON/USER_PRESENT) e utilidades
    // ---------------------------------------------------------------------------------------------

    private fun registerScreenReceiver() {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context?, intent: Intent?) {
                Log.i(TAG, "gatilho ${intent?.action}: conferindo já")
                submitCheck()
            }
        }
        screenReceiver = receiver
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_SCREEN_ON)
            addAction(Intent.ACTION_USER_PRESENT)
        }
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
            } else {
                @Suppress("UnspecifiedRegisterReceiverFlag")
                registerReceiver(receiver, filter)
            }
        } catch (error: Exception) {
            Log.w(TAG, "não foi possível registrar receivers de tela: ${error.javaClass.simpleName}")
            screenReceiver = null
        }
    }

    private fun unregisterScreenReceiver() {
        val receiver = screenReceiver ?: return
        screenReceiver = null
        try {
            unregisterReceiver(receiver)
        } catch (error: Exception) {
            Log.w(TAG, "falha ao desregistrar receiver de tela: ${error.javaClass.simpleName}")
        }
    }

    private fun sleepQuietly(milliseconds: Long) {
        try {
            Thread.sleep(milliseconds)
        } catch (interrupted: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }

    companion object {
        private const val TAG = "GuardianService"

        private const val CHANNEL_STATUS = "guardian_status"
        private const val CHANNEL_ALERTS = "guardian_alerts"
        private const val NOTIFICATION_ID_STATUS = 41
        private const val NOTIFICATION_ID_ALERT = 42
        private const val REQUEST_STATUS_ACTIVITY = 51

        private const val CHECK_INTERVAL_MS = 10_000L
        private const val MIN_CHECK_GAP_MS = 1_500L
        private const val REVIVE_COOLDOWN_MS = 20_000L
        private const val HEARTBEAT_INTERVAL_MS = 60_000L
        private const val RECOVERY_ACK_MIN_GAP_MS = 5_000L
        private const val FAILURE_ACK_MIN_GAP_MS = 60_000L
        private const val PERMISSION_ALERT_GAP_MS = 10 * 60_000L
        private const val MAIN_UNAVAILABLE_ALERT_GAP_MS = 10 * 60_000L

        // Esperas do ciclo de revive (mesma ordem do teste T4: launch explícito -> settings -> recheck).
        private const val CLEAR_STOPPED_WAIT_MS = 2_000L
        private const val BIND_WAIT_MS = 3_000L
        private const val TOGGLE_WAIT_MS = 3_000L
        private const val AFTER_LAUNCH_WAIT_MS = 3_000L
        private const val AFTER_LAST_SETTINGS_WAIT_MS = 1_500L

        /** Visível para a tela de status (diagnóstico). */
        @Volatile
        internal var running: Boolean = false

        /**
         * Ponto único de entrada do serviço. Chamado pelo app principal (startForegroundService com
         * action START), boot, alarme, admin e tela de status. Exceções são engolidas de propósito:
         * o próximo gatilho/alarme tenta de novo.
         */
        fun start(context: Context) {
            try {
                val intent = Intent(context, GuardianService::class.java)
                    .setAction(ProtectionContract.ACTION_GUARDIAN_START)
                context.startForegroundService(intent)
            } catch (error: Exception) {
                Log.e(TAG, "falha ao iniciar o guardião: ${error.javaClass.simpleName}: ${error.message}")
            }
        }
    }
}
