package com.example.focusapp.service

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Regras novas da v1.7: navegadores ampliados/descobertos (B-01), varredura conservadora de domínio
 * adulto em qualquer app (B-02) e anti-tamper das telas de Acessibilidade/Admin/Detalhes do app (A-02).
 */
class FocusBlockRulesHardeningTest {

    private val ownPackage = "com.example.focusapp"
    private val ownLabels = listOf("Foco & Bem-Estar", "Serviço de Foco e Proteção")

    @After
    fun resetDiscoveredBrowsers() {
        FocusBlockRules.updateDiscoveredBrowsers(emptySet())
    }

    @Test
    fun expandedFixedBrowserListCoversModernBrowsersWithAddressBarIds() {
        listOf(
            "org.mozilla.fenix",
            "org.mozilla.firefox_beta",
            "com.chrome.beta",
            "com.chrome.canary",
            "com.vivaldi.browser",
            "com.duckduckgo.mobile.android",
            "com.ecosia.android",
            "com.kiwibrowser.browser",
            "com.yandex.browser",
            "com.UCMobile.intl",
            "com.opera.gx",
            "org.bromite.bromite"
        ).forEach { packageName ->
            assertTrue(packageName, FocusBlockRules.isBrowser(packageName))
            assertTrue(packageName, FocusBlockRules.addressBarViewIds(packageName).isNotEmpty())
        }
    }

    @Test
    fun discoveredBrowserIsInspectedEvenWithoutKnownAddressBarIds() {
        FocusBlockRules.updateDiscoveredBrowsers(setOf("com.example.newbrowser"))
        assertTrue(FocusBlockRules.isBrowser("com.example.newbrowser"))
        assertTrue(FocusBlockRules.addressBarViewIds("com.example.newbrowser").isEmpty())
        assertTrue(FocusBlockRules.runtimeMonitoredPackages().contains("com.example.newbrowser"))
        // A descoberta não transforma qualquer outro pacote em navegador.
        assertFalse(FocusBlockRules.isBrowser("com.example.notabrowser"))
    }

    @Test
    fun selectiveModeOrAdultWindowMakesTheServiceListenToAllApps() {
        assertTrue(FocusBlockRules.shouldScanAllPackages(selective = true, adultWindowActive = false))
        assertTrue(FocusBlockRules.shouldScanAllPackages(selective = false, adultWindowActive = true))
        assertFalse(FocusBlockRules.shouldScanAllPackages(selective = false, adultWindowActive = false))
    }

    @Test
    fun genericScanSkipsOwnAppSettingsInstallersAndPhoneCalls() {
        assertFalse(FocusBlockRules.shouldScanAnyAppPackage(ownPackage, ownPackage))
        listOf(
            "android",
            "com.android.systemui",
            "com.android.settings",
            "com.google.android.packageinstaller",
            "com.android.permissioncontroller",
            "com.android.dialer",
            "com.google.android.dialer",
            "com.android.incallui",
            "com.android.server.telecom"
        ).forEach { assertFalse(it, FocusBlockRules.shouldScanAnyAppPackage(it, ownPackage)) }
        listOf("org.telegram.messenger", "com.discord", "com.facebook.katana", "com.example.chat")
            .forEach { assertTrue(it, FocusBlockRules.shouldScanAnyAppPackage(it, ownPackage)) }
    }

    @Test
    fun adultDomainIsDetectedInAnyAppOnlyByHost() {
        assertTrue(FocusBlockRules.containsAdultDomain("https://xvideos.com/watch?v=1"))
        assertTrue(FocusBlockRules.containsAdultDomain("m.pornhub.com/video"))
        assertTrue(FocusBlockRules.containsAdultDomain("acesse xvideos.com agora"))
        assertTrue(FocusBlockRules.containsAdultDomain("www.xnxx.com"))
        assertEquals("www.xvideos.com", FocusBlockRules.firstAdultDomainIn("link: https://www.xvideos.com/x"))
    }

    @Test
    fun adultKeywordAloneDoesNotBlockAGenericApp() {
        // Regra conservadora: em app que não é navegador, só URL com HOST adulto — nunca palavra solta.
        assertFalse(FocusBlockRules.containsAdultDomain("pornografia é um problema de saúde"))
        assertFalse(FocusBlockRules.containsAdultDomain("assunto adulto responsável"))
        assertFalse(FocusBlockRules.containsAdultDomain("https://notxvideos.com"))
        assertFalse(FocusBlockRules.containsAdultDomain("https://reddit.com/r/porn"))
    }

    @Test
    fun tamperScreenIsDetectedByAccessibilityActivityClass() {
        assertTrue(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.Settings\$AccessibilitySettingsActivity",
                emptyList(),
                ownLabels,
                ownPackage
            )
        )
        assertTrue(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.accessibility.AccessibilitySettings",
                emptyList(),
                ownLabels,
                ownPackage
            )
        )
    }

    @Test
    fun tamperScreenIsDetectedByServiceLabelWhichDoesNotContainTheAppName() {
        // A-02: a tela de Acessibilidade mostra "Serviço de Foco e Proteção", não o nome do app.
        assertTrue(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.SubSettings",
                listOf("Serviço de Foco e Proteção"),
                ownLabels,
                ownPackage
            )
        )
    }

    @Test
    fun tamperScreenIsDetectedByDeviceAdminTitles() {
        listOf(
            "Apps de administração do dispositivo",
            "Administradores do dispositivo",
            "Device administrators"
        ).forEach { title ->
            assertTrue(
                title,
                FocusBlockRules.isProtectionTamperScreen(
                    "com.android.settings",
                    "com.android.settings.SubSettings",
                    listOf(title),
                    ownLabels,
                    ownPackage
                )
            )
        }
    }

    @Test
    fun ownAppInfoScreenIsBlockedButOtherAppsInfoIsNot() {
        assertTrue(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.applications.InstalledAppDetails",
                listOf("Foco & Bem-Estar", "Forçar parada", "Limpar dados"),
                ownLabels,
                ownPackage
            )
        )
        assertFalse(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.applications.InstalledAppDetails",
                listOf("Outro aplicativo", "Forçar parada", "Limpar dados"),
                ownLabels,
                ownPackage
            )
        )
    }

    @Test
    fun unrelatedSettingsScreensAreNotBlocked() {
        listOf(
            "Wi-Fi" to "com.android.settings.Settings\$WifiSettingsActivity",
            "Redes e internet" to "com.android.settings.SubSettings",
            "Bateria" to "com.android.settings.Settings\$BatterySettingsActivity"
        ).forEach { (title, windowClass) ->
            assertFalse(
                "$title / $windowClass",
                FocusBlockRules.isProtectionTamperScreen(
                    "com.android.settings",
                    windowClass,
                    listOf(title),
                    ownLabels,
                    ownPackage
                )
            )
        }
    }

    @Test
    fun nonSettingsPackagesAreNeverTamperScreens() {
        assertFalse(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.chrome",
                "com.android.chrome.Main",
                listOf("Serviço de Foco e Proteção"),
                ownLabels,
                ownPackage
            )
        )
    }

    @Test
    fun usageAccessScreenIsNotTamperEvenWithAppLabel() {
        // Regressão 2026-11-11: a tela de Acesso aos dados de uso mostra o nome do app e usa
        // className genérica (SubSettings); não pode ser confundida com tamper.
        listOf("Acesso aos dados de uso", "Usage access", "Acesso especial", "Special access").forEach { title ->
            assertFalse(
                title,
                FocusBlockRules.isProtectionTamperScreen(
                    "com.android.settings",
                    "com.android.settings.SubSettings",
                    listOf("Foco & Bem-Estar", title, "Permitir acesso aos dados de uso"),
                    ownLabels,
                    ownPackage
                )
            )
        }
    }

    @Test
    fun batteryOptimizationScreenIsNotTamperEvenWithAppLabel() {
        assertFalse(
            FocusBlockRules.isProtectionTamperScreen(
                "com.android.settings",
                "com.android.settings.SubSettings",
                listOf("Foco & Bem-Estar", "Otimização da bateria", "Battery optimization"),
                ownLabels,
                ownPackage
            )
        )
    }
}
