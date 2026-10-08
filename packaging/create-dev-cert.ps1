<#
.SYNOPSIS
Cria um certificado autoassinado de desenvolvimento (code signing) para assinar o MSIX.

.DESCRIPTION
O Subject é lido de Package.appxmanifest (Identity/@Publisher), para que seja exatamente igual ao
Publisher do pacote. Exporta:
  - DynamicIslandDev.pfx  (chave privada, protegida por senha; NÃO commitar)
  - DynamicIslandDev.cer  (somente a parte pública, usada para confiar no certificado)

O certificado é criado em Cert:\CurrentUser\My, exportado e removido dessa loja em seguida.
O script NÃO importa nada em Cert:\LocalMachine. Essa etapa exige administrador e é feita manualmente
(o próprio script mostra o comando ao final).

.PARAMETER PfxPassword
Senha do .pfx como SecureString. Se omitida, o PowerShell pede de forma segura.
Nunca passe a senha em texto puro na linha de comando nem a grave em arquivo.

.EXAMPLE
.\create-dev-cert.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [SecureString]$PfxPassword,

    [string]$OutputDir = $PSScriptRoot,

    [ValidateRange(1, 10)]
    [int]$ValidYears = 2,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
$subject = $manifest.Package.Identity.Publisher
if ([string]::IsNullOrWhiteSpace($subject)) {
    throw 'Não foi possível ler o Publisher de Package.appxmanifest.'
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$pfxPath = Join-Path $OutputDir 'DynamicIslandDev.pfx'
$cerPath = Join-Path $OutputDir 'DynamicIslandDev.cer'

foreach ($existing in @($pfxPath, $cerPath)) {
    if (Test-Path -LiteralPath $existing) {
        if (-not $Force) {
            throw "Já existe: $existing. Use -Force para substituir (o certificado antigo deixará de valer para novos pacotes)."
        }
        Remove-Item -LiteralPath $existing -Force
    }
}

Write-Host "Criando certificado com Subject '$subject'..."
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $subject `
    -FriendlyName 'Dynamic Island (dev)' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($ValidYears)

try {
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $PfxPassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
}
finally {
    # O .pfx passa a ser a fonte da verdade; não deixa o certificado acumulado na loja do usuário.
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Certificado criado.'
Write-Host "  PFX (privado, não commitar): $pfxPath"
Write-Host "  CER (público):               $cerPath"
Write-Host "  Válido até:                  $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ''
Write-Host 'Para instalar o MSIX assinado, confie no .cer. Passo MANUAL, em PowerShell como Administrador:'
Write-Host "  Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host 'Sem essa confiança, o Windows recusa a instalação do pacote.'
