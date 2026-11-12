package com.example.focusapp.ui

import com.example.focusapp.util.DATE_DMY
import android.app.TimePickerDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.SharedPreferences
import android.net.Uri
import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import androidx.compose.foundation.layout.*
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.example.focusapp.push.PushToken
import com.example.focusapp.schedule.*
import com.example.focusapp.guard.FocusEnforcementState
import com.example.focusapp.security.PasswordManager
import com.example.focusapp.service.FocusAccessibilityService
import com.example.focusapp.theme.Glass
import com.example.focusapp.ui.glass.*
import android.widget.Toast
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDateTime

@Composable
internal fun DailyBlockCard() {
    val c = Glass.colors
    val soft = c.ink.copy(alpha = 0.78f)
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current
    val scope = rememberCoroutineScope()
    val store = remember(context) { DailyBlockStore(context) }
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    var version by remember { mutableIntStateOf(0) }
    var accessibility by remember { mutableStateOf(FocusAccessibilityService.isEnabled(context)) }
    // A-03: "Ativo" só vale com heartbeat fresco do serviço (enabled != bound, o card não pode mentir).
    var enforcing by remember { mutableStateOf(FocusEnforcementState.isEnforcing(context)) }
    var alarms by remember { mutableStateOf(DailyBlockScheduler.canSchedule(context)) }
    var editing by remember { mutableStateOf(false) }
    var start by remember { mutableIntStateOf(7 * 60) }
    var end by remember { mutableIntStateOf(12 * 60) }
    var message by remember { mutableStateOf<String?>(null) }
    var settingsIntent by remember { mutableStateOf<Intent?>(null) }
    var password by remember { mutableStateOf("") }
    var passwordError by remember { mutableStateOf(false) }
    // A-01: desligar o "Modo seletivo" é protegido pela senha mestra (mesmo padrão do card de nível).
    var pendingSelectiveOff by remember { mutableStateOf(false) }
    var selectivePassword by remember { mutableStateOf("") }
    var selectivePasswordError by remember { mutableStateOf(false) }
    val record = remember(version, now.toLocalDate()) { store.load(now.toLocalDate()) }
    val yesterday = remember(version, now.toLocalDate()) { store.load(now.toLocalDate().minusDays(1)) }
    val blocked = DailyBlockPolicy.shouldBlock(record, now)
    val incomplete = record != null && !record.released && now.isBefore(record.end()) && (!enforcing || !alarms)
    val pomodoro = remember(version) { store.loadPomodoro() }
    val pomodoroActive = DailyBlockPolicy.shouldBlockAt(
        pomodoro,
        System.currentTimeMillis(),
        SystemClock.elapsedRealtime()
    )
    val adultEnd = remember(version) { store.loadAdultEnd() }
    val adultMonoEnd = remember(version) { store.loadAdultMonoEnd() }
    val selective = remember(version) { store.loadSelective() }
    val pushToken = remember(version) { store.pushToken }
    LaunchedEffect(store) { PushToken.ensure(store) }

    DisposableEffect(store) {
        val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> version++ }
        store.preferences.registerOnSharedPreferenceChangeListener(listener)
        onDispose { store.preferences.unregisterOnSharedPreferenceChangeListener(listener) }
    }
    LaunchedEffect(lifecycle, context) {
        lifecycle.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            DailyBlockScheduler.refresh(context)
            version++
            while (true) {
                // Estados com o mesmo valor não recompõem; o card só muda de fato na virada do minuto.
                now = LocalDateTime.now()
                accessibility = FocusAccessibilityService.isEnabled(context)
                enforcing = FocusEnforcementState.isEnforcing(context)
                alarms = DailyBlockScheduler.canSchedule(context)
                delay(60_000L - System.currentTimeMillis() % 60_000L)
            }
        }
    }
    fun openSettings(intent: Intent) {
        val manager = PasswordManager.getInstance(context)
        if (accessibility && manager.isPasswordSet()) {
            password = ""
            passwordError = false
            settingsIntent = intent
        } else context.startActivity(intent)
    }
    fun edit(fromYesterday: Boolean) {
        val source = if (fromYesterday) yesterday else record
        start = source?.startMinute ?: 7 * 60
        end = source?.endMinute ?: 12 * 60
        message = null
        editing = true
    }

    GlassCard(
        modifier = Modifier.fillMaxWidth(),
        shape = GlassShapes.Card,
        contentPadding = PaddingValues(18.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text(
                "Bloqueio por horário",
                color = c.ink,
                style = MaterialTheme.typography.titleMedium
            )
            Text("Só vale hoje · ${now.format(DATE_DMY)}", color = soft)
            Text(
                when {
                    record == null -> "Hoje está livre. Defina um intervalo se quiser bloquear o uso."
                    incomplete -> "Horário salvo, mas a proteção está incompleta. Reative acessibilidade e alarmes precisos."
                    blocked -> "Bloqueado até ${DailyBlockPolicy.formatMinute(record.endMinute)}. Digite a frase na tela de bloqueio para liberar."
                    record.released -> "Intervalo ${DailyBlockPolicy.formatMinute(record.startMinute)}–${DailyBlockPolicy.formatMinute(record.endMinute)} liberado por frase."
                    !now.isBefore(record.end()) -> "O intervalo de hoje terminou."
                    else -> "Programado para ${DailyBlockPolicy.formatMinute(record.startMinute)}–${DailyBlockPolicy.formatMinute(record.endMinute)}."
                },
                color = if (incomplete) c.danger else c.accent
            )
            // O bloqueio do PC chega pelo push e é gravado mesmo sem acessibilidade, mas só o serviço de
            // acessibilidade aplica o overlay: sem ele o card não pode dizer que o celular está bloqueado.
            if (selective) {
                Text(
                    if (enforcing) {
                        "Modo seletivo: Twitter/X e conteúdo adulto bloqueados sempre. O Angry do PC não trava a tela."
                    } else {
                        "Modo seletivo ligado, mas NÃO está sendo aplicado (acessibilidade desligada ou serviço morto)."
                    },
                    color = if (enforcing) c.warning else c.danger
                )
            } else {
                if (pomodoroActive && pomodoro != null) Text(
                    if (enforcing) {
                        "Pomodoro Angry do PC: bloqueado até ${DailyBlockPolicy.formatInstant(pomodoro.endMs)}."
                    } else {
                        "Pomodoro Angry do PC: chegou até ${DailyBlockPolicy.formatInstant(pomodoro.endMs)}, mas NÃO está sendo aplicado (acessibilidade desligada)."
                    },
                    color = if (enforcing) c.warning else c.danger
                )
                if (adultEnd != null && DailyBlockPolicy.isAdultBlockActive(
                        adultEnd,
                        System.currentTimeMillis(),
                        adultMonoEnd,
                        SystemClock.elapsedRealtime()
                    )
                ) Text(
                    if (enforcing) {
                        "Conteúdo adulto bloqueado até ${DailyBlockPolicy.formatInstant(adultEnd)} (Pomodoro Angry)"
                    } else {
                        "Conteúdo adulto: o PC pediu até ${DailyBlockPolicy.formatInstant(adultEnd)}, mas NÃO está sendo aplicado (acessibilidade desligada)."
                    },
                    color = if (enforcing) c.warning else c.danger
                )
            }
            Text(
                if (selective) {
                    "Fora do modo seletivo: Instagram/Reddit são bloqueados no Nível 2 e o Angry volta a travar a tela."
                } else {
                    "A frase libera até o fim do intervalo. Amanhã não há bloqueio automático. Chamadas ficam disponíveis."
                },
                color = soft,
                style = MaterialTheme.typography.bodySmall
            )
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1f)) {
                    Text("Modo seletivo", color = c.ink, style = MaterialTheme.typography.bodyLarge)
                    Text(
                        "Twitter/X e conteúdo adulto sempre; sem Instagram/Reddit e sem travar a tela no Angry",
                        color = soft,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
                Switch(
                    colors = glassSwitchColors(),
                    checked = selective,
                    onCheckedChange = { enabled ->
                        // A-01: desligar o seletivo desliga o bloqueio de conteúdo adulto. Exige senha.
                        if (!enabled && PasswordManager.getInstance(context).isPasswordSet()) {
                            selectivePassword = ""
                            selectivePasswordError = false
                            pendingSelectiveOff = true
                        } else {
                            store.saveSelective(enabled)
                            version++
                            message = if (enabled) "Modo seletivo ativado." else "Modo seletivo desativado."
                        }
                    },
                    modifier = Modifier.semantics { contentDescription = "Ativar modo seletivo" }
                )
            }
            if (!enforcing || (!selective && !alarms)) {
                Text(
                    if (!enforcing) "Para aplicar o bloqueio, ative a acessibilidade."
                    else "Para ativar: habilite alarmes precisos.",
                    color = c.warning
                )
                if (!enforcing) GlassButton(
                    text = "Ativar acessibilidade",
                    onClick = {
                        openSettings(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
                    },
                    kind = GlassButtonKind.Tonal,
                    compact = true
                )
                if (!alarms && !selective && Build.VERSION.SDK_INT >= 31) GlassButton(
                    text = "Permitir alarmes precisos",
                    onClick = {
                        openSettings(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:${context.packageName}")))
                    },
                    kind = GlassButtonKind.Tonal,
                    compact = true
                )
            }
            GlassButton(
                text = if (record == null) "Definir horário de hoje" else "Alterar horário de hoje",
                onClick = { edit(false) },
                modifier = Modifier.fillMaxWidth(),
                kind = GlassButtonKind.Primary,
                enabled = !blocked
            )
            GlassButton(
                text = "Repetir horário de ontem",
                onClick = { edit(true) },
                modifier = Modifier.fillMaxWidth(),
                kind = GlassButtonKind.Tonal,
                enabled = yesterday != null && !blocked
            )
            if (yesterday != null) Text(
                "Ontem: ${DailyBlockPolicy.formatMinute(yesterday.startMinute)}–${DailyBlockPolicy.formatMinute(yesterday.endMinute)}",
                color = soft,
                style = MaterialTheme.typography.bodySmall
            )
            if (record != null && !blocked) GlassButton(
                text = "Cancelar programação de hoje",
                onClick = {
                    if (store.cancelToday()) { DailyBlockScheduler.refresh(context); message = "Programação de hoje cancelada." }
                    else message = "Não foi possível cancelar. Se o intervalo começou, digite a frase primeiro."
                },
                kind = GlassButtonKind.Ghost
            )
            message?.let { Text(it, color = soft) }
            Text(
                if (selective) {
                    "O modo seletivo não depende do PC. O token abaixo só é usado nos avisos do Pomodoro Angry."
                } else {
                    "O Angry do Dynamic Island bloqueia este celular pelo mesmo tempo. Cole o token abaixo nas Settings da ilha."
                },
                color = soft,
                style = MaterialTheme.typography.bodySmall
            )
            if (pushToken != null) {
                GlassButton(
                    text = "Renovar e copiar token do celular",
                    onClick = {
                        // Renova antes de copiar: o token guardado pode ter sido invalidado pelo FCM (por exemplo,
                        // restaurado de backup de outro aparelho) e o PC passaria a enviar para um registro morto.
                        PushToken.refresh(store) { token ->
                            val clipboard = context.getSystemService(ClipboardManager::class.java)
                            clipboard.setPrimaryClip(ClipData.newPlainText("Token do celular", token))
                            message = "Token atualizado e copiado. Cole nas Settings do PC."
                        }
                    },
                    kind = GlassButtonKind.Ghost
                )
            } else {
                Text("Token indisponível (Firebase não configurado)", color = soft)
            }
        }
    }
    if (editing) GlassAlertDialog(
        onDismissRequest = { editing = false },
        title = { Text("Horário de hoje") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Este intervalo termina hoje. Se ele já começou, o bloqueio será imediato ao salvar.")
                GlassButton(
                    text = "Início: ${DailyBlockPolicy.formatMinute(start)}",
                    onClick = {
                        TimePickerDialog(context, { _, hour, minute -> start = hour * 60 + minute }, start / 60, start % 60, true).show()
                    },
                    modifier = Modifier.fillMaxWidth(),
                    kind = GlassButtonKind.Tonal
                )
                GlassButton(
                    text = "Fim: ${DailyBlockPolicy.formatMinute(end)}",
                    onClick = {
                        TimePickerDialog(context, { _, hour, minute -> end = hour * 60 + minute }, end / 60, end % 60, true).show()
                    },
                    modifier = Modifier.fillMaxWidth(),
                    kind = GlassButtonKind.Tonal
                )
                message?.let { Text(it) }
            }
        },
        confirmButton = {
            GlassButton(
                text = "Salvar e ativar",
                onClick = {
                    message = when {
                        !FocusEnforcementState.isEnforcing(context) || !DailyBlockScheduler.canSchedule(context) ->
                            "Ative acessibilidade e alarmes precisos antes de salvar."
                        !DailyBlockPolicy.canSchedule(start, end, LocalDateTime.now()) ->
                            "O fim precisa ser depois do início e ainda não pode ter passado."
                        !store.saveToday(start, end) -> "Não foi possível salvar. Um bloqueio ativo precisa ser liberado pela frase primeiro."
                        else -> {
                            editing = false
                            DailyBlockScheduler.refresh(context)
                            "Horário salvo para hoje."
                        }
                    }
                },
                kind = GlassButtonKind.Primary
            )
        },
        dismissButton = {
            GlassButton(
                text = "Voltar",
                onClick = { editing = false },
                kind = GlassButtonKind.Ghost
            )
        }
    )
    if (pendingSelectiveOff) {
        GlassAlertDialog(
            onDismissRequest = { pendingSelectiveOff = false },
            title = { Text("Desligar o modo seletivo") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        "Desligar o modo seletivo deixa de bloquear conteúdo adulto enquanto não houver " +
                            "um bloqueio do PC ativo. Digite a senha mestra para confirmar."
                    )
                    OutlinedTextField(
                        value = selectivePassword,
                        onValueChange = { selectivePassword = it; selectivePasswordError = false },
                        label = { Text("Senha mestra") },
                        visualTransformation = PasswordVisualTransformation(),
                        isError = selectivePasswordError,
                        singleLine = true,
                        colors = glassTextFieldColors(),
                        shape = GlassShapes.Control
                    )
                }
            },
            confirmButton = {
                GlassButton(
                    text = "Desligar",
                    onClick = {
                        val manager = PasswordManager.getInstance(context)
                        scope.launch {
                            val ok = withContext(Dispatchers.Default) { manager.verifyPassword(selectivePassword) }
                            if (ok) {
                                store.saveSelective(false)
                                version++
                                pendingSelectiveOff = false
                                message = "Modo seletivo desativado."
                            } else {
                                selectivePasswordError = true
                                Toast.makeText(context, manager.failureMessage(), Toast.LENGTH_SHORT).show()
                            }
                        }
                    },
                    kind = GlassButtonKind.Primary
                )
            },
            dismissButton = {
                GlassButton(
                    text = "Voltar",
                    onClick = { pendingSelectiveOff = false },
                    kind = GlassButtonKind.Ghost
                )
            }
        )
    }
    settingsIntent?.let { intent ->
        GlassAlertDialog(
            onDismissRequest = { settingsIntent = null },
            title = { Text("Autorizar configuração") },
            text = {
                Column {
                    Text("Digite sua senha mestra para abrir as configurações protegidas.")
                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it; passwordError = false },
                        label = { Text("Senha mestra") },
                        visualTransformation = PasswordVisualTransformation(),
                        isError = passwordError,
                        singleLine = true,
                        colors = glassTextFieldColors(),
                        shape = GlassShapes.Control
                    )
                }
            },
            confirmButton = {
                GlassButton(
                    text = "Abrir configurações",
                    onClick = {
                        val manager = PasswordManager.getInstance(context)
                        scope.launch {
                            val ok = withContext(Dispatchers.Default) { manager.verifyPassword(password) }
                            if (ok) {
                                manager.grantGracePeriod()
                                settingsIntent = null
                                context.startActivity(intent)
                            } else {
                                passwordError = true
                                Toast.makeText(context, manager.failureMessage(), Toast.LENGTH_SHORT).show()
                            }
                        }
                    },
                    kind = GlassButtonKind.Primary
                )
            },
            dismissButton = {
                GlassButton(
                    text = "Voltar",
                    onClick = { settingsIntent = null },
                    kind = GlassButtonKind.Ghost
                )
            }
        )
    }
}
