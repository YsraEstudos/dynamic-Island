# Dynamic Island

App WPF (.NET 10) em `src/`, testes em `tests/`. Visão geral e auto-update: `README.md`.

## Publicar uma versão nova (auto-update)
O usuário não quer rodar nada no terminal. Quando ele pedir para publicar, lançar ou atualizar a versão do app, **rode você mesmo**, na raiz do projeto:

```
pwsh -NoProfile -File .\release.ps1            # sobe o último número (0.4.1 -> 0.4.2)
pwsh -NoProfile -File .\release.ps1 -Version 1.0.0   # só se ele pedir um número
```

Antes, rode `dotnet test DynamicIsland.slnx` e só publique se passar (o teste `WindowsClipboardServiceTests.ExcludedFormat_IsIgnored_WhileNormalCopyIsCaptured` é instável: se for o único a falhar, rode-o de novo isolado). O script sobe `<Version>` em `Directory.Build.props`, compila, gera o zip, faz commit + tag + push e cria a Release no GitHub. Não pede senha nem certificado. Depois confira com `gh release list --repo YsraEstudos/dynamic-Island` e diga ao usuário que ele atualiza pelo menu da ilha (botão direito → Install update).

O `release.ps1` abre o build publicado com `--smoke-test` e aborta se o app não abrir (v0.4.12 saiu quebrada por um serviço não registrado em `ServiceRegistration`). Se o script abortar por isso, corrija o app, não pule o teste. Veja "Armadilhas do auto-update" no `README.md`.

Não use MSIX nem assinatura: foi removido de propósito. O app instalado do usuário fica em `%LocalAppData%\Programs\DynamicIsland`.

## Atenção
- Ao editar arquivos, preserve o fim de linha existente (vários arquivos são CRLF).
- Não coloque segredos no repositório (ele é público): chaves FCM, `.pfx`, tokens.
