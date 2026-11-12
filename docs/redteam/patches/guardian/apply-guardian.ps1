<#
.SYNOPSIS
    Aplica o patch do GUARDIÃO Android (Fase 2 do plano de blindagem) no repositório local do app.

.DESCRIPTION
    Copia os arquivos de docs/redteam/patches/guardian/files/** para o repositório Android:
      - arquivos novos do módulo guardian/ (Kotlin, manifest, resources, build.gradle.kts);
      - settings.gradle.kts: faz APENAS a edição mínima include(":guardian") (para não sobrescrever
        mudanças paralelas no arquivo), com backup .bak na primeira alteração.

    Idempotente: rodar de novo não altera nada (compara bytes e detecta o include já presente).
    Backup: cada arquivo alterado ganha <arquivo>.bak na primeira vez; se o .bak já existir ele é
    PRESERVADO (backup original).

.PARAMETER RepoPath
    Caminho do repositório Android. Padrão: C:\Users\israe\OneDrive\Desktop\Projetos\app

.PARAMETER DryRun
    Mostra o relatório do que seria feito, sem escrever nada.

.PARAMETER ForceSettingsCopy
    Substitui settings.gradle.kts pelo conteúdo completo de files/settings.gradle.kts (backup .bak
    continua sendo feito). Use só se a edição mínima falhar.

.EXAMPLE
    pwsh -NoProfile -File .\apply-guardian.ps1
    pwsh -NoProfile -File .\apply-guardian.ps1 -DryRun
    pwsh -NoProfile -File .\apply-guardian.ps1 -RepoPath 'D:\app'
#>
[CmdletBinding()]
param(
    [string]$RepoPath = 'C:\Users\israe\OneDrive\Desktop\Projetos\app',
    [switch]$DryRun,
    [switch]$ForceSettingsCopy
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$filesRoot = Join-Path $scriptRoot 'files'
if (-not (Test-Path -LiteralPath $filesRoot)) {
    throw "Pasta 'files' não encontrada em '$scriptRoot'. Rode o script da pasta docs/redteam/patches/guardian."
}
if ([string]::IsNullOrWhiteSpace($RepoPath) -or -not (Test-Path -LiteralPath $RepoPath)) {
    throw "Repositório Android não encontrado: '$RepoPath'."
}
$repo = (Resolve-Path -LiteralPath $RepoPath).Path

if (-not (Test-Path -LiteralPath (Join-Path $repo 'settings.gradle.kts'))) {
    throw "'$repo' não parece ser o repo Android (settings.gradle.kts ausente)."
}
if (-not (Test-Path -LiteralPath (Join-Path $repo 'app\debug.keystore'))) {
    throw "'$repo' não tem app\debug.keystore — o guardião precisa da MESMA assinatura do app principal."
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$created = New-Object System.Collections.Generic.List[string]
$updated = New-Object System.Collections.Generic.List[string]
$unchanged = New-Object System.Collections.Generic.List[string]
$backups = New-Object System.Collections.Generic.List[string]
$settingsAction = $null

function Test-BytesEqual([byte[]]$left, [byte[]]$right) {
    if ($left.Length -ne $right.Length) { return $false }
    for ($index = 0; $index -lt $left.Length; $index++) {
        if ($left[$index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Write-TargetBytes([string]$path, [byte[]]$bytes) {
    if ($DryRun) { return }
    $directory = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllBytes($path, $bytes)
}

function Backup-Once([string]$path) {
    $backup = "$path.bak"
    if (Test-Path -LiteralPath $backup) {
        $backups.Add("$backup (já existia; preservado)")
        return
    }
    if (-not $DryRun) {
        Copy-Item -LiteralPath $path -Destination $backup -Force
    }
    $backups.Add($backup)
}

Write-Host ''
Write-Host '=== Guardiao Foco (Android Fase 2) -> apply-guardian ===' -ForegroundColor Cyan
Write-Host "Repo Android : $repo"
Write-Host "Staging      : $filesRoot"
if ($DryRun) { Write-Host 'MODO         : -DryRun (nada será escrito)' -ForegroundColor Yellow }
Write-Host ''

# 1) Arquivos do módulo (tudo menos settings.gradle.kts, que tem tratamento especial).
$stagedFiles = Get-ChildItem -LiteralPath $filesRoot -Recurse -File | Sort-Object FullName
foreach ($stagedFile in $stagedFiles) {
    $relative = [System.IO.Path]::GetRelativePath($filesRoot, $stagedFile.FullName)
    if ($relative -eq 'settings.gradle.kts') { continue }

    $targetPath = Join-Path $repo $relative
    $stagedBytes = [System.IO.File]::ReadAllBytes($stagedFile.FullName)

    if (-not (Test-Path -LiteralPath $targetPath)) {
        Write-TargetBytes $targetPath $stagedBytes
        $created.Add($relative)
        continue
    }

    $currentBytes = [System.IO.File]::ReadAllBytes($targetPath)
    if (Test-BytesEqual $currentBytes $stagedBytes) {
        $unchanged.Add($relative)
        continue
    }

    Backup-Once $targetPath
    Write-TargetBytes $targetPath $stagedBytes
    $updated.Add($relative)
}

# 2) settings.gradle.kts: include idempotente, sem clobber de mudanças de outros patches.
$settingsRelative = 'settings.gradle.kts'
$settingsTarget = Join-Path $repo $settingsRelative
$settingsStaged = Join-Path $filesRoot $settingsRelative
$currentSettings = [System.IO.File]::ReadAllText($settingsTarget)

if ($ForceSettingsCopy) {
    $stagedBytes = [System.IO.File]::ReadAllBytes($settingsStaged)
    $currentBytes = [System.IO.File]::ReadAllBytes($settingsTarget)
    if (Test-BytesEqual $currentBytes $stagedBytes) {
        $settingsAction = 'inalterado (já idêntico à staging)'
        $unchanged.Add($settingsRelative)
    } else {
        Backup-Once $settingsTarget
        Write-TargetBytes $settingsTarget $stagedBytes
        $settingsAction = 'substituído pelo conteúdo completo da staging (-ForceSettingsCopy)'
        $updated.Add($settingsRelative)
    }
} elseif ($currentSettings.Contains('include(":guardian")')) {
    $settingsAction = 'inalterado (já contém include(":guardian"))'
    $unchanged.Add($settingsRelative)
} else {
    $usesCrlf = $currentSettings.Contains("`r`n")
    $eol = if ($usesCrlf) { "`r`n" } else { "`n" }
    $appInclude = [regex]::Match($currentSettings, '(?m)^[ \t]*include\(":app"\)[ \t]*$')
    if ($appInclude.Success) {
        $insertAt = $appInclude.Index + $appInclude.Length
        $newSettings = $currentSettings.Substring(0, $insertAt) + $eol +
            '// Guardião (Fase 2): segundo APK que sobrevive ao force-stop do app principal e religa o bloqueio.' + $eol +
            'include(":guardian")' +
            $currentSettings.Substring($insertAt)
    } elseif ($currentSettings.EndsWith("`n")) {
        $newSettings = $currentSettings + 'include(":guardian")' + $eol
    } else {
        $newSettings = $currentSettings + $eol + 'include(":guardian")' + $eol
    }
    Backup-Once $settingsTarget
    if (-not $DryRun) {
        [System.IO.File]::WriteAllText($settingsTarget, $newSettings, $utf8NoBom)
    }
    $settingsAction = 'editado: include(":guardian") adicionado logo após include(":app")'
    $updated.Add($settingsRelative)
}

# 3) Sanidade pós-cópia (só no modo real).
if (-not $DryRun) {
    $expectedFiles = @(
        'guardian\build.gradle.kts',
        'guardian\src\main\AndroidManifest.xml',
        'guardian\src\main\java\com\example\focusapp\guardian\GuardianService.kt',
        'guardian\src\main\res\values\strings.xml'
    )
    foreach ($expected in $expectedFiles) {
        $expectedPath = Join-Path $repo $expected
        if (-not (Test-Path -LiteralPath $expectedPath)) {
            throw "Sanidade falhou: esperava '$expectedPath' depois da cópia."
        }
    }
    $settingsAfter = [System.IO.File]::ReadAllText($settingsTarget)
    if (-not $settingsAfter.Contains('include(":guardian")')) {
        throw 'Sanidade falhou: settings.gradle.kts ficou sem include(":guardian").'
    }
}

# 4) Aviso se o patch da Fase 1 (app principal) ainda não estiver aplicado.
$mainPatchMarkers = @(
    (Join-Path $repo 'app\src\main\java\com\example\focusapp\guard\ProtectionStateProvider.kt'),
    (Join-Path $repo 'app\src\main\java\com\example\focusapp\guard\ReviveService.kt')
)
$missingMarkers = @($mainPatchMarkers | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missingMarkers.Count -gt 0) {
    Write-Host ''
    Write-Warning 'Patch da Fase 1 do app principal NÃO parece aplicado (faltam ProtectionStateProvider/ReviveService).'
    Write-Warning 'O guardião compila, mas o revive cai no fallback (settings + MainActivity) e os acks ficam sem token FCM.'
    foreach ($marker in $missingMarkers) { Write-Warning "  ausente: $marker" }
}

# 5) Resumo.
Write-Host ''
Write-Host '--- Resumo ---' -ForegroundColor Cyan
Write-Host ("Criados   : {0}" -f $created.Count)
foreach ($item in $created) { Write-Host "  + $item" -ForegroundColor Green }
Write-Host ("Atualizados: {0}" -f $updated.Count)
foreach ($item in $updated) { Write-Host "  ~ $item" -ForegroundColor Yellow }
Write-Host ("Sem mudança: {0}" -f $unchanged.Count)
foreach ($item in $unchanged) { Write-Host "  = $item" }
Write-Host ("Backups    : {0}" -f $backups.Count)
foreach ($item in $backups) { Write-Host "  . $item" }
Write-Host ''
Write-Host "settings.gradle.kts: $settingsAction"
Write-Host ''
Write-Host 'Próximos passos (ver README.md desta pasta para o roteiro completo):'
Write-Host '  1) cd <repo>; .\gradlew.bat :app:assembleRelease :guardian:assembleRelease'
Write-Host '  2) adb install -r <app-release.apk> ; adb install -r <guardian-release.apk>  (app principal PRIMEIRO)'
Write-Host '  3) grants de ADB: WRITE_SECURE_SETTINGS nos dois, SYSTEM_ALERT_WINDOW + admin no guardião'
Write-Host '  4) adb shell am start -n com.example.focusapp.guardian/.GuardianStatusActivity'
Write-Host '  5) teste de aceite: adb shell am force-stop com.example.focusapp -> a a11y volta em <= 30 s'
Write-Host ''
if ($DryRun) {
    Write-Host 'Dry-run concluído: nenhum arquivo foi escrito.' -ForegroundColor Yellow
} else {
    Write-Host 'Patch do guardião aplicado.' -ForegroundColor Green
}
