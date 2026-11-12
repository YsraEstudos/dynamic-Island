package com.example.focusapp.service

import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Handler
import android.os.HandlerThread
import android.os.SystemClock
import android.util.Log
import androidx.core.content.ContextCompat
import com.example.focusapp.guard.AccessibilityGuard
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.guard.GuardNotifier
import com.example.focusapp.guard.GuardPolicy
import com.example.focusapp.guard.GuardState
import com.example.focusapp.guard.GuardianBridge
import com.example.focusapp.push.AckClient
import com.example.focusapp.push.AckPayloads
import com.example.focusapp.schedule.DailyBlockPolicy
import com.example.focusapp.security.PasswordManager

/**
 * Watchdog da proteção (Fase 1).
 *
 * É um foreground service `specialUse` com loop de 10 s (bloqueio ativo) / 60 s (ocioso) que mantém a
 * acessibilidade viva, reativa o admin quando possível, detecta perfis sem proteção, tenta acordar o
 * guardião da Fase 2 e manda
 * heartbeat/acks para o PC.
 *
 * O que ele resolve de verdade (comprovado no red-team):
 *  - `settings put secure enabled_accessibility_services ''` com o app VIVO: religa em ≤ ~5 s;
 *  - acessibilidade desligada pelo toggle/ajustes: religa (ou notifica com motivo);
 *  - ADB ligado durante bloqueio: desliga e avisa;
 *  - card do app deixa de mentir "Ativo": heartbeat real em [FocusEnforcementState].
 *
 * O que ele NÃO resolve (documentado): `am force-stop` — o Android marca o pacote como *stopped*,
 * apaga o setting e recusa o bind. Só o guardião da Fase 2 (outro pacote, launch explícito do
 * [com.example.focusapp.guard.ReviveService] + write) recupera disso.
 */
class GuardWatchdogService : Service() {

    companion object {
        private const val TAG = "GuardWatchdogService"
        private const val ACTION_START = "com.example.focusapp.action.START_GUARD_WATCHDOG"

        fun start(context: Context) {
            val intent = Intent(context, GuardWatchdogService::class.java).setAction(ACTION_START)
            try {
                ContextCompat.startForegroundService(context, intent)
            } catch (error: Exception) {
                Log.w(TAG, "Não foi possível iniciar o watchdog", error)
            }
        }
    }

    private lateinit var handlerThread: HandlerThread
    private var handler: Handler? = null

    @Volatile
    private var stopped = false

    private var lastStatus: String? = null
    private var lastA11yEnforcing: Boolean? = null
    private var lastAdminActive: Boolean? = null
    private var repairFailures = 0
    private var lastRepairAttemptElapsedMs = 0L

    private val tick = object : Runnable {
        override fun run() {
            if (stopped) return
            try {
                checkOnce()
            } catch (error: Exception) {
                Log.e(TAG, "Falha no ciclo do watchdog", error)
            }
            scheduleNextTick()
        }
    }

    private val screenReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            when (intent?.action) {
                Intent.ACTION_SCREEN_ON,
                Intent.ACTION_USER_PRESENT,
                Intent.ACTION_MY_PACKAGE_REPLACED -> requestCheckNow()
            }
        }
    }

    override fun onCreate() {
        super.onCreate()
        GuardNotifier.createChannels(this)
        GuardNotifier.startForegroundCompat(
            this,
            GuardNotifier.NOTIFICATION_WATCHDOG,
            GuardNotifier.buildOngoing(this, "Watchdog da proteção", "Iniciando verificação…")
        )
        handlerThread = HandlerThread("focus-guard-watchdog").apply { start() }
        handler = Handler(handlerThread.looper)
        registerScreenReceiver()
        handler?.post(tick)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // START_STICKY: se o sistema matar por memória, ele reergue o serviço; onCreate recria o loop.
        requestCheckNow()
        return START_STICKY
    }

    private fun registerScreenReceiver() {
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_SCREEN_ON)
            addAction(Intent.ACTION_USER_PRESENT)
            addAction(Intent.ACTION_MY_PACKAGE_REPLACED)
        }
        runCatching {
            ContextCompat.registerReceiver(
                this,
                screenReceiver,
                filter,
                ContextCompat.RECEIVER_NOT_EXPORTED
            )
        }.onFailure { Log.w(TAG, "Não foi possível registrar o receiver de tela", it) }
    }

    private fun requestCheckNow() {
        val current = handler ?: return
        current.removeCallbacks(tick)
        current.post(tick)
    }

    private fun scheduleNextTick() {
        if (stopped) return
        val blockActive = runCatching { GuardState.isBlockActive(this) }.getOrDefault(false)
        handler?.postDelayed(tick, GuardPolicy.tickIntervalMs(blockActive))
    }

    /** Um ciclo completo: roda na HandlerThread (nunca na main); bloqueia até ~10 s em reparo. */
    private fun checkOnce() {
        val nowMs = System.currentTimeMillis()
        val nowElapsed = SystemClock.elapsedRealtime()
        val snapshot = GuardState.snapshot(this)
        val grace = PasswordManager.getInstance(this).isGracePeriodActive()
        val a11yEnabled = AccessibilityGuard.isEnabled(this)
        var enforcing = FocusEnforcementState.isEnforcing(this)
        val hasSecureSettings = AccessibilityGuard.hasWriteSecureSettings(this)

        // 1. Acessibilidade: religa quando caiu. Durante a janela de tolerância por senha, respeita.
        if (!grace) {
            if (!enforcing && repairAllowed(nowElapsed)) {
                lastRepairAttemptElapsedMs = nowElapsed
                val result = if (a11yEnabled && AccessibilityGuard.isComponentListed(this)) {
                    // Habilitado porém morto (crash): toggle do componente costuma forçar o rebind.
                    AccessibilityGuard.forceRebind(this)
                } else {
                    AccessibilityGuard.ensureServiceEnabled(this)
                }
                enforcing = FocusEnforcementState.isEnforcing(this)
                if (result.ok || enforcing) {
                    repairFailures = 0
                    GuardNotifier.cancelAccessibilityAlert(this)
                } else {
                    repairFailures++
                    handleAccessibilityFailure(result.reason, nowMs)
                }
            }
        } else {
            repairFailures = 0
        }

        val a11yStateChanged = lastA11yEnforcing != null && lastA11yEnforcing != enforcing
        if (a11yStateChanged) {
            val reason = if (enforcing) "a11y_restored" else "a11y_off"
            AckClient.send(this, AckPayloads.current(this, id = reason, reason = reason))
        }
        lastA11yEnforcing = enforcing

        // 2. Admin de dispositivo: tenta reativar; sem sucesso, o dono precisa abrir o app.
        var admin = AccessibilityGuard.isAdminActive(this)
        if (!grace && !admin) {
            admin = AccessibilityGuard.tryReactivateAdmin(this)
            if (!admin && GuardPolicy.shouldNotifyFailure(GuardState.lastFailureNotifyMs(this), nowMs)) {
                GuardState.markFailureNotified(this, nowMs)
                GuardNotifier.notifyAdminMissing(this)
                AckClient.send(this, AckPayloads.current(this, id = "admin_off", reason = "admin_off"))
            }
        }
        if (lastAdminActive != null && lastAdminActive != admin) {
            AckClient.send(
                this,
                AckPayloads.current(this, id = if (admin) "admin_on" else "admin_off",
                    reason = if (admin) "admin_on" else "admin_off")
            )
        }
        lastAdminActive = admin

        // 3. Depuração USB/sem fio: NÃO é mais alterada automaticamente. Decisão do dono (2026-11-11):
        // ele usa ADB + IA no PC e o anti-ADB derrubava a sessão. O estado do ADB continua sendo
        // reportado no ack (campo "adb"), mas o app nunca escreve em ADB_ENABLED/ADB_WIFI_ENABLED.

        // 4. Perfis/usuários: acessibilidade é por usuário — o bloqueio não vale em outro perfil.
        val users = AccessibilityGuard.userCount(this)
        val profiles = AccessibilityGuard.profileCount(this)
        if (snapshot.blockActive && GuardPolicy.isProfileUnprotected(users, profiles) &&
            GuardPolicy.shouldWarnProfiles(GuardState.lastProfileWarnMs(this), nowMs)
        ) {
            GuardState.markProfileWarned(this, nowMs)
            GuardNotifier.notifyProfilesUnprotected(this, users, profiles)
            AckClient.send(this, AckPayloads.current(this, id = "profiles", reason = "profiles"))
        }

        // 5. Guardião da Fase 2: tenta acordar e reporta ausência no ack (sem spam de notificação).
        if (GuardPolicy.shouldCheckGuardian(GuardState.lastGuardianCheckMs(this), nowMs)) {
            GuardState.markGuardianChecked(this, nowMs)
            GuardianBridge.start(this)
        }

        // 6. Heartbeat para o PC enquanto a proteção é esperada (o PC só deve mostrar "bloqueado" com ack fresco).
        if (snapshot.protectionExpected &&
            GuardPolicy.shouldReportHeartbeat(GuardState.lastAckAtMs(this), nowMs)
        ) {
            AckClient.sendHeartbeat(this)
        }

        updateOngoingNotification(snapshot, enforcing, grace, a11yEnabled)
    }

    /** Backoff simples para não tentar reparar a cada 10 s em caso de falha persistente. */
    private fun repairAllowed(nowElapsed: Long): Boolean {
        if (repairFailures == 0) return true
        val backoff = GuardPolicy.repairBackoffMs(repairFailures)
        return nowElapsed - lastRepairAttemptElapsedMs >= backoff
    }

    private fun handleAccessibilityFailure(reason: String, nowMs: Long) {
        if (!GuardPolicy.shouldNotifyFailure(GuardState.lastFailureNotifyMs(this), nowMs)) return
        GuardState.markFailureNotified(this, nowMs)
        if (reason == AccessibilityGuard.REASON_NO_PERMISSION) {
            GuardNotifier.notifySecureSettingsMissing(this)
        } else {
            GuardNotifier.notifyAccessibilityLost(this, reason)
        }
        AckClient.send(this, AckPayloads.current(this, id = "a11y_off", reason = reason))
    }

    private fun updateOngoingNotification(
        snapshot: com.example.focusapp.guard.ProtectionSnapshot,
        enforcing: Boolean,
        grace: Boolean,
        a11yEnabled: Boolean
    ) {
        val adbOff = GuardState.adbAutoDisabled(this)
        val title: String
        val text: String
        when {
            grace -> {
                title = "Proteção em tolerância"
                text = "Janela por senha ativa; o watchdog volta a exigir a acessibilidade quando expirar."
            }
            enforcing && snapshot.blockActive -> {
                title = "Bloqueio ativo — proteção OK"
                val end = snapshot.blockEndMs?.let { " até ${DailyBlockPolicy.formatInstant(it)}" }.orEmpty()
                text = "Acessibilidade ativa$end${if (adbOff) " · depuração USB desligada" else ""}"
            }
            enforcing -> {
                title = "Proteção ativa"
                text = "Acessibilidade ativa (seletivo/Twitter e conteúdo adulto)${
                    if (adbOff) " · depuração USB desligada" else ""
                }"
            }
            a11yEnabled -> {
                title = "BLOQUEIO NÃO APLICADO"
                text = "Acessibilidade habilitada mas sem heartbeat (serviço morto). Tentando religar…"
            }
            else -> {
                title = "BLOQUEIO NÃO APLICADO"
                text = "Acessibilidade desligada. Tentando religar; abra o app se não voltar."
            }
        }
        val status = "$title|$text"
        if (status == lastStatus) return
        lastStatus = status
        runCatching {
            GuardNotifier.updateOngoing(
                this,
                GuardNotifier.buildOngoing(this, title, text)
            )
        }
    }

    override fun onDestroy() {
        stopped = true
        handler?.removeCallbacksAndMessages(null)
        handler = null
        runCatching { unregisterReceiver(screenReceiver) }
        runCatching { handlerThread.quitSafely() }
        // Se o serviço está morrendo com o bloqueio já encerrado, não deixa o ADB desligado órfão.
        runCatching {
            if (GuardState.adbAutoDisabled(this) && !GuardState.isBlockActive(this)) {
                AccessibilityGuard.restoreAdb(this)
            }
        }
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): android.os.IBinder? = null
}

