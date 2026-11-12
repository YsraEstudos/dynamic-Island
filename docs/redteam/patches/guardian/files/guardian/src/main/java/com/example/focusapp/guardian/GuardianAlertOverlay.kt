package com.example.focusapp.guardian

import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.Gravity
import android.view.WindowManager
import android.widget.TextView

/**
 * Faixa de aviso sobreposta à tela enquanto a proteção está quebrada. Não é bloqueio de tela cheia:
 * apresenta o problema e, no toque, abre o app principal. Exige SYSTEM_ALERT_WINDOW e só aparece quando
 * a recuperação falha (some sozinha quando o serviço de acessibilidade volta).
 *
 * Detalhe Android 14: a isenção de background activity start por SYSTEM_ALERT_WINDOW exige uma janela
 * visível do app, então o guardião mostra esta faixa ANTES de tentar abrir a MainActivity do principal.
 */
internal class GuardianAlertOverlay(context: Context) {

    private val appContext = context.applicationContext
    private val mainHandler = Handler(Looper.getMainLooper())
    private val windowManager = appContext.getSystemService(Context.WINDOW_SERVICE) as WindowManager
    private var view: TextView? = null

    fun show() {
        mainHandler.post {
            if (view != null) return@post
            if (!GuardianPermissions.canDrawOverlays(appContext)) {
                Log.w(TAG, "sem SYSTEM_ALERT_WINDOW: overlay de aviso não exibido")
                return@post
            }
            val text = TextView(appContext).apply {
                text = "BLOQUEIO DESLIGADO — toque para reativar a proteção"
                setTextColor(Color.WHITE)
                setBackgroundColor(BLOCKED_RED)
                textSize = 15f
                val padding = dp(14)
                setPadding(padding, dp(12), padding, dp(12))
                gravity = Gravity.CENTER
                setOnClickListener {
                    hide()
                    MainAppControl.launchMainActivity(appContext)
                }
            }
            val params = WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                    WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT,
            ).apply {
                gravity = Gravity.TOP
                y = dp(8)
            }
            try {
                windowManager.addView(text, params)
                view = text
                Log.i(TAG, "overlay de aviso exibido")
            } catch (error: Exception) {
                Log.w(TAG, "falha ao exibir overlay: ${error.javaClass.simpleName}: ${error.message}")
            }
        }
    }

    fun hide() {
        mainHandler.post {
            val current = view ?: return@post
            view = null
            try {
                windowManager.removeView(current)
                Log.i(TAG, "overlay de aviso removido")
            } catch (error: Exception) {
                Log.w(TAG, "falha ao remover overlay: ${error.javaClass.simpleName}")
            }
        }
    }

    private fun dp(value: Int): Int = (value * appContext.resources.displayMetrics.density).toInt()

    private companion object {
        const val TAG = "GuardianOverlay"
        const val BLOCKED_RED = 0xFFB00020.toInt()
    }
}
