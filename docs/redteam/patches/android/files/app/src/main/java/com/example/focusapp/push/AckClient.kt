package com.example.focusapp.push

import android.content.Context
import android.util.Log
import com.example.focusapp.guard.GuardState
import com.example.focusapp.schedule.DailyBlockStore
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors

/**
 * Envia o ack do celular para o PC, sem dependências novas.
 *
 * Canais (em ordem):
 * 1. `ackUrl` que o PC pode mandar no data do FCM (LAN, ex.: `http://192.168.0.10:8787/phone-ack`) — opcional;
 * 2. `POST https://ntfy.sh/<tópico>` com o JSON no corpo cru; tópico = `di-ack-` + 32 primeiros hex de
 *    SHA-256("dynamic-island-ack-v1:" + token FCM) — o PC deriva o mesmo tópico do token que já tem.
 *
 * Garantias: nunca lança para o chamador; roda em uma thread própria (daemon); não imprime token nem
 * tópico no log; falha de rede não derruba o app. O PC deve tratar o ack como "fresco" por ~90 s.
 */
internal object AckClient {
    private const val TAG = "AckClient"
    private const val NTFY_BASE_URL = "https://ntfy.sh/"
    private const val CONNECT_TIMEOUT_MS = 8_000
    private const val READ_TIMEOUT_MS = 8_000

    private val executor = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "focus-ack").apply { isDaemon = true }
    }

    /** Enfileira o envio; retorna imediatamente. `ackUrl` LAN tem prioridade; ntfy é fallback. */
    fun send(context: Context, payload: AckPayload, ackUrl: String? = null) {
        val appContext = context.applicationContext
        val body = runCatching { payload.toJson() }.getOrElse { error ->
            Log.w(TAG, "Payload de ack inválido", error)
            return
        }
        val topic = runCatching { AckPayload.topicForToken(DailyBlockStore(appContext).pushToken) }
            .getOrNull()
        if (topic == null && ackUrl.isNullOrBlank()) {
            // Sem token FCM não há tópico derivável; sem ackUrl não há para onde mandar.
            Log.d(TAG, "Ack sem canal disponível (token FCM ausente e sem ackUrl)")
            return
        }
        executor.execute {
            val lanOk = if (!ackUrl.isNullOrBlank()) postJson(ackUrl, body) else false
            val ntfyOk = if (topic != null) postJson(NTFY_BASE_URL + topic, body) else false
            runCatching { GuardState.recordAck(appContext, lanOk || ntfyOk) }
            if (!lanOk && !ntfyOk) {
                Log.w(TAG, "Ack não entregue (LAN e ntfy falharam)")
            }
        }
    }

    /** Heartbeat periódico enquanto há proteção esperada. */
    fun sendHeartbeat(context: Context) {
        send(context, AckPayloads.current(context, id = "heartbeat", reason = "heartbeat", heartbeat = true))
    }

    /**
     * POST do JSON no corpo cru. Para o ntfy, postar em `/<tópico>` publica o corpo como mensagem —
     * o PC lê essa string na mensagem e faz o parse. Nunca lança.
     */
    private fun postJson(url: String, body: String): Boolean = runCatching {
        val connection = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = CONNECT_TIMEOUT_MS
            readTimeout = READ_TIMEOUT_MS
            doOutput = true
            setRequestProperty("Content-Type", "text/plain; charset=utf-8")
            setRequestProperty("User-Agent", "focusapp-ack/1")
            setRequestProperty("Cache-Control", "no-cache")
        }
        try {
            connection.outputStream.use { stream -> stream.write(body.toByteArray(Charsets.UTF_8)) }
            val code = connection.responseCode
            if (code !in 200..299) {
                Log.w(TAG, "Ack recusado pelo servidor (HTTP $code)")
            }
            code in 200..299
        } finally {
            runCatching { connection.disconnect() }
        }
    }.getOrElse { error ->
        Log.w(TAG, "Falha de rede ao publicar ack", error)
        false
    }
}
