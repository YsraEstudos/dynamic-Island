package com.example.focusapp.guardian

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.PowerManager
import android.provider.Settings

/** Checagens das permissões externas do guardião (as duas primeiras são concedidas por ADB uma vez). */
internal object GuardianPermissions {

    /**
     * Permission de assinatura definida pelo app principal. Se o guardião foi instalado ANTES do
     * principal, ela fica negada até reinstalar o guardião (`adb install -r`), e aí ele não consegue
     * consultar o provider nem chamar o ReviveService.
     */
    const val PROTECTION_CONTROL = "com.example.focusapp.permission.PROTECTION_CONTROL"

    fun hasProtectionControl(context: Context): Boolean =
        context.checkSelfPermission(PROTECTION_CONTROL) == PackageManager.PERMISSION_GRANTED

    fun hasWriteSecureSettings(context: Context): Boolean =
        context.checkSelfPermission(Manifest.permission.WRITE_SECURE_SETTINGS) == PackageManager.PERMISSION_GRANTED

    fun canDrawOverlays(context: Context): Boolean =
        Settings.canDrawOverlays(context)

    fun isIgnoringBatteryOptimizations(context: Context): Boolean = try {
        val powerManager = context.getSystemService(Context.POWER_SERVICE) as PowerManager
        powerManager.isIgnoringBatteryOptimizations(context.packageName)
    } catch (error: Exception) {
        false
    }
}
