# Teste ponta a ponta do bloqueio do celular: envia um FCM real (bloqueio adulto de N segundos),
# escuta o ack no ntfy.sh e mede o watchdog (F1) e o guardião (F2) com proteção ATIVA.
# Rodar com permissão elevada (rede/TLS fora do sandbox + acesso ao token DPAPI do usuário).
[CmdletBinding()]
param(
    [string]$Serial = 'ZF523QVD4J',
    [int]$AdultSeconds = 300,
    [string]$KeyPath = "$env:LOCALAPPDATA\DynamicIsland\fcm-service-account.json",
    [string]$SettingsPath = "$env:LOCALAPPDATA\DynamicIsland\settings.json",
    [string]$TokenOverride = '',
    [switch]$AdultOnly
)

$ErrorActionPreference = 'Stop'
$workspace = 'C:\Users\israe\OneDrive\Desktop\Projetos\Dynamic Island'
$evidence = Join-Path $workspace 'docs\redteam\evidence'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$logPath = Join-Path $evidence 'fcm-e2e-android.txt'
$out = [System.Collections.Generic.List[string]]::new()
function Say([string]$text) { $out.Add($text); Write-Host $text }
function AdbShell([string]$command) { & adb.exe -s $Serial shell $command 2>&1 }
function A11y {
    $dump = (AdbShell 'dumpsys accessibility') | Out-String
    [pscustomobject]@{
        Bound = ($dump -match 'Bound services:\{Service')
        EnabledList = ((AdbShell 'settings get secure enabled_accessibility_services') | Out-String).Trim()
        Enabled = ((AdbShell 'settings get secure accessibility_enabled') | Out-String).Trim()
    }
}
function WaitBound([int]$timeoutSeconds) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds) {
        Start-Sleep -Seconds 2
        if ((A11y).Bound) { return [int]((Get-Date) - $start).TotalSeconds }
    }
    return -1
}
function B64Url([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

try {
    # 1) token do celular (DPAPI da sessão do usuário, salvo override explícito)
    if ($TokenOverride) {
        $token = $TokenOverride
        Say "token do celular informado (len=$($token.Length), prefixo $($token.Substring(0,6))...)"
    }
    else {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        $protected = [string]$settings.phoneFcmToken
        if (-not $protected.StartsWith('dpapi:v1:')) { throw 'Token do celular não está protegido por DPAPI.' }
        $tokenBytes = [Convert]::FromBase64String($protected.Substring('dpapi:v1:'.Length))
        $token = [Text.Encoding]::UTF8.GetString([System.Security.Cryptography.ProtectedData]::Unprotect(
                $tokenBytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
        if ([string]::IsNullOrWhiteSpace($token)) { throw 'Token do celular vazio no settings.json.' }
        Say "token do celular lido (len=$($token.Length), prefixo $($token.Substring(0,6))...)"
    }

    # 2) chave da service account e JWT
    $sa = Get-Content -LiteralPath $KeyPath -Raw | ConvertFrom-Json
    $privateKeyPem = [string]$sa.private_key
    if ($privateKeyPem.StartsWith('dpapi:v1:')) {
        $keyBytes = [Convert]::FromBase64String($privateKeyPem.Substring('dpapi:v1:'.Length))
        $privateKeyPem = [Text.Encoding]::UTF8.GetString([System.Security.Cryptography.ProtectedData]::Unprotect(
                $keyBytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
        Say 'chave privada do FCM desprotegida via DPAPI.'
    }
    $now = [DateTimeOffset]::UtcNow
    $header = B64Url([Text.Encoding]::UTF8.GetBytes((@{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress)))
    $claims = B64Url([Text.Encoding]::UTF8.GetBytes((@{
                    iss = $sa.client_email
                    scope = 'https://www.googleapis.com/auth/firebase.messaging'
                    aud = $sa.token_uri
                    iat = $now.ToUnixTimeSeconds()
                    exp = $now.AddHours(1).ToUnixTimeSeconds()
                } | ConvertTo-Json -Compress)))
    $unsigned = "$header.$claims"
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem($privateKeyPem)
    $signature = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($unsigned),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $jwt = "$unsigned.$(B64Url($signature))"

    $tokenResponse = Invoke-RestMethod -Method Post -Uri $sa.token_uri -ContentType 'application/x-www-form-urlencoded' `
        -Body @{ grant_type = 'urn:ietf:params:oauth:grant-type:jwt-bearer'; assertion = $jwt } -TimeoutSec 30
    $accessToken = $tokenResponse.access_token
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw 'OAuth não devolveu access_token.' }
    Say 'access token do FCM obtido.'

    # 3) aquece o app: limpa o estado "stopped" (o FCM não chega em pacote force-stopped) e liga a a11y.
    # Preserva a lista original (ex.: Bitwarden no S23) para restaurar no final.
    $originalA11yList = ((AdbShell 'settings get secure enabled_accessibility_services') | Out-String).Trim()
    $a11yComponent = 'com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService'
    $warmList = if ([string]::IsNullOrWhiteSpace($originalA11yList) -or $originalA11yList -eq 'null') { $a11yComponent }
        elseif ($originalA11yList.Contains('com.example.focusapp/')) { $originalA11yList }
        else { "$originalA11yList`:$a11yComponent" }
    AdbShell "settings put secure enabled_accessibility_services '$warmList'" | Out-Null
    AdbShell 'settings put secure accessibility_enabled 1' | Out-Null
    AdbShell 'am start -n com.example.focusapp/.MainActivity' | Out-Null
    Start-Sleep -Seconds 4
    AdbShell 'input keyevent KEYCODE_HOME' | Out-Null
    Start-Sleep -Seconds 2
    $warm = A11y
    Say "warm-up: bound=$($warm.Bound) enabled=$($warm.Enabled) list=$($warm.EnabledList)"

    # 4) tópico ntfy do ack (mesma derivação do app/ilha)
    $sha = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("dynamic-island-ack-v1:$token"))
    $hex = -join ($sha | ForEach-Object { $_.ToString('x2') })
    $topic = 'di-ack-' + $hex.Substring(0, 32)
    Say "tópico de ack: $topic"

    # 4) envia o FCM de bloqueio
    $sendAt = [DateTimeOffset]::UtcNow
    $durationMs = $AdultSeconds * 1000
    $endMs = $sendAt.ToUnixTimeMilliseconds() + $durationMs
    $data = if ($AdultOnly) {
        @{ adultEndMs = "$endMs" }
    }
    else {
        @{
            id = [guid]::NewGuid().ToString('N')
            durationMs = "$durationMs"
            endMs = "$endMs"
            adultEndMs = "$endMs"
        }
    }
    $body = @{
        message = @{
            token = $token
            android = @{ priority = 'HIGH'; ttl = "$($AdultSeconds + 30)s" }
            data = $data
        }
    } | ConvertTo-Json -Depth 6 -Compress
    $uri = "https://fcm.googleapis.com/v1/projects/$($sa.project_id)/messages:send"
    $response = Invoke-RestMethod -Method Post -Uri $uri -Headers @{ Authorization = "Bearer $accessToken" } `
        -ContentType 'application/json' -Body $body -TimeoutSec 30
    Say "FCM aceito: name=$($response.name)"

    # 5) ack do celular via ntfy (poll)
    $since = $sendAt.ToUnixTimeSeconds() - 5
    $ack = $null
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline -and -not $ack) {
        Start-Sleep -Seconds 3
        try {
            $lines = Invoke-RestMethod -Method Get -Uri "https://ntfy.sh/$topic/json?poll=1&since=$since" -TimeoutSec 20
            foreach ($line in ($lines -split "`n")) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                $item = $line | ConvertFrom-Json
                if ($item.event -eq 'message' -and $item.message) { $ack = $item; break }
            }
        }
        catch { Say "poll ntfy: $($_.Exception.Message)" }
    }
    if ($ack) { Say "ACK recebido: $($ack.message)" } else { Say 'ACK NÃO recebido em 25 s (FCM pode não ter chegado ou tópico divergente).' }
    Say 'logcat após o FCM:'
    Say ((AdbShell 'logcat -d -v time -t 400 | grep -E "PomodoroPush|AckClient|FocusAccessibility|GuardWatchdog|FirebaseMessaging|fcm"') | Select-Object -Last 40 | Out-String)

    # 6) watchdog: com bloqueio ATIVO, desliga a a11y e mede o religamento
    Start-Sleep -Seconds 3
    $before = A11y
    Say "antes do F1: bound=$($before.Bound) enabled=$($before.Enabled)"
    AdbShell "settings put secure enabled_accessibility_services ''" | Out-Null
    AdbShell 'settings put secure accessibility_enabled 0' | Out-Null
    Start-Sleep -Seconds 2
    $f1 = WaitBound 45
    Say "F1 watchdog (a11y religada em): $f1 s (esperado <= 20 s)"

    # 7) guardião: force-stop com bloqueio ATIVO
    AdbShell 'am force-stop com.example.focusapp' | Out-Null
    Start-Sleep -Seconds 2
    $f2 = WaitBound 75
    Say "F2 guardião (a11y religada após force-stop em): $f2 s (esperado <= 40 s)"
    $after = A11y
    Say "depois do F2: bound=$($after.Bound) enabled=$($after.Enabled) list=$($after.EnabledList)"

    Say 'admins:'
    Say ((AdbShell 'dumpsys device_policy') | Select-String -Pattern 'com.example.focusapp[^ ]*/.+:').Line -join "`n"
    Say 'logcat (Guardião/Foco/watchdog, últimas linhas):'
    Say ((AdbShell 'logcat -d -v time -t 600 | grep -E "GuardianService|GuardWatchdog|FocusAccessibility|AckClient|ReviveService|PomodoroPush"') | Select-Object -Last 60 | Out-String)
}
catch {
    Say "ERRO: $($_.Exception.Message)"
}
finally {
    # restaura a lista de acessibilidade original (preserva outros serviços, ex.: Bitwarden)
    if ($originalA11yList -and $originalA11yList -ne 'null') {
        AdbShell "settings put secure enabled_accessibility_services '$originalA11yList'" | Out-Null
        AdbShell 'settings put secure accessibility_enabled 1' | Out-Null
    }
    $out | Out-File -LiteralPath $logPath -Encoding utf8
    Write-Host "`nResultados em $logPath"
}


