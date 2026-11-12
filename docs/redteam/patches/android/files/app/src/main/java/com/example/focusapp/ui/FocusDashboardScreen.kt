package com.example.focusapp.ui

import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.focusapp.util.formatClock
import android.Manifest
import android.content.Intent
import android.provider.Settings
import android.widget.Toast
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.scrollBy
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.zIndex
import androidx.compose.ui.geometry.Rect
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.example.focusapp.security.PasswordManager
import com.example.focusapp.service.LiveUpdateManager
import com.example.focusapp.service.LiveUpdateSetupAction
import com.example.focusapp.theme.Glass
import com.example.focusapp.ui.glass.GlassAlertDialog
import com.example.focusapp.ui.glass.GlassButton
import com.example.focusapp.ui.glass.GlassButtonKind
import com.example.focusapp.ui.glass.GlassShapes
import com.example.focusapp.ui.glass.glass
import com.example.focusapp.ui.glass.glassBottomInset
import com.example.focusapp.ui.glass.glassTextFieldColors
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.compose.ui.text.input.PasswordVisualTransformation
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun FocusDashboardScreen(
    onRequestUninstall: () -> Unit,
    onOpenHistory: () -> Unit,
    onOpenTasks: () -> Unit,
    listState: LazyListState
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val coroutineScope = rememberCoroutineScope()
    val density = LocalDensity.current
    val moduleBounds = remember { mutableStateMapOf<DashboardModule, Rect>() }
    var moduleOrder by remember(context) { mutableStateOf(DashboardModuleOrderStore.load(context)) }
    var draggedModule by remember { mutableStateOf<DashboardModule?>(null) }
    var pointerYInRoot by remember { mutableFloatStateOf(0f) }
    var pointerToCenterOffset by remember { mutableFloatStateOf(0f) }
    var orderBeforeDrag by remember { mutableStateOf<List<DashboardModule>?>(null) }
    var listBounds by remember { mutableStateOf<Rect?>(null) }

    val viewModel: DashboardViewModel = viewModel()
    val state by viewModel.state.collectAsStateWithLifecycle()
    val usedTimeMillisState = rememberUpdatedState(state.usedTimeMillis)
    val dailyLimitMillis = state.dailyLimitMillis
    val baseDailyLimitMillis = state.baseDailyLimitMillis
    val allowanceSummary = state.allowanceSummary
    val extraTimeGrantCount = state.extraTimeGrantCount
    val securityLevel = state.securityLevel
    var showChangePasswordDialog by remember { mutableStateOf(false) }
    var showExtraTimeDialog by remember { mutableStateOf(false) }
    var pendingLevelDowngrade by remember { mutableStateOf<Int?>(null) }
    // D-03: subir o limite diário e zerar a dívida são os parâmetros do próprio autocontrole — exigem senha.
    var pendingLimitIncrease by remember { mutableStateOf<Pair<Long, String>?>(null) }
    var pendingMoneyOwedReset by remember { mutableStateOf(false) }
    val notificationPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) LiveUpdateManager.refreshCurrentNotification(context)
        else openAppNotificationSettings(context)
    }

    fun moveDraggedModuleToPointer() {
        val dragged = draggedModule ?: return
        val peers = moduleOrder.filterNot { it == dragged }
        val visiblePeers = peers.filter { moduleBounds.containsKey(it) }
        if (visiblePeers.isEmpty()) return

        val firstVisiblePeerIndex = peers.indexOf(visiblePeers.first())
        val draggedCenterY = pointerYInRoot + pointerToCenterOffset
        val passedVisiblePeers = visiblePeers.count { moduleBounds[it]?.center?.y?.let { center -> draggedCenterY > center } == true }
        val targetIndex = (firstVisiblePeerIndex + passedVisiblePeers).coerceIn(0, peers.size)
        val currentIndex = moduleOrder.indexOf(dragged)
        if (currentIndex == targetIndex) return

        moduleOrder = peers.toMutableList().apply { add(targetIndex, dragged) }
    }

    fun finishDrag(saveOrder: Boolean) {
        if (saveOrder) {
            DashboardModuleOrderStore.save(context, moduleOrder)
        } else {
            orderBeforeDrag?.let { moduleOrder = it }
        }
        draggedModule = null
        orderBeforeDrag = null
    }

    LaunchedEffect(context) {
        moduleOrder = DashboardModuleOrderStore.load(context)
    }

    LaunchedEffect(viewModel, context) {
        viewModel.messages.collect { message ->
            Toast.makeText(context, message.text, if (message.long) Toast.LENGTH_LONG else Toast.LENGTH_SHORT).show()
        }
    }

    LaunchedEffect(lifecycleOwner, viewModel) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            viewModel.refresh()
        }
    }

    DashboardUsageTicker(
        snapshotDate = state.date,
        version = state.snapshotVersion,
        onDateChanged = { viewModel.refresh() }
    )
    LaunchedEffect(draggedModule) {
        while (draggedModule != null) {
            val viewport = listBounds
            if (viewport != null) {
                val edgeSize = with(density) { 72.dp.toPx() }
                val scrollDelta = when {
                    pointerYInRoot < viewport.top + edgeSize ->
                        -(((viewport.top + edgeSize - pointerYInRoot) / edgeSize).coerceIn(0f, 1f) * 20f)
                    pointerYInRoot > viewport.bottom - edgeSize ->
                        (((pointerYInRoot - (viewport.bottom - edgeSize)) / edgeSize).coerceIn(0f, 1f) * 20f)
                    else -> 0f
                }
                if (scrollDelta != 0f) {
                    listState.scrollBy(scrollDelta)
                    delay(24L)
                    moveDraggedModuleToPointer()
                }
            }
            delay(16L)
        }
    }

    val c = Glass.colors

    LazyColumn(
        state = listState,
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .onGloballyPositioned { listBounds = it.boundsInRoot() },
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = glassBottomInset()),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        item(key = "dashboard_header") {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                DashboardHeader(onOpenHistory = onOpenHistory)
                Text(
                    "Segure um módulo e arraste para reorganizar",
                    modifier = Modifier.padding(start = 6.dp),
                    style = MaterialTheme.typography.labelMedium,
                    color = c.muted
                )
            }
        }

        items(items = moduleOrder, key = { it.id }) { module ->
            DisposableEffect(module) {
                onDispose { moduleBounds.remove(module) }
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .onGloballyPositioned { moduleBounds[module] = it.boundsInRoot() }
                    .zIndex(if (draggedModule == module) 1f else 0f)
                    .graphicsLayer {
                        val lifting = draggedModule == module
                        scaleX = if (lifting) 1.015f else 1f
                        scaleY = if (lifting) 1.015f else 1f
                    }
                    .pointerInput(module) {
                        detectDragGesturesAfterLongPress(
                            onDragStart = { position ->
                                val bounds = moduleBounds[module] ?: return@detectDragGesturesAfterLongPress
                                orderBeforeDrag = moduleOrder
                                draggedModule = module
                                pointerYInRoot = bounds.top + position.y
                                pointerToCenterOffset = bounds.center.y - pointerYInRoot
                            },
                            onDrag = { change, dragAmount ->
                                change.consume()
                                pointerYInRoot += dragAmount.y
                                moveDraggedModuleToPointer()
                            },
                            onDragEnd = { finishDrag(saveOrder = true) },
                            onDragCancel = { finishDrag(saveOrder = false) }
                        )
                    }
            ) {
                when (module) {
                    DashboardModule.USAGE -> Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        UsageSummarySection(
                            usedTimeMillisState = usedTimeMillisState,
                            dailyLimitMillis = dailyLimitMillis,
                            baseDailyLimitMillis = baseDailyLimitMillis,
                            allowanceSummary = allowanceSummary,
                            onStopForToday = {
                                val homeIntent = Intent(Intent.ACTION_MAIN).apply {
                                    addCategory(Intent.CATEGORY_HOME)
                                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                                }
                                context.startActivity(homeIntent)
                            },
                            onRequestExtraTime = { showExtraTimeDialog = true },
                            formatDuration = ::formatClock
                        )
                        if (extraTimeGrantCount > 0) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .glass(GlassShapes.Row)
                                    .padding(12.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(Icons.Rounded.Schedule, contentDescription = null, tint = c.accent, modifier = Modifier.size(18.dp))
                                Text(
                                    "Extensões usadas hoje: $extraTimeGrantCount • Limite efetivo: ${formatClock(dailyLimitMillis)}",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = c.muted
                                )
                            }
                        }
                    }
                    DashboardModule.DAILY_BLOCK -> DailyBlockCard()
                    DashboardModule.SECURITY -> SecurityLevelSection(
                        currentLevel = securityLevel,
                        isDeviceOwner = state.isDeviceOwner,
                        onSelectLevel = { newLevel ->
                            // Rebaixar a proteção exige a senha mestra.
                            if (viewModel.selectSecurityLevel(newLevel) == LevelSelection.NEEDS_PASSWORD) {
                                pendingLevelDowngrade = newLevel
                            }
                        }
                    )
                    DashboardModule.DAILY_LIMIT -> DailyLimitPickerSection(
                        currentLimitMillis = dailyLimitMillis,
                        onSelectLimit = { millis, label ->
                            // Subir o limite do dia afrouxa o autocontrole: pede senha. Reduzir é livre.
                            if (millis > dailyLimitMillis) {
                                pendingLimitIncrease = millis to label
                            } else {
                                viewModel.setDailyLimit(millis, label)
                            }
                        }
                    )
                    DashboardModule.PERMISSIONS -> PermissionsHubSection(
                        grantedCount = state.permissionsGrantedCount,
                        totalCount = 5,
                        hasLiveUpdates = state.hasLiveUpdates,
                        onEnableLiveUpdates = {
                            when (LiveUpdateManager.getSetupAction(context)) {
                                LiveUpdateSetupAction.REQUEST_NOTIFICATION_PERMISSION ->
                                    notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                                LiveUpdateSetupAction.OPEN_APP_NOTIFICATION_SETTINGS -> openAppNotificationSettings(context)
                                LiveUpdateSetupAction.OPEN_PROMOTION_SETTINGS -> LiveUpdateManager.openPromotedNotificationSettings(context)
                                LiveUpdateSetupAction.SHOW_SAMSUNG_NOW_BAR_INSTRUCTIONS -> {
                                    Toast.makeText(context, "Ative em Configurações > Tela de bloqueio e AOD > Now Bar > Ver mais.", Toast.LENGTH_LONG).show()
                                    openDeviceSettings(context)
                                }
                            }
                        },
                        hasUsageStats = state.hasUsageStats,
                        hasFocusAccessibility = state.hasFocusAccessibility,
                        hasAdmin = state.hasAdmin,
                        hasSecureSettings = state.hasSecureSettings,
                        context = context
                    )
                    DashboardModule.QUICK_ACTIONS -> QuickActionsSection(
                        grayscaleTestSecondsRemaining = state.grayscaleTestSecondsRemaining,
                        onTestGrayscale = { viewModel.testGrayscale() },
                        onChangePassword = { showChangePasswordDialog = true }
                    )
                    DashboardModule.TASKS -> TasksSummaryCard(
                        pendingTaskCount = state.pendingTaskCount,
                        activeObjectiveCount = state.activeObjectiveCount,
                        onClick = onOpenTasks
                    )
                    DashboardModule.MONEY_OWED -> MoneyOwedCard(
                        amountCents = state.moneyOwedCents,
                        onAdd = { viewModel.addMoneyOwed() },
                        onSubtract = { viewModel.subtractMoneyOwed() },
                        // Zerar a dívida apaga o compromisso acumulado: exige senha (D-03).
                        onReset = { pendingMoneyOwedReset = true }
                    )
                    DashboardModule.UNLOCKS -> UnlocksTodayCard(
                        count = state.todayUnlockCount,
                        hasUsageAccess = state.hasUsageStats,
                        onClick = {
                            if (state.hasUsageStats) onOpenHistory()
                            else context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
                        }
                    )
                }
            }
        }

        item(key = "dashboard_uninstall") {
            GlassButton(
                text = "Desinstalar Aplicativo (Exige Senha)",
                onClick = onRequestUninstall,
                kind = GlassButtonKind.Danger,
                icon = Icons.Rounded.DeleteForever,
                modifier = Modifier.fillMaxWidth()
            )
        }
        item(key = "dashboard_bottom_space") { Spacer(Modifier.height(8.dp)) }
    }

    pendingLevelDowngrade?.let { targetLevel ->
        PasswordConfirmDialog(
            title = "Reduzir proteção",
            message = "Digite a senha mestra para ativar o ${securityLevelLabel(targetLevel)}.",
            onDismiss = { pendingLevelDowngrade = null },
            onConfirmed = {
                pendingLevelDowngrade = null
                viewModel.applySecurityLevel(targetLevel)
            }
        )
    }

    pendingLimitIncrease?.let { (millis, label) ->
        PasswordConfirmDialog(
            title = "Aumentar limite diário",
            message = "Digite a senha mestra para subir o limite diário para $label. Reduzir o limite é livre.",
            onDismiss = { pendingLimitIncrease = null },
            onConfirmed = {
                pendingLimitIncrease = null
                viewModel.setDailyLimit(millis, label)
            }
        )
    }

    if (pendingMoneyOwedReset) {
        PasswordConfirmDialog(
            title = "Zerar a dívida",
            message = "Digite a senha mestra para zerar o saldo acumulado. Pagar (subtrair) continua livre.",
            onDismiss = { pendingMoneyOwedReset = false },
            onConfirmed = {
                pendingMoneyOwedReset = false
                viewModel.resetMoneyOwed()
            }
        )
    }

    if (showChangePasswordDialog) {
        ChangePasswordDialog(
            onDismiss = { showChangePasswordDialog = false },
            onChangePassword = { currentPass, newPass, callback ->
                val passwordManager = PasswordManager.getInstance(context)
                coroutineScope.launch {
                    val changed = withContext(Dispatchers.Default) { passwordManager.changePassword(currentPass, newPass) }
                    if (changed) {
                        Toast.makeText(context, "Senha alterada com sucesso!", Toast.LENGTH_SHORT).show()
                        callback(true, null)
                    } else callback(false, passwordManager.failureMessage())
                }
            }
        )
    }

    if (showExtraTimeDialog) {
        ExtraTimeDialog(
            onDismiss = { showExtraTimeDialog = false },
            onGrant = { typedPhrase ->
                coroutineScope.launch {
                    if (viewModel.grantExtraTime(typedPhrase)) showExtraTimeDialog = false
                }
            }
        )
    }
}

/** Pede a senha mestra; a verificação (PBKDF2) roda fora da main thread. */
@Composable
private fun PasswordConfirmDialog(
    title: String,
    message: String,
    onDismiss: () -> Unit,
    onConfirmed: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var password by remember { mutableStateOf("") }
    var errorText by remember { mutableStateOf<String?>(null) }
    var verifying by remember { mutableStateOf(false) }

    GlassAlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(message)
                OutlinedTextField(
                    value = password,
                    onValueChange = { password = it; errorText = null },
                    label = { Text("Senha mestra") },
                    colors = glassTextFieldColors(),
                    shape = GlassShapes.Control,
                    visualTransformation = PasswordVisualTransformation(),
                    isError = errorText != null,
                    supportingText = errorText?.let { message -> { Text(message) } },
                    singleLine = true
                )
            }
        },
        confirmButton = {
            TextButton(
                enabled = password.isNotBlank() && !verifying,
                onClick = {
                    verifying = true
                    scope.launch {
                        val ok = withContext(Dispatchers.Default) {
                            PasswordManager.getInstance(context).verifyPassword(password)
                        }
                        verifying = false
                        if (ok) onConfirmed() else errorText = PasswordManager.getInstance(context).failureMessage()
                    }
                }
            ) { Text("Confirmar") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancelar") } }
    )
}
