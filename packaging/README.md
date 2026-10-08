# Empacotamento MSIX

Gera um pacote `.msix` (full-trust desktop) do `Island.App`.

## Arquivos
- `Package.appxmanifest`: manifesto (Identity `DynamicIsland`, Publisher `CN=DynamicIslandDev`, versão 0.1.0.0, executável `DynamicIsland.exe`). Declara o `startupTask` `DynamicIslandStartup` desativado.
- `Assets/`: logos **placeholder** (fundo escuro com pílula branca). Trocar pela arte final antes de distribuir.
- `create-dev-cert.ps1`: cria o certificado autoassinado de desenvolvimento (`.pfx` + `.cer`).
- `build-msix.ps1`: publica, monta o layout, empacota com `makeappx` e assina com `signtool`. Saída em `packaging\out\`.

## Pré-requisitos
- .NET 10 SDK.
- Windows SDK instalado (`makeappx.exe` e `signtool.exe` em `C:\Program Files (x86)\Windows Kits\10\bin\<versão>\x64`).
- Certificado de desenvolvimento confiável na máquina onde o pacote será instalado.

## Passos
1. Criar o certificado (uma vez). Pede a senha do `.pfx` de forma segura:
   ```powershell
   .\packaging\create-dev-cert.ps1
   ```
2. Confiar no `.cer`. Passo **manual**, em PowerShell como Administrador:
   ```powershell
   Import-Certificate -FilePath packaging\DynamicIslandDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. Gerar e assinar o pacote:
   ```powershell
   .\packaging\build-msix.ps1
   ```
   Resultado: `packaging\out\DynamicIsland_0.1.0.0_x64.msix`. Sem `packaging\DynamicIslandDev.pfx` o pacote sai sem assinatura e não instala. Use `-SkipSign` para gerar sem assinar de propósito.
4. Instalar (manual): `Add-AppxPackage -Path packaging\out\DynamicIsland_0.1.0.0_x64.msix`.

Os arquivos `packaging\*.pfx` e `packaging\out\` estão no `.gitignore`. Não grave a senha em arquivo nem a passe em texto na linha de comando.

## Publicar uma nova versão
O menu da ilha oferece **Install update** quando a última Release do GitHub (`YsraEstudos/dynamic-Island`) é mais nova que o app em execução e tem um `.msix`. Para liberar uma versão:
1. Suba a versão nos dois lugares, com o mesmo número: `<Version>` em `Directory.Build.props` (ex.: `0.2.0`) e `Version` do `Package.appxmanifest` com quatro partes (ex.: `0.2.0.0`). O MSIX só instala uma identidade de versão maior que a atual.
2. Gere o pacote com `.\packaging\build-msix.ps1` (sai `packaging\out\DynamicIsland_0.2.0.0_x64.msix`).
3. Crie uma Release no GitHub com a tag `v0.2.0` e anexe o `.msix`. Ela precisa estar publicada, não em rascunho nem como pré-release: a consulta usa `releases/latest`, que ignora as outras.

As máquinas veem a oferta na consulta seguinte (no início do app ou em até 6 h). A instalação só funciona se o pacote estiver assinado com o certificado que a máquina já confia.

## Atenção ao rodar empacotado
- **Dados virtualizados:** dentro do MSIX, as gravações em `%LocalAppData%` são redirecionadas para `%LocalAppData%\Packages\<PackageFamilyName>\LocalCache\Local\`. Settings, logs e `fcm-service-account.json` ficam nesse caminho, não em `%LocalAppData%\DynamicIsland`. Um build empacotado e um `dotnet run` usam dados separados. O `fcm-service-account.json` precisa ser copiado para o local virtualizado.
- **Autostart:** `StartupRegistration` grava em `HKCU\...\Run`. Dentro do pacote essa escrita também é virtualizada e não é confiável. Quando empacotado, o autostart deve usar o `startupTask` declarado no manifesto (`TaskId` `DynamicIslandStartup`, via API `StartupTask`). Essa troca **não foi feita** no código: é mudança em `src/`, fora desta fase.
- **Versão:** `Version` do manifesto (`0.1.0.0`) não é lida de `Directory.Build.props` (`0.1.0`). Atualize os dois ao mudar a versão.

## Status
Scripts escritos e o manifesto validado como XML. A instalação e a execução do pacote **não foram testadas** nesta máquina.
