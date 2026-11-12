package com.example.focusapp.schedule

import android.accessibilityservice.AccessibilityService
import android.app.KeyguardManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.SharedPreferences
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.SystemClock
import android.telecom.TelecomManager
import android.util.Log
import android.view.WindowManager
import android.view.accessibility.AccessibilityWindowInfo
import androidx.core.content.ContextCompat
import com.example.focusapp.guard.GuardNotifier
import com.example.focusapp.guard.GuardPolicy
import com.example.focusapp.guard.GuardState
import com.example.focusapp.push.AckClient
import com.example.focusapp.push.AckPayloads
import com.example.focusapp.service.GuardWatchdogService
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import kotlin.math.abs

/** Owns the scheduled overlay; existing network blocks retain their independent policy. */
internal class DailyBlockController(
    private val service: AccessibilityService,
    private val onBlockingChanged: (Boolean) -> Unit,
    private val removeExistingOverlay: () -> Unit
) {
    private val store = DailyBlockStore(service)
    private val handler = Handler(Looper.getMainLooper())
    private val windowManager = service.getSystemService(WindowManager::class.java)
    private var overlay: PhraseKeyboardView? = null
    private var overlayKey: String? = null
    private var foregroundPackage: String? = null
    private var connected = false
    private val dialer = service.getSystemService(TelecomManager::class.java)?.defaultDialerPackage
    private val callPackages = setOfNotNull(dialer, "com.android.dialer", "com.google.android.dialer",
        "com.samsung.android.dialer", "com.samsung.android.incallui", "com.android.incallui", "com.android.phone",
        "com.android.server.telecom", "com.samsung.android.app.telephonyui")
    private val tick = Runnable { evaluate() }
    // Cache of today's record: evaluate() runs on every accessibility event, so avoid re-parsing JSON each time.
    private var cachedDate: LocalDate? = null
    private var cachedRecord: DailyBlockRecord? = null
    private var pomodoroLoaded = false
    private var cachedPomodoro: PomodoroBlockRecord? = null
    private var selectiveLoaded = false
    private var cachedSelective = false
    private var lastBlocked: Boolean? = null
    // Detecção de salto de relógio (C-01): compara o wall-clock com o monotônico a cada avaliação.
    private var lastWallClockMs = 0L
    private var lastElapsedClockMs = 0L
    private val changed = SharedPreferences.OnSharedPreferenceChangeListener { _, _ ->
        cachedDate = null
        pomodoroLoaded = false
        selectiveLoaded = false
        handler.post {
            if (connected) {
                DailyBlockScheduler.refresh(service)
                evaluate()
            }
        }
    }
    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != DailyBlockScheduler.ACTION_REFRESH) {
                foregroundPackage = applicationPackage() ?: foregroundPackage
                DailyBlockScheduler.refresh(service)
            }
            evaluate()
        }
    }

    fun connect() {
        connected = true
        store.preferences.registerOnSharedPreferenceChangeListener(changed)
        val filter = IntentFilter().apply {
            addAction(DailyBlockScheduler.ACTION_REFRESH)
            addAction(Intent.ACTION_SCREEN_ON)
            addAction(Intent.ACTION_SCREEN_OFF)
            addAction(Intent.ACTION_USER_PRESENT)
            addAction(Intent.ACTION_TIME_CHANGED)
            addAction(Intent.ACTION_TIMEZONE_CHANGED)
            addAction(Intent.ACTION_DATE_CHANGED)
            addAction("android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED")
        }
        ContextCompat.registerReceiver(service, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
        foregroundPackage = applicationPackage()
        DailyBlockScheduler.refresh(service)
        evaluate()
    }

    fun onWindow(packageName: String?, windowChanged: Boolean): Boolean {
        if (windowChanged) {
            // Without a record today nothing can block; skip the window-list IPC (evaluate() refreshes it once blocked).
            val fallback = when {
                packageName == service.packageName && overlay != null -> foregroundPackage
                else -> packageName ?: foregroundPackage
            }
            val anyRecord = todayRecord(LocalDate.now()) != null || pomodoroRecord() != null
            foregroundPackage = if (!anyRecord) fallback else applicationPackage() ?: fallback
        }
        return evaluate()
    }

    private fun pomodoroRecord(): PomodoroBlockRecord? {
        if (!pomodoroLoaded) {
            cachedPomodoro = store.loadPomodoro()
            pomodoroLoaded = true
        }
        return cachedPomodoro
    }

    /** Modo seletivo: o Angry do PC não trava a tela; Twitter/X e conteúdo adulto continuam sendo aplicados. */
    private fun selectiveMode(): Boolean {
        if (!selectiveLoaded) {
            cachedSelective = store.loadSelective()
            selectiveLoaded = true
        }
        return cachedSelective
    }

    /** O que o overlay mostra e como libera. O pomodoro tem prioridade: é o mais recente e tem fim exato. */
    private class ActiveBlock(val key: String, val title: String, val endLabel: String, val release: (String) -> Boolean)

    private fun activeBlock(
        daily: DailyBlockRecord?,
        dailyActive: Boolean,
        pomodoro: PomodoroBlockRecord?,
        now: LocalDateTime,
        nowMs: Long,
        nowElapsed: Long
    ): ActiveBlock? = when {
        pomodoro != null && DailyBlockPolicy.shouldShowPomodoroOverlay(selectiveMode(), pomodoro, nowMs, nowElapsed) ->
            ActiveBlock("p:${pomodoro.id}", "Pomodoro Angry", DailyBlockPolicy.formatInstant(pomodoro.endMs)) { phrase ->
                store.releasePomodoro(pomodoro, phrase)
            }
        daily != null && dailyActive ->
            ActiveBlock("d:${daily.id}", "Horário de foco", DailyBlockPolicy.formatMinute(daily.endMinute)) { phrase ->
                store.release(daily, phrase)
            }
        else -> null
    }

    private fun todayRecord(date: LocalDate): DailyBlockRecord? {
        if (cachedDate != date) {
            cachedRecord = store.load(date)
            cachedDate = date
        }
        return cachedRecord
    }

    private fun applicationPackage(): String? = runCatching {
        // An accessibility overlay can own focus; inspect the highest underlying application window.
        service.windows.filter { it.type == AccessibilityWindowInfo.TYPE_APPLICATION }
            .sortedByDescending { it.layer }
            .firstNotNullOfOrNull { it.root?.packageName?.toString() }
            ?: service.rootInActiveWindow?.packageName?.toString()?.takeUnless { it == service.packageName && overlay != null }
    }.getOrNull()

    /**
     * Bloqueio do dia com âncora monotônica (C-01): se o relógio de parede for adiantado, o bloqueio
     * que já estava valendo continua até o fim ancorado em `elapsedRealtime`. Se o salto passou da
     * meia-noite, o record de ontem é recuperado pelo id da âncora.
     */
    private fun isDailyBlockActive(
        record: DailyBlockRecord?,
        now: LocalDateTime,
        nowMs: Long,
        nowElapsed: Long
    ): Boolean {
        if (record == null) {
            GuardState.clearDailyAnchor(service)
            return false
        }
        if (DailyBlockPolicy.shouldBlock(record, now)) {
            val anchor = GuardState.loadDailyAnchor(service)
            if (anchor == null || anchor.first != record.id) {
                val endWallMs = record.end().atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
                GuardState.saveDailyAnchor(service, record.id, nowElapsed + (endWallMs - nowMs))
            }
            return true
        }
        val anchor = GuardState.loadDailyAnchor(service)
        val anchored = DailyBlockPolicy.shouldBlockAnchored(
            record,
            anchor?.first,
            anchor?.second ?: 0L,
            nowElapsed
        )
        if (!anchored && anchor != null && anchor.first == record.id) GuardState.clearDailyAnchor(service)
        return anchored
    }

    private fun evaluate(): Boolean {
        if (!connected) return false
        handler.removeCallbacks(tick)
        val now = LocalDateTime.now()
        val nowMs = System.currentTimeMillis()
        val nowElapsed = SystemClock.elapsedRealtime()
        detectClockJump(nowMs, nowElapsed)
        val today = todayRecord(now.toLocalDate())
        // Salto de relógio que passou da meia-noite: recupera o record ancorado (de ontem) pelo id.
        val record = today ?: anchoredDailyRecord(now, nowElapsed)
        val pomodoro = pomodoroRecord()
        val dailyActive = isDailyBlockActive(record, now, nowMs, nowElapsed)
        val active = activeBlock(record, dailyActive, pomodoro, now, nowMs, nowElapsed)
        val blocked = active != null
        if (blocked != lastBlocked) {
            lastBlocked = blocked
            onBlockingChanged(blocked)
            if (blocked) GuardWatchdogService.start(service)
        }
        // At an alarm boundary the call app may have opened while the old package filter was active.
        if (blocked) foregroundPackage = applicationPackage() ?: foregroundPackage
        val interactive = service.getSystemService(PowerManager::class.java).isInteractive
        val keyguard = service.getSystemService(KeyguardManager::class.java).isKeyguardLocked
        val callVisible = foregroundPackage in callPackages
        if (active != null && interactive && !keyguard && !callVisible) {
            removeExistingOverlay()
            if (overlayKey != active.key) {
                removeOverlay()
                showOverlay(active)
            }
        } else removeOverlay()
        // Com a tela apagada o alarme exato da fronteira e o broadcast de SCREEN_ON reavaliam; o tick só vale com a tela acesa.
        if (interactive) {
            val dailyDelay = if (dailyActive && !DailyBlockPolicy.shouldBlock(record, now)) {
                // Ativo só pela âncora (relógio adiantado): acorda no fim monotônico para remover o overlay.
                val anchor = GuardState.loadDailyAnchor(service)
                if (anchor != null && anchor.first == record?.id) anchor.second - nowElapsed else null
            } else {
                DailyBlockPolicy.nextBoundary(record, now)?.let { Duration.between(now, it).toMillis() }
            }
            val pomodoroDelay = DailyBlockPolicy.nextBoundaryMs(pomodoro, nowMs, nowElapsed)?.let { it - nowMs }
            listOfNotNull(dailyDelay, pomodoroDelay).minOrNull()?.let { delay ->
                handler.postDelayed(tick, delay.coerceIn(1L, MAX_TICK_MILLIS))
            }
        }
        // While scheduled block owns the screen, do not run the legacy overlay removal path.
        return blocked
    }

    /**
     * Detecta mudança de data/hora/fuso comparando o wall-clock com o monotônico. Adianta/atrasa o
     * relógio não pode encerrar um bloqueio do PC (ancorado em elapsedRealtime) nem o do dia (âncora
     * gravada em [isDailyBlockActive]); avisamos e reancoramos os fins de parede para o display/alarme.
     */
    /** Record de ontem que ainda vale pela âncora monotônica (relógio adiantado passou da meia-noite). */
    private fun anchoredDailyRecord(now: LocalDateTime, nowElapsed: Long): DailyBlockRecord? {
        val anchor = GuardState.loadDailyAnchor(service) ?: return null
        val yesterday = store.load(now.toLocalDate().minusDays(1)) ?: return null
        if (yesterday.id != anchor.first) return null
        return yesterday.takeIf {
            DailyBlockPolicy.shouldBlockAnchored(it, anchor.first, anchor.second, nowElapsed)
        }
    }

    private fun detectClockJump(nowMs: Long, nowElapsed: Long) {
        if (lastWallClockMs > 0L) {
            val expectedWall = lastWallClockMs + (nowElapsed - lastElapsedClockMs)
            val jumpMs = nowMs - expectedWall
            if (abs(jumpMs) >= CLOCK_JUMP_THRESHOLD_MS) onClockJump(nowMs, nowElapsed)
        }
        lastWallClockMs = nowMs
        lastElapsedClockMs = nowElapsed
    }

    private fun onClockJump(nowMs: Long, nowElapsed: Long) {
        Log.w("DailyBlock", "Salto de relógio detectado; reancorando bloqueios")
        if (GuardPolicy.shouldWarnClock(GuardState.lastClockWarnMs(service), nowMs)) {
            GuardState.markClockWarned(service, nowMs)
            GuardNotifier.notifyClockChanged(service)
        }
        val pomodoro = store.loadPomodoro()
        if (pomodoro != null && !pomodoro.released && pomodoro.endElapsedRealtime > 0L) {
            val remaining = pomodoro.remainingMs(nowMs, nowElapsed)
            if (remaining > 0L) {
                store.reanchorPomodoro(pomodoro, nowMs + remaining)
            }
        }
        val adultMono = store.loadAdultMonoEnd()
        if (adultMono > 0L && nowElapsed < adultMono) {
            val newEnd = nowMs + (adultMono - nowElapsed)
            if (store.loadAdultEnd() != newEnd) store.reanchorAdult(newEnd)
        }
        DailyBlockScheduler.refresh(service)
        AckClient.send(service, AckPayloads.current(service, id = "clock_change", reason = "clock_change"))
    }

    private fun showOverlay(block: ActiveBlock) {
        // Regra do produto: bloqueio de tela SÓ no S23 Ultra. No moto g(20) (e em qualquer outro
        // aparelho) o app segue registrando o bloqueio, bloqueando apps/sites e reportando ao PC,
        // mas nunca cobre a tela com o overlay de frase.
        if (!ScreenLockDevicePolicy.isScreenLockAllowed()) {
            Log.i("DailyBlock", "Bloqueio de tela ignorado: aparelho não é o S23 Ultra (regra do produto)")
            removeOverlay()
            return
        }
        val view = PhraseKeyboardView(service, block.title, block.endLabel,
            onRelease = { phrase ->
                val saved = block.release(phrase)
                if (saved) {
                    removeOverlay()
                    DailyBlockScheduler.refresh(service)
                    evaluate()
                }
                saved
            },
            onCall = {
                try {
                    val intent = Intent(Intent.ACTION_DIAL).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    dialer?.let { intent.setPackage(it) }
                    service.startActivity(intent)
                    // The dialer window event removes the overlay; failed launches keep the screen blocked.
                } catch (error: Exception) { Log.w("DailyBlock", "Não foi possível abrir chamadas", error) }
            })
        val params = WindowManager.LayoutParams(WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN, PixelFormat.OPAQUE)
        try {
            windowManager.addView(view, params)
            overlay = view
            overlayKey = block.key
        } catch (error: Exception) {
            Log.e("DailyBlock", "Não foi possível exibir bloqueio", error)
            // Reattempt on the next window event or timer; never mark the record released.
        }
    }

    private fun removeOverlay() {
        overlay?.let { view -> runCatching { windowManager.removeView(view) } }
        overlay = null
        overlayKey = null
    }

    private companion object {
        const val MAX_TICK_MILLIS = 5 * 60_000L

        /** Abaixo disso é o relógio normal andando; acima é salto de fuso/data/hora. */
        const val CLOCK_JUMP_THRESHOLD_MS = 90_000L
    }

    fun close() {
        if (!connected) return
        connected = false
        handler.removeCallbacksAndMessages(null)
        store.preferences.unregisterOnSharedPreferenceChangeListener(changed)
        service.unregisterReceiver(receiver)
        removeOverlay()
    }
}
