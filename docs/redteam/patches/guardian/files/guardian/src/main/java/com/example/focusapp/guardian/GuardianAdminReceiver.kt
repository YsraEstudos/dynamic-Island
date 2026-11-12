package com.example.focusapp.guardian

import android.app.admin.DeviceAdminReceiver
import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Administrador de dispositivo do guardião (sem políticas). Não impede o `am force-stop` — nenhum app
 * comum impede — mas adiciona atrito para desinstalar/desativar o guardião pela UI do sistema e mantém
 * o pacote visível na lista de administradores. Ativado uma vez por
 * `adb shell dpm set-active-admin com.example.focusapp.guardian/.GuardianAdminReceiver`
 * ou pela tela de status.
 */
class GuardianAdminReceiver : DeviceAdminReceiver() {

    override fun onEnabled(context: Context, intent: Intent) {
        super.onEnabled(context, intent)
        Log.i(TAG, "administrador de dispositivo ativado")
        GuardianService.start(context)
    }

    override fun onDisableRequested(context: Context, intent: Intent): CharSequence =
        context.getString(R.string.guardian_admin_disable_warning)

    override fun onDisabled(context: Context, intent: Intent) {
        super.onDisabled(context, intent)
        Log.w(TAG, "administrador de dispositivo desativado")
    }

    private companion object {
        const val TAG = "GuardianAdmin"
    }
}
