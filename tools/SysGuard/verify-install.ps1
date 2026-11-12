#requires -Version 7
<#
.SYNOPSIS
Teste ponta a ponta do SysGuard numa máquina real: instala em silêncio com uma senha de teste,
confere as camadas de bloqueio, os dois serviços, a recuperação e desinstala (a menos que
-KeepInstalled seja usado).

.DESCRIPTION
Precisa de um terminal ELEVADO (o teste instala serviços e altera DNS/hosts de verdade).
Use uma senha de teste descartável; se algo falhar no meio, o comando abaixo recupera a máquina
(a remoção forçada agora exige a senha, como a bandeja):

    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('teste-123456'))
    SysGuard.exe --force-uninstall --silent --password $b64

O teste para o serviço principal de propósito (sc stop) e confere que o serviço vigia o religa e a
proteção volta sozinha — é o aceite do red-team PC-13.

.EXAMPLE
pwsh -NoProfile -File tools/SysGuard/verify-install.ps1 -Password "teste-123456"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\release-out\SysGuard\SysGuard.exe'),
    [switch]$KeepInstalled
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "FALHOU: $Message" }
    Write-Host "OK: $Message"
}

function Wait-Until {
    param([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$What)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Timeout esperando: $What"
}

function Get-FilterDnsAdapters {
    [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        Where-Object {
            $_.NetworkInterfaceType -notin 'Loopback', 'Tunnel' -and
            $_.OperationalStatus -eq 'Up' -and
            ($_.GetIPProperties().GatewayAddresses | Where-Object { $_.Address -and $_.Address.ToString() -notin '0.0.0.0', '::' })
        } |
        ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                Dns  = @($_.GetIPProperties().DnsAddresses | ForEach-Object { $_.ToString() })
            }
        }
}

function Get-ServiceStateText {
    param([string]$Name)
    return (sc.exe query $Name | Out-String)
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
Assert-True ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'terminal elevado'

$exe = (Resolve-Path -LiteralPath $ExePath).Path
Write-Host "Testando: $exe"
Assert-True (Test-Path -LiteralPath $exe) 'exe encontrado'

$serviceName = 'SysGuardSvc'
$watchdogName = 'SysGuardWatchdogSvc'
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$watchdogKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$watchdogName"
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$dataDirectory = Join-Path $env:ProgramData 'SysGuard'
$runKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$passwordBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Password))
$wrongPasswordBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('senha-errada-xyz'))
$newPasswordBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('senha-nova-123456'))

function Invoke-SysGuard {
    param([string[]]$Arguments)
    $process = Start-Process -FilePath $exe -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

try {
    Write-Host '--- Instalando (silencioso) ---'
    $installCode = Invoke-SysGuard @('--setup', '--silent', '--password', $passwordBase64)
    Assert-True ($installCode -eq 0) 'instalação retornou 0'

    Write-Host '--- Verificando os dois serviços ---'
    Assert-True (Test-Path -LiteralPath $serviceKey) 'serviço principal registrado'
    Assert-True (Test-Path -LiteralPath $watchdogKey) 'serviço vigia registrado'
    Assert-True ((Get-ItemProperty -LiteralPath $serviceKey).Start -eq 2) 'serviço principal com início automático'
    Assert-True ((Get-ItemProperty -LiteralPath $watchdogKey).Start -eq 2) 'serviço vigia com início automático'
    Wait-Until { (Get-ServiceStateText $serviceName) -match 'RUNNING' } 30 'serviço principal em execução'
    Wait-Until { (Get-ServiceStateText $watchdogName) -match 'RUNNING' } 30 'serviço vigia em execução'

    Write-Host '--- Verificando recuperação do SCM ---'
    $mainFailure = (sc.exe qfailure $serviceName | Out-String)
    $watchdogFailure = (sc.exe qfailure $watchdogName | Out-String)
    Assert-True ($mainFailure -match '5000' -and $mainFailure -match '10000' -and $mainFailure -match '30000') 'recuperação do principal em 5s/10s/30s'
    Assert-True ($mainFailure -match '86400') 'recuperação do principal com reset diário'
    Assert-True ($watchdogFailure -match '5000') 'recuperação do vigia configurada'

    Write-Host '--- Verificando bloqueio de rede ---'
    $hosts = Get-Content -LiteralPath $hostsPath -Raw
    Assert-True ($hosts -match '>>> SysGuard protection') 'bloco do hosts presente'
    Assert-True ($hosts -match '0\.0\.0\.0 pornhub\.com') 'domínio adulto no hosts'
    $addresses = [System.Net.Dns]::GetHostAddresses('pornhub.com')
    Assert-True (-not ($addresses | Where-Object { $_.AddressFamily -eq 'InterNetwork' -and -not $_.Equals([System.Net.IPAddress]::Any) })) 'pornhub.com resolve para 0.0.0.0'
    $filterAdapters = Get-FilterDnsAdapters
    Assert-True ($filterAdapters.Count -gt 0) 'adaptador com rota padrão encontrado'
    foreach ($adapter in $filterAdapters) {
        Assert-True ($adapter.Dns -contains '185.228.168.10') "DNS do filtro Adult aplicado em '$($adapter.Name)'"
    }

    Write-Host '--- Verificando políticas de navegador ---'
    Assert-True ((Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Google\Chrome' -Name DnsOverHttpsMode -ErrorAction SilentlyContinue).DnsOverHttpsMode -eq 'off') 'Chrome sem DoH'
    Assert-True ((Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' -Name ForceBingSafeSearch -ErrorAction SilentlyContinue).ForceBingSafeSearch -eq 2) 'Edge com SafeSearch estrito'
    Assert-True ((Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS' -Name Enabled -ErrorAction SilentlyContinue).Enabled -eq 0) 'Firefox sem DoH'
    Assert-True ((Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Google\Chrome' -Name ForceGoogleSafeSearch -ErrorAction SilentlyContinue).ForceGoogleSafeSearch -eq 1) 'Chrome com SafeSearch de busca'
    Assert-True ($null -eq (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Google\Chrome' -Name ForceYouTubeRestrict -ErrorAction SilentlyContinue).ForceYouTubeRestrict) 'Chrome sem modo restrito do YouTube (HKLM)'
    Assert-True ($null -eq (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' -Name ForceYouTubeRestrict -ErrorAction SilentlyContinue).ForceYouTubeRestrict) 'Edge sem modo restrito do YouTube (HKLM)'
    Assert-True ($null -eq (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Policies\Google\Chrome' -Name ForceYouTubeRestrict -ErrorAction SilentlyContinue).ForceYouTubeRestrict) 'Chrome sem modo restrito do YouTube (HKCU)'
    Assert-True ($null -eq (Get-ItemProperty -Path 'HKCU:\SOFTWARE\Policies\Microsoft\Edge' -Name ForceYouTubeRestrict -ErrorAction SilentlyContinue).ForceYouTubeRestrict) 'Edge sem modo restrito do YouTube (HKCU)'

    Write-Host '--- Verificando autostart e controle ---'
    Assert-True ((Get-ItemProperty -LiteralPath $runKey -Name SysGuard -ErrorAction SilentlyContinue).SysGuard -match 'SysGuard\.exe') 'bandeja no início automático'
    Assert-True ((Invoke-SysGuard @('--status', '--silent')) -eq 0) 'pipe do serviço responde PROTEGIDO'

    Write-Host '--- Verificando que a remoção forçada exige senha (PC-14) ---'
    $forcedWithoutPassword = Invoke-SysGuard @('--force-uninstall', '--silent')
    Assert-True ($forcedWithoutPassword -eq 5) 'remoção forçada sem senha recusada (código 5)'
    $forcedWithWrongPassword = Invoke-SysGuard @('--force-uninstall', '--silent', '--password', $wrongPasswordBase64)
    Assert-True ($forcedWithWrongPassword -eq 5) 'remoção forçada com senha errada recusada (código 5)'
    Assert-True (Test-Path -LiteralPath $serviceKey) 'serviço continua instalado após tentativa sem senha'
    Assert-True ((Invoke-SysGuard @('--status', '--silent')) -eq 0) 'proteção continua ativa após tentativa sem senha'

    Write-Host '--- Verificando que --reset exige a senha atual (PC-14) ---'
    $resetWithoutCurrent = Invoke-SysGuard @('--setup', '--silent', '--password', $newPasswordBase64, '--reset')
    Assert-True ($resetWithoutCurrent -eq 5) 'reset sem senha atual recusado (código 5)'

    Write-Host '--- Verificando o vigia: sc stop não desliga a proteção (PC-13) ---'
    sc.exe stop $serviceName | Out-Null
    Wait-Until { (Get-ServiceStateText $serviceName) -match 'RUNNING' } 45 'vigia religou o serviço principal'
    Wait-Until { (Invoke-SysGuard @('--status', '--silent')) -eq 0 } 45 'proteção reaplicada após religar'
    Assert-True ((Get-Content -LiteralPath $hostsPath -Raw) -match '>>> SysGuard protection') 'bloco do hosts presente após religar'

    if ($KeepInstalled) {
        Write-Host '--- Instalação mantida (-KeepInstalled) ---'
        exit 0
    }

    Write-Host '--- Desinstalando (senha correta) ---'
    $uninstallCode = Invoke-SysGuard @('--uninstall', '--silent', '--password', $passwordBase64)
    Assert-True ($uninstallCode -eq 0) 'desinstalação retornou 0'
    Wait-Until { -not (Test-Path -LiteralPath $serviceKey) } 40 'serviço principal removido'
    Wait-Until { -not (Test-Path -LiteralPath $watchdogKey) } 40 'serviço vigia removido'
    Wait-Until { -not (Test-Path -LiteralPath $dataDirectory) } 40 'pasta de instalação removida'
    Wait-Until { -not ((Get-Content -LiteralPath $hostsPath -Raw) -match 'SysGuard') } 30 'hosts restaurado'
    Assert-True ((Get-Content -LiteralPath $hostsPath -Raw) -match 'localhost') 'hosts continua válido'
    Wait-Until {
        $filtered = Get-FilterDnsAdapters
        -not ($filtered | Where-Object { $_.Dns -contains '185.228.168.10' })
    } 30 'DNS original restaurado'
    Assert-True ($null -eq (Get-ItemProperty -LiteralPath $runKey -Name SysGuard -ErrorAction SilentlyContinue)) 'início automático removido'
    Assert-True ($null -eq (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Google\Chrome' -Name DnsOverHttpsMode -ErrorAction SilentlyContinue)) 'política do Chrome restaurada'
    $finalStatus = Invoke-SysGuard @('--status', '--silent')
    Assert-True ($finalStatus -ne 0) 'serviço já não responde'

    Write-Host ''
    Write-Host 'TESTE PONTA A PONTA: OK'
}
catch {
    Write-Host ''
    Write-Host ('TESTE PONTA A PONTA: FALHOU - ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ('Para limpar a máquina:  SysGuard.exe --force-uninstall --silent --password ' + $passwordBase64 + '   (terminal elevado)')
    throw
}
