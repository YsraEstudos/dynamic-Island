#requires -Version 7
<#
.SYNOPSIS
Compila, testa e publica o SysGuard como um único SysGuard.exe (self-contained, win-x64).

.DESCRIPTION
O exe publicado não precisa de .NET instalado no computador de destino. Basta copiá-lo e executar:
o instalador se eleva (UAC), pede a senha e instala o serviço. O arquivo final é copiado para
release-out\SysGuard\SysGuard.exe.

.PARAMETER Offline
Usa somente os pacotes já baixados no cache do NuGet (para ambientes sem internet/SSL). Requer que
os pacotes e runtime packs já estejam em %USERPROFILE%\.nuget\packages.

.PARAMETER Version
Versão a estampar no assembly. Padrão: a versão do .csproj.

.EXAMPLE
pwsh -NoProfile -File .\build.ps1
pwsh -NoProfile -File .\build.ps1 -Offline
#>
[CmdletBinding()]
param(
    [switch]$Offline,
    [switch]$SkipTests,
    [switch]$SkipSelfTest,
    [string]$OutputDirectory,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$dotnetHome = Join-Path $PSScriptRoot '.dotnet-home'
New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') falhou (código $LASTEXITCODE)." }
}

$nugetArguments = @()
if ($Offline) {
    $cache = Join-Path $env:USERPROFILE '.nuget\packages'
    if (-not (Test-Path -LiteralPath $cache)) { throw "Cache do NuGet não encontrado em '$cache'." }
    $configPath = Join-Path ([IO.Path]::GetTempPath()) 'SysGuard-nuget-offline.config'
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
  <fallbackPackageFolders>
    <add key="cache" value="$cache" />
  </fallbackPackageFolders>
</configuration>
"@ | Set-Content -LiteralPath $configPath -Encoding utf8
    $nugetArguments = @('--configfile', $configPath)
}

$versionArguments = @()
if ($Version) { $versionArguments = @("-p:Version=$Version") }

$publish = Join-Path $PSScriptRoot 'dist'
Remove-Item -LiteralPath $publish -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Restaurando pacotes (build normal)...'
Invoke-Dotnet -Arguments (@('restore', 'SysGuard.csproj') + $nugetArguments + $versionArguments)

if (-not $SkipTests) {
    Write-Host 'Rodando os testes...'
    Invoke-Dotnet -Arguments (@('restore', '..\SysGuard.Tests\SysGuard.Tests.csproj') + $nugetArguments)
    Invoke-Dotnet -Arguments @('build', '..\SysGuard.Tests\SysGuard.Tests.csproj', '-c', 'Debug', '--no-restore')
    Invoke-Dotnet -Arguments @('run', '--project', '..\SysGuard.Tests\SysGuard.Tests.csproj', '-c', 'Debug', '--no-build')
}

Write-Host 'Restaurando pacotes com runtime win-x64...'
Invoke-Dotnet -Arguments (@('restore', 'SysGuard.csproj', '-r', 'win-x64', '-p:SelfContained=true') + $nugetArguments + $versionArguments)

Write-Host 'Publicando SysGuard.exe (self-contained, single-file, win-x64)...'
Invoke-Dotnet -Arguments (@('publish', 'SysGuard.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publish) + $versionArguments)

$exe = Join-Path $publish 'SysGuard.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'SysGuard.exe não foi gerado.' }

if (-not $SkipSelfTest) {
    Write-Host 'Rodando o selftest do exe publicado...'
    $log = Join-Path ([IO.Path]::GetTempPath()) 'SysGuard-selftest.log'
    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
        throw "Selftest do exe publicado falhou (código $($process.ExitCode))."
    }

    Write-Host 'Selftest OK.'
}

$finalDirectory = if ($OutputDirectory) {
    $OutputDirectory
} else {
    Join-Path $PSScriptRoot '..\..\release-out\SysGuard'
}
New-Item -ItemType Directory -Force -Path $finalDirectory | Out-Null
$final = Join-Path $finalDirectory 'SysGuard.exe'
Copy-Item -LiteralPath $exe -Destination $final -Force
$size = [math]::Round((Get-Item -LiteralPath $final).Length / 1MB, 1)
Write-Host ''
Write-Host "Pronto: $final ($size MB)"
Write-Host 'Copie esse exe para o notebook, execute uma vez e crie a senha.'
