# Fases
0 janela transparente · 1 fundação/testes · 2 UI/animações · 3 mídia · 4 volume/sistema · 5 configurações/bandeja (implementadas no MVP).
7 estabilidade: testes de estresse, vazamentos, soak (`--soak`) e tratamento de reinício do Explorer implementados; validação manual em `docs/stability-checklist.md` ainda a ser marcada.
8 distribuição: auto-update por zip no GitHub Releases (`release.ps1`), implementado e validado.
Pendentes: 6 notificações (UserNotificationListener exige identidade de pacote, que o app não tem mais).
