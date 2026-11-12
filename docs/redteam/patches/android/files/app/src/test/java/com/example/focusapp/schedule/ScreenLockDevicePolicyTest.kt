package com.example.focusapp.schedule

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Regra de produto: bloqueio de tela só no S23 Ultra (SM-S918*), nunca no moto g(20). */
class ScreenLockDevicePolicyTest {

    @Test
    fun s23UltraIsSupported() {
        assertTrue(ScreenLockDevicePolicy.isSupported("SM-S918B", "dm3q", "dm3qxxx"))
        assertTrue(ScreenLockDevicePolicy.isSupported("sm-s918w", "dm3q", "dm3qxxx"))
        assertTrue(ScreenLockDevicePolicy.isSupported(" SM-S918U1 ", null, null))
    }

    @Test
    fun motoG20AndOthersAreNeverSupported() {
        assertFalse(ScreenLockDevicePolicy.isSupported("moto g(20)", "java", "java_retail"))
        assertFalse(ScreenLockDevicePolicy.isSupported("moto_g_20_", "java_retail", "java"))
        assertFalse(ScreenLockDevicePolicy.isSupported("SM-S911B", "dm1q", "dm1qxxx")) // S23 base não é Ultra
        assertFalse(ScreenLockDevicePolicy.isSupported(null, null, null))
        assertFalse(ScreenLockDevicePolicy.isSupported("", "", ""))
    }
}
