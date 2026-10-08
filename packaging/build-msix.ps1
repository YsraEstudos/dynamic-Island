<#
.SYNOPSIS
Publica o Island.App (win-x64, framework-dependent), monta o pacote MSIX e, se houver .pfx, assina.

.DESCRIPTION
Etapas:
  1. Verifica dotnet, makeappx.exe e (se for assinar) signtool.exe do Windows SDK.
  2. dotnet publish src/Island.App -c <Configuration> -r win-x64 --self-contained false
  3. Monta o layout: saída do publish + AppxManifest.xml (cópia de Package.appxmanifest) + Assets\
  4. makeappx pack -> packaging\out\DynamicIsland_<versão>_x64.msix
  5. Assina com signtool (SHA256) se o .pfx existir. Sem .pfx o pacote sai NÃO assinado e não instala.

.PARAMETER PfxPath
Certificado para assinar. Padrão: packaging\DynamicIslandDev.pfx (gerado por create-dev-cert.ps1).

.PARAMETER PfxPassword
Senha do .pfx como SecureString. Se o .pfx existir e a senha não for passada, ela é pedida de forma segura.

.PARAMETER SkipSign
Gera o pacote sem assinar, mesmo que o .pfx exista.

.EXAMPLE
.\build-msix.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$OutputDir = (Join-Path $PSScriptRoot 'out'),

    [string]$PfxPath = (Join-Path $PSScriptRoot 'DynamicIslandDev.pfx'),

    [SecureString]$PfxPassword,

    [switch]$SkipSign
)

$ErrorActionPreference = 'Stop'

$repoRoot       = Split-Path -Parent $PSScriptRoot
$projectPath    = Join-Path $repoRoot 'src\Island.App\Island.App.csproj'
$manifestSource = Join-Path $PSScriptRoot 'Package.appxmanifest'
$assetsSource   = Join-Path $PSScriptRoot 'Assets'
$sdkBinRoot     = 'C:\Program Files (x86)\Windows Kits\10\bin'

function Find-SdkTool([string]$ToolName) {
    # Pega a versão mais alta de Windows SDK instalada que tenha a ferramenta em x64.
    if (-not (Test-Path -LiteralPath $sdkBinRoot)) { return $null }
    return Get-ChildItem -LiteralPath $sdkBinRoot -Directory |
        Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$ToolName" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

# --- 1. Pré-requisitos (antes de qualquer build) ---------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet não encontrado no PATH. Instale o .NET 10 SDK (o global.json fixa a versão 10.0.100 com rollForward latestFeature).'
}

[xml]$manifest = Get-Content -LiteralPath $manifestSource -Raw
$version = $manifest.Package.Identity.Version

$makeappx = Find-SdkTool 'makeappx.exe'
if (-not $makeappx) {
    throw "makeappx.exe não encontrado em '$sdkBinRoot\<versão>\x64'. Instale o Windows SDK (Visual Studio Installer, componente 'Windows 10/11 SDK', ou developer.microsoft.com/windows/downloads/windows-sdk) e execute o script de novo."
}

$willSign = (-not $SkipSign) -and (Test-Path -LiteralPath $PfxPath)
$signtool = $null
if ($willSign) {
    $signtool = Find-SdkTool 'signtool.exe'
    if (-not $signtool) {
        throw "signtool.exe não encontrado em '$sdkBinRoot\<versão>\x64'. Instale o Windows SDK ou use -SkipSign."
    }
}

Write-Host "Ferramentas: makeappx = $makeappx"
if ($willSign) { Write-Host "             signtool = $signtool" }

# --- 2. Publish -------------------------------------------------------------------------------

$publishDir = Join-Path $OutputDir 'publish'
$layoutDir  = Join-Path $OutputDir 'layout'
$msixPath   = Join-Path $OutputDir "DynamicIsland_${version}_x64.msix"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Remove-Item -LiteralPath $publishDir, $layoutDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publicando Island.App ($Configuration, win-x64, framework-dependent)..."
& dotnet publish $projectPath -c $Configuration -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }

# --- 3. Layout do pacote ----------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $layoutDir | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $layoutDir -Recurse -Force
# makeappx exige o manifesto com este nome exato na raiz do layout.
Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $layoutDir 'AppxManifest.xml') -Force
Copy-Item -LiteralPath $assetsSource -Destination $layoutDir -Recurse -Force

if (-not (Test-Path -LiteralPath (Join-Path $layoutDir 'DynamicIsland.exe'))) {
    throw "DynamicIsland.exe não está no layout. Verifique a saída do publish em '$publishDir'."
}

# --- 4. Empacotamento -------------------------------------------------------------------------

Write-Host "Gerando $msixPath ..."
& $makeappx pack /d $layoutDir /p $msixPath /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw "makeappx pack falhou (código $LASTEXITCODE)." }

# --- 5. Assinatura ----------------------------------------------------------------------------

if ($SkipSign) {
    Write-Host 'Assinatura ignorada (-SkipSign). O pacote não instala sem assinatura confiável.'
}
elseif (-not $willSign) {
    Write-Warning "Sem certificado em '$PfxPath': o pacote foi gerado NÃO assinado e não instala. Rode create-dev-cert.ps1 e depois este script de novo."
}
else {
    if (-not $PfxPassword) {
        $PfxPassword = Read-Host 'Senha do .pfx' -AsSecureString
    }

    # signtool só aceita a senha em texto puro via /p; o valor fica na memória só pelo tempo da chamada.
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
    try {
        $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        & $signtool sign /fd SHA256 /f $PfxPath /p $plainPassword $msixPath | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "signtool sign falhou (código $LASTEXITCODE). Confira a senha do .pfx." }
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        $plainPassword = $null
    }
    Write-Host 'Pacote assinado.'
}

Write-Host ''
Write-Host "Pacote gerado: $msixPath"
Write-Host 'Instalação (manual, não executada por este script): Add-AppxPackage -Path "<caminho do .msix>"'
