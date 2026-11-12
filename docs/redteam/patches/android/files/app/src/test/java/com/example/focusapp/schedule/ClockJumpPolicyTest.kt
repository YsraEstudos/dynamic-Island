package com.example.focusapp.schedule

import java.time.LocalDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * C-01: adiantar data/hora não pode encerrar os bloqueios do PC (âncora em `elapsedRealtime`) nem o
 * bloqueio do dia que já estava valendo (âncora monotônica do record). Relógios de parede continuam
 * funcionando para registros antigos (sem âncora).
 */
class ClockJumpPolicyTest {

    private val minute = 60_000L
    private val wallNow = 1_800_000_000_000L
    private val elapsedNow = 5_000_000L

    @Test
    fun forwardWallClockJumpDoesNotEndAnAnchoredPomodoro() {
        val record = PomodoroBlockRecord(
            id = "p",
            startMs = wallNow,
            endMs = wallNow + 25 * minute,
            startElapsedRealtime = elapsedNow,
            endElapsedRealtime = elapsedNow + 25 * minute
        )
        val jumpedWall = wallNow + 2 * 60 * minute
        assertTrue(DailyBlockPolicy.shouldBlockAt(record, jumpedWall, elapsedNow + minute))
        assertEquals(24 * minute, record.remainingMs(jumpedWall, elapsedNow + minute))
        assertFalse(DailyBlockPolicy.shouldBlockAt(record, jumpedWall, elapsedNow + 25 * minute))
        assertFalse(DailyBlockPolicy.shouldBlockAt(record.copy(released = true), jumpedWall, elapsedNow + minute))
    }

    @Test
    fun legacyPomodoroWithoutAnchorStillUsesWallClock() {
        val record = PomodoroBlockRecord("legacy", wallNow, wallNow + 25 * minute)
        assertTrue(DailyBlockPolicy.shouldBlockAt(record, wallNow + minute))
        assertFalse(DailyBlockPolicy.shouldBlockAt(record, wallNow + 25 * minute))
        assertEquals(25 * minute, record.remainingMs(wallNow, elapsedNow))
    }

    @Test
    fun nextBoundaryUsesTheMonotonicClockWhenAvailable() {
        val record = PomodoroBlockRecord(
            id = "p",
            startMs = wallNow,
            endMs = wallNow + 25 * minute,
            startElapsedRealtime = elapsedNow,
            endElapsedRealtime = elapsedNow + 25 * minute
        )
        // 1 minuto depois, faltam 24 min reais → o limite no relógio de parede saltado é
        // (agora saltado) + 24 min. O importante é que o relógio de parede original seja ignorado.
        assertEquals(
            wallNow + 144 * minute,
            DailyBlockPolicy.nextBoundaryMs(record, wallNow + 120 * minute, elapsedNow + minute)
        )
        assertEquals(null, DailyBlockPolicy.nextBoundaryMs(record, wallNow, elapsedNow + 26 * minute))
    }

    @Test
    fun acceptPomodoroStoresTheMonotonicAnchor() {
        val record = DailyBlockPolicy.acceptPomodoro(
            existing = null,
            id = "p",
            endMs = wallNow + 25 * minute,
            durationMs = 25 * minute,
            nowMs = wallNow,
            nowElapsed = elapsedNow
        )!!
        assertEquals(elapsedNow, record.startElapsedRealtime)
        assertEquals(elapsedNow + 25 * minute, record.endElapsedRealtime)
        assertTrue(record.isActiveAt(wallNow + 60 * minute, elapsedNow + minute))
    }

    @Test
    fun adultBlockWithMonotonicAnchorIgnoresForwardWallClockJump() {
        val jumpedWall = wallNow + 2 * 60 * minute
        // Relógio de parede já passou do fim, mas a âncora monotônica ainda está no prazo.
        assertTrue(
            DailyBlockPolicy.isAdultBlockActive(
                wallNow + 10 * minute,
                jumpedWall,
                endMonoMs = elapsedNow + 30 * minute,
                nowElapsed = elapsedNow + minute
            )
        )
        assertFalse(
            DailyBlockPolicy.isAdultBlockActive(
                wallNow + 10 * minute,
                jumpedWall,
                endMonoMs = elapsedNow + 30 * minute,
                nowElapsed = elapsedNow + 31 * minute
            )
        )
    }

    @Test
    fun legacyAdultBlockStillUsesWallClock() {
        assertTrue(DailyBlockPolicy.isAdultBlockActive(wallNow + 10 * minute, wallNow + minute))
        assertFalse(DailyBlockPolicy.isAdultBlockActive(wallNow + 10 * minute, wallNow + 10 * minute))
    }

    @Test
    fun dailyBlockAnchorKeepsTheBlockAfterTheWallClockIsAdvanced() {
        val today = LocalDate.of(2026, 10, 6)
        val record = DailyBlockRecord("d", today, 420, 720)
        val justAfterWallEnd = today.atTime(12, 5)
        assertFalse(DailyBlockPolicy.shouldBlock(record, justAfterWallEnd))

        val anchorEnd = elapsedNow + 5 * minute
        assertTrue(DailyBlockPolicy.shouldBlockAnchored(record, record.id, anchorEnd, elapsedNow + minute))
        assertFalse(DailyBlockPolicy.shouldBlockAnchored(record, record.id, anchorEnd, anchorEnd))
        assertFalse(DailyBlockPolicy.shouldBlockAnchored(record, "outro-id", anchorEnd, elapsedNow + minute))
        assertFalse(
            DailyBlockPolicy.shouldBlockAnchored(
                record.copy(released = true),
                record.id,
                anchorEnd,
                elapsedNow + minute
            )
        )
    }
}
