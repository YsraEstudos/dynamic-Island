#requires -Version 7
<#
.SYNOPSIS
Publica uma nova versão: sobe o número, compila, gera o .zip, envia o código ao GitHub e cria a Release.

.DESCRIPTION
Um comando só. Os apps instalados veem a Release na próxima verificação (menu da ilha -> Check for updates) e se atualizam sozinhos.
Sem certificado, sem senha. Precisa de: dotnet, git e gh (GitHub CLI, já logado com `gh auth login`).

.PARAMETER Version
Número da nova versão (ex.: 0.5.0). Sem ele, sobe o último número (0.4.2 -> 0.4.3).

.EXAMPLE
.\release.ps1
.\release.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$props = Join-Path $PSScriptRoot 'Directory.Build.props'
$text = Get-Content -LiteralPath $props -Raw
if ($text -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') { throw 'Não achei <Version> em Directory.Build.props.' }

if (-not $Version) { $Version = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida '$Version'. Use o formato 1.2.3." }
$tag = "v$Version"

if (git tag --list $tag) { throw "A tag $tag já existe." }

$text = $text -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
Set-Content -LiteralPath $props -Value $text -NoNewline

$out = Join-Path $PSScriptRoot 'release-out'
$publish = Join-Path $out 'publish'
$zip = Join-Path $out "DynamicIsland-$Version-win-x64.zip"
Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "Compilando $tag..."
dotnet publish src/Island.App/Island.App.csproj -c Release -r win-x64 --self-contained false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

git add -A
git commit -m "Release $tag"
if ($LASTEXITCODE -ne 0) { throw 'git commit falhou.' }
git tag $tag
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw 'git push falhou.' }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw 'git push da tag falhou.' }

gh release create $tag $zip --title $tag --generate-notes
if ($LASTEXITCODE -ne 0) { throw 'gh release create falhou.' }

Write-Host "Pronto: $tag publicada. Os apps instalados se atualizam pelo menu da ilha."
