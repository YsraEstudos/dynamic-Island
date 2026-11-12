# BRIEFING — Red-team dos bloqueios (Moto G20 + PC)

**Data:** 2026-11-11, 13:35 (America/Sao_Paulo)
**Autorização:** o dono de todos os aparelhos e do código-fonte é o usuário desta sessão. Ele pediu explicitamente,
em português, "que os subagentes tentem quebrar os bloqueios de todas as formas possíveis e inimagináveis" e que
procurem "várias formas de bloquear a acessibilidade". O objetivo final é **defesa** (self-control / bloqueio de
pornografia e redes sociais), nunca burlar controle de terceiros. Todos os alvos abaixo pertencem ao usuário.

**Regra de ouro:** um relatório honesto vale mais que uma lista de achados teóricos. Marque claramente o que foi
**TESTADO** (com comando e saída) e o que é **análise estática** (com `arquivo:linha`).

---

## Alvos

### A) Celular — Moto G20 (Android 11, SDK 30)
- Serial ADB: `ZF523QVD4J` — `adb -s ZF523QVD4J ...` (adb está no PATH; binário completo em
  `C:\Users\israe\Documents\Codex\AndroidPlatformTools\platform-tools\adb.exe`).
- App: `com.example.focusapp` ("Foco & Bem-Estar") v1.6 (versionCode 7, targetSdk 35, minSdk 30).
- Estado atual (snapshot em `docs/redteam/snapshots/`): serviço de acessibilidade
  `com.example.focusapp/.service.FocusAccessibilityService` **ligado e bound**; admin de dispositivo
  `com.example.focusapp/.admin.AdminPolicyReceiver` **ativo**; `WRITE_SECURE_SETTINGS`, `SYSTEM_ALERT_WINDOW`,
  `PACKAGE_USAGE_STATS`, alarmes exatos, notificações; ADB/opções de desenvolvedor **ligados**;
  **um único usuário: 0 (Israel)**; aparelho na tela de bloqueio (PIN do usuário — NÃO tente desbloquear/digitar PIN).
  APK de backup: `%TEMP%\dsh-redteam\focusapp-v1.6-base.apk` (SHA-256
  `D7188B6A82E059D1C5EFAD18CA29B7FEDA6336096ECD8D7659F751A232C7E571`). Não desinstale o app.
- Código-fonte (SOMENTE LEITURA; fica FORA do workspace e o sandbox bloqueia escrita lá):
  `C:\Users\israe\OneDrive\Desktop\Projetos\app`
  — Kotlin/Compose; arquivos-chave em `app/src/main/java/com/example/focusapp/`:
  `service/FocusAccessibilityService.kt`, `service/FocusBlockRules.kt`, `push/PomodoroPushService.kt`,
  `schedule/*.kt`, `admin/AdminPolicyReceiver.kt`, `receiver/BootReceiver.kt`, `service/ScreenTimeTrackerService.kt`,
  `ui/DailyBlockCard.kt`, `ui/SettingsScreen.kt`, `update/*.kt`, `AndroidManifest.xml`,
  `app/src/main/res/xml/accessibility_service_config.xml`, `app/src/main/res/xml/device_admin_sample.xml`.
  Docs: `docs/bloqueio-diario.md`, `README.md`. Há alterações locais não commitadas (v1.6) — leia o working tree.
- Como o bloqueio chega: o PC (Dynamic Island / Pomodoro Angry) envia mensagem FCM de dados; o app grava o
  bloqueio e agenda o fim. "Modo seletivo" (padrão ligado): sem overlay de tela cheia do Angry; Twitter/X +
  conteúdo adulto bloqueados 24h; Instagram/Reddit liberados. Sem o serviço de acessibilidade **nada é aplicado**.

### B) PC — Windows (esta máquina, usuário padrão NÃO elevado)
- Dynamic Island (WPF/.NET 10), fonte no workspace: `src/Island.Windows/Focus/ForegroundSiteGuard.cs`,
  `src/Island.Core/Pomodoro/AngryPomodoro.cs`, `src/Island.Core/Pomodoro/BlockedSites.cs`,
  `src/Island.App/App.xaml.cs` (quit/bloqueio do celular), `src/Island.Windows/Phone/FcmPhoneBlockNotifier.cs`,
  `src/Island.Core/Pomodoro/` (sessão, frases), `src/Island.App/Shell/SettingsWindow.xaml`,
  `src/Island.Windows/Configuration/JsonSettingsStore.cs`. Instalado em
  `%LocalAppData%\Programs\DynamicIsland` (pode não estar rodando agora — verifique).
- `SysGuard.exe` (bloqueio independente): fonte em `tools/SysGuard/` (serviço Windows `SysGuardSvc` como SYSTEM,
  DNS CleanBrowsing, hosts, políticas de navegador, guarda por título de janela) e binário
  `release-out/SysGuard/SysGuard.exe` (+ `tools/SysGuard/dist/SysGuard.exe`). **Não está instalado nesta máquina**
  (sem serviço, sem `C:\ProgramData\SysGuard`). Não instale/desinstale (exige admin).
- Usuário é administrador da máquina, mas o shell da sessão é **não elevado**. Não abra UAC para instalar nada.

## Regras de segurança (obrigatórias)

1. **Nunca** desinstalar o app, limpar dados, remover admin de dispositivo de forma definitiva, fazer factory reset,
   apagar arquivos do usuário, ou desligar o ADB (`adb_enabled 0`) — isso pode deixar o celular inacessível.
2. **Somente o agente `android-live` altera estado do celular.** `android-code` faz análise estática e, no máximo,
   leituras ADB (dumpsys/logcat); não muda settings.
3. Antes de cada mutação, anote o comando exato de restauração; restaure **na mesma sequência** e verifique.
   Ao final de qualquer rodada: acessibilidade LIGADA + service bound + admin ativo + pacote habilitado.
   Se algo ficar desligado, religue imediatamente e reporte no relatório.
4. Testes destrutivos proibidos: `pm uninstall`, `pm clear`, `pm remove-active-admin` definitivo, `adb reboot` sem
   necessidade. `pm disable-user`/`force-stop`/`am kill` são permitidos **se restaurados** em seguida.
5. Não interaja com a tela de bloqueio nem com dados pessoais (mensagens, fotos, contas). Use só comandos de
   sistema do app.
6. PC: não mexa em registro/serviços/DNS/hosts do sistema. Não mate processos do usuário além do DynamicIsland
   (e reinicie-o se estava aberto). Não instale nada.
7. **Escreva apenas dentro do workspace**, em `docs/redteam/**`. NUNCA tente escrever no repo Android
   (`Projetos\app`) nem em `%LocalAppData%`: o sandbox nega. Correções de código vão como proposta/patch textual
   em `docs/redteam/patches/`.
8. Relatórios em **português (pt-BR)**, com evidência real colada (saída de comando resumida, `arquivo:linha`).

## Entregáveis por agente

- `android-code` → `docs/redteam/01-android-codigo.md` (análise estática do app Android)
- `android-live` → `docs/redteam/02-android-adb.md` (testes reais no Moto G20 via ADB + evidências)
- `pc` → `docs/redteam/03-pc.md` (Dynamic Island/Angry + SysGuard.exe no PC)
- Evidências brutas em `docs/redteam/evidence/<seu-nome>/...` (logs, saídas de comando).
- Mensagem final ao Lead (`send_message` para `lead`): ≤ 25 linhas — veredito, top 5 vetores com severidade,
  o que já resiste, caminho do relatório e estado final do celular/PC.

## Formato mínimo do relatório (cada linha da tabela de vetores)

| ID | Vetor | Como se faz (passo a passo) | Pré-requisitos | Resultado (TESTADO/estático + evidência) | Severidade | Por que a defesa atual falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|

Severidade: **CRÍTICO** = bypass trivial, sem senha, em < 1 min; **ALTO** = bypass fácil mas com atrito;
**MÉDIO** = requer ferramenta/conhecimento; **BAIXO** = teórico/improprio.
Inclua também: "Defesas que já funcionam" e "Defesas recomendadas (ordem de impacto/implementabilidade)".

## Perguntas centrais que o usuário quer respondidas

1. Dá para fugir do bloqueio? Por quais caminhos hoje (por fora, fechando processo no Gerenciador de Tarefas,
   desligando acessibilidade, ADB, navegador alternativo, VPN/DoH, outro usuário etc.)?
2. Dá para impedir o usuário de desligar a acessibilidade? De que formas (watchdog que religa, 2º pacote,
   Device Owner, detecção de ADB, bloqueio de telas de Settings, alarmes)? Quais funcionam de verdade no Android 11?
3. Dá para o app sobreviver a `force-stop`/Task Manager? O que é possível (detectar + recuperar) e o que é mito?
4. O `.exe` do PC (SysGuard) sobrevive a Gerenciador de Tarefas/serviços/senha? O que dá para endurecer?
5. A ilha só deve dizer "Celular bloqueado" com confirmação real do celular — como o celular pode confirmar
   (ack) para o PC? Analise canais possíveis (LAN HTTP, ntfy, FCM upstream, etc.) e recomende um.

## Coordenação

- Tarefas no quadro compartilhado: reivindique a sua com `team_task_update` (action=claim, expected_revision).
- Não modifique arquivos de outros agentes. Se precisar do resultado de outro, peça ao Lead.
- Se achar um bypass CRÍTICO cedo, mande mensagem imediata ao Lead além do relatório.
- Profundidade > velocidade, mas evite repetir testes já cobertos. Priorize o que o usuário vai sentir no uso real.
