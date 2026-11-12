package com.example.focusapp.ui

import android.app.admin.DevicePolicyManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.provider.Settings
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.focusapp.admin.AdminPolicyReceiver
import com.example.focusapp.display.GrayscaleController
import com.example.focusapp.theme.Glass
import com.example.focusapp.ui.glass.*

@Composable
internal fun PermissionsHubSection(
    grantedCount: Int,
    totalCount: Int,
    hasLiveUpdates: Boolean,
    onEnableLiveUpdates: () -> Unit,
    hasUsageStats: Boolean,
    hasFocusAccessibility: Boolean,
    hasAdmin: Boolean,
    hasSecureSettings: Boolean,
    context: Context
) {
    val c = Glass.colors
    var adbCopied by remember { mutableStateOf(false) }
    val indicator = if (grantedCount == totalCount) c.success else c.warning

    GlassCard(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(vertical = 16.dp)
    ) {
        // Cabeçalho: ícone de blindagem, título e contagem
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            GlassIconBadge(icon = Icons.Rounded.VerifiedUser, tint = c.violetSoft)
            Column(modifier = Modifier.weight(1f).padding(start = 12.dp)) {
                Text(
                    text = "Blindagem do Sistema",
                    style = MaterialTheme.typography.titleMedium,
                    color = c.ink
                )
                Text(
                    text = "$grantedCount de $totalCount ativas",
                    style = MaterialTheme.typography.bodySmall,
                    color = c.muted
                )
            }
        }

        Spacer(modifier = Modifier.height(12.dp))

        // Barra de progresso da blindagem
        GlassProgressBar(
            progress = grantedCount.toFloat() / totalCount.toFloat(),
            modifier = Modifier.padding(horizontal = 16.dp),
            color = indicator
        )

        // Itens de Permissão, separados por divisórias dentro do mesmo card
        ModernPermissionItem(
            title = "Live Updates & Samsung Now Bar",
            subtitle = if (hasLiveUpdates) {
                "Permissões Android ativas. Se não aparecer, ative o app em Tela de bloqueio e AOD > Now Bar"
            } else {
                "Ative notificações e Live Updates para exibir a Now Bar"
            },
            icon = Icons.Rounded.NotificationsActive,
            isGranted = hasLiveUpdates,
            onAction = onEnableLiveUpdates
        )

        GlassDivider()

        ModernPermissionItem(
            title = "Acesso a Dados de Uso",
            subtitle = "Motor A: cálculo preciso do tempo de tela ativa",
            icon = Icons.Rounded.QueryStats,
            isGranted = hasUsageStats,
            onAction = {
                context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
            }
        )

        GlassDivider()

        ModernPermissionItem(
            title = "Bloqueador de Redes (Acessibilidade)",
            subtitle = "Bloqueio < 50ms de Twitter/X, Instagram, Reddit e derivados. O watchdog religa se for desligado.",
            icon = Icons.Rounded.Block,
            isGranted = hasFocusAccessibility,
            onAction = {
                context.startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
            }
        )

        GlassDivider()

        // A-06: o admin NÃO impede desinstalação fora de Device Owner (setUninstallBlocked só vale para DO).
        // A UI precisa ser honesta; quem impede de verdade é o watchdog + guardião, e o DO no Nível 3.
        ModernPermissionItem(
            title = "Administrador do Dispositivo",
            subtitle = "Avisa/atrasa desinstalação. Bloqueio real de desinstalação só no Nível 3 (Device Owner).",
            icon = Icons.Rounded.AdminPanelSettings,
            isGranted = hasAdmin,
            onAction = {
                val intent = Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN).apply {
                    putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, AdminPolicyReceiver.getComponentName(context))
                    putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION, "Protege contra desinstalação sem a senha mestra.")
                }
                context.startActivity(intent)
            }
        )

        GlassDivider()

        ModernPermissionItem(
            title = "Modo P&B (Escala de Cinza)",
            subtitle = "Permissão WRITE_SECURE_SETTINGS via ADB",
            icon = Icons.Rounded.InvertColors,
            isGranted = hasSecureSettings,
            onAction = {
                val cmd = GrayscaleController.getAdbCommand(context)
                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(ClipData.newPlainText("Comando ADB", cmd))
                adbCopied = true
                Toast.makeText(context, "Comando ADB copiado!", Toast.LENGTH_SHORT).show()
            }
        )

        // Bloco com o comando ADB, se a permissão ainda não foi concedida
        if (!hasSecureSettings) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp)
                    .glass(GlassShapes.Badge)
                    .padding(14.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.Terminal,
                            contentDescription = null,
                            tint = c.accent,
                            modifier = Modifier.size(16.dp)
                        )
                        Text(
                            text = "Comando ADB (uma única vez):",
                            style = MaterialTheme.typography.labelSmall,
                            fontWeight = FontWeight.Bold,
                            color = c.ink
                        )
                    }

                    // Botão de Copiar com feedback visual
                    Row(
                        modifier = Modifier
                            .clip(GlassShapes.Badge)
                            .background(if (adbCopied) c.success.copy(alpha = 0.16f) else c.ink.copy(alpha = 0.08f))
                            .clickable {
                                val cmd = GrayscaleController.getAdbCommand(context)
                                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                                clipboard.setPrimaryClip(ClipData.newPlainText("Comando ADB", cmd))
                                adbCopied = true
                                Toast.makeText(context, "Comando ADB copiado!", Toast.LENGTH_SHORT).show()
                            }
                            .heightIn(min = 40.dp)
                            .padding(horizontal = 10.dp, vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(4.dp)
                    ) {
                        Icon(
                            imageVector = if (adbCopied) Icons.Rounded.Check else Icons.Rounded.ContentCopy,
                            contentDescription = "Copiar",
                            tint = if (adbCopied) c.success else c.ink.copy(alpha = 0.78f),
                            modifier = Modifier.size(14.dp)
                        )
                        Text(
                            text = if (adbCopied) "Copiado!" else "Copiar",
                            style = MaterialTheme.typography.labelSmall,
                            fontWeight = FontWeight.Bold,
                            color = if (adbCopied) c.success else c.ink.copy(alpha = 0.78f)
                        )
                    }
                }

                Text(
                    text = GrayscaleController.getAdbCommand(context),
                    style = MaterialTheme.typography.bodySmall,
                    fontFamily = FontFamily.Monospace,
                    color = c.accent,
                    fontSize = 11.sp,
                    lineHeight = 15.sp
                )
            }
        }
    }
}

internal fun openAppNotificationSettings(context: Context) {
    val intent = Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
        putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
    context.startActivity(intent)
}

internal fun openDeviceSettings(context: Context) {
    context.startActivity(Intent(Settings.ACTION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
}

@Composable
private fun ModernPermissionItem(
    title: String,
    subtitle: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    isGranted: Boolean,
    onAction: () -> Unit
) {
    val c = Glass.colors
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        GlassIconBadge(
            icon = icon,
            tint = if (isGranted) c.success else c.warning,
            size = 36.dp,
            iconSize = 18.dp
        )

        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyLarge,
                color = c.ink
            )
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = c.muted
            )
        }

        if (isGranted) {
            Row(
                modifier = Modifier
                    .clip(GlassShapes.Control)
                    .background(c.success.copy(alpha = 0.16f))
                    .clickable { onAction() }
                    .heightIn(min = 40.dp)
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                Icon(
                    imageVector = Icons.Rounded.Check,
                    contentDescription = null,
                    tint = c.success,
                    modifier = Modifier.size(14.dp)
                )
                Text(
                    text = "Ativo",
                    style = MaterialTheme.typography.labelLarge,
                    color = c.success
                )
            }
        } else {
            GlassButton(
                text = "Ativar",
                onClick = onAction,
                kind = GlassButtonKind.Tonal,
                compact = true
            )
        }
    }
}

@Composable
internal fun QuickActionsSection(
    grayscaleTestSecondsRemaining: Int,
    onTestGrayscale: () -> Unit,
    onChangePassword: () -> Unit
) {
    val c = Glass.colors
    Column(modifier = Modifier.fillMaxWidth()) {
        GlassSectionLabel("Ações Rápidas & Segurança")

        GlassGroup {
            // Demonstração P&B com contagem regressiva
            GlassRow(
                title = if (grayscaleTestSecondsRemaining > 0) "Testando (${grayscaleTestSecondsRemaining}s)" else "Testar P&B",
                subtitle = "Demonstração por 5s",
                onClick = onTestGrayscale,
                leading = {
                    GlassIconBadge(
                        icon = Icons.Rounded.RemoveRedEye,
                        tint = c.accent,
                        size = 36.dp,
                        iconSize = 18.dp
                    )
                }
            )

            GlassDivider()

            GlassRow(
                title = "Senha Mestra",
                subtitle = "Alterar proteção",
                onClick = onChangePassword,
                leading = {
                    GlassIconBadge(
                        icon = Icons.Rounded.VpnKey,
                        tint = c.violetSoft,
                        size = 36.dp,
                        iconSize = 18.dp
                    )
                }
            )
        }
    }
}
