package com.example.focusapp.guard

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.Bundle
import android.util.Log
import com.example.focusapp.schedule.DailyBlockStore
import org.json.JSONObject

/**
 * Estado da proteção para o **guardião da Fase 2** (`com.example.focusapp.guardian`), que assina com a
 * mesma chave e recebe a permissão `com.example.focusapp.permission.PROTECTION_CONTROL` (signature).
 *
 * Contrato combinado com o agente do guardião:
 * - authority: `com.example.focusapp.protection`
 * - URI: `content://com.example.focusapp.protection/state`
 * - 1 linha, coluna String `json`:
 *   `{"v":1,"protectionActive":bool,"a11yEnabled":bool,"adminActive":bool,"fcmToken":"<token>","blockEndMs":<long|null>}`
 *
 * O `fcmToken` é sensível (quem tiver o token + a chave FCM pode mandar bloqueios); por isso o provider
 * é exportado **somente** com permissão de assinatura. Nenhum outro app (nem o shell do ADB) lê isso.
 */
internal class ProtectionStateProvider : ContentProvider() {

    companion object {
        const val AUTHORITY = "com.example.focusapp.protection"
        const val PATH_STATE = "state"
        const val COLUMN_JSON = "json"
        private const val TAG = "ProtectionStateProvider"
        private val STATE_URI: Uri = Uri.parse("content://$AUTHORITY/$PATH_STATE")

        /** Constrói o JSON do estado; também usado pelo unit test da parte pura? (aqui precisa de Context). */
        fun stateJson(context: android.content.Context): String {
            val snapshot = GuardState.snapshot(context)
            val a11yEnabled = AccessibilityGuard.isEnabled(context)
            val adminActive = AccessibilityGuard.isAdminActive(context)
            val fcmToken = DailyBlockStore(context).pushToken
            return JSONObject()
                .put("v", 1)
                .put("protectionActive", snapshot.protectionExpected)
                .put("a11yEnabled", a11yEnabled)
                .put("adminActive", adminActive)
                .put("fcmToken", fcmToken ?: JSONObject.NULL)
                .put("blockEndMs", snapshot.blockEndMs ?: JSONObject.NULL)
                .toString()
        }
    }

    override fun onCreate(): Boolean = true

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?
    ): Cursor? {
        val appContext = context ?: return null
        if (uri.path != "/$PATH_STATE") return null
        val cursor = MatrixCursor(arrayOf(COLUMN_JSON))
        cursor.addRow(arrayOf(stateJson(appContext)))
        return cursor
    }

    override fun getType(uri: Uri): String? = null

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<out String>?
    ): Int = 0

    /** Atalho opcional para o guardião: `call("state")` devolve o mesmo JSON em um Bundle. */
    override fun call(method: String, arg: String?, extras: Bundle?): Bundle? {
        if (method != PATH_STATE) return super.call(method, arg, extras)
        val appContext = context ?: return null
        return Bundle().apply { putString(COLUMN_JSON, runCatching { stateJson(appContext) }
            .onFailure { Log.w(TAG, "Falha ao montar o estado da proteção", it) }
            .getOrNull()) }
    }
}
