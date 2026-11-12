package com.example.focusapp.schedule

import android.os.Build
import java.util.Locale

/**
 * Regra do produto: o **bloqueio de tela** (overlay de tela cheia + tela de frase, tanto do
 * "Bloqueio por horário" quanto do Pomodoro Angry vindo do PC) só pode ser aplicado no
 * **Samsung Galaxy S23 Ultra** (modelo `SM-S918*`).
 *
 * No **moto g(20)** — e em qualquer outro aparelho — nunca deve travar a tela: o app continua
 * contando tempo, bloqueando apps/sites por acessibilidade e reportando estado ao PC, mas sem o
 * overlay de tela cheia. Ver docs/bloqueio-diario.md.
 */
internal object ScreenLockDevicePolicy {

    private const val S23_ULTRA_MODEL_PREFIX = "SM-S918"

    /** Função pura (testável em JVM) com os valores equivalentes a Build.MODEL/DEVICE/PRODUCT. */
    fun isSupported(model: String?, device: String?, product: String?): Boolean {
        val normalizedModel = model?.trim()?.uppercase(Locale.ROOT).orEmpty()
        return normalizedModel.startsWith(S23_ULTRA_MODEL_PREFIX)
    }

    /** true somente no S23 Ultra; false no moto g(20) e em qualquer outro modelo. */
    fun isScreenLockAllowed(): Boolean = isSupported(Build.MODEL, Build.DEVICE, Build.PRODUCT)
}
