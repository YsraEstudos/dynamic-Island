package com.example.focusapp.schedule

import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

internal data class DailyBlockRecord(
    val id: String,
    val date: LocalDate,
    val startMinute: Int,
    val endMinute: Int,
    val released: Boolean = false
) {
    init {
        require(id.isNotBlank())
        require(startMinute in 0..1439 && endMinute in 1..1439 && startMinute < endMinute)
    }
    fun start(): LocalDateTime = date.atStartOfDay().plusMinutes(startMinute.toLong())
    fun end(): LocalDateTime = date.atStartOfDay().plusMinutes(endMinute.toLong())
}

/**
 * Bloqueio disparado pelo pomodoro Angry do PC. Usa instantes absolutos (epoch ms): não depende da data
 * nem do limite "antes da meia-noite" do [DailyBlockRecord], e convive com o horário manual do dia.
 *
 * A partir da v1.7 carrega também uma **âncora monotônica** (`SystemClock.elapsedRealtime`, que não é
 * afetado por mudar data/hora/fuso). Quando a âncora é válida ela manda: adiantar o relógio não encerra
 * o bloqueio (C-01). Registros antigos (sem âncora) continuam funcionando por wall-clock.
 */
internal data class PomodoroBlockRecord(
    val id: String,
    val startMs: Long,
    val endMs: Long,
    val released: Boolean = false,
    /** Fim em elapsedRealtime; 0 = sem âncora (registro antigo ou inválido). */
    val endElapsedRealtime: Long = 0L,
    /** Início em elapsedRealtime; 0 = sem âncora. */
    val startElapsedRealtime: Long = 0L,
    /** Boot em que a âncora foi gravada (-1 = desconhecido); elapsedRealtime zera no reboot. */
    val anchorBootCount: Int = -1,
    /** elapsedRealtime no momento em que a âncora foi gravada (fallback sem boot_count). */
    val anchoredAtElapsedRealtime: Long = 0L
) {
    init {
        require(id.isNotBlank())
        require(endMs > startMs) // o teto de 120 min vale por mensagem; extensões podem passar dele
    }

    /** Âncora monotônica utilizável? Precisa de início e fim válidos e do relógio monotônico atual. */
    fun hasMonotonicAnchor(nowElapsed: Long): Boolean =
        nowElapsed > 0L && startElapsedRealtime > 0L && endElapsedRealtime > 0L

    /**
     * Milissegundos restantes. A âncora monotônica, quando existe, é a verdade: mexer no relógio de
     * parede não antecipa o fim. Sem âncora (registro antigo), usa o wall-clock como antes.
     */
    fun remainingMs(nowMs: Long, nowElapsed: Long = 0L): Long =
        if (endElapsedRealtime > 0L && nowElapsed > 0L) {
            (endElapsedRealtime - nowElapsed).coerceAtLeast(0L)
        } else {
            (endMs - nowMs).coerceAtLeast(0L)
        }

    /** Ativo agora? Com âncora, compara só no relógio monotônico (imune a mudança de data/hora). */
    fun isActiveAt(nowMs: Long, nowElapsed: Long = 0L): Boolean {
        if (released) return false
        if (hasMonotonicAnchor(nowElapsed)) {
            return nowElapsed >= startElapsedRealtime && nowElapsed < endElapsedRealtime
        }
        return nowMs >= startMs && nowMs < endMs
    }
}

internal object DailyBlockPolicy {
    /** Mesmo teto do PomodoroTimer do PC (120 min). */
    const val POMODORO_MAX_MS = 120 * 60_000L

    /** Teto do bloqueio de conteúdo adulto por mensagem: nem o PC consegue prender o celular além disso. */
    const val ADULT_MAX_MS = 4 * 60 * 60_000L

    // Lowercase text fits a small internal keyboard while preserving Portuguese accents.
    const val PHRASE = "eu escolhi reservar este horário para cuidar da minha atenção e do meu tempo. " +
        "ao continuar agora, reconheço que estou interrompendo um compromisso que fiz comigo. " +
        "vou usar o celular com consciência, lembrar do motivo da minha pausa e assumir a responsabilidade por esta escolha."

    fun shouldBlock(record: DailyBlockRecord?, now: LocalDateTime): Boolean =
        record != null && !record.released && record.date == now.toLocalDate() &&
            !now.isBefore(record.start()) && now.isBefore(record.end())

    /**
     * Extensão monotônica do bloqueio do dia (C-01): se o bloqueio estava valendo quando ganhou âncora,
     * adiantar o relógio não o encerra antes do fim ancorado. Vale só para o record com o mesmo id.
     */
    fun shouldBlockAnchored(
        record: DailyBlockRecord?,
        anchorId: String?,
        anchorEndElapsed: Long,
        nowElapsed: Long
    ): Boolean = record != null && !record.released && anchorId == record.id &&
        anchorEndElapsed > 0L && nowElapsed > 0L && nowElapsed < anchorEndElapsed

    fun canSchedule(start: Int, end: Int, now: LocalDateTime): Boolean =
        start in 0..1439 && end in 1..1439 && start < end &&
            now.isBefore(now.toLocalDate().atStartOfDay().plusMinutes(end.toLong()))

    fun repeatFor(record: DailyBlockRecord, date: LocalDate, id: String): DailyBlockRecord =
        DailyBlockRecord(id, date, record.startMinute, record.endMinute)

    fun correctPhrase(text: String): Boolean = text == PHRASE

    fun nextBoundary(record: DailyBlockRecord?, now: LocalDateTime): LocalDateTime? = when {
        record == null || record.released || record.date != now.toLocalDate() -> null
        now.isBefore(record.start()) -> record.start()
        now.isBefore(record.end()) -> record.end()
        else -> null
    }

    fun shouldBlockAt(record: PomodoroBlockRecord?, nowMs: Long, nowElapsed: Long = 0L): Boolean =
        record?.isActiveAt(nowMs, nowElapsed) == true

    /**
     * No modo seletivo o Pomodoro Angry do PC não trava a tela: o registro chega e é mostrado, mas o
     * overlay de tela cheia não aparece. O bloqueio de Twitter/X e conteúdo adulto continua valendo.
     */
    fun shouldShowPomodoroOverlay(
        selective: Boolean,
        record: PomodoroBlockRecord?,
        nowMs: Long,
        nowElapsed: Long = 0L
    ): Boolean = !selective && shouldBlockAt(record, nowMs, nowElapsed)

    /**
     * Próxima fronteira do pomodoro: início (se ainda não começou) ou fim. Com âncora monotônica o fim é
     * calculado pelo relógio monotônico, então a mudança de data/hora não reagenda o desbloqueio errado.
     */
    fun nextBoundaryMs(record: PomodoroBlockRecord?, nowMs: Long, nowElapsed: Long = 0L): Long? = when {
        record == null || record.released -> null
        record.endElapsedRealtime > 0L && nowElapsed > 0L -> {
            val remaining = record.endElapsedRealtime - nowElapsed
            if (remaining > 0L) nowMs + remaining else null
        }
        nowMs < record.startMs -> record.startMs
        nowMs < record.endMs -> record.endMs
        else -> null
    }

    /**
     * Decide o que gravar quando chega uma mensagem do PC; null = ignorar. O fim é o menor entre o enviado
     * e agora + duração (tolera relógios um pouco diferentes). Um bloqueio ativo nunca é encurtado nem
     * liberado por mensagem: um novo Angry só o estende, mantendo o início. Com [nowElapsed] > 0, grava
     * também a âncora monotônica (C-01).
     */
    fun acceptPomodoro(
        existing: PomodoroBlockRecord?,
        id: String,
        endMs: Long,
        durationMs: Long,
        nowMs: Long,
        nowElapsed: Long = 0L
    ): PomodoroBlockRecord? {
        if (id.isBlank() || durationMs <= 0 || durationMs > POMODORO_MAX_MS) return null
        if (existing?.id == id) return null
        val end = minOf(endMs, nowMs + durationMs)
        if (end <= nowMs) return null
        if (existing != null && shouldBlockAt(existing, nowMs, nowElapsed)) {
            if (end <= existing.endMs) return null
            val endMono = if (nowElapsed > 0L) nowElapsed + (end - nowMs) else 0L
            return existing.copy(id = id, endMs = end, endElapsedRealtime = endMono)
        }
        return PomodoroBlockRecord(
            id = id,
            startMs = nowMs,
            endMs = end,
            startElapsedRealtime = if (nowElapsed > 0L) nowElapsed else 0L,
            endElapsedRealtime = if (nowElapsed > 0L) nowElapsed + (end - nowMs) else 0L
        )
    }

    /**
     * Fim do bloqueio adulto a gravar, ou null = ignorar. Vale o menor entre o enviado e agora + 4 h, e nunca
     * encurta um bloqueio que já existe: o maior fim vence. Não é liberado pela frase (ver DailyBlockStore).
     */
    fun acceptAdultBlock(existingEndMs: Long?, adultEndMs: Long, nowMs: Long): Long? {
        val end = minOf(adultEndMs, nowMs + ADULT_MAX_MS)
        if (end <= nowMs) return null
        val merged = maxOf(existingEndMs ?: Long.MIN_VALUE, end)
        return if (merged == existingEndMs) null else merged
    }

    /**
     * Bloqueio adulto ativo? Com âncora monotônica, ela é a verdade (adiantar o relógio não encerra).
     * O fim de parede continua sendo o fallback dos registros gravados antes da v1.7.
     */
    fun isAdultBlockActive(
        endMs: Long?,
        nowMs: Long,
        endMonoMs: Long = 0L,
        nowElapsed: Long = 0L
    ): Boolean = if (endMonoMs > 0L && nowElapsed > 0L) {
        nowElapsed < endMonoMs
    } else {
        endMs != null && nowMs < endMs
    }

    fun formatMinute(minute: Int): String = "%02d:%02d".format(minute / 60, minute % 60)

    fun formatInstant(epochMs: Long, zone: ZoneId = ZoneId.systemDefault()): String {
        val time = Instant.ofEpochMilli(epochMs).atZone(zone).toLocalTime()
        return formatMinute(time.hour * 60 + time.minute)
    }
}
