# Aplica os patches Android (Fase 1 + Guardião), compila release e publica os APKs.
# RODAR COM PERMISSÃO ELEVADA (workspace-write não deixa escrever no repo Android nem em ~/.gradle).
[CmdletBinding()]
param(
    [string]$RepoPath = 'C:\Users\israe\OneDrive\Desktop\Projetos\app',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$workspace = 'C:\Users\israe\OneDrive\Desktop\Projetos\Dynamic Island'
$evidence = Join-Path $workspace 'docs\redteam\evidence'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Start-Transcript -Path (Join-Path $evidence 'build-android-transcript.txt') -Force | Out-Null

try {
    if (-not (Test-Path -LiteralPath (Join-Path $RepoPath 'app\build.gradle.kts'))) {
        throw "Repo Android não encontrado: $RepoPath"
    }

    Write-Host '== 1/4 Aplicando patch Fase 1 (app principal) =='
    & pwsh -NoProfile -File (Join-Path $workspace 'docs\redteam\patches\android\apply.ps1') -RepoPath $RepoPath
    if ($LASTEXITCODE -ne 0) { throw "apply.ps1 falhou: $LASTEXITCODE" }

    Write-Host '== 2/4 Aplicando patch Guardião (Fase 2) =='
    & pwsh -NoProfile -File (Join-Path $workspace 'docs\redteam\patches\guardian\apply-guardian.ps1') -RepoPath $RepoPath
    if ($LASTEXITCODE -ne 0) { throw "apply-guardian.ps1 falhou: $LASTEXITCODE" }

    $env:JAVA_HOME = Join-Path $env:USERPROFILE '.gradle\jdks\eclipse_adoptium-17-amd64-windows.2'
    if (-not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe'))) {
        throw "JDK 17 não encontrado em $env:JAVA_HOME"
    }

    Write-Host '== 3/4 Gradle: testes unitários + releases =='
    $tasks = @(':app:assembleRelease', ':guardian:assembleRelease')
    if (-not $SkipTests) { $tasks = @(':app:testDebugUnitTest') + $tasks }
    Push-Location -LiteralPath $RepoPath
    try {
        & .\gradlew.bat @tasks --console=plain --stacktrace 2>&1 |
            Tee-Object -FilePath (Join-Path $evidence 'gradle-build.log')
        if ($LASTEXITCODE -ne 0) { throw "Gradle falhou (exit $LASTEXITCODE). Veja docs/redteam/evidence/gradle-build.log" }
    }
    finally {
        Pop-Location
    }

    Write-Host '== 4/4 Copiando APKs =='
    $dest = Join-Path $workspace 'release-out\redteam'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $appApk = Join-Path $RepoPath 'app\build\outputs\apk\release\app-release.apk'
    $guardApk = Join-Path $RepoPath 'guardian\build\outputs\apk\release\guardian-release.apk'
    $appOut = Join-Path $dest 'Foco-BemEstar-1.7.6-release.apk'
    $guardOut = Join-Path $dest 'Foco-Guardiao-1.0.1-release.apk'
    if (-not (Test-Path -LiteralPath $appApk)) { throw "APK do app não encontrado: $appApk" }
    if (-not (Test-Path -LiteralPath $guardApk)) { throw "APK do guardião não encontrado: $guardApk" }
    Copy-Item -LiteralPath $appApk -Destination $appOut -Force
    Copy-Item -LiteralPath $guardApk -Destination $guardOut -Force

    foreach ($apk in @($appOut, $guardOut)) {
        $item = Get-Item -LiteralPath $apk
        $hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
        Write-Host ("APK: {0} | {1:N1} MB | SHA256={2}" -f $item.FullName, ($item.Length / 1MB), $hash)
    }

    Write-Host 'BUILD OK'
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}






