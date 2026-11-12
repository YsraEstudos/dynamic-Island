package com.example.focusapp.guardian

import android.util.Log
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ThreadFactory

/**
 * Derivação do tópico ntfy usada pelo PC (Dynamic Island). O PC tem o token FCM que usa para mandar o
 * bloqueio; o guardião lê o mesmo token do provider do app principal. Tópico =
 * "di-ack-" + primeiros 32 hex de SHA-256("dynamic-island-ack-v1:" + token).
 */
internal object AckTopic {

    private const val TOPIC_PREFIX = "di-ack-"
    private const val TOPIC_SALT = "dynamic-island-ack-v1:"
    private val HEX = "0123456789abcdef".toCharArray()

    fun derive(fcmToken: String): String {
        // Trim igual ao app principal/PC: o tópico tem de bater exatamente com o derivado dos dois lados.
        val digest = MessageDigest.getInstance("SHA-256")
            .digest((TOPIC_SALT + fcmToken.trim()).toByteArray(Charsets.UTF_8))
        val hex = buildString(digest.size * 2) {
            digest.forEach { byte ->
                val value = byte.toInt() and 0xFF
                append(HEX[value ushr 4])
                append(HEX[value and 0x0F])
            }
        }
        return TOPIC_PREFIX + hex.substring(0, 32)
    }
}

/** JSONs de ack. O corpo da mensagem ntfy é exatamente este JSON compacto. */
internal object AckPayload {

    fun revive(applied: Boolean, a11y: Boolean, admin: Boolean, blockEndMs: Long?): String =
        baseJson(
            id = "guardian-revive",
            applied = applied,
            reason = "guardian-revive",
            a11y = a11y,
            admin = admin,
            blockEndMs = blockEndMs,
            heartbeat = false,
        )

    fun heartbeat(a11y: Boolean, admin: Boolean, blockEndMs: Long?): String =
        baseJson(
            id = "guardian-heartbeat",
            applied = true,
            reason = "guardian-heartbeat",
            a11y = a11y,
            admin = admin,
            blockEndMs = blockEndMs,
            heartbeat = true,
        )

    /**
     * Campos do contrato do Lead (`v`, `applied`, `reason`, `a11y`, `admin`, `ts`, `blockEndMs`) mais os
     * equivalentes que o leitor do PC (PhoneBlockAck) exige/tolera: `id` (obrigatório no parser atual),
     * `endMs` (nome usado pelo app principal; espelha blockEndMs) e `hb` (heartbeat). Campos extras são
     * ignorados pelos parsers, então isto não quebra o contrato.
     */
    private fun baseJson(
        id: String,
        applied: Boolean,
        reason: String,
        a11y: Boolean,
        admin: Boolean,
        blockEndMs: Long?,
        heartbeat: Boolean,
    ): String {
        val json = JSONObject()
            .put("v", 1)
            .put("id", id)
            .put("applied", applied)
            .put("reason", reason)
            .put("a11y", a11y)
            .put("admin", admin)
            .put("ts", System.currentTimeMillis())
            .put("hb", heartbeat)
            .put("source", "guardian")
        if (blockEndMs == null) {
            json.put("blockEndMs", JSONObject.NULL)
            json.put("endMs", JSONObject.NULL)
        } else {
            json.put("blockEndMs", blockEndMs)
            json.put("endMs", blockEndMs)
        }
        return json.toString()
    }
}

/**
 * POST no ntfy.sh em thread própria: o loop de verificação nunca bloqueia esperando rede.
 * Corpo = JSON como texto puro (text/plain), sem auth, sem conteúdo sensível.
 */
internal class AckPublisher {

    private val executor = Executors.newSingleThreadExecutor(
        ThreadFactory { runnable -> Thread(runnable, "guardian-ack").apply { isDaemon = true } },
    )
    private var lastMissingTokenWarningAt = 0L

    fun publish(fcmToken: String, payload: String) {
        if (fcmToken.isBlank()) {
            val now = System.currentTimeMillis()
            if (now - lastMissingTokenWarningAt > 10 * 60_000L) {
                lastMissingTokenWarningAt = now
                Log.w(TAG, "sem token FCM no provider: ack ntfy não enviado")
            }
            return
        }
        val url = "https://ntfy.sh/" + AckTopic.derive(fcmToken)
        try {
            executor.execute { post(url, payload) }
        } catch (_: RejectedExecutionException) {
            Log.w(TAG, "executor de ack encerrado; ack descartado")
        }
    }

    fun shutdown() {
        executor.shutdownNow()
    }

    private fun post(url: String, payload: String) {
        var connection: HttpURLConnection? = null
        try {
            val bytes = payload.toByteArray(Charsets.UTF_8)
            connection = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "POST"
                connectTimeout = 5_000
                readTimeout = 5_000
                doOutput = true
                setRequestProperty("Content-Type", "text/plain; charset=utf-8")
                setRequestProperty("X-Title", "focus-guardian-ack")
                setFixedLengthStreamingMode(bytes.size)
            }
            connection.outputStream.use { it.write(bytes) }
            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            stream?.use { it.readBytes() }
            Log.i(TAG, "ack ntfy HTTP $code")
        } catch (error: Exception) {
            Log.w(TAG, "ack ntfy falhou: ${error.javaClass.simpleName}: ${error.message}")
        } finally {
            connection?.disconnect()
        }
    }

    private companion object {
        const val TAG = "GuardianAck"
    }
}
