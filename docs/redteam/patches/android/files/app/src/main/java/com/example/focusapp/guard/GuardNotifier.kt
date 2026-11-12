package com.example.focusapp.guard

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.example.focusapp.MainActivity
import com.example.focusapp.R

/**
 * Notificações do guardião. Dois canais: um silencioso para o foreground service (status) e um de
 * alta prioridade para falhas — o usuário pode silenciar os canais nas configurações do Android
 * (foi observado que todos os canais do app estão mutados no Moto G20), então **toda falha também
 * vai no ack para o PC** e fica registrada nas prefs; a notificação é a camada visível, não a única.
 */
internal object GuardNotifier {
    private const val TAG = "GuardNotifier"

    const val CHANNEL_STATUS = "channel_focus_guard_status_v1"
    const val CHANNEL_ALERTS = "channel_focus_guard_alerts_v1"

    const val NOTIFICATION_WATCHDOG = 2001
    const val NOTIFICATION_REVIVE = 2002
    const val NOTIFICATION_A11Y_LOST = 2003
    private const val NOTIFICATION_BLOCK_NOT_APPLIED = 2004
    private const val NOTIFICATION_PROFILES = 2005
    private const val NOTIFICATION_CLOCK = 2006
    private const val NOTIFICATION_ADMIN = 2007
    private const val NOTIFICATION_PERMISSION = 2008
    private const val NOTIFICATION_RELOGGED_ADB = 2009

    @Volatile
    private var channelsCreated = false

    fun createChannels(context: Context) {
        if (channelsCreated) return
        channelsCreated = true
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        val status = NotificationChannel(
            CHANNEL_STATUS,
            "Watchdog da proteção",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "Mantém o bloqueio de acessibilidade ativo; mostra o estado real da proteção"
            setShowBadge(false)
            setSound(null, null)
            enableVibration(false)
        }
        val alerts = NotificationChannel(
            CHANNEL_ALERTS,
            "Alertas de proteção",
            NotificationManager.IMPORTANCE_HIGH
        ).apply {
            description = "Avisos de que o bloqueio NÃO está aplicado (acessibilidade desligada, admin ou perfil)"
            setShowBadge(true)
        }
        manager.createNotificationChannel(status)
        manager.createNotificationChannel(alerts)
    }

    /** startForeground com o tipo `specialUse` exigido a partir do Android 14. */
    fun startForegroundCompat(service: Service, notificationId: Int, notification: Notification) {
        if (Build.VERSION.SDK_INT >= 34) {
            service.startForeground(
                notificationId,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
            )
        } else {
            service.startForeground(notificationId, notification)
        }
    }

    /** Atualiza a notificação persistente do watchdog sem re-chamar startForeground. */
    fun updateOngoing(context: Context, notification: Notification) {
        runCatching {
            NotificationManagerCompat.from(context).notify(NOTIFICATION_WATCHDOG, notification)
        }.onFailure { Log.w(TAG, "Não foi possível atualizar a notificação do watchdog", it) }
    }

    fun cancelAccessibilityAlert(context: Context) {
        runCatching { NotificationManagerCompat.from(context).cancel(NOTIFICATION_A11Y_LOST) }
    }

    fun buildOngoing(context: Context, title: String, text: String): Notification {
        createChannels(context)
        return NotificationCompat.Builder(context, CHANNEL_STATUS)
            .setSmallIcon(R.drawable.ic_stat_timer)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(openAppIntent(context))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    fun buildRevive(context: Context): Notification {
        createChannels(context)
        return NotificationCompat.Builder(context, CHANNEL_STATUS)
            .setSmallIcon(R.drawable.ic_stat_timer)
            .setContentTitle("Restaurando a proteção")
            .setContentText("Religando o serviço de acessibilidade…")
            .setContentIntent(openAppIntent(context))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }

    private fun openAppIntent(context: Context): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        return PendingIntent.getActivity(
            context,
            0,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }

    private fun alert(
        context: Context,
        id: Int,
        title: String,
        text: String,
        category: String = NotificationCompat.CATEGORY_ERROR
    ) {
        createChannels(context)
        val notification = NotificationCompat.Builder(context, CHANNEL_ALERTS)
            .setSmallIcon(R.drawable.ic_stat_alarm)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openAppIntent(context))
            .setAutoCancel(true)
            .setCategory(category)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setDefaults(NotificationCompat.DEFAULT_ALL)
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(id, notification) }
            .onFailure { Log.w(TAG, "Não foi possível publicar o alerta $id", it) }
    }

    /** ACL desligada/desvinculada e o watchdog não conseguiu religar. */
    fun notifyAccessibilityLost(context: Context, reason: String) {
        alert(
            context,
            NOTIFICATION_A11Y_LOST,
            "BLOQUEIO NÃO APLICADO",
            "A acessibilidade está desligada e não deu para religar (${humanReason(reason)}). " +
                "Toque para abrir o app e reativar."
        )
    }

    /** O PC pediu um bloqueio e ele não está sendo aplicado neste instante. */
    fun notifyBlockNotApplied(context: Context, reason: String) {
        alert(
            context,
            NOTIFICATION_BLOCK_NOT_APPLIED,
            "BLOQUEIO NÃO APLICADO — acessibilidade desligada",
            "O bloqueio chegou, mas a acessibilidade está desligada (${humanReason(reason)}). " +
                "Toque para abrir o app e reativar."
        )
    }

    /** Sem WRITE_SECURE_SETTINGS o watchdog não consegue religar acessibilidade/ADB. */
    fun notifySecureSettingsMissing(context: Context) {
        alert(
            context,
            NOTIFICATION_PERMISSION,
            "Permissão ADB necessária",
            "Conceda uma vez: adb shell pm grant ${context.packageName} " +
                "android.permission.WRITE_SECURE_SETTINGS. Sem isso o watchdog não religa a " +
                "acessibilidade sozinho."
        )
    }

    fun notifyAdminMissing(context: Context) {
        alert(
            context,
            NOTIFICATION_ADMIN,
            "Administrador do dispositivo desativado",
            "O admin de dispositivo foi desligado (por exemplo depois de pm disable-user). " +
                "Toque para abrir o app e reativar a proteção."
        )
    }

    /** Multiusuário/perfil de trabalho: a acessibilidade é por usuário, o bloqueio não vale lá. */
    fun notifyProfilesUnprotected(context: Context, userCount: Int, profileCount: Int) {
        alert(
            context,
            NOTIFICATION_PROFILES,
            "Perfil sem proteção",
            "Este aparelho tem $userCount usuário(s) e $profileCount perfil(es). O bloqueio só vale " +
                "no perfil atual — não crie/use outro perfil enquanto estiver em bloqueio."
        )
    }

    /** Relógio do aparelho alterado durante um bloqueio (C-01): a proteção é reancorada, mas vamos avisar. */
    fun notifyClockChanged(context: Context) {
        alert(
            context,
            NOTIFICATION_CLOCK,
            "Relógio alterado durante bloqueio",
            "A data/hora do aparelho mudou durante um bloqueio ativo. O bloqueio do PC foi mantido " +
                "pelo relógio monotônico; o horário do dia pode ter sido afetado.",
            category = NotificationCompat.CATEGORY_STATUS
        )
    }

    fun notifyAdbOff(context: Context) {
        alert(
            context,
            NOTIFICATION_RELOGGED_ADB,
            "Depuração USB desligada durante o bloqueio",
            "Para fechar o atalho do ADB, a depuração foi desligada enquanto o bloqueio dura. " +
                "Ela volta ao terminar.",
            category = NotificationCompat.CATEGORY_STATUS
        )
    }

    fun cancel(context: Context, id: Int) {
        runCatching { NotificationManagerCompat.from(context).cancel(id) }
    }

    private fun humanReason(reason: String): String = when (reason) {
        AccessibilityGuard.REASON_NO_PERMISSION -> "sem permissão WRITE_SECURE_SETTINGS"
        AccessibilityGuard.REASON_WRITE_REJECTED -> "o sistema recusou a escrita do settings"
        AccessibilityGuard.REASON_NOT_BOUND -> "o serviço não voltou a vincular"
        AccessibilityGuard.REASON_ENABLED_NOT_BOUND -> "habilitado, mas morto (reinicie o app)"
        else -> reason.ifBlank { "motivo desconhecido" }
    }
}
