package com.example.focusapp.guardian

import android.content.Context
import android.net.Uri
import android.util.Log
import org.json.JSONObject

/**
 * Contrato entre os dois APKs (NÃO alterar um lado sem o outro):
 *
 * - app principal `com.example.focusapp` publica o estado em
 *   `content://com.example.focusapp.protection/state` (1 linha, coluna String "json"):
 *   `{"v":1,"protectionActive":bool,"a11yEnabled":bool,"adminActive":bool,"fcmToken":"...","blockEndMs":number|null}`
 * - app principal expõe `com.example.focusapp.guard.ReviveService` (action
 *   `com.example.focusapp.action.REVIVE`), protegido por `com.example.focusapp.permission.PROTECTION_CONTROL`
 *   (signature); um start explícito dele limpa o estado "stopped" deixado pelo force-stop.
 * - este guardião expõe `com.example.focusapp.guardian.GuardianService` (action
 *   `com.example.focusapp.guardian.action.START`) protegido por
 *   `com.example.focusapp.guardian.permission.GUARDIAN_CONTROL` (signature) para o principal reerguê-lo.
 */
internal object ProtectionContract {

    private const val TAG = "GuardianContract"

    const val MAIN_PACKAGE = "com.example.focusapp"
    const val MAIN_ACTIVITY = "com.example.focusapp.MainActivity"
    const val MAIN_ACCESSIBILITY_SERVICE = "com.example.focusapp.service.FocusAccessibilityService"
    const val MAIN_REVIVE_SERVICE = "com.example.focusapp.guard.ReviveService"
    const val ACTION_REVIVE = "com.example.focusapp.action.REVIVE"

    const val STATE_URI = "content://com.example.focusapp.protection/state"
    const val STATE_COLUMN = "json"

    const val ACTION_GUARDIAN_START = "com.example.focusapp.guardian.action.START"

    /** Linha publicada por ProtectionStateProvider (app principal). */
    data class MainState(
        val protectionActive: Boolean,
        val a11yEnabled: Boolean,
        val adminActive: Boolean,
        val fcmToken: String,
        val blockEndMs: Long?,
    )

    /**
     * Query síncrona (rodar SEMPRE fora da main thread). Devolve null quando o provider não responde:
     * isso é o sinal de app principal morto (force-stopped), desinstalado ou ainda subindo.
     */
    fun query(context: Context): MainState? = try {
        val cursor = context.contentResolver.query(
            Uri.parse(STATE_URI),
            arrayOf(STATE_COLUMN),
            null,
            null,
            null,
        )
        if (cursor == null) {
            null
        } else {
            cursor.use { row ->
                if (!row.moveToFirst()) {
                    null
                } else {
                    val column = row.getColumnIndexOrThrow(STATE_COLUMN)
                    if (row.isNull(column)) null else parse(row.getString(column))
                }
            }
        }
    } catch (error: Exception) {
        Log.w(TAG, "estado do app principal indisponível (${error.javaClass.simpleName}): ${error.message}")
        null
    }

    fun parse(raw: String?): MainState? {
        if (raw.isNullOrBlank()) return null
        return try {
            val json = JSONObject(raw)
            // O provider do principal publica JSONObject.NULL quando não há token; optString devolveria a
            // string "null" nesse caso, então normalizamos para vazio (sem token = sem ack ntfy).
            val token = if (json.isNull("fcmToken")) "" else json.optString("fcmToken", "").trim().takeUnless { it == "null" }.orEmpty()
            MainState(
                protectionActive = json.optBoolean("protectionActive", false),
                a11yEnabled = json.optBoolean("a11yEnabled", false),
                adminActive = json.optBoolean("adminActive", false),
                fcmToken = token,
                blockEndMs = if (json.has("blockEndMs") && !json.isNull("blockEndMs")) json.optLong("blockEndMs") else null,
            )
        } catch (error: Exception) {
            Log.w(TAG, "json de estado inválido (${error.javaClass.simpleName}): ${error.message}")
            null
        }
    }
}
