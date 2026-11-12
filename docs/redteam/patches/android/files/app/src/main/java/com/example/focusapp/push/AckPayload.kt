package com.example.focusapp.push

import org.json.JSONObject
import java.security.MessageDigest
import java.util.Locale

/**
 * Payload do ack que o celular manda para o PC (ntfy e/ou URL LAN negociada no FCM).
 *
 * Contrato combinado com a ilha (Dynamic Island):
 * - tópico ntfy = `di-ack-` + os **32 primeiros hex** de SHA-256("dynamic-island-ack-v1:" + token FCM);
 *   o PC deriva o mesmo tópico do token que ele já tem — o tópico é segredo de capacidade, não
 *   aparece em texto na UI e não expõe o token;
 * - corpo = JSON publicado em `https://ntfy.sh/<tópico>` (corpo bruto; o PC lê essa string na
 *   mensagem do ntfy e faz parse);
 * - campos obrigatórios: `v`, `id`, `applied`, `a11y`, `admin`, `level`, `endMs`, `ts`, `gapMs`;
 * - campos extras (ignorados por um PC antigo): `hb`, `reason`, `adb`, `users`, `profiles`, `guardian`.
 *
 * Este arquivo é puro (sem Android): roda no `testDebugUnitTest` com o `org.json` de teste.
 */
internal data class AckPayload(
    /** Id do bloqueio recebido (pomodoro), messageId do FCM ou um rótulo do tipo de evento. */
    val id: String,
    /** true = a proteção está realmente aplicada no celular agora (acessibilidade vinculada + bloqueio). */
    val applied: Boolean,
    /** Acessibilidade habilitada e com heartbeat recente. */
    val a11y: Boolean,
    val admin: Boolean,
    /** Nível do bloqueio ativo: 0 = nada, 1 = seletivo/adulto, 2 = pomodoro/horário. */
    val level: Int,
    /** Fim do bloqueio ativo em epoch ms, ou null quando não há bloqueio por tempo. */
    val endMs: Long?,
    /** Momento do ack em epoch ms (relógio do celular). */
    val tsMs: Long,
    /** Quanto tempo passou entre o envio do PC (sentAtMs do FCM) e o ack; null se o PC não mandou. */
    val gapMs: Long? = null,
    /** true = heartbeat periódico (não é resposta a uma mensagem específica). */
    val heartbeat: Boolean = false,
    /** Motivo legível: fcm, heartbeat, watchdog, revive, clock_change, profiles, a11y_off, admin_off. */
    val reason: String? = null,
    val adb: Boolean = false,
    val users: Int = 1,
    val profiles: Int = 1,
    /** Guardião da Fase 2 instalado? (o pacote externo que sobrevive ao force-stop). */
    val guardian: Boolean = false
) {

    fun toJson(): String {
        val json = JSONObject()
            .put("v", VERSION)
            .put("id", id)
            .put("applied", applied)
            .put("a11y", a11y)
            .put("admin", admin)
            .put("level", level)
            .put("endMs", endMs ?: JSONObject.NULL)
            .put("ts", tsMs)
        if (gapMs != null) json.put("gapMs", gapMs)
        json.put("hb", heartbeat)
        if (reason != null) json.put("reason", reason)
        json.put("adb", adb)
        json.put("users", users)
        json.put("profiles", profiles)
        json.put("guardian", guardian)
        return json.toString()
    }

    companion object {
        const val VERSION = 1
        const val TOPIC_PREFIX = "di-ack-"
        const val TOPIC_SALT = "dynamic-island-ack-v1:"

        /** 32 caracteres hex; tópico final fica `di-ack-` + 32 = 39 caracteres. */
        const val TOPIC_HASH_CHARS = 32

        /** Deriva o tópico ntfy do token FCM. Devolve null sem token (não há para onde mandar ack). */
        fun topicForToken(token: String?): String? {
            val value = token?.trim().orEmpty()
            if (value.isEmpty()) return null
            return TOPIC_PREFIX + sha256Hex(TOPIC_SALT + value).take(TOPIC_HASH_CHARS)
        }

        fun sha256Hex(value: String): String {
            val digest = MessageDigest.getInstance("SHA-256").digest(value.toByteArray(Charsets.UTF_8))
            return buildString(digest.size * 2) {
                for (byte in digest) append(String.format(Locale.ROOT, "%02x", byte.toInt() and 0xFF))
            }
        }
    }
}
