package com.example.focusapp

import android.app.admin.DevicePolicyManager
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.compose.BackHandler
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.SystemBarStyle
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.*
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Checklist
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.*
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.example.focusapp.admin.AdminPolicyReceiver
import com.example.focusapp.display.GrayscaleController
import com.example.focusapp.service.GuardWatchdogService
import com.example.focusapp.security.PasswordManager
import com.example.focusapp.service.ScreenTimeTrackerService
import com.example.focusapp.theme.AppearanceController
import com.example.focusapp.theme.AppearanceMode
import com.example.focusapp.theme.AppearanceStore
import com.example.focusapp.theme.FocusAppTheme
import com.example.focusapp.theme.LocalAppearance
import com.example.focusapp.ui.glass.GlassNavBar
import com.example.focusapp.ui.glass.GlassNavItem
import com.example.focusapp.ui.glass.GlassStage
import com.example.focusapp.ui.FocusDashboardScreen
import com.example.focusapp.ui.ProductivityScreen
import com.example.focusapp.ui.SettingsScreen
import com.example.focusapp.ui.UnifiedHistoryScreen
import com.example.focusapp.ui.PasswordSetupDialog
import com.example.focusapp.ui.UninstallDialog
import com.example.focusapp.data.ProductivityRepository
import com.example.focusapp.data.UnifiedHistoryRepository
import com.example.focusapp.data.HistoryCategory

private enum class MainDestination { DASHBOARD, HISTORY, PRODUCTIVITY, SETTINGS }

class MainActivity : ComponentActivity() {
    private val destinationState = mutableStateOf(MainDestination.DASHBOARD)

    override fun onPause() {
        ScreenTimeTrackerService.notifyAppForeground(this, foreground = false)
        super.onPause()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        destinationState.value = destinationFor(intent)

        val passwordManager = PasswordManager.getInstance(this)

        setContent {
            var appearance by remember { mutableStateOf(AppearanceStore.load(this@MainActivity)) }
            val appearanceController = remember(appearance) {
                AppearanceController(appearance) { newMode ->
                    AppearanceStore.save(this@MainActivity, newMode)
                    appearance = newMode
                }
            }
            // Ícones das barras do sistema acompanham o modo (o claro precisa de ícones escuros).
            DisposableEffect(appearance) {
                val transparent = Color.Transparent.toArgb()
                val barStyle = if (appearance == AppearanceMode.LIGHT) {
                    SystemBarStyle.light(transparent, transparent)
                } else {
                    SystemBarStyle.dark(transparent)
                }
                enableEdgeToEdge(statusBarStyle = barStyle, navigationBarStyle = barStyle)
                onDispose { }
            }
            FocusAppTheme(mode = appearance) {
              CompositionLocalProvider(LocalAppearance provides appearanceController) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = Color.Transparent,
                    contentColor = MaterialTheme.colorScheme.onBackground
                ) {
                    var isPasswordSet by remember { mutableStateOf(passwordManager.isPasswordSet()) }
                    var showUninstallDialog by remember { mutableStateOf(false) }
                    val passwordScope = rememberCoroutineScope()
                    var destination by destinationState
                    val dashboardListState = rememberLazyListState()
                    val productivityRepository = remember { ProductivityRepository(applicationContext) }
                    val historyRepository = remember { UnifiedHistoryRepository(applicationContext) }
                    var selectedHistoryItemId by remember { mutableStateOf<Long?>(null) }
                    var selectedHistoryCategory by remember { mutableStateOf(HistoryCategory.ALL) }

                    BackHandler(enabled = destination != MainDestination.DASHBOARD) {
                        destination = MainDestination.DASHBOARD
                    }

                    // 1. Diálogo obrigatório de criação de senha na primeira execução
                    if (!isPasswordSet) {
                        PasswordSetupDialog(
                            onPasswordCreated = { newPass ->
                                // PBKDF2 é caro: fora da main thread.
                                passwordScope.launch {
                                    val saved = withContext(Dispatchers.Default) { passwordManager.setPassword(newPass) }
                                    if (saved) {
                                        isPasswordSet = true
                                        Toast.makeText(this@MainActivity, "Senha mestra criada com sucesso!", Toast.LENGTH_SHORT).show()
                                    } else {
                                        Toast.makeText(this@MainActivity, "Não foi possível salvar a senha. Tente novamente.", Toast.LENGTH_LONG).show()
                                    }
                                }
                            }
                        )
                    }

                    // 2. Palco de vidro com as abas e a barra de navegação flutuante
                    GlassStage {
                        androidx.compose.animation.AnimatedContent(
                            modifier = Modifier.fillMaxSize(),
                            targetState = destination,
                            transitionSpec = {
                                androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(220))
                                    .togetherWith(androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(160)))
                            },
                            label = "MainScreenNavigation"
                        ) { currentDestination ->
                            when (currentDestination) {
                                MainDestination.HISTORY -> UnifiedHistoryScreen(
                                    onBack = {
                                        selectedHistoryItemId = null
                                        selectedHistoryCategory = HistoryCategory.ALL
                                        destination = MainDestination.DASHBOARD
                                    },
                                    repository = historyRepository,
                                    initialItemId = selectedHistoryItemId,
                                    initialCategory = selectedHistoryCategory
                                )
                                MainDestination.PRODUCTIVITY -> ProductivityScreen(
                                    onBack = { destination = MainDestination.DASHBOARD },
                                    repository = productivityRepository,
                                    onOpenHistory = { itemId, category ->
                                        selectedHistoryItemId = itemId
                                        selectedHistoryCategory = category
                                        destination = MainDestination.HISTORY
                                    }
                                )
                                MainDestination.SETTINGS -> SettingsScreen()
                                MainDestination.DASHBOARD -> FocusDashboardScreen(
                                    onRequestUninstall = { showUninstallDialog = true },
                                    onOpenHistory = {
                                        selectedHistoryItemId = null
                                        selectedHistoryCategory = HistoryCategory.ALL
                                        destination = MainDestination.HISTORY
                                    },
                                    onOpenTasks = { destination = MainDestination.PRODUCTIVITY },
                                    listState = dashboardListState
                                )
                            }
                        }

                        val navItems = remember {
                            listOf(
                                GlassNavItem("Início", Icons.Rounded.Home),
                                GlassNavItem("Tarefas", Icons.Rounded.Checklist),
                                GlassNavItem("Histórico", Icons.Rounded.History),
                                GlassNavItem("Ajustes", Icons.Rounded.Settings)
                            )
                        }
                        val destinationOrder = remember {
                            listOf(
                                MainDestination.DASHBOARD,
                                MainDestination.PRODUCTIVITY,
                                MainDestination.HISTORY,
                                MainDestination.SETTINGS
                            )
                        }
                        GlassNavBar(
                            modifier = Modifier.align(Alignment.BottomCenter),
                            items = navItems,
                            selectedIndex = destinationOrder.indexOf(destination),
                            onSelect = { index ->
                                val target = destinationOrder[index]
                                if (target == MainDestination.HISTORY) {
                                    selectedHistoryItemId = null
                                    selectedHistoryCategory = HistoryCategory.ALL
                                }
                                destination = target
                            }
                        )
                    }

                    // 3. Diálogo de Desinstalação protegida por senha
                    if (showUninstallDialog) {
                        UninstallDialog(
                            onDismiss = { showUninstallDialog = false },
                            onPasswordValidated = { enteredPass ->
                                passwordScope.launch {
                                    val ok = withContext(Dispatchers.Default) { passwordManager.verifyPassword(enteredPass) }
                                    if (ok) {
                                        showUninstallDialog = false
                                        Toast.makeText(this@MainActivity, "Senha confirmada. Desinstalando...", Toast.LENGTH_SHORT).show()
                                        performSafeUninstall()
                                    } else {
                                        Toast.makeText(this@MainActivity, passwordManager.failureMessage(), Toast.LENGTH_LONG).show()
                                    }
                                }
                            }
                        )
                    }
                }
              }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        destinationState.value = destinationFor(intent)
    }

    private fun destinationFor(intent: Intent): MainDestination =
        if (intent.getBooleanExtra(com.example.focusapp.widget.TaskWidgetProvider.EXTRA_OPEN_TASKS, false)) {
            MainDestination.PRODUCTIVITY
        } else {
            MainDestination.DASHBOARD
        }

    override fun onResume() {
        super.onResume()
        ScreenTimeTrackerService.notifyAppForeground(this, foreground = true)
        // Inicia o serviço depois que a Activity está visível e o app pode promover o foreground service.
        ScreenTimeTrackerService.start(this)
        // v1.7: watchdog que religa a acessibilidade e vigia admin/ADB/perfis enquanto o app estiver vivo.
        GuardWatchdogService.start(this)
        requestBatteryOptimizationExemptionIfNeeded()
    }

    private var batteryOptimizationAsked = false

    /**
     * Sem isenção de otimização de bateria o Android pode atrasar/suspender o watchdog em Doze. Pede
     * uma vez por processo (a tela do sistema é dispensável; se o usuário recusar, tudo continua
     * funcionando, só com mais atraso).
     */
    private fun requestBatteryOptimizationExemptionIfNeeded() {
        if (batteryOptimizationAsked) return
        batteryOptimizationAsked = true
        val powerManager = getSystemService(PowerManager::class.java) ?: return
        if (powerManager.isIgnoringBatteryOptimizations(packageName)) return
        runCatching {
            startActivity(
                Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
                    .setData(Uri.parse("package:$packageName"))
            )
        }
    }

    private var adminWasActiveBeforeUninstall = false

    // Se a desinstalação der certo o processo morre e este callback nunca roda; se chegou aqui, o app continua instalado.
    private val uninstallLauncher =
        registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
            restoreProtectionsAfterCancelledUninstall()
        }

    private fun performSafeUninstall() {
        val passwordManager = PasswordManager.getInstance(this)
        passwordManager.grantGracePeriod(300_000L) // Concede 5 minutos de tolerância

        // O sistema não desinstala um app com administrador ativo; as proteções voltam se o usuário cancelar.
        adminWasActiveBeforeUninstall = AdminPolicyReceiver.isAdminActive(this)
        AdminPolicyReceiver.setUninstallBlocked(this, false)
        AdminPolicyReceiver.removeAdmin(this)
        GrayscaleController.disableGrayscale(this)

        // Desinstalador oficial do Android, com retorno do resultado.
        uninstallLauncher.launch(
            Intent(Intent.ACTION_UNINSTALL_PACKAGE).apply {
                data = Uri.parse("package:$packageName")
                putExtra(Intent.EXTRA_RETURN_RESULT, true)
            }
        )
    }

    private fun restoreProtectionsAfterCancelledUninstall() {
        PasswordManager.getInstance(this).revokeGracePeriod()
        AdminPolicyReceiver.setUninstallBlocked(this, true)
        if (adminWasActiveBeforeUninstall && !AdminPolicyReceiver.isAdminActive(this)) {
            Toast.makeText(this, "Desinstalação cancelada. Reative o administrador do dispositivo.", Toast.LENGTH_LONG).show()
            startActivity(
                Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN).apply {
                    putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, AdminPolicyReceiver.getComponentName(this@MainActivity))
                    putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION, "Protege contra desinstalação sem a senha mestra.")
                }
            )
        }
        adminWasActiveBeforeUninstall = false
    }
}
