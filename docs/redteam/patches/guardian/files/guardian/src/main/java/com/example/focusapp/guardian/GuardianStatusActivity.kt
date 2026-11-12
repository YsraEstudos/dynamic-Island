package com.example.focusapp.guardian

import android.Manifest
import android.app.Activity
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.util.Log
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast

/**
 * Tela mínima de status/setup do guardião (sem Compose e sem launcher). É o caminho sem terminal para
 * conceder sobreposição, bateria e administrador; a permissão WRITE_SECURE_SETTINGS só existe por ADB.
 *
 * Abrir por: `adb shell am start -n com.example.focusapp.guardian/.GuardianStatusActivity`
 * (também é o que limpa o estado "stopped" do pacote após a instalação).
 */
class GuardianStatusActivity : Activity() {

    private lateinit var statusView: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val density = resources.displayMetrics.density
        fun dp(value: Int): Int = (value * density).toInt()

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(20), dp(20), dp(20))
        }
        val title = TextView(this).apply {
            text = getString(R.string.app_name)
            textSize = 22f
        }
        statusView = TextView(this).apply {
            textSize = 13f
            setPadding(0, dp(12), 0, dp(16))
        }
        root.addView(title)
        root.addView(statusView)

        root.addView(actionButton("Iniciar/garantir guardião") {
            GuardianService.start(this)
            Toast.makeText(this, "Guardião solicitado…", Toast.LENGTH_SHORT).show()
            refreshStatus()
        })
        root.addView(actionButton("Permitir sobreposição (SYSTEM_ALERT_WINDOW)") {
            openSettings(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
        })
        root.addView(actionButton("Ignorar otimização de bateria") {
            openSettings(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
        })
        root.addView(actionButton("Ativar administrador do dispositivo") { requestDeviceAdmin() })
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            root.addView(actionButton("Permitir notificações") {
                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), REQUEST_NOTIFICATIONS)
            })
        }
        root.addView(actionButton("Abrir app principal (Foco & Bem-Estar)") {
            MainAppControl.launchMainActivity(this)
        })

        setContentView(ScrollView(this).apply { addView(root) })
        GuardianService.start(this)
        GuardianAlarmScheduler.schedule(this)
    }

    override fun onResume() {
        super.onResume()
        refreshStatus()
    }

    private fun actionButton(label: String, action: () -> Unit): Button =
        Button(this).apply {
            text = label
            setOnClickListener { action() }
        }

    private fun openSettings(intent: Intent) {
        try {
            startActivity(intent)
        } catch (error: Exception) {
            Log.w(TAG, "tela de ajuste indisponível: ${error.javaClass.simpleName}: ${error.message}")
            Toast.makeText(this, "Tela indisponível neste aparelho.", Toast.LENGTH_SHORT).show()
        }
    }

    private fun requestDeviceAdmin() {
        val intent = Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN).apply {
            putExtra(
                DevicePolicyManager.EXTRA_DEVICE_ADMIN,
                ComponentName(this@GuardianStatusActivity, GuardianAdminReceiver::class.java),
            )
            putExtra(
                DevicePolicyManager.EXTRA_ADD_EXPLANATION,
                "O administrador dificulta a desinstalação acidental do guardião. " +
                    "Ele não apaga dados nem controla o aparelho.",
            )
        }
        openSettings(intent)
    }

    private fun refreshStatus() {
        statusView.text = "Lendo estado…"
        Thread {
            val summary = buildStatus()
            runOnUiThread { statusView.text = summary }
        }.start()
    }

    private fun buildStatus(): String {
        val activity = this
        val state = ProtectionContract.query(activity)
        val a11yListed = AccessibilityRepair.isMainServiceListed(activity)
        val admin = try {
            val dpm = getSystemService(DevicePolicyManager::class.java)
            dpm.isAdminActive(ComponentName(activity, GuardianAdminReceiver::class.java))
        } catch (error: Exception) {
            false
        }
        return buildString {
            appendLine("Guardião (foreground service): ${if (GuardianService.running) "ativo" else "parado"}")
            appendLine("Permissão PROTECTION_CONTROL (assinatura): ${if (GuardianPermissions.hasProtectionControl(activity)) "concedida" else "AUSENTE — reinstale o guardião após o app principal"}")
            appendLine("WRITE_SECURE_SETTINGS: ${if (GuardianPermissions.hasWriteSecureSettings(activity)) "concedida" else "AUSENTE — conceda por ADB"}")
            appendLine("Sobreposição (SYSTEM_ALERT_WINDOW): ${if (GuardianPermissions.canDrawOverlays(activity)) "permitida" else "NEGADA"}")
            appendLine("Bateria sem otimização: ${if (GuardianPermissions.isIgnoringBatteryOptimizations(activity)) "sim" else "não (recomendado permitir)"}")
            appendLine("Administrador do guardião: ${if (admin) "ativo" else "inativo"}")
            appendLine()
            if (state == null) {
                appendLine("App principal: SEM RESPOSTA (force-stopped/desinstalado ou subindo)")
            } else {
                appendLine("Proteção ativa: ${if (state.protectionActive) "sim" else "não"}")
                appendLine("Acessibilidade do app principal: ${if (a11yListed && state.a11yEnabled) "OK" else "AUSENTE"}")
                appendLine("Admin do app principal: ${if (state.adminActive) "ativo" else "inativo"}")
                appendLine("Fim do bloqueio: ${state.blockEndMs ?: "sem fim definido"}")
                appendLine(
                    "Token FCM: " + if (state.fcmToken.isBlank()) {
                        "ausente (sem ack)"
                    } else {
                        "presente — tópico ntfy " + AckTopic.derive(state.fcmToken)
                    },
                )
            }
        }
    }

    private companion object {
        const val TAG = "GuardianStatus"
        const val REQUEST_NOTIFICATIONS = 61
    }
}
