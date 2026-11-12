# Instala os APKs novos no Moto G20, concede as permissões e roda os testes de aceitação.
# Não exige elevação (adb grava no aparelho; os APKs estão no workspace).
[CmdletBinding()]
param(
    [string]$Serial = 'ZF523QVD4J',
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$workspace = 'C:\Users\israe\OneDrive\Desktop\Projetos\Dynamic Island'
$apkDir = Join-Path $workspace 'release-out\redteam'
$appApk = Join-Path $apkDir 'Foco-BemEstar-1.7.6-release.apk'
$guardApk = Join-Path $apkDir 'Foco-Guardiao-1.0.1-release.apk'
$evidence = Join-Path $workspace 'docs\redteam\evidence'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$log = Join-Path $evidence 'acceptance-android.txt'
$appPkg = 'com.example.focusapp'
$guardPkg = 'com.example.focusapp.guardian'
$a11y = 'com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService'
$results = [System.Collections.Generic.List[string]]::new()

function A([string]$serial, [string]$command) { & adb.exe -s $serial shell $command 2>&1 }
function AdbRun([string]$serial, [string[]]$arguments) { & adb.exe -s $serial @arguments 2>&1 }

function Get-A11yState([string]$serial) {
    $raw = A $serial 'dumpsys accessibility'
    $bound = ($raw | Select-String -Pattern 'Bound services:.*focusapp' -Quiet)
    $enabledList = A $serial 'settings get secure enabled_accessibility_services'
    $enabled = A $serial 'settings get secure accessibility_enabled'
    [pscustomobject]@{
        Bound   = [bool]$bound
        List    = ($enabledList | Out-String).Trim()
        Enabled = ($enabled | Out-String).Trim()
    }
}

function Wait-A11yBound([string]$serial, [int]$timeoutSeconds) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds) {
        Start-Sleep -Seconds 2
        if ((Get-A11yState $serial).Bound) { return [int]((Get-Date) - $start).TotalSeconds }
    }
    return -1
}

Write-Host "== Instalação no aparelho $Serial =="
if (-not $SkipInstall) {
    foreach ($apk in @($appApk, $guardApk)) {
        if (-not (Test-Path -LiteralPath $apk)) { throw "APK não encontrado: $apk (rode apply-build-android.ps1 primeiro)" }
    }
    $installApp = AdbRun $Serial @('install', '-r', $appApk)
    $installGuard = AdbRun $Serial @('install', '-r', $guardApk)
    $results.Add("install app: $($installApp -join ' ')")
    $results.Add("install guard: $($installGuard -join ' ')")
}

Write-Host '== Permissões e grants =='
$results.Add('pm grant WRITE_SECURE_SETTINGS app: ' + ((A $Serial "pm grant $appPkg android.permission.WRITE_SECURE_SETTINGS") -join ' '))
$results.Add('pm grant WRITE_SECURE_SETTINGS guard: ' + ((A $Serial "pm grant $guardPkg android.permission.WRITE_SECURE_SETTINGS") -join ' '))
$results.Add('appops SAW guard: ' + ((A $Serial "appops set $guardPkg SYSTEM_ALERT_WINDOW allow") -join ' '))
$results.Add('appops SAW app: ' + ((A $Serial "appops set $appPkg SYSTEM_ALERT_WINDOW allow") -join ' '))
$results.Add('dpm admin guard: ' + ((A $Serial 'dpm set-active-admin com.example.focusapp.guardian/.GuardianAdminReceiver') -join ' '))
$results.Add('dpm admin app: ' + ((A $Serial 'dpm set-active-admin com.example.focusapp/.admin.AdminPolicyReceiver') -join ' '))

Write-Host '== Ligando acessibilidade e serviços =='
A $Serial "settings put secure enabled_accessibility_services $a11y" | Out-Null
A $Serial 'settings put secure accessibility_enabled 1' | Out-Null
# Abre o app (por cima da tela de bloqueio) para iniciar o GuardWatchdogService e o Bridge do guardião.
A $Serial 'am start -n com.example.focusapp/.MainActivity' | Out-Null
Start-Sleep -Seconds 3
A $Serial 'am start -n com.example.focusapp.guardian/.GuardianStatusActivity' | Out-Null
Start-Sleep -Seconds 5
A $Serial 'input keyevent KEYCODE_HOME' | Out-Null
Start-Sleep -Seconds 2

$state = Get-A11yState $Serial
$results.Add("a11y inicial: bound=$($state.Bound) enabled=$($state.Enabled) list=$($state.List)")
$guardRunning = (A $Serial 'dumpsys activity services com.example.focusapp.guardian' | Select-String -Pattern 'GuardianService' -Quiet)
$results.Add("guardião rodando: $guardRunning")
$pkgDump = A $Serial 'dumpsys package com.example.focusapp'
$debuggable = ($pkgDump | Select-String -Pattern 'DEBUGGABLE' -Quiet)
$version = ($pkgDump | Select-String -Pattern 'versionName=' | Select-Object -First 1).Line.Trim()
$results.Add("app debuggable=|1.7.1$debuggable | $version")

Write-Host '== Aceite F1: watchdog religa acessibilidade desligada (app vivo) =='
A $Serial "settings put secure enabled_accessibility_services ''" | Out-Null
A $Serial 'settings put secure accessibility_enabled 0' | Out-Null
Start-Sleep -Seconds 2
$f1 = Wait-A11yBound $Serial 40
$results.Add("F1: acessibilidade religada em $f1 s (esperado <= 20 s)")

Write-Host '== Aceite F2: guardião revive o app após force-stop =='
A $Serial 'am force-stop com.example.focusapp' | Out-Null
Start-Sleep -Seconds 2
$f2 = Wait-A11yBound $Serial 70
$results.Add("F2: acessibilidade religada após force-stop em $f2 s (esperado <= 40 s)")
$stateAfter = Get-A11yState $Serial
$results.Add("a11y pós-F2: bound=$($stateAfter.Bound) enabled=$($stateAfter.Enabled) list=$($stateAfter.List)")

Write-Host '== Aceite F3: run-as deve falhar (release não-debuggable) =='
$runAs = A $Serial 'run-as com.example.focusapp ls' 2>&1
$runAsFailed = (($runAs | Out-String) -match 'not debuggable|Package .* is not debuggable|Error')
$results.Add("F3: run-as falhou=$runAsFailed (saída: $(($runAs | Out-String).Trim()))")

Write-Host '== Aceite F4: admin/estado final =='
$admin = A $Serial 'dumpsys device_policy'
$appAdmin = ($admin | Select-String -Pattern 'com.example.focusapp/.admin.AdminPolicyReceiver' -Quiet)
$guardAdmin = ($admin | Select-String -Pattern 'com.example.focusapp.guardian/.GuardianAdminReceiver' -Quiet)
$users = A $Serial 'pm list users'
$results.Add("admins: app=$appAdmin guard=$guardAdmin")
$results.Add('users: ' + (($users | Out-String).Trim()))
$results.Add('logcat guardian/focus (últimas 80):')
$results.Add(((A $Serial 'logcat -d -t 400 -s GuardianService GuardianBoot GuardianAlarm FocusAccessibility GuardWatchdog') | Out-String).Trim())

$results | Out-File -FilePath $log -Encoding utf8
Write-Host ''
Write-Host "Resultados salvos em $log"
$results | ForEach-Object { Write-Host $_ }








