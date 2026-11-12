# Teste do guardião (Fase 2): com um bloqueio adulto ATIVO (FCM real), force-stop do app principal e
# medição do tempo até a acessibilidade voltar a ficar bound. O app principal fica sem WRITE_SECURE_SETTINGS
# durante o teste (o guardião tem a dele) para o anti-ADB não derrubar a sessão de teste.
# Rodar com permissão elevada (FCM/TLS fora do sandbox).
[CmdletBinding()]
param(
    [string]$Serial = 'ZF523QVD4J',
    [int]$AdultSeconds = 180,
    [Parameter(Mandatory = $true)][string]$Token,
    [string]$KeyPath = "$env:LOCALAPPDATA\DynamicIsland\fcm-service-account.json"
)

$ErrorActionPreference = 'Stop'
$workspace = 'C:\Users\israe\OneDrive\Desktop\Projetos\Dynamic Island'
$evidence = Join-Path $workspace 'docs\redteam\evidence'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$logPath = Join-Path $evidence 'guardian-force-stop.txt'
$out = [System.Collections.Generic.List[string]]::new()
function Say([string]$text) { $out.Add($text); Write-Host $text }
function A([string]$command) { & adb.exe -s $Serial shell $command 2>&1 }
function B64Url([byte[]]$bytes) { [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
function A11y {
    $dump = (A 'dumpsys accessibility') | Out-String
    [pscustomobject]@{
        Bound = ($dump -match 'Bound services:\{Service')
        Enabled = ((A 'settings get secure accessibility_enabled') | Out-String).Trim()
        List = ((A 'settings get secure enabled_accessibility_services') | Out-String).Trim()
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

try {
    Say ("dispositivo: " + ((& adb.exe devices) -join ' '))
    Say '--- 1) permissões: guardião com WRITE_SECURE_SETTINGS; app principal sem (para o ADB sobreviver ao teste)'
    Say ((A 'pm grant com.example.focusapp.guardian android.permission.WRITE_SECURE_SETTINGS') | Out-String).Trim()
    Say ((A 'pm revoke com.example.focusapp android.permission.WRITE_SECURE_SETTINGS') | Out-String).Trim()
    Start-Sleep -Seconds 2

    Say '--- 2) aquecendo o app e a acessibilidade'
    A 'settings put secure enabled_accessibility_services com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService' | Out-Null
    A 'settings put secure accessibility_enabled 1' | Out-Null
    A 'am start -n com.example.focusapp/.MainActivity' | Out-Null
    Start-Sleep -Seconds 4
    A 'input keyevent KEYCODE_HOME' | Out-Null
    Start-Sleep -Seconds 2
    $warm = A11y
    Say "warm-up: bound=$($warm.Bound) enabled=$($warm.Enabled)"

    Say '--- 3) FCM adulto (bloqueio ativo)'
    $sa = Get-Content -LiteralPath $KeyPath -Raw | ConvertFrom-Json
    $pem = [string]$sa.private_key
    if ($pem.StartsWith('dpapi:v1:')) {
        $pem = [Text.Encoding]::UTF8.GetString([System.Security.Cryptography.ProtectedData]::Unprotect(
                [Convert]::FromBase64String($pem.Substring('dpapi:v1:'.Length)), $null,
                [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
    }
    $now = [DateTimeOffset]::UtcNow
    $header = B64Url([Text.Encoding]::UTF8.GetBytes((@{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress)))
    $claims = B64Url([Text.Encoding]::UTF8.GetBytes((@{
                    iss = $sa.client_email; scope = 'https://www.googleapis.com/auth/firebase.messaging'
                    aud = $sa.token_uri; iat = $now.ToUnixTimeSeconds(); exp = $now.AddHours(1).ToUnixTimeSeconds()
                } | ConvertTo-Json -Compress)))
    $unsigned = "$header.$claims"
    $rsa = [System.Security.Cryptography.RSA]::Create(); $rsa.ImportFromPem($pem)
    $sig = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($unsigned),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $jwt = "$unsigned.$(B64Url($sig))"
    $oauth = Invoke-RestMethod -Method Post -Uri $sa.token_uri -ContentType 'application/x-www-form-urlencoded' `
        -Body @{ grant_type = 'urn:ietf:params:oauth:grant-type:jwt-bearer'; assertion = $jwt } -TimeoutSec 30
    $sendAt = [DateTimeOffset]::UtcNow
    $endMs = $sendAt.ToUnixTimeMilliseconds() + ($AdultSeconds * 1000)
    $body = @{ message = @{ token = $Token; android = @{ priority = 'HIGH'; ttl = "$($AdultSeconds + 30)s" }
                data = @{ adultEndMs = "$endMs" } } } | ConvertTo-Json -Depth 6 -Compress
    $resp = Invoke-RestMethod -Method Post -Uri "https://fcm.googleapis.com/v1/projects/$($sa.project_id)/messages:send" `
        -Headers @{ Authorization = "Bearer $($oauth.access_token)" } -ContentType 'application/json' -Body $body -TimeoutSec 30
    Say "FCM aceito: $($resp.name)"
    $sha = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("dynamic-island-ack-v1:$Token"))
    $topic = 'di-ack-' + (-join ($sha | ForEach-Object { $_.ToString('x2') })).Substring(0, 32)
    $ack = $null
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $ack) {
        Start-Sleep -Seconds 3
        try {
            $lines = Invoke-RestMethod -Method Get -Uri "https://ntfy.sh/$topic/json?poll=1&since=$($sendAt.ToUnixTimeSeconds() - 5)" -TimeoutSec 20
            foreach ($line in ($lines -split "`n")) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                $item = $line | ConvertFrom-Json
                if ($item.event -eq 'message' -and $item.message) { $ack = $item; break }
            }
        }
        catch { }
    }
    if ($ack) { Say "ACK: $($ack.message)" } else { Say 'ACK não recebido (seguindo com o teste de force-stop).' }
    Start-Sleep -Seconds 3

    Say '--- 4) force-stop do app principal (com bloqueio ativo)'
    $before = A11y
    Say "antes: bound=$($before.Bound) enabled=$($before.Enabled)"
    A 'am force-stop com.example.focusapp' | Out-Null
    $f2 = WaitBound 80
    Say "F2 guardião: a11y bound novamente em $f2 s (esperado <= 40 s)"
    $after = A11y
    Say "depois: bound=$($after.Bound) enabled=$($after.Enabled) list=$($after.List)"
    Say '--- 5) logs do guardião/repair'
    Say ((A 'logcat -d -v time -t 800 | grep -E "GuardianService|AccessibilityRepair|ReviveService|MainAppControl|GuardianRepair"') | Select-Object -Last 40 | Out-String)
    Say '--- 6) adb continua ligado? (anti-ADB não deve ter derrubado durante o teste)'
    Say ((A 'settings get global adb_enabled') | Out-String).Trim()

    Say '--- 7) restaurando permissão do app principal e estado final'
    Say ((A 'pm grant com.example.focusapp android.permission.WRITE_SECURE_SETTINGS') | Out-String).Trim()
    A 'settings put secure enabled_accessibility_services com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService' | Out-Null
    A 'settings put secure accessibility_enabled 1' | Out-Null
    Start-Sleep -Seconds 3
    $final = A11y
    Say "estado final: bound=$($final.Bound) enabled=$($final.Enabled) list=$($final.List)"
}
catch { Say "ERRO: $($_.Exception.Message)" }
finally {
    $out | Out-File -LiteralPath $logPath -Encoding utf8
    Write-Host "`nResultados em $logPath"
}
