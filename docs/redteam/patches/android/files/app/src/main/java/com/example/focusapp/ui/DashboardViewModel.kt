package com.example.focusapp.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.focusapp.admin.AdminPolicyReceiver
import com.example.focusapp.data.FocusDatabase
import com.example.focusapp.data.MoneyOwedStore
import com.example.focusapp.data.ProductivityItemType
import com.example.focusapp.data.ProductivityRepository
import com.example.focusapp.display.GrayscaleController
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.security.PasswordManager
import com.example.focusapp.service.FocusAccessibilityService
import com.example.focusapp.service.LiveUpdateManager
import com.example.focusapp.service.ScreenTimeTrackerService
import com.example.focusapp.tracker.DailyAllowanceSummary
import com.example.focusapp.tracker.ScreenTimeManager
import com.example.focusapp.tracker.TrackerPrefs
import com.example.focusapp.tracker.UsageStatsReader
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Tudo o que o Dashboard exibe. A tela só lê este estado e chama as ações do [DashboardViewModel]. */
data class DashboardUiState(
    val date: String = FocusDatabase.getTodayDateString(),
    /** Incrementa a cada atualização do snapshot; reinicia o ticker de virada de dia. */
    val snapshotVersion: Int = 0,
    val usedTimeMillis: Long = 0L,
    val dailyLimitMillis: Long = TrackerPrefs.DEFAULT_LIMIT_MILLIS,
    val baseDailyLimitMillis: Long = TrackerPrefs.DEFAULT_LIMIT_MILLIS,
    val extraTimeGrantCount: Int = 0,
    val allowanceSummary: DailyAllowanceSummary? = null,
    val securityLevel: Int = TrackerPrefs.LEVEL_REINFORCED,
    val hasLiveUpdates: Boolean = false,
    val hasUsageStats: Boolean = false,
    val hasFocusAccessibility: Boolean = false,
    val hasAdmin: Boolean = false,
    val hasSecureSettings: Boolean = false,
    val isDeviceOwner: Boolean = false,
    val todayUnlockCount: Int? = null,
    val pendingTaskCount: Int = 0,
    val activeObjectiveCount: Int = 0,
    val moneyOwedCents: Long = 0L,
    val grayscaleTestSecondsRemaining: Int = 0
) {
    val permissionsGrantedCount: Int
        get() = listOf(hasLiveUpdates, hasUsageStats, hasFocusAccessibility, hasAdmin, hasSecureSettings).count { it }
}

/** Mensagem pontual (Toast) emitida pelo ViewModel. */
data class DashboardMessage(val text: String, val long: Boolean = false)

enum class LevelSelection { APPLIED, UNCHANGED, REJECTED, NEEDS_PASSWORD }

private data class GrantOutcome(
    val baseLimitMillis: Long,
    val effectiveLimitMillis: Long,
    val grantCount: Int,
    val allowanceSummary: DailyAllowanceSummary
)

internal fun securityLevelLabel(level: Int): String = when (level) {
    TrackerPrefs.LEVEL_STANDARD -> "Nível 1 (Padrão)"
    TrackerPrefs.LEVEL_REINFORCED -> "Nível 2 (Reforçado)"
    else -> "Nível 3 (Device Owner)"
}

class DashboardViewModel(application: Application) : AndroidViewModel(application) {

    private val context get() = getApplication<Application>()

    private val _state = MutableStateFlow(DashboardUiState(moneyOwedCents = MoneyOwedStore.loadCents(application)))
    val state: StateFlow<DashboardUiState> = _state.asStateFlow()

    private val _messages = MutableSharedFlow<DashboardMessage>(extraBufferCapacity = 8)
    val messages: SharedFlow<DashboardMessage> = _messages.asSharedFlow()

    /** Recarrega uso, limite, permissões e contagens. Chame ao voltar para a tela e na virada do dia. */
    suspend fun refresh() {
        val loaded = withContext(Dispatchers.IO) {
            val used = ScreenTimeManager.getScreenOnTimeToday(context)
            val summary = ScreenTimeManager.getAllowanceSummary(context)
            val roots = ProductivityRepository(context).loadRoots()
            DashboardUiState(
                date = FocusDatabase.getTodayDateString(),
                usedTimeMillis = used,
                dailyLimitMillis = ScreenTimeManager.getEffectiveDailyLimitMillis(context),
                baseDailyLimitMillis = TrackerPrefs.getDailyLimitMillis(context),
                extraTimeGrantCount = TrackerPrefs.getExtraTimeGrantCountToday(context),
                allowanceSummary = summary,
                securityLevel = TrackerPrefs.getSecurityLevel(context),
                hasLiveUpdates = LiveUpdateManager.canShowLiveUpdates(context),
                hasUsageStats = UsageStatsReader.hasUsageStatsPermission(context),
                // A-03: "Ativo" só com heartbeat fresco do próprio serviço (enabled ≠ bound). Assim o card
                // não mente quando o serviço foi morto por force-stop/crash.
                hasFocusAccessibility = FocusEnforcementState.isEnforcing(context),
                hasAdmin = AdminPolicyReceiver.isAdminActive(context),
                hasSecureSettings = GrayscaleController.hasSecureSettingsPermission(context),
                isDeviceOwner = AdminPolicyReceiver.isDeviceOwner(context),
                todayUnlockCount = ScreenTimeManager.getUnlockCountToday(context),
                pendingTaskCount = roots.count { it.type == ProductivityItemType.TASK && !it.completed },
                activeObjectiveCount = roots.count { it.type == ProductivityItemType.OBJECTIVE && !it.completed }
            )
        }
        // Preserva o que a tela controla localmente (saldo e contagem regressiva do teste de P&B).
        _state.update { current ->
            loaded.copy(
                snapshotVersion = current.snapshotVersion + 1,
                moneyOwedCents = current.moneyOwedCents,
                grayscaleTestSecondsRemaining = current.grayscaleTestSecondsRemaining
            )
        }
    }

    /** Decide o que fazer com um toque num nível; o rebaixamento só é aplicado depois da senha ([applySecurityLevel]). */
    fun selectSecurityLevel(newLevel: Int): LevelSelection {
        val current = _state.value
        return when {
            newLevel == current.securityLevel -> LevelSelection.UNCHANGED
            newLevel >= TrackerPrefs.LEVEL_DEVICE_OWNER && !current.isDeviceOwner -> {
                emit("Nível 3 exige Device Owner.")
                LevelSelection.REJECTED
            }
            newLevel < current.securityLevel && PasswordManager.getInstance(context).isPasswordSet() ->
                LevelSelection.NEEDS_PASSWORD
            else -> {
                applySecurityLevel(newLevel)
                LevelSelection.APPLIED
            }
        }
    }

    fun applySecurityLevel(level: Int) {
        TrackerPrefs.setSecurityLevel(context, level)
        _state.update { it.copy(securityLevel = level) }
        emit("${securityLevelLabel(level)} ativado")
    }

    fun setDailyLimit(millis: Long, label: String) {
        viewModelScope.launch {
            // setDailyLimitMillis consulta UsageStats e grava no banco.
            val (base, effective) = withContext(Dispatchers.IO) {
                ScreenTimeManager.setDailyLimitMillis(context, millis)
                TrackerPrefs.getDailyLimitMillis(context) to ScreenTimeManager.getEffectiveDailyLimitMillis(context)
            }
            _state.update { it.copy(baseDailyLimitMillis = base, dailyLimitMillis = effective) }
            ScreenTimeTrackerService.start(context)
            emit("Meta definida: $label")
        }
    }

    /** Concede +5 min se a frase estiver correta. Retorna true quando concedeu. */
    suspend fun grantExtraTime(typedPhrase: String): Boolean {
        val granted = withContext(Dispatchers.IO) { ScreenTimeManager.grantExtraTime(context, typedPhrase) }
        if (!granted) {
            emit("Frase incorreta. Tente novamente.", long = true)
            return false
        }
        val (base, effective, count, summary) = withContext(Dispatchers.IO) {
            GrantOutcome(
                TrackerPrefs.getDailyLimitMillis(context),
                ScreenTimeManager.getEffectiveDailyLimitMillis(context),
                TrackerPrefs.getExtraTimeGrantCountToday(context),
                ScreenTimeManager.getAllowanceSummary(context)
            )
        }
        _state.update {
            it.copy(
                baseDailyLimitMillis = base,
                dailyLimitMillis = effective,
                extraTimeGrantCount = count,
                allowanceSummary = summary
            )
        }
        ScreenTimeTrackerService.start(context)
        emit("Mais 5 minutos concedidos com sucesso!")
        return true
    }

    fun addMoneyOwed() {
        val updated = MoneyOwedStore.addFiftyReais(context)
        if (updated == null) emit("Limite máximo do saldo alcançado.")
        else _state.update { it.copy(moneyOwedCents = updated) }
    }

    fun subtractMoneyOwed() {
        val updated = MoneyOwedStore.subtractFiftyReais(context)
        _state.update { it.copy(moneyOwedCents = updated) }
    }

    fun resetMoneyOwed() {
        MoneyOwedStore.resetToZero(context)
        _state.update { it.copy(moneyOwedCents = 0L) }
    }

    /** Liga o P&B por 5 s para o usuário ver o efeito; sempre volta ao normal, mesmo se a tela sair. */
    fun testGrayscale() {
        if (_state.value.grayscaleTestSecondsRemaining > 0) return
        viewModelScope.launch {
            GrayscaleController.enableGrayscale(context)
            try {
                for (second in 5 downTo 1) {
                    _state.update { it.copy(grayscaleTestSecondsRemaining = second) }
                    delay(1000L)
                }
                emit("Cores restauradas!")
            } finally {
                withContext(NonCancellable) {
                    _state.update { it.copy(grayscaleTestSecondsRemaining = 0) }
                    // Não desliga o P&B se o limite diário o estiver exigindo de fato.
                    if (!GrayscaleController.isEnforcedByDailyLimit(context)) {
                        GrayscaleController.disableGrayscale(context)
                    }
                }
            }
        }
    }

    private fun emit(text: String, long: Boolean = false) {
        _messages.tryEmit(DashboardMessage(text, long))
    }
}
