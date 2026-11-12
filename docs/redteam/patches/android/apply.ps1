<#
.SYNOPSIS
    Aplica o patch Android v1.7 (blindagem Fase 1) no repositório do app.

.DESCRIPTION
    Copia os arquivos completos de `files/` para o repositório indicado, preservando o resto da árvore.
    Regras de segurança:
      - só arquivos listados em `files/` são tocados; NADA é apagado;
      - arquivo existente e diferente recebe backup `<arquivo>.bak-<timestamp>` antes da cópia;
      - arquivo idêntico é ignorado (idempotente);
      - valida que o destino é o repo Android (app/build.gradle.kts + app/src/main/AndroidManifest.xml);
      - não roda gradle, não mexe no celular, não lê/grava segredos.

.PARAMETER RepoPath
    Caminho do repositório Android. Padrão: C:\Users\israe\OneDrive\Desktop\Projetos\app

.PARAMETER DryRun
    Mostra o que seria feito, sem escrever nada.

.EXAMPLE
    pwsh -NoProfile -File .\apply.ps1
    pwsh -NoProfile -File .\apply.ps1 -RepoPath C:\Users\israe\OneDrive\Desktop\Projetos\app -DryRun
#>
[CmdletBinding()]
param(
    [string]$RepoPath = 'C:\Users\israe\OneDrive\Desktop\Projetos\app',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$filesRoot = Join-Path $scriptRoot 'files'
if (-not (Test-Path -LiteralPath $filesRoot)) {
    throw "Pasta files/ não encontrada: $filesRoot"
}
if (-not (Test-Path -LiteralPath (Join-Path $RepoPath 'app\build.gradle.kts')) -or
    -not (Test-Path -LiteralPath (Join-Path $RepoPath 'app\src\main\AndroidManifest.xml'))) {
    throw "RepoPath inválido: $RepoPath (esperado um repo Android com app/build.gradle.kts e app/src/main/AndroidManifest.xml)"
}

$resolvedRepo = (Resolve-Path -LiteralPath $RepoPath).Path
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$rows = [System.Collections.Generic.List[object]]::new()

$sourceFiles = Get-ChildItem -LiteralPath $filesRoot -Recurse -File | Sort-Object FullName
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($filesRoot.Length).TrimStart('\')
    $target = Join-Path $resolvedRepo $relative
    $targetDir = Split-Path -Parent $target
    $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash

    if (Test-Path -LiteralPath $target) {
        $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($sourceHash -eq $targetHash) {
            $rows.Add([pscustomobject]@{ Arquivo = $relative; Acao = 'inalterado'; Backup = '' })
            continue
        }
        $backup = "$target.bak-$timestamp"
        if (-not $DryRun) {
            Copy-Item -LiteralPath $target -Destination $backup -Force
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        $rows.Add([pscustomobject]@{ Arquivo = $relative; Acao = 'alterado'; Backup = $backup })
    }
    else {
        if (-not $DryRun) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        $rows.Add([pscustomobject]@{ Arquivo = $relative; Acao = 'novo'; Backup = '' })
    }
}

Write-Host ''
Write-Host "Repo Android: $resolvedRepo"
if ($DryRun) { Write-Host 'MODO DRY-RUN: nada foi escrito.' -ForegroundColor Yellow }
Write-Host ''
$rows | Format-Table -AutoSize | Out-String | Write-Host

$newCount = @($rows | Where-Object { $_.Acao -eq 'novo' }).Count
$changedCount = @($rows | Where-Object { $_.Acao -eq 'alterado' }).Count
$sameCount = @($rows | Where-Object { $_.Acao -eq 'inalterado' }).Count
Write-Host ("Resumo: {0} novo(s), {1} alterado(s), {2} inalterado(s)." -f $newCount, $changedCount, $sameCount)
if ($changedCount -gt 0) {
    Write-Host "Backups: sufixo .bak-$timestamp ao lado de cada arquivo alterado."
}
Write-Host ''
Write-Host 'Próximos passos (não executados por este script):'
Write-Host '  1) $env:JAVA_HOME = "$HOME\.gradle\jdks\eclipse_adoptium-17-amd64-windows.2"'
Write-Host '  2) .\gradlew.bat :app:testDebugUnitTest   # testes unitários novos'
Write-Host '  3) .\gradlew.bat :app:assembleRelease     # APK não-debuggable'
Write-Host '  4) adb install -r app\build\outputs\apk\release\app-release.apk'
Write-Host '  5) adb shell pm grant com.example.focusapp android.permission.WRITE_SECURE_SETTINGS'
Write-Host ''
Write-Host 'Reverter: copiar de volta cada *.bak-<timestamp> (use o resumo acima) ou git checkout -- . no repo.'
