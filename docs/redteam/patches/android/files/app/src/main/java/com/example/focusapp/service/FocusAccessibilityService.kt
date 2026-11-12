package com.example.focusapp.service

import com.example.focusapp.tracker.TrackerPrefs
import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.AccessibilityServiceInfo
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.Gravity
import android.view.LayoutInflater
import android.view.View
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityManager
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.Button
import android.widget.TextView
import com.example.focusapp.MainActivity
import com.example.focusapp.R
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.guard.GuardNotifier
import com.example.focusapp.service.GuardWatchdogService
import com.example.focusapp.push.AckClient
import com.example.focusapp.push.AckPayloads
import com.example.focusapp.security.PasswordManager
import com.example.focusapp.tracker.ScreenTimeManager
import com.example.focusapp.schedule.DailyBlockController
import com.example.focusapp.schedule.DailyBlockPolicy
import com.example.focusapp.schedule.DailyBlockStore
import android.content.SharedPreferences

/**
 * Serviço de acessibilidade: bloqueia apps/sites proibidos (Twitter/X, Instagram e Reddit), delega o bloqueio
 * por horário ao [DailyBlockController] e protege as telas de Settings / PackageInstaller,
 * onde overlays convencionais podem ser suprimidos pelo Android.
 *
 * v1.7 (blindagem Fase 1):
 *  - heartbeat real ([FocusEnforcementState]) para a UI/watchdog saberem se o serviço está VIVO;
 *  - inicia o [GuardWatchdogService], que religa a acessibilidade e vigia admin/ADB;
 *  - anti-tamper das telas de Acessibilidade/Administrador/Detalhes do app casando os três rótulos
 *    (`app_name`, `accessibility_service_name`, package) e o className — A-02 do red-team;
 *  - descoberta de navegadores em runtime e varredura genérica de URL adulta em WebView/custom tabs;
 *  - ack para o PC quando o serviço é desligado (o PC não pode mais achar que está bloqueado).
 */
class FocusAccessibilityService : AccessibilityService() {

    private var windowManager: WindowManager? = null
    private var blockedOverlayView: View? = null
    private var lastBlockedPackage: String? = null
    private var lastBlockedAtElapsedRealtime = 0L
    private var dailyBlockController: DailyBlockController? = null
    private var homePackageName: String? = null
    private var dailyBlockActive = false
    private var lastWindowClass: String? = null

    private val blockStore by lazy { DailyBlockStore(this) }
    // Fim do bloqueio adulto em cache: onAccessibilityEvent roda muito, então não lemos as preferências a cada evento.
    private var adultBlockEndMs: Long? = null
    private var adultBlockMonoEndMs: Long = 0L
    private val adultBlockListener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        if (key == null || key == DailyBlockStore.ADULT_KEY || key == ADULT_MONO_KEY) {
            adultBlockEndMs = blockStore.loadAdultEnd()
            adultBlockMonoEndMs = blockStore.loadAdultMonoEnd()
            updatePackageFilter(dailyBlockActive)
        }
    }
    // Modo seletivo em cache pelas mesmas razões: só Twitter/X + adulto, sempre, sem travar a tela no Angry.
    private var selectiveBlock = false
    private val selectiveListener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        if (key == null || key == DailyBlockStore.SELECTIVE_KEY) {
            selectiveBlock = blockStore.loadSelective()
            updatePackageFilter(dailyBlockActive)
        }
    }
    // A varredura de textos visíveis é mais cara que ler a barra: roda no máximo a cada DeepScanIntervalMs.
    private var lastDeepScanAtElapsedRealtime = 0L
    // Varredura genérica (qualquer app/WebView): orçamento e intervalo próprios, mais conservadores.
    private var lastGenericScanAtElapsedRealtime = 0L
    // Navegadores instalados depois do onServiceConnected: reconsulta o PackageManager de minuto em minuto.
    private var lastBrowserDiscoveryAtElapsedRealtime = 0L
    // O aviso de bloqueio precisa sobreviver ao evento do launcher disparado pelo próprio home action.
    private var overlayHoldUntilElapsedRealtime = 0L

    // Heartbeat: prova de vida lida pela UI/watchdog (enabled != bound).
    private val heartbeatHandler = Handler(Looper.getMainLooper())
    private val heartbeatTick = object : Runnable {
        override fun run() {
            FocusEnforcementState.beat(this@FocusAccessibilityService)
            heartbeatHandler.postDelayed(this, HEARTBEAT_INTERVAL_MS)
        }
    }

    override fun onServiceConnected() {
        super.onServiceConnected()
        windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager

        val homeIntent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
        homePackageName = packageManager.resolveActivity(homeIntent, 0)?.activityInfo?.packageName

        // Prova de vida imediata + periódica (a UI só mostra "Ativo" com heartbeat fresco).
        FocusEnforcementState.beat(this)
        heartbeatHandler.removeCallbacks(heartbeatTick)
        heartbeatHandler.postDelayed(heartbeatTick, HEARTBEAT_INTERVAL_MS)

        // B-01: descobre em runtime quem trata ACTION_VIEW http/https (pega navegador instalado depois).
        FocusBlockRules.updateDiscoveredBrowsers(FocusBlockRules.discoverBrowserPackages(this))
        lastBrowserDiscoveryAtElapsedRealtime = SystemClock.elapsedRealtime()

        blockStore.preferences.unregisterOnSharedPreferenceChangeListener(adultBlockListener)
        blockStore.preferences.registerOnSharedPreferenceChangeListener(adultBlockListener)
        adultBlockEndMs = blockStore.loadAdultEnd()
        adultBlockMonoEndMs = blockStore.loadAdultMonoEnd()
        blockStore.preferences.unregisterOnSharedPreferenceChangeListener(selectiveListener)
        blockStore.preferences.registerOnSharedPreferenceChangeListener(selectiveListener)
        selectiveBlock = blockStore.loadSelective()

        dailyBlockController?.close()
        dailyBlockController = DailyBlockController(this, onBlockingChanged = { blocked ->
            dailyBlockActive = blocked
            updatePackageFilter(blocked)
        }, removeExistingOverlay = { removeBlockedOverlay() }).also { it.connect() }

        // O watchdog religa a acessibilidade se alguém desligar com o app vivo.
        GuardWatchdogService.start(this)
    }

    /**
     * Define quais pacotes o AccessibilityService recebe. Com bloqueio de tela cheia OU varredura
     * genérica ligada (seletivo/adulto), recebe de todos os apps; fora disso, só a lista monitorada.
     */
    private fun updatePackageFilter(blocked: Boolean) {
        serviceInfo?.let { info ->
            val packages: Array<String>? =
                if (blocked || FocusBlockRules.shouldScanAllPackages(selectiveBlock, isAdultBlockActive())) {
                    null
                } else {
                    (FocusBlockRules.runtimeMonitoredPackages() + listOfNotNull(homePackageName)).toTypedArray()
                }
            if (!(info.packageNames contentEquals packages)) {
                info.packageNames = packages
                serviceInfo = info
            }
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event == null) return

        if (event.eventType == AccessibilityEvent.TYPE_WINDOWS_CHANGED) {
            dailyBlockController?.onWindow(null, true)
            return
        }

        val packageName = event.packageName?.toString() ?: return
        if (event.eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) {
            lastWindowClass = event.className?.toString()
            maybeRefreshBrowserDiscovery()
        }
        if (dailyBlockController?.onWindow(packageName,
                event.eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) == true) return

        // Nível 1 (monitoramento) não bloqueia via Accessibility; o bloqueio começa no Nível 2+. No modo
        // seletivo, Twitter/X e conteúdo adulto valem em qualquer nível, e o Angry do PC não trava a tela.
        val level = TrackerPrefs.getSecurityLevel(this)
        val reinforced = level >= TrackerPrefs.LEVEL_REINFORCED
        val selective = selectiveBlock

        // 1. Bloqueio Instantâneo de Apps Proibidos: seletivo = só Twitter/X, sempre; senão redes no Nível 2+.
        if (FocusBlockRules.shouldBlockApp(packageName, selective, reinforced)) {
            handleBlockedApp(packageName)
            return
        }

        // 2. Verifica a barra de endereço nos navegadores: seletivo = Twitter/X + adulto sempre; modo antigo =
        // sites bloqueados (Nível 2+) e conteúdo adulto (enquanto o bloqueio adulto do PC estiver ativo).
        val adultActive = selective || isAdultBlockActive()
        val deepScan = event.eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED ||
            event.eventType == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED
        if (FocusBlockRules.isBrowser(packageName) &&
            FocusBlockRules.shouldInspectBrowser(selective, reinforced, adultActive) &&
            shouldInspectBrowserEvent(event) &&
            checkBrowserUrl(packageName, rootInActiveWindow, selective, reinforced, adultActive, deepScan)
        ) {
            return
        }

        // 3. B-02: varredura conservadora de URL adulta em app que não é navegador (WebView/custom tab de
        // Telegram/Discord/Facebook etc.). Só host da lista adulta; nunca palavra solta.
        if (adultActive && FocusBlockRules.shouldScanAnyAppPackage(packageName, this.packageName) &&
            checkGenericAdultUrl(packageName, event)
        ) {
            return
        }

        // 4. Escudo Anti-Tamper e Anti-Desinstalação sobre Settings / PackageInstaller
        if (FocusBlockRules.isSettingsPackage(packageName)) {
            if (reinforced) {
                checkSettingsTampering(packageName, rootInActiveWindow)
            } else {
                removeBlockedOverlayIfExpired()
            }
        } else {
            removeBlockedOverlayIfExpired()
        }
    }

    /** Reconsulta os navegadores de minuto em minuto (instalação de navegador novo não é protegida). */
    private fun maybeRefreshBrowserDiscovery() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastBrowserDiscoveryAtElapsedRealtime < BrowserDiscoveryIntervalMs) return
        lastBrowserDiscoveryAtElapsedRealtime = now
        val discovered = FocusBlockRules.discoverBrowserPackages(this)
        if (discovered != FocusBlockRules.discoveredBrowserPackages()) {
            FocusBlockRules.updateDiscoveredBrowsers(discovered)
            updatePackageFilter(dailyBlockActive)
        }
    }

    private fun shouldInspectBrowserEvent(event: AccessibilityEvent): Boolean = when (event.eventType) {
        AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED,
        AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED -> true
        AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED ->
            event.contentChangeTypes == 0 ||
                    (event.contentChangeTypes and AccessibilityEvent.CONTENT_CHANGE_TYPE_TEXT) != 0
        else -> false
    }

    private fun shouldInspectGenericEvent(event: AccessibilityEvent): Boolean = when (event.eventType) {
        AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED -> true
        AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED ->
            event.contentChangeTypes == 0 ||
                    (event.contentChangeTypes and AccessibilityEvent.CONTENT_CHANGE_TYPE_TEXT) != 0
        else -> false
    }

    private fun handleBlockedApp(packageName: String) {
        val now = SystemClock.elapsedRealtime()
        if (lastBlockedPackage == packageName && now - lastBlockedAtElapsedRealtime < 500L) return
        lastBlockedPackage = packageName
        lastBlockedAtElapsedRealtime = now
        FocusEnforcementState.beatBlockedEvent(this)
        performGlobalAction(GLOBAL_ACTION_HOME)
        showBlockedOverlay(packageName)
    }

    private fun isAdultBlockActive(): Boolean = DailyBlockPolicy.isAdultBlockActive(
        adultBlockEndMs,
        System.currentTimeMillis(),
        adultBlockMonoEndMs,
        SystemClock.elapsedRealtime()
    )

    private fun checkBrowserUrl(
        packageName: String,
        rootNode: AccessibilityNodeInfo?,
        selective: Boolean,
        reinforced: Boolean,
        adultActive: Boolean,
        deepScan: Boolean
    ): Boolean {
        if (rootNode == null) return false
        try {
            // 1. Barra de endereço pelos ids conhecidos (texto e descrição acessível).
            for (viewId in FocusBlockRules.addressBarViewIds(packageName)) {
                val nodes = runCatching { rootNode.findAccessibilityNodeInfosByViewId(viewId) }.getOrNull().orEmpty()
                for (node in nodes) {
                    val values = listOfNotNull(node.text?.toString(), node.contentDescription?.toString())
                    for (value in values) {
                        if (FocusBlockRules.shouldBlockWebAddress(value, selective, reinforced, adultActive)) {
                            handleBlockedApp(browserBlockLabel(value))
                            return true
                        }
                    }
                }
            }

            // 2. Rede de segurança: navegadores trocam o id da barra entre versões (e os descobertos em
            // runtime não têm id conhecido). Percorre a árvore visível (limitada e com intervalo mínimo) e
            // testa textos curtos — barra, título da aba, título da página.
            val now = SystemClock.elapsedRealtime()
            if (!deepScan || now - lastDeepScanAtElapsedRealtime < DeepScanIntervalMs) return false
            lastDeepScanAtElapsedRealtime = now
            val texts = LinkedHashSet<String>()
            collectVisibleTexts(rootNode, texts, intArrayOf(MaxScanNodes))
            for (text in texts) {
                if (FocusBlockRules.shouldBlockBrowserText(text, selective, reinforced, adultActive)) {
                    handleBlockedApp(browserBlockLabel(text))
                    return true
                }
            }
        } catch (e: Exception) {
            // ignore
        }
        return false
    }

    /** Varredura genérica em app não-navegador; só bloqueia com host adulto explícito (B-02). */
    private fun checkGenericAdultUrl(packageName: String, event: AccessibilityEvent): Boolean {
        if (!shouldInspectGenericEvent(event)) return false
        val now = SystemClock.elapsedRealtime()
        if (now - lastGenericScanAtElapsedRealtime < GenericScanIntervalMs) return false
        lastGenericScanAtElapsedRealtime = now
        val rootNode = rootInActiveWindow ?: return false
        return try {
            val texts = LinkedHashSet<String>()
            collectVisibleTexts(rootNode, texts, intArrayOf(MaxGenericScanNodes))
            val host = texts.firstNotNullOfOrNull { FocusBlockRules.firstAdultDomainIn(it) } ?: return false
            handleBlockedApp("conteúdo adulto ($host)")
            true
        } catch (e: Exception) {
            false
        }
    }

    /** Coleta textos/descrições curtos da árvore visível, com orçamento de nós para não pesar no evento. */
    private fun collectVisibleTexts(node: AccessibilityNodeInfo?, out: MutableSet<String>, budget: IntArray) {
        if (node == null || budget[0] <= 0) return
        budget[0]--
        node.text?.toString()?.trim()?.takeIf { it.isNotEmpty() }?.let(out::add)
        node.contentDescription?.toString()?.trim()?.takeIf { it.isNotEmpty() }?.let(out::add)
        for (index in 0 until node.childCount) {
            collectVisibleTexts(node.getChild(index), out, budget)
            if (budget[0] <= 0) return
        }
    }

    /**
     * Texto curto para o aviso de bloqueio. A barra do Firefox mostra "xvideos.com. Pesquise ou digite um
     * endereço"; o aviso fica melhor com só o domínio. Um título de página não tem domínio: usa "Navegador Web".
     */
    private fun browserBlockLabel(text: String): String {
        val first = text.trim().substringBefore(' ').trimEnd('.')
        return if (first.contains('.')) "Navegador Web ($first)" else "Navegador Web"
    }

    companion object {
        private const val TAG = "FocusAccessibility"
        private const val MaxScanNodes = 500
        private const val MaxGenericScanNodes = 250
        private const val MaxTamperScanNodes = 200
        private const val DeepScanIntervalMs = 700L
        private const val GenericScanIntervalMs = 1_200L
        private const val BrowserDiscoveryIntervalMs = 60_000L
        private const val OverlayHoldMs = 5_000L
        private const val HEARTBEAT_INTERVAL_MS = 20_000L
        private const val ADULT_MONO_KEY = "adult_end_elapsed_ms"

        fun isEnabled(context: Context): Boolean {
            val manager = context.getSystemService(Context.ACCESSIBILITY_SERVICE) as AccessibilityManager
            return manager.getEnabledAccessibilityServiceList(AccessibilityServiceInfo.FEEDBACK_ALL_MASK)
                .any { service ->
                    val info = service.resolveInfo.serviceInfo
                    info.packageName == context.packageName && info.name == FocusAccessibilityService::class.java.name
                }
        }
    }

    /**
     * Resolução da vulnerabilidade documentada pelo LockKeeper:
     * Overlays normais podem ser ocultados em telas de configurações do sistema.
     * Portanto, em vez de depender de overlay em Settings, usamos performGlobalAction(GLOBAL_ACTION_HOME)
     * e redirecionamos o usuário para a tela de autenticação do app.
     */
    private fun checkSettingsTampering(packageName: String, rootNode: AccessibilityNodeInfo?): Boolean {
        val passwordManager = PasswordManager.getInstance(this)
        if (passwordManager.isGracePeriodActive()) {
            return false
        }

        if (rootNode == null) return false

        try {
            val texts = LinkedHashSet<String>()
            collectVisibleTexts(rootNode, texts, intArrayOf(MaxTamperScanNodes))
            // A tela de Acessibilidade mostra o label do SERVIÇO ("Serviço de Foco e Proteção"), não o
            // nome do app: os três rótulos precisam entrar no match (A-02 do red-team).
            val ownLabels = listOf(
                getString(R.string.app_name),
                getString(R.string.accessibility_service_name)
            )
            val tampering = FocusBlockRules.isProtectionTamperScreen(
                packageName,
                lastWindowClass,
                texts,
                ownLabels,
                packageName
            )
            if (tampering) {
                Log.i(TAG, "Anti-tamper: bloqueando tela de Ajustes (class=$lastWindowClass)")
                // Interrupção imediata via Home action
                performGlobalAction(GLOBAL_ACTION_HOME)

                val intent = Intent(this, MainActivity::class.java).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
                }
                startActivity(intent)
            }
            return tampering
        } catch (e: Exception) {
            Log.w(TAG, "Falha ao inspecionar a tela de Configurações", e)
            return false
        }
    }

    private fun showBlockedOverlay(packageName: String) {
        if (blockedOverlayView != null) return
        // O home action faz o launcher emitir evento logo em seguida; o aviso não pode desaparecer antes disso.
        overlayHoldUntilElapsedRealtime = SystemClock.elapsedRealtime() + OverlayHoldMs

        val params = WindowManager.LayoutParams(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.CENTER
        }

        val inflater = LayoutInflater.from(this)
        blockedOverlayView = inflater.inflate(R.layout.layout_blocked_overlay, null).apply {
            findViewById<TextView>(R.id.tv_blocked_message).text =
                "O acesso a $packageName foi bloqueado para garantir seu tempo de foco diário."

            findViewById<Button>(R.id.btn_exit_blocked).setOnClickListener {
                removeBlockedOverlay()
                performGlobalAction(GLOBAL_ACTION_HOME)
            }
        }

        try {
            windowManager?.addView(blockedOverlayView, params)
        } catch (e: Exception) {
            Log.w(TAG, "Não foi possível exibir o overlay de bloqueio", e)
        }
    }

    /** Remove o aviso só depois do intervalo mínimo: o botão "Entendi" remove na hora, por outro caminho. */
    private fun removeBlockedOverlayIfExpired() {
        if (SystemClock.elapsedRealtime() < overlayHoldUntilElapsedRealtime) return
        removeBlockedOverlay()
    }

    private fun removeBlockedOverlay() {
        overlayHoldUntilElapsedRealtime = 0L
        blockedOverlayView?.let {
            try {
                windowManager?.removeView(it)
            } catch (e: Exception) {
                // ignore
            }
            blockedOverlayView = null
        }
    }

    /**
     * Quando o usuário desliga o serviço (ou o sistema desvincula), o app pode continuar vivo: avisamos
     * o PC imediatamente (ack `a11y_off`), zeramos o heartbeat e acordamos o watchdog para religar.
     */
    override fun onUnbind(intent: Intent?): Boolean {
        heartbeatHandler.removeCallbacks(heartbeatTick)
        FocusEnforcementState.clear(this)
        runCatching {
            AckClient.send(this, AckPayloads.current(this, id = "a11y_off", reason = "a11y_off"))
            GuardNotifier.notifyAccessibilityLost(this, "serviço desvinculado")
        }
        GuardWatchdogService.start(this)
        return super.onUnbind(intent)
    }

    override fun onInterrupt() {
        removeBlockedOverlay()
    }

    override fun onDestroy() {
        super.onDestroy()
        heartbeatHandler.removeCallbacksAndMessages(null)
        FocusEnforcementState.clear(this)
        dailyBlockController?.close()
        dailyBlockController = null
        blockStore.preferences.unregisterOnSharedPreferenceChangeListener(adultBlockListener)
        blockStore.preferences.unregisterOnSharedPreferenceChangeListener(selectiveListener)
        removeBlockedOverlay()
    }
}
