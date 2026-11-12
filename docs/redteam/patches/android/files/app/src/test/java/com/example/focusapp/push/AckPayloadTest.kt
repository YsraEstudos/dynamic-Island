package com.example.focusapp.push

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Tópico/payload do ack: contrato com o PC (ntfy) e serialização estável. */
class AckPayloadTest {

    @Test
    fun sha256MatchesTheKnownVector() {
        assertEquals(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            AckPayload.sha256Hex("abc")
        )
    }

    @Test
    fun topicIsDerivedDeterministicallyFromTheFcmToken() {
        val topic = AckPayload.topicForToken("abc")
        assertEquals("di-ack-fd77baf1a574eaf7892282322d40e446", topic)
        assertEquals(39, topic!!.length)
        assertTrue(topic.removePrefix("di-ack-").all { character -> character in "0123456789abcdef" })
        assertNotEquals(topic, AckPayload.topicForToken("abd"))
    }

    @Test
    fun missingOrBlankTokenHasNoTopic() {
        assertNull(AckPayload.topicForToken(null))
        assertNull(AckPayload.topicForToken(""))
        assertNull(AckPayload.topicForToken("   "))
    }

    @Test
    fun payloadHasTheContractFields() {
        val payload = AckPayload(
            id = "pomodoro-1",
            applied = true,
            a11y = true,
            admin = false,
            level = 2,
            endMs = 1_800_000_000_000L,
            tsMs = 1_700_000_000_000L,
            gapMs = 1_234L,
            heartbeat = false,
            reason = "fcm"
        )
        val json = JSONObject(payload.toJson())
        assertEquals(1, json.getInt("v"))
        assertEquals("pomodoro-1", json.getString("id"))
        assertTrue(json.getBoolean("applied"))
        assertTrue(json.getBoolean("a11y"))
        assertFalse(json.getBoolean("admin"))
        assertEquals(2, json.getInt("level"))
        assertEquals(1_800_000_000_000L, json.getLong("endMs"))
        assertEquals(1_700_000_000_000L, json.getLong("ts"))
        assertEquals(1_234L, json.getLong("gapMs"))
        assertFalse(json.getBoolean("hb"))
        assertEquals("fcm", json.getString("reason"))
    }

    @Test
    fun nullableFieldsAreJsonNullOrAbsent() {
        val json = JSONObject(
            AckPayload(
                id = "heartbeat",
                applied = false,
                a11y = false,
                admin = false,
                level = 0,
                endMs = null,
                tsMs = 1L,
                gapMs = null,
                heartbeat = true
            ).toJson()
        )
        assertTrue(json.isNull("endMs"))
        assertFalse(json.has("gapMs"))
        assertFalse(json.has("reason"))
        assertTrue(json.getBoolean("hb"))
    }

    @Test
    fun extraDiagnosticsFieldsArePresent() {
        val json = JSONObject(
            AckPayload(
                id = "x",
                applied = false,
                a11y = false,
                admin = false,
                level = 0,
                endMs = null,
                tsMs = 1L,
                adb = true,
                users = 2,
                profiles = 2,
                guardian = false
            ).toJson()
        )
        assertTrue(json.getBoolean("adb"))
        assertEquals(2, json.getInt("users"))
        assertEquals(2, json.getInt("profiles"))
        assertFalse(json.getBoolean("guardian"))
    }
}
