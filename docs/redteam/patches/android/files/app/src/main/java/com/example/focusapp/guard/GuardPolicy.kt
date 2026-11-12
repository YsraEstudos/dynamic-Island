package com.example.focusapp.guard

/**
 * Regras puras do guardião de proteção (sem Android), para serem testáveis no `testDebugUnitTest`.
 *
 * O [com.example.focusapp.service.GuardWatchdogService] usa estas decisões para saber:
 * - com que frequência verificar (10 s com bloqueio ativo, 60 s ocioso);
 * - quando repetir um alerta (sem spammar a cada tick);
 * - quando desligar/restaurar a depuração USB (só durante um bloqueio ativo);
 * - qual o nível de bloqueio ativo (0 = nada, 1 = seletivo/adulto, 2 = pomodoro/horário).
 */
internal data class ProtectionSnapshot(
    /** A proteção deve estar ativa agora? (modo seletivo ligado ou algum bloqueio gravado). */
    val protectionExpected: Boolean,
    /** Algum bloqueio por tempo está valendo agora (adulto, pomodoro ou horário do dia). */
    val blockActive: Boolean,
    /** 0 = nada; 1 = seletivo/adulto; 2 = pomodoro/horário (overlay de tela cheia). */
    val blockLevel: Int,
    /** Fim do bloqueio mais longo, em wall-clock (epoch ms), ou null. */
    val blockEndMs: Long?,
    val selective: Boolean,
    val adultActive: Boolean,
    val pomodoroActive: Boolean,
    val dailyActive: Boolean
) {
    companion object {
        val NONE = ProtectionSnapshot(
            protectionExpected = false,
            blockActive = false,
            blockLevel = 0,
            blockEndMs = null,
            selective = false,
            adultActive = false,
            pomodoroActive = false,
            dailyActive = false
        )
    }
}

internal object GuardPolicy {

    /** Intervalo do watchdog com um bloqueio ativo: religar rápido importa. */
    const val TICK_PROTECTED_MS = 10_000L

    /** Intervalo ocioso: manter a acessibilidade viva sem gastar bateria. */
    const val TICK_IDLE_MS = 60_000L

    /** Heartbeat de estado para o PC enquanto houver proteção esperada. */
    const val HEARTBEAT_INTERVAL_MS = 60_000L

    /** Espaçamento mínimo entre dois alertas de falha idênticos (notificação é mutável pelo usuário). */
    const val FAILURE_NOTIFY_INTERVAL_MS = 10 * 60_000L

    /** Espaçamento entre alertas de "perfil sem proteção". */
    const val PROFILE_NOTIFY_INTERVAL_MS = 30 * 60_000L

    /** Espaçamento entre avisos de "relógio alterado durante bloqueio". */
    const val CLOCK_WARN_INTERVAL_MS = 5 * 60_000L

    /** Espaçamento entre tentativas de acordar o guardião (Fase 2). */
    const val GUARDIAN_CHECK_INTERVAL_MS = 60_000L

    fun tickIntervalMs(blockActive: Boolean): Long = if (blockActive) TICK_PROTECTED_MS else TICK_IDLE_MS

    fun shouldNotifyFailure(lastNotifiedAtMs: Long, nowMs: Long): Boolean =
        lastNotifiedAtMs <= 0L || nowMs - lastNotifiedAtMs >= FAILURE_NOTIFY_INTERVAL_MS

    fun shouldWarnProfiles(lastWarnedAtMs: Long, nowMs: Long): Boolean =
        lastWarnedAtMs <= 0L || nowMs - lastWarnedAtMs >= PROFILE_NOTIFY_INTERVAL_MS

    fun shouldWarnClock(lastWarnedAtMs: Long, nowMs: Long): Boolean =
        lastWarnedAtMs <= 0L || nowMs - lastWarnedAtMs >= CLOCK_WARN_INTERVAL_MS

    fun shouldReportHeartbeat(lastReportedAtMs: Long, nowMs: Long): Boolean =
        lastReportedAtMs <= 0L || nowMs - lastReportedAtMs >= HEARTBEAT_INTERVAL_MS

    fun shouldCheckGuardian(lastCheckAtMs: Long, nowMs: Long): Boolean =
        lastCheckAtMs <= 0L || nowMs - lastCheckAtMs >= GUARDIAN_CHECK_INTERVAL_MS

    /** LEGADO (não usado): o app não altera mais a depuração USB. Mantido só para leitura histórica. */
    fun shouldDisableAdb(blockActive: Boolean, adbEnabled: Boolean): Boolean = blockActive && adbEnabled

    /** LEGADO (não usado): restauração do anti-ADB removido em 2026-11-11 a pedido do dono. */
    fun shouldRestoreAdb(autoDisabled: Boolean, blockActive: Boolean): Boolean =
        autoDisabled && !blockActive

    fun protectionLevel(
        selective: Boolean,
        adultActive: Boolean,
        pomodoroActive: Boolean,
        dailyActive: Boolean
    ): Int = when {
        pomodoroActive || dailyActive -> 2
        selective || adultActive -> 1
        else -> 0
    }

    fun isProtectionExpected(
        selective: Boolean,
        adultActive: Boolean,
        pomodoroActive: Boolean,
        dailyActive: Boolean
    ): Boolean = selective || adultActive || pomodoroActive || dailyActive

    /**
     * Multiusuário/perfil de trabalho: a acessibilidade é por usuário, então qualquer perfil a mais
     * significa um espaço onde o bloqueio não existe. Não dá para impedir sem Device Owner; o
     * watchdog torna o risco visível (notificação + ack).
     */
    fun isProfileUnprotected(userCount: Int, profileCount: Int): Boolean =
        userCount > 1 || profileCount > 1

    /** Maior fim entre os bloqueios ativos (epoch ms), para o ack dizer até quando vale a proteção. */
    fun latestEndMs(vararg ends: Long?): Long? =
        ends.asSequence().filterNotNull().filter { it > 0L }.maxOrNull()

    /**
     * Backoff simples para novas tentativas de reparo quando a acessibilidade não volta:
     * 10 s, 20 s, 40 s, 60 s (teto). O próximo tick continua sendo avaliado no intervalo normal.
     */
    fun repairBackoffMs(failures: Int): Long {
        val safe = failures.coerceIn(0, 3)
        return (TICK_PROTECTED_MS shl safe).coerceAtMost(4 * TICK_PROTECTED_MS)
    }
}

