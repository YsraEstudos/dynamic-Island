package com.example.focusapp.schedule

import android.content.Context
import android.content.SharedPreferences
import android.os.SystemClock
import android.provider.Settings
import org.json.JSONObject
import java.time.LocalDate
import java.time.LocalDateTime
import java.util.UUID

/** Each date is its own durable record. No midnight cleanup is required for correctness. */
internal class DailyBlockStore(
    context: Context,
    private val now: () -> LocalDateTime = { LocalDateTime.now() },
    private val nowMs: () -> Long = { System.currentTimeMillis() },
    private val nowElapsed: () -> Long = { SystemClock.elapsedRealtime() }
) {
    private val appContext: Context = context.applicationContext
    val preferences: SharedPreferences = appContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun load(date: LocalDate = now().toLocalDate()): DailyBlockRecord? =
        preferences.getString(date.toString(), null)?.let { raw ->
            runCatching {
                val json = JSONObject(raw)
                DailyBlockRecord(json.getString("id"), date, json.getInt("start"), json.getInt("end"), json.getBoolean("released"))
            }.getOrNull()
        }

    fun saveToday(start: Int, end: Int): Boolean = synchronized(LOCK) {
        val currentTime = now()
        if (!DailyBlockPolicy.canSchedule(start, end, currentTime) ||
            DailyBlockPolicy.shouldBlock(load(), currentTime)) return@synchronized false
        val record = DailyBlockRecord(UUID.randomUUID().toString(), currentTime.toLocalDate(), start, end)
        if (!writeRecord(record.date, encode(record))) return@synchronized false
        val editor = preferences.edit()
        // Retain yesterday for explicit repetition, discard older records without affecting current state.
        preferences.all.keys.forEach { key ->
            val date = runCatching { LocalDate.parse(key) }.getOrNull()
            if (date != null && date.isBefore(record.date.minusDays(1))) editor.remove(key)
        }
        editor.apply()
        true
    }

    fun release(expected: DailyBlockRecord, phrase: String): Boolean = synchronized(LOCK) {
        val currentTime = now()
        val current = load(currentTime.toLocalDate())
        if (current != expected || !DailyBlockPolicy.shouldBlock(current, currentTime) ||
            !DailyBlockPolicy.correctPhrase(phrase)) return@synchronized false
        writeRecord(expected.date, encode(expected.copy(released = true)))
    }

    fun cancelToday(): Boolean = synchronized(LOCK) {
        val currentTime = now()
        if (DailyBlockPolicy.shouldBlock(load(), currentTime)) return@synchronized false
        writeRecord(currentTime.toLocalDate(), null)
    }

    fun loadPomodoro(): PomodoroBlockRecord? =
        preferences.getString(POMODORO_KEY, null)?.let { raw ->
            runCatching {
                val json = JSONObject(raw)
                val endMono = json.optLong("endMono", 0L)
                val startMono = json.optLong("startMono", 0L)
                val anchorBoot = json.optInt("boot", -1)
                val anchorAt = json.optLong("anchorAt", 0L)
                val monoValid = monoAnchorUsable(anchorBoot, anchorAt)
                PomodoroBlockRecord(
                    id = json.getString("id"),
                    startMs = json.getLong("start"),
                    endMs = json.getLong("end"),
                    released = json.getBoolean("released"),
                    endElapsedRealtime = if (monoValid) endMono else 0L,
                    startElapsedRealtime = if (monoValid) startMono else 0L,
                    anchorBootCount = anchorBoot,
                    anchoredAtElapsedRealtime = anchorAt
                )
            }.getOrNull()
        }

    /**
     * Grava o bloqueio vindo do PC (ver [DailyBlockPolicy.acceptPomodoro]) e a âncora monotônica.
     * False = ignorado ou falha ao gravar.
     */
    fun savePomodoro(id: String, endMs: Long, durationMs: Long): Boolean = synchronized(LOCK) {
        val elapsed = nowElapsed()
        val accepted = DailyBlockPolicy.acceptPomodoro(loadPomodoro(), id, endMs, durationMs, nowMs(), elapsed)
            ?: return@synchronized false
        val stored = accepted.copy(
            anchorBootCount = bootCount(),
            anchoredAtElapsedRealtime = elapsed
        )
        writeKey(POMODORO_KEY, encodePomodoro(stored))
    }

    /**
     * Reancora o FIM de parede do pomodoro depois de um salto de relógio, mantendo a âncora monotônica.
     * Usado pelo [DailyBlockController] quando detecta `ACTION_TIME_CHANGED` durante bloqueio (C-01).
     */
    fun reanchorPomodoro(expected: PomodoroBlockRecord, newEndWallMs: Long): Boolean = synchronized(LOCK) {
        if (newEndWallMs <= expected.startMs) return@synchronized false
        writeKey(POMODORO_KEY, encodePomodoro(expected.copy(endMs = newEndWallMs)))
    }

    /** Fim do bloqueio adulto (epoch ms), ou null se nunca foi gravado. Não há chave de liberação por frase. */
    fun loadAdultEnd(): Long? = preferences.getString(ADULT_KEY, null)?.toLongOrNull()

    /**
     * Fim do bloqueio adulto em elapsedRealtime (0 = sem âncora válida). Vale para o bloqueio que foi
     * gravado por esta versão enquanto o aparelho não reiniciou (C-01).
     */
    fun loadAdultMonoEnd(): Long {
        val endMono = preferences.getLong(ADULT_MONO_KEY, 0L)
        if (endMono <= 0L) return 0L
        val anchorBoot = preferences.getInt(ADULT_BOOT_KEY, -1)
        val anchorAt = preferences.getLong(ADULT_ANCHOR_AT_KEY, 0L)
        return if (monoAnchorUsable(anchorBoot, anchorAt)) endMono else 0L
    }

    /** Reancora o fim de parede do bloqueio adulto (a âncora monotônica continua mandando). */
    fun reanchorAdult(newEndWallMs: Long): Boolean = synchronized(LOCK) {
        writeKey(ADULT_KEY, newEndWallMs.toString())
    }

    /** Grava o bloqueio adulto vindo do PC (ver [DailyBlockPolicy.acceptAdultBlock]). False = ignorado ou falha. */
    fun saveAdult(adultEndMs: Long): Boolean = synchronized(LOCK) {
        val end = DailyBlockPolicy.acceptAdultBlock(loadAdultEnd(), adultEndMs, nowMs()) ?: return@synchronized false
        if (!writeKey(ADULT_KEY, end.toString())) return@synchronized false
        val elapsed = nowElapsed()
        preferences.edit()
            .putLong(ADULT_MONO_KEY, elapsed + (end - nowMs()))
            .putInt(ADULT_BOOT_KEY, bootCount())
            .putLong(ADULT_ANCHOR_AT_KEY, elapsed)
            .commit()
        true
    }

    /**
     * Modo seletivo: Twitter/X e conteúdo adulto sempre, sem Instagram/Reddit e sem o overlay do Pomodoro
     * Angry do PC. Ligado por padrão; desligá-lo devolve o comportamento antigo, por nível de segurança.
     */
    fun loadSelective(): Boolean = preferences.getBoolean(SELECTIVE_KEY, true)

    fun saveSelective(enabled: Boolean): Boolean =
        preferences.edit().putBoolean(SELECTIVE_KEY, enabled).commit()

    fun releasePomodoro(expected: PomodoroBlockRecord, phrase: String): Boolean = synchronized(LOCK) {
        val current = loadPomodoro()
        if (current != expected || !DailyBlockPolicy.shouldBlockAt(current, nowMs(), nowElapsed()) ||
            !DailyBlockPolicy.correctPhrase(phrase)) return@synchronized false
        writeKey(POMODORO_KEY, encodePomodoro(expected.copy(released = true)))
    }

    /** Token FCM deste aparelho, para colar nas Settings da ilha. Só leitura/escrita local. */
    var pushToken: String?
        get() = preferences.getString(TOKEN_KEY, null)
        set(value) { preferences.edit().apply { if (value == null) remove(TOKEN_KEY) else putString(TOKEN_KEY, value) }.apply() }

    private fun writeRecord(date: LocalDate, value: String?): Boolean = writeKey(date.toString(), value)

    private fun writeKey(key: String, value: String?): Boolean {
        val previous = preferences.getString(key, null)
        fun write(text: String?): Boolean {
            val editor = preferences.edit()
            if (text == null) editor.remove(key) else editor.putString(key, text)
            return editor.commit()
        }
        if (write(value)) return true
        // commit() updates memory even on disk failure. Restore memory before queued observers run.
        write(previous)
        return false
    }

    /** `boot_count`: elapsedRealtime zera no reboot, então a âncora só vale dentro do mesmo boot. */
    private fun bootCount(): Int = runCatching {
        Settings.Global.getInt(appContext.contentResolver, "boot_count", -1)
    }.getOrDefault(-1)

    /**
     * Valida a âncora monotônica: boot conhecido tem prioridade; sem ele, exige que o relógio monotônico
     * atual não tenha "voltado" para antes da gravação (que é o que acontece após reboot).
     */
    private fun monoAnchorUsable(anchorBoot: Int, anchorAtElapsed: Long): Boolean {
        if (anchorAtElapsed <= 0L) return false
        val currentBoot = bootCount()
        if (anchorBoot >= 0 && currentBoot >= 0) return anchorBoot == currentBoot
        return nowElapsed() >= anchorAtElapsed
    }

    companion object {
        const val PREFS = "focus_daily_block"
        // Chaves que não parseiam como LocalDate: saveToday() nunca as apaga na limpeza de dias antigos.
        private const val POMODORO_KEY = "pomodoro"
        // Fim do bloqueio adulto em epoch ms. Não parseia como LocalDate, então a limpeza de dias antigos não o apaga.
        const val ADULT_KEY = "adult_end_ms"
        // Âncora monotônica do bloqueio adulto (C-01) e do boot em que foi gravada.
        private const val ADULT_MONO_KEY = "adult_end_elapsed_ms"
        private const val ADULT_BOOT_KEY = "adult_anchor_boot"
        private const val ADULT_ANCHOR_AT_KEY = "adult_anchor_at_elapsed"
        // Modo seletivo (ver FocusBlockRules.shouldBlockApp). Bool também não parseia como LocalDate.
        const val SELECTIVE_KEY = "selective_block"
        private const val TOKEN_KEY = "push_token"
        private val LOCK = Any()
        fun encodePomodoro(record: PomodoroBlockRecord): String = JSONObject()
            .put("id", record.id).put("start", record.startMs).put("end", record.endMs)
            .put("released", record.released)
            .put("startMono", record.startElapsedRealtime)
            .put("endMono", record.endElapsedRealtime)
            .put("boot", record.anchorBootCount)
            .put("anchorAt", record.anchoredAtElapsedRealtime)
            .toString()
        fun encode(record: DailyBlockRecord): String = JSONObject()
            .put("id", record.id).put("start", record.startMinute).put("end", record.endMinute)
            .put("released", record.released).toString()
    }
}
