package com.example.focusapp.guard

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Decisões puras do watchdog (sem Android): intervalos, throttle, nível, ADB e perfis. */
class GuardPolicyTest {

    @Test
    fun tickIsFasterWhileABlockIsActive() {
        assertEquals(10_000L, GuardPolicy.tickIntervalMs(blockActive = true))
        assertEquals(60_000L, GuardPolicy.tickIntervalMs(blockActive = false))
    }

    @Test
    fun failureNotificationIsThrottledToAvoidSpam() {
        val now = 1_800_000_000_000L
        assertTrue(GuardPolicy.shouldNotifyFailure(0L, now))
        assertFalse(GuardPolicy.shouldNotifyFailure(now - 60_000L, now))
        assertTrue(GuardPolicy.shouldNotifyFailure(now - 10 * 60_000L, now))
    }

    @Test
    fun heartbeatGoesOutEveryMinute() {
        val now = 1_800_000_000_000L
        assertTrue(GuardPolicy.shouldReportHeartbeat(0L, now))
        assertFalse(GuardPolicy.shouldReportHeartbeat(now - 59_000L, now))
        assertTrue(GuardPolicy.shouldReportHeartbeat(now - 60_000L, now))
    }

    @Test
    fun profilesAndClockWarningsAreThrottled() {
        val now = 1_800_000_000_000L
        assertTrue(GuardPolicy.shouldWarnProfiles(0L, now))
        assertFalse(GuardPolicy.shouldWarnProfiles(now - 60_000L, now))
        assertTrue(GuardPolicy.shouldWarnClock(0L, now))
        assertFalse(GuardPolicy.shouldWarnClock(now - 60_000L, now))
        assertTrue(GuardPolicy.shouldCheckGuardian(0L, now))
        assertFalse(GuardPolicy.shouldCheckGuardian(now - 30_000L, now))
    }

    @Test
    fun adbIsOnlyDisabledDuringABlockAndRestoredAfterwards() {
        assertTrue(GuardPolicy.shouldDisableAdb(blockActive = true, adbEnabled = true))
        assertFalse(GuardPolicy.shouldDisableAdb(blockActive = true, adbEnabled = false))
        assertFalse(GuardPolicy.shouldDisableAdb(blockActive = false, adbEnabled = true))

        assertTrue(GuardPolicy.shouldRestoreAdb(autoDisabled = true, blockActive = false))
        assertFalse(GuardPolicy.shouldRestoreAdb(autoDisabled = true, blockActive = true))
        assertFalse(GuardPolicy.shouldRestoreAdb(autoDisabled = false, blockActive = false))
    }

    @Test
    fun protectionLevelPrioritizesFullScreenBlocks() {
        assertEquals(0, GuardPolicy.protectionLevel(false, false, false, false))
        assertEquals(1, GuardPolicy.protectionLevel(selective = true, adultActive = false, pomodoroActive = false, dailyActive = false))
        assertEquals(1, GuardPolicy.protectionLevel(selective = false, adultActive = true, pomodoroActive = false, dailyActive = false))
        assertEquals(2, GuardPolicy.protectionLevel(selective = false, adultActive = false, pomodoroActive = true, dailyActive = false))
        assertEquals(2, GuardPolicy.protectionLevel(selective = true, adultActive = false, pomodoroActive = false, dailyActive = true))
    }

    @Test
    fun protectionIsExpectedInSelectiveModeEvenWithoutATimedBlock() {
        assertTrue(GuardPolicy.isProtectionExpected(selective = true, adultActive = false, pomodoroActive = false, dailyActive = false))
        assertFalse(GuardPolicy.isProtectionExpected(selective = false, adultActive = false, pomodoroActive = false, dailyActive = false))
        assertTrue(GuardPolicy.isProtectionExpected(selective = false, adultActive = true, pomodoroActive = false, dailyActive = false))
    }

    @Test
    fun latestEndPicksTheLongestActiveBlockAndIgnoresInvalidValues() {
        val end = GuardPolicy.latestEndMs(null, 0L, -5L, 30L, 20L)
        assertEquals(30L, end)
        assertNull(GuardPolicy.latestEndMs(null, 0L, -1L))
    }

    @Test
    fun extraUserOrWorkProfileMeansAnUnprotectedSpace() {
        assertFalse(GuardPolicy.isProfileUnprotected(userCount = 1, profileCount = 1))
        assertTrue(GuardPolicy.isProfileUnprotected(userCount = 2, profileCount = 1))
        assertTrue(GuardPolicy.isProfileUnprotected(userCount = 1, profileCount = 2))
    }

    @Test
    fun repairBackoffGrowsAndCapsAtFourTicks() {
        assertEquals(10_000L, GuardPolicy.repairBackoffMs(0))
        assertEquals(20_000L, GuardPolicy.repairBackoffMs(1))
        assertEquals(40_000L, GuardPolicy.repairBackoffMs(2))
        assertEquals(40_000L, GuardPolicy.repairBackoffMs(9))
    }

    @Test
    fun emptySnapshotMeansNothingToProtect() {
        val snapshot = ProtectionSnapshot.NONE
        assertFalse(snapshot.protectionExpected)
        assertFalse(snapshot.blockActive)
        assertEquals(0, snapshot.blockLevel)
        assertNull(snapshot.blockEndMs)
    }
}
