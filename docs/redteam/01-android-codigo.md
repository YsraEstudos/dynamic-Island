# 01 — Red-team estático do app Android “Foco & Bem-Estar” (`com.example.focusapp` v1.6)

**Agente:** `android-code` · **Data:** 2026-11-11, 14:15 (America/Sao_Paulo)
**Alvo:** Moto G20 (Android 11, SDK 30) — código-fonte em `C:\Users\israe\OneDrive\Desktop\Projetos\app`
(working tree v1.6, commit base `5ff6b6a` + alterações não commitadas), APK instalado `versionCode 7/versionName 1.6`.
**Método:** leitura integral dos arquivos Kotlin/XML/docs citados + `git diff` da v1.6 + **somente leituras ADB**
(`dumpsys`, `settings get`, `pm list`, `dumpsys package`, `logcat -d -b events`, `run-as ... ls/cat` de prefs não sensíveis)
+ porte manual das listas/regex de `FocusBlockRules` para PowerShell para verificar cobertura (apoio estático).
**Nada foi alterado no celular e nada foi escrito fora de `docs/redteam/**`.**

> Legenda de resultado: **OBSERVADO** = saída ADB real desta sessão · **ESTÁTICO** = comprovado no código com
> `arquivo:linha` · **HIPÓTESE** = plausível, pendente de teste do `android-live`.

---

## 0. Veredito executivo (curto)

O bloqueio de pornografia/redes hoje **não é um bloqueio de conteúdo**: é (a) uma lista fixa de 6 pacotes de
navegador + (b) comparação de texto da barra de endereço/título curto contra uma lista de 55 domínios e uma regex,
e (c) overlay/bloqueio por acessibilidade. Isso deixa três famílias de fuga triviais, sem senha:

1. **Derrubar o serviço**: parada forçada/queda deixa o serviço “enabled” mas **não bound** — o app **não tem
   watchdog**, o card continua dizendo “Ativo” e o bloqueio fica OFF em silêncio. Para o bloqueio de adulto existe
   ainda um caminho mais curto: **um toque no interruptor “Modo seletivo” (sem senha) desliga o bloqueio de conteúdo
   adulto** quando não há janela adulta ativa do PC (`FocusBlockRules.shouldBlockWebAddress`, linha 80).
2. **Fugir da detecção de texto**: qualquer **navegador fora da lista fixa** (instalável pela Play Store sem
   senha), qualquer **WebView/navegador interno** (Telegram, Discord, Facebook estão instalados) e **imagens de
   busca** (Google/Bing Imagens) abrem pornografia sem nenhum evento inspecionado. Regra do app: se o pacote não
   está em `browserAddressBars`, nada é lido (`FocusAccessibilityService.kt:117`).
3. **Mexer no relógio**: a página *Ajustes → Data e hora* **não exibe o nome do app**, logo a proteção anti-tamper
   (que procura a string “Foco & Bem-Estar”) não dispara; no modo seletivo não há overlay de tela cheia. Adiantar a
   hora **encerra o bloqueio diário, encerra a janela adulta do PC e faz mensagens FCM novas parecerem expiradas**.

Além disso: o admin de dispositivo **não bloqueia desinstalação** (só funciona como Device Owner, e o aparelho não
é DO — a UI promete o contrário), o APK instalado é **DEBUGGABLE**, não há **ack** do celular para o PC, e o limite
de tempo/“dívida” são ajustáveis **sem senha**.

**O que já resiste bem:** os bloqueios do PC nunca são encurtados/liberados por mensagem (monotônicos, teto 120 min
e 4 h), o bloqueio adulto não tem “frase de liberação”, o bloqueio diário sobrevive a reboot, o app não expõe
componentes sensíveis, a senha é PBKDF2 com lockout e o backup exclui justamente os prefs sensíveis.
Detalhes em §5 e §6.

---

## 1. Escopo, fontes e limitações

**Arquivos lidos (todos):** `service/FocusAccessibilityService.kt`, `service/FocusBlockRules.kt`,
`push/PomodoroPushService.kt`, `push/PushToken.kt`, `schedule/{DailyBlockPolicy,DailyBlockStore,DailyBlockController,
DailyBlockScheduler,PhraseKeyboardView}.kt`, `admin/AdminPolicyReceiver.kt`, `receiver/{BootReceiver,
NowBarDismissReceiver}.kt`, `service/{ScreenTimeTrackerService,LiveUpdateManager,ScreenTimeTrackerService}.kt`,
`update/{UpdateClient,ApkInstaller,AppUpdateViewModel,AppVersion,GitHubReleaseParser,UpdateTokenStore}.kt`,
`MainActivity.kt`, `security/{PasswordManager,PasswordLockoutPolicy}.kt`, `display/{GrayscaleController,
GrayscaleEnforcementPolicy}.kt`, `tracker/{TrackerPrefs,ScreenTimeManager,DailyExtensionPolicy,UsageStatsReader}.kt`,
`ui/{DailyBlockCard,SettingsScreen,DashboardFocusSections,FocusDashboardScreen,DashboardPermissions,
DashboardViewModel,UninstallDialog,ChangePasswordDialog,ExtraTimeDialog}.kt`,
`AndroidManifest.xml`, `res/xml/{accessibility_service_config,device_admin_sample,backup_rules,
data_extraction_rules}.xml`, `res/values/strings.xml`, `app/build.gradle.kts`, `docs/bloqueio-diario.md`, `README.md`,
`git diff` (v1.6) e `app/src/test/.../SelectiveBlockModeTest.kt`.

**Limitações:** não testei mutações (regra 2 do BRIEFING). Os caminhos de UI dos Ajustes (rótulos, atalhos,
“forçar parada”, modo seguro, usuário visitante) são **estático/alta-confiança** e estão listados como
“a confirmar pelo `android-live`”; o estado do aparelho observado é um instantâneo e foi coletado enquanto o
`android-live` também testava (inclui efeitos dos testes dele — marco onde é o caso).
Evidências brutas: `docs/redteam/evidence/android-code/` (`estado-observado.txt`, `a11y-dumpsys.txt`,
`device-policy.txt`, `users.txt`, `packages-browser.txt`, `settings-secure.txt`, `props.txt`, `logcat-focus.txt`,
`verificacao-listas.txt`, `apk-instalado-sha256.txt`).

---

## 2. Estado observado no Moto G20 (leitura ADB, 13:50–14:15)

| Fato | Evidência | Leitura |
|---|---|---|
| Serviço de acessibilidade **enabled** e (após 14:05:49) **bound** | `dumpsys accessibility`: `Enabled services:{...FocusAccessibilityService}`, `Bound services:{Service[label=Serviço de Foco e Proteç…]}` | OBSERVADO |
| Entre ~13:52 e ~13:55 o serviço estava **enabled porém NÃO bound e “Crashed”**; só voltou depois de ação externa | `a11y-dumpsys.txt`: `Bound services:{}`, `Crashed services:{{...FocusAccessibilityService}}`; `dumpsys activity processes`: várias `ConnectionRecord … DEAD` | OBSERVADO |
| Extração do crash: sem evidência própria no logcat (buffer rotacionou); a recuperação às 14:05:49 aparece como `am_proc_start … {…FocusAccessibilityService}` | `logcat -d -b events` | OBSERVADO (causa provável: teste de força-parada do `android-live`) |
| Admin de dispositivo ativo, **NÃO** Device Owner | `dumpsys device_policy`: só “Enabled Device Admins”, sem seção Device Owner; `Users`: `Device owner id:-10000`, `Device managed: false` | OBSERVADO |
| **Modo seletivo = false** no aparelho, apesar de o default do código ser `true` | `focus_daily_block.xml`: `<boolean name="selective_block" value="false" />`; default em `DailyBlockStore.kt:84` | OBSERVADO |
| Nível de segurança = 2 (default; sem chave gravada em `focus_tracker_prefs.xml`) | `TrackerPrefs.kt:29-30` + prefs | OBSERVADO |
| Bloqueios do PC gravados e já expirados (Pomodoro 13:00:10–13:28:50, adulto até 13:29:50) | `focus_daily_block.xml` (`pomodoro`, `adult_end_ms`) | OBSERVADO |
| ADB e opções de desenvolvedor ligados | `settings get global adb_enabled=1`, `development_settings_enabled=1` | OBSERVADO |
| Private DNS `opportunistic` com specifier AdGuard (modo *opportunistic* ignora o specifier — não há filtro de conteúdo) | `settings get global private_dns_*` | OBSERVADO |
| Multi-usuário: **máx. 4**, `Supports switchable users: true`, restrições de visitante existem; hoje só user 0 | `dumpsys user` | OBSERVADO |
| App é **DEBUGGABLE** e `RUN-AS` funciona (prefs legíveis) | `dumpsys package`: `flags=[ DEBUGGABLE HAS_CODE ALLOW_CLEAR_USER_DATA ALLOW_BACKUP ]`; `run-as com.example.focusapp ls shared_prefs` | OBSERVADO |
| APK instalado = cópia base v1.6 (SHA-256 `D7188B6A…C7E571`, 69.501.540 bytes) | `apk-instalado-sha256.txt`; backup em `%TEMP%\dsh-redteam` | OBSERVADO |
| Instalados: Chrome, Firefox, Edge (mapeados); **Play Store, Telegram, Discord, Facebook, YouTube, Google app, Gmail** (não mapeados) | `pm list packages` | OBSERVADO |
| Sem Twitter/Instagram/Reddit instalados | `pm list packages` | OBSERVADO |
| Atalho de acessibilidade (teclas de volume) **não** configurado; botão de acessibilidade da barra **null** | `settings get secure accessibility_shortcut_target_service` = TalkBack; `dumpsys accessibility`: `button target:{null}` | OBSERVADO |
| Durante a coleta o teste do `android-live` criou/encerrou o usuário 10 e o `system_server` registrou `am_crash … No UserInfo for 10` | `logcat -d -b events` (14:05) | OBSERVADO — ver §4-D01 (multi-usuário instável nesta ROM) |

---

## 3. Tabela de vetores

Severidade conforme BRIEFING: **CRÍTICO** = trivial/sem senha/<1 min · **ALTO** = fácil com atrito ·
**MÉDIO** = ferramenta/conhecimento · **BAIXO** = teórico/impróprio.

### Grupo A — Derrubar a proteção

| ID | Vetor | Como se faz | Pré-requisitos | Resultado | Severidade | Por que a defesa falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|
| **A-01** | **Desligar o bloqueio de adulto com 1 toque (sem senha)** | Abrir o app → card “Bloqueio por horário” → interruptor **“Modo seletivo”** ON→OFF. Com `selective=false` e sem janela adulta do PC ativa, `shouldBlockWebAddress` só considera `reinforced && isBlockedWebAddress`; o ramo adulto vira `adultWindowActive && isAdultWebAddress` = false. | Ter o app (sem senha) | **ESTÁTICO comprovado**: switch chama `store.saveSelective(enabled)` direto (`ui/DailyBlockCard.kt:178-188`; `schedule/DailyBlockStore.kt:84-87`); regra em `service/FocusBlockRules.kt:72-81`. Confirmado pelo design: o doc admite “a única forma de desligá-lo é o interruptor do card” (`docs/bloqueio-diario.md:42`). No aparelho `selective_block=false` já | **CRÍTICO** | O “sempre ligado” do adulto depende de um booleano que a própria UI muda sem autenticação. O bloqueio de Twitter/X continua (hosts do modo nível 2), mas **pornografia deixa de ser bloqueada** até o próximo push adulto do PC | Exigir senha para desligar o seletivo (e para trocar nível/limite); melhor: **bloqueio adulto independente do switch** e do nível (`enabled` por preferência separada, protegida). Ver §7.9 | Se a senha for conhecida (é o dono), ainda dá para mudar depois de autenticar — fricção, não prevenção |
| **A-02** | **Desligar acessibilidade em Ajustes sem a proteção anti-tamper pegar** | Abrir Ajustes → Acessibilidade → “Serviços instalados” → **Serviço de Foco e Proteção** → desligar. O guard anti-tamper só procura as strings “Foco & Bem-Estar”/`com.example.focusapp` (`FocusAccessibilityService.kt:244-268`), mas a tela de Acessibilidade mostra o **label do serviço** `@string/accessibility_service_name` = “Serviço de Foco e Proteção” (`AndroidManifest.xml:133`, `strings.xml`), que não contém o nome do app. Em modo seletivo **não há overlay** bloqueando os Ajustes. | Nenhum (modo seletivo) | **ESTÁTICO, alta confiança** (rótulos e páginas AOSP do Android 11). Confirmar UI com `android-live` | **CRÍTICO** | A página exata que desliga o bloqueador é justamente a que não casa com a string procurada; e a única barreira secundária (overlay) não existe no modo seletivo | (1) procurar **os dois rótulos** + package + conteúdo da tela do serviço; (2) **watchdog** religando via `WRITE_SECURE_SETTINGS` (§7.2); (3) overlay de aviso ao detectar intenção. Ver §7.5/§7.2 | Ainda há janela de ~1–3 s entre desligar e religar; overlay de fallback do guardião reduz |
| **A-03** | **Forçar parada / matar o app → bloqueio OFF silencioso; UI mente “Ativo”** | Ajustes → Apps → Foco & Bem-Estar → **Forçar parada** (ou Gerenciador de tarefas do fabricante / `adb shell am force-stop`). O serviço de acessibilidade **não** volta sozinho: fica enabled na configuração porém não bound. | Nenhum (UI) ou ADB | **OBSERVADO**: 13:52–13:55 `Enabled services` sim, `Bound services:{}`, `Crashed services` preenchido; só religou às 14:05:49 por ação externa. **ESTÁTICO**: nenhum listener de `AccessibilityManager`/`ACTION_ACCESSIBILITY_STATE_CHANGED`, nenhum alarme/watchdog, `BootReceiver` só trata boot/update (`receiver/BootReceiver.kt:20-31`). `FocusAccessibilityService.isEnabled()` usa `getEnabledAccessibilityServiceList` (`:228-235`) — **lista de enabled, não de bound** — e o card exibe “Ativo” com base nisso (`DailyBlockCard.kt:51,83,135`; `DashboardViewModel.kt:104`) | **CRÍTICO** | Não há liveness check nem auto-restauração; o app não percebe que está morto e afirma o contrário no card | Heartbeat do serviço + watchdog no FGS com religamento via `WRITE_SECURE_SETTINGS`; corrigir `isEnforcing()`; segundo pacote (§7.1–7.3) | Processo morto e segundo pacote também morto: só reboot/religar manual (ver A-04/A-05) |
| **A-04** | **Modo seguro (safe mode)** | Power menu → manter “Desligar/Reiniciar” pressionado → confirmar **Reiniciar no modo seguro**. Em safe mode serviços de acessibilidade e apps de terceiros não rodam; o Chrome (app de sistema) continua disponível. | Confirmar diálogo do sistema | **HIPÓTESE** (comportamento padrão do Android; confirmar Chrome em safe mode no aparelho) | **ALTO** | O app não pode se opor sem Device Owner; não há detecção de que o aparelho reiniciou em safe mode | Device Owner + `DISALLOW_SAFE_BOOT` (§6, item 7); sem DO: ack/PC detecta ausência e alerta | Sem DO é inevitável |
| **A-05** | **ADB / PC do próprio usuário** | `adb shell settings put secure enabled_accessibility_services ''` (ou `accessibility_enabled 0`), `am force-stop`, `pm clear`, `dpm remove-active-admin` + `pm uninstall`; com `run-as` (APK **debuggable**) apagar/editar prefs: `run-as com.example.focusapp rm shared_prefs/focus_daily_block.xml`, etc. | Cabo USB + depuração ligada (já ligada) | **OBSERVADO** que ADB/desenvolvedor estão ligados e que `run-as` funciona; comandos em si são os documentados no próprio README (`README.md:87-93`) | **ALTO** (para o modelo “usuário com o próprio PC”) | Não há detecção de ADB, e o app tem `WRITE_SECURE_SETTINGS` mas não usa para se defender; o build debug permite `run-as` | Detectar `adb_enabled`/`development_settings_enabled` e reagir (revalidar estado, notificar PC); Device Owner + `DISALLOW_DEBUGGING_FEATURES`; build release não-debuggable (§7.8/D-04) | Usuário com ADB/root sempre vence — mitigação é atrito/alerta, não prevenção |
| **A-06** | **Desinstalar sem a senha (cadeia a11y-off → admin-off → uninstall)** | 1) desliga a acessibilidade (A-02, que mata também o guard); 2) Ajustes → Segurança → **Apps de administração do dispositivo** → Foco & Bem-Estar → **Desativar**; 3) desinstalar. O admin **não** bloqueia desinstalação: `setUninstallBlocked` só executa se `isDeviceOwnerApp` (`admin/AdminPolicyReceiver.kt:29-43`), e o aparelho não é DO (`dumpsys device_policy`). A UI promete “Previne desinstalação sem a senha” (`ui/DashboardPermissions.kt:119-131`). | Nenhum além do aparelho | **ESTÁTICO + OBSERVADO** (admin ativo, não-DO). Desinstalar com admin ativo falha no sistema, mas **desativar o admin não pede senha** (só o texto de `onDisableRequested`, `:61-63`, é informativo) | **ALTO** | A proteção de desinstalação é um no-op fora de Device Owner; nada no app verifica/impede a desativação do admin (só o guard de Ajustes, que morre com A-02) | Verificar e **reaplicar** `setUninstallBlocked` (quando DO); sem DO, exigir senha para desativar o admin não é possível — usar o watchdog para manter a11y viva e o guardião para reabrir o app; corrigir a promessa da UI (§7.9) | Sem DO, usuário determinado desinstala; admin só atrasa |
| **A-07** | **Atualização do app desliga a acessibilidade e nada a religa** | Instalar a atualização pelo fluxo do app (ou `adb install -r`). O próprio README avisa que o Android costuma desligar o serviço do pacote atualizado (`README.md:87`, `docs/bloqueio-diario.md:34`). Nada religa: `BootReceiver` trata `MY_PACKAGE_REPLACED` mas só inicia o FGS (`receiver/BootReceiver.kt:24-29`). | Atualização pendente + senha para instalar | **ESTÁTICO** (fluxo e aviso do próprio autor) | **MÉDIO** | O app tem `WRITE_SECURE_SETTINGS` e poderia se re-ligar após update, mas não usa; o usuário só descobre se abrir o card | No `MY_PACKAGE_REPLACED` (e no FGS) religar a acessibilidade via secure settings e reportar ao PC (§7.2) | Se o Android exigir confirmação do usuário, sobra alerta |
| **A-08** | **Limpar dados do app** | Ajustes → Apps → Foco & Bem-Estar → Armazenamento → Limpar dados (a página mostra o nome do app; o guard dispara **se** a a11y estiver viva, e como A-02/A-03 podem matá-la, a ordem importa). Via ADB `pm clear` é imediato. | Aparelho | **ESTÁTICO** (fluxo de Settings); o guard depende de a11y viva (`FocusAccessibilityService.kt:126-134,244-268`) | **MÉDIO** (via UI; ALTO via ADB) | Dados são a raiz do estado; sem DO não há como impedir `pm clear`/limpar dados; e o guard depende do próprio serviço que se está tentando matar | Estado mínimo/derivado: blocos **re-enviados pelo PC** (fonte da verdade) + ack; sem isso, limpar dados libera tudo | Sem DO/root, impossível impedir totalmente |
| **A-09** | **Bateria/Doze/tick** | Com tela apagada o overlay é removido e o próximo tick pode ficar até 5 min; alarmes exatos podem ser adiados em Doze. | — | **ESTÁTICO**: `DailyBlockController.kt:174-181` (tick só com tela acesa; `MAX_TICK_MILLIS = 5 min`, `:225`); `SCREEN_ON/USER_PRESENT` reavaliam (`:73-83`) | **BAIXO** | Não é bypass real (eventos de janela reavaliam), mas pode atrasar o overlay/re-bloqueio após a tela ligar em cenários de bloqueio do PC | religar no `SCREEN_ON` já existe; manter | Atraso raro |

### Grupo B — Burlar a detecção de conteúdo (o núcleo do bloqueio de pornografia)

| ID | Vetor | Como se faz | Pré-requisitos | Resultado | Severidade | Por que a defesa falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|
| **B-01** | **Navegador fora da lista fixa** | Play Store (não bloqueada) → instalar **DuckDuckGo Browser**, **Firefox Focus**, **Vivaldi**, **Kiwi**, **Yandex**, **Opera Mini**, **Firefox Nightly (`org.mozilla.fenix`)**, etc. → navegar em qualquer site. `isBrowser(pkg)` = `pkg in browserAddressBars` (`service/FocusBlockRules.kt:25-36,54`) e todo o fluxo de inspeção está sob `if (isBrowser(...))` (`FocusAccessibilityService.kt:117-123`). | Play Store + rede | **ESTÁTICO comprovado**; lista tem exatamente 6 pacotes; Play Store instalada (`OBSERVADO`) | **CRÍTICO** | Lista fechada de navegadores; instalar app não é protegido por senha; não há varredura de quem trata `ACTION_VIEW` http/https | Descobrir navegadores em runtime via `PackageManager.queryIntentActivities(VIEW http/https)` e acrescentar ao filtro; **bloquear instalação de apps** (senha) e/ou Device Owner (§7.7, §7.5) | Apps com nomes/atividades que não declarem VIEW http (ex.: WebView puro) ainda escapam da descoberta |
| **B-02** | **WebView / navegador interno de apps permitidos** | Telegram → “Mensagens salvas” (ou qualquer chat) → cole `https://xvideos.com` → toque no link → abre no **navegador interno** do Telegram (`org.telegram.messenger`, não mapeado). Idem Discord (`com.discord`), Facebook (`com.facebook.katana`), e no modo seletivo Instagram (`com.instagram.android`) e Reddit (`com.reddit.frontpage`), que são explicitamente liberados (`FocusBlockRules.shouldBlockApp` em modo seletivo só bloqueia Twitter, `:64-65`). | Apps já instalados (Telegram, Discord, Facebook — **OBSERVADO**) | **ESTÁTICO comprovado**: `isBrowser()` só conhece 6 pacotes; para os demais nenhum evento é inspecionado e nenhum app é bloqueado no seletivo | **CRÍTICO** | O bloqueio é “por pacote de navegador”, não “por conteúdo”. Telegram/Discord/Facebook estão instalados e fora de `blockedApps` e de `browserAddressBars` | Varredura de texto também para apps não-navegador enquanto a janela adulta estiver ativa (títulos/toolbars de WebView); bloquear/limitar apps de risco no modo seletivo; DNS/VPN local como camada de rede (§7.7) | WebView que só renderiza canvas/imagem não expõe URL ao a11y; Telegram/etc. entregam mídia por MTProto (invisível a DNS) |
| **B-03** | **Mídia adulta dentro de apps de mensagem** | Entrar em canais públicos do Telegram (ou grupos do Discord/Facebook) que postam pornografia; o conteúdo é exibido nativamente, sem barra de endereço e sem URL. Telegram **não está** nas listas de bloqueio. | Telegram instalado (OBSERVADO) | **ESTÁTICO comprovado** (não há regra para app de mensagem; só 6 navegadores + 6 apps sociais) | **CRÍTICO** | O modelo de defesa (texto de URL) não enxerga conteúdo de app; e Telegram não é bloqueado | Decisão de produto: bloquear Telegram/Discord/Facebook no modo seletivo (com senha para mudar a lista); ou aceitar como risco residual explícito (§6, item 11) | Filtro local não distingue conteúdo E2E/MTProto; só bloquear o app resolve |
| **B-04** | **Imagens de busca (Google/Bing Imagens)** | No Chrome/Firefox (mapeados) abrir `google.com/imghp` ou `google.com/search?tbm=isch`, desligar SafeSearch na conta, buscar termos que a regex não cobre (ex.: “brasileirinhas”, “sexlog”, “missav”) → a página de resultados exibe **as imagens adultas**; a barra continua `google.com/...`, o título “Google Imagens” não casa com as listas. | Navegador mapeado; SafeSearch off | **ESTÁTICO** (o detector olha host da URL = google.com; `shouldBlockBrowserText` só casa host de adulto em textos visíveis, `:88-101`) | **ALTO** | A imagem é servida pela página do buscador; não há análise de conteúdo (`MAX_BROWSER_TEXT=500`, sem OCR/categoria) e a lista de termos é curta | DNS/VPN de filtro de conteúdo (não cobre thumbnails), bloquear a página de imagens por URL (`tbm=isch`) teria falso-positivo; na prática: exigir SafeSearch forçado (só DO/políticas) ou aceitar | Difícil de fechar sem MDM/DNS com categoria de imagem |
| **B-05** | **Domínios adultos fora da lista e sem palavra-chave** | Abrir em navegador mapeado sites não listados e sem termo no domínio/título: ex. `brasileirinhas.com.br`, `sexlog.com`, `missav.com`, `javdb.com`, `fatalmodel.com`, `coelhinhas.tv`. O porte das listas de `FocusBlockRules` (55 hosts + regex) confirma `bloqueado=false` para todos | Navegador mapeado | **ESTÁTICO comprovado** com `verificacao-listas.txt` (porte fiel das listas/regex) | **ALTO** | Lista de 55 domínios é uma fração mínima do universo; regex cobre poucos termos e só o que está visível/curto | DNS/VPN com filtro de conteúdo (lista mantida por terceiros) + detecção de páginas por título/keyword ampliada; sem rede, é impossível cobrir por lista local | DNS/DoH embutido em navegador contorna DNS; só VPN local com interceptação resolve de verdade |
| **B-06** | **Reddit NSFW no modo seletivo (por design)** | No modo seletivo, `reddit.com` não está nos hosts inspecionados (só `twitterHosts`) e o check adulto só casa host/keyword — `reddit.com/r/gonewild` não casa nenhum. O feed do Reddit exibe imagens NSFW (subs sem “porn/nsfw” no path). No modo antigo (nível 2) o host é bloqueado (`blockedHosts`). | Navegador mapeado | **ESTÁTICO**: `FocusBlockRules.kt:77-81,96-101` + `SelectiveBlockModeTest` (“reddit.com/r/aww liberado”); `verificacao-listas.txt` confirma `reddit.com/r/gonewild` => bloqueado=false no seletivo | **ALTO** | A política “Instagram e Reddit liberados” colide com “pornografia bloqueada”: Reddit é das maiores fontes de NSFW | No seletivo, bloquear host `reddit.com` (ou permitir só subreddits específicos via app); mudar a política com senha (§7.9) | Se liberar Reddit por decisão, aceitar NSFW; não há como filtrar feed por URL |
| **B-07** | **Frontends/proxies de Twitter-X** | `nitter.net`, `xcancel.com` (frontends do X), `translate.google.com/translate?u=https://x.com/...`, `web.archive.org/web/*/x.com/...`: o host da URL não é `x.com/twitter.com` e o título não contém o host; conteúdo do X renderizado. | Navegador mapeado | **ESTÁTICO** (a checagem é `hostOf(address)` contra `twitterHosts`, `:108-111`) | **ALTO** | Bloqueio por domínio exato não cobre espelhos/proxies; a lista de hosts do X é literal | Detecção de texto de página (nomes “Nitter”, “xcancel”, conteúdo do X) + DNS; residualmente, proxies infinitos | Burlável por qualquer espelho novo |
| **B-08** | **PWA/WebAPK em modo standalone** | Instalar site como app (Chrome → “Adicionar à tela inicial”) → abre em janela sem barra de endereço; o pacote é Chrome (inspecionado), mas não há nó de URL e o título pode não casar. Só é possível instalar PWA de sites que já abrem (bloqueados antes); útil se combinado com A-01/B-07. | site acessível no momento da instalação | **HIPÓTESE/ESTÁTICO** (sem barra ⇒ sem URL para ler; deep scan só textos ≤500 chars) | **MÉDIO** | A detecção depende do texto da barra/título curto que o modo standalone não expõe | Varredura de título por keyword também em modo standalone; DNS/VPN | Fica difícil separar conteúdo legítimo |
| **B-09** | **Instalar APK adulto/side-load** | Baixar APK de site adulto (Telegram/Chrome → instalar) ou app de vídeo; `PackageInstaller` está na lista de `settingsPackages`, **mas** o guard só procura “Foco & Bem-Estar” na tela — a tela de instalação mostra o nome do **app instalador**/arquivo, não o do bloqueador. `install_non_market_apps=1` (OBSERVADO). | aparelho + APK | **ESTÁTICO**: `FocusAccessibilityService.kt:126-134` + `:244-268`; nenhum gate de instalação | **ALTO** | Não há proteção de instalação; o guard de PackageInstaller dispara só se o nome do bloqueador aparecer na tela (não aparece) | Exigir senha para abrir tela de instalação (guard no PackageInstaller com overlay) e/ou DO `DISALLOW_INSTALL_UNKNOWN_SOURCES`; bloquear a Play Store durante janelas de bloqueio (opcional) | Sem DO, instalar APK é livre se o gate for só a11y (pode ser morto com A-02) |
| **B-10** | **Mídia já em reprodução / PiP** | Vídeo adulto iniciado antes do bloqueio continua tocando; Chrome PiP pode manter o vídeo em janelinha. O bloqueio remove overlay e manda HOME, mas **não pausa mídia**. | vídeo iniciado antes | **HIPÓTESE** (testável pelo `android-live`) | **MÉDIO** | Nada no código interage com áudio/PiP (`handleBlockedApp` só HOME + overlay) | Ao ativar bloqueio, enviar `AudioManager.requestAudioFocus`/`ACTION_AUDIO_BECOMING_NOISY`? (não pausa outros apps); detectar janela PiP do Chrome e bloquear | Sem DO/MediaSession universal, não dá para pausar app alheio |
| **B-11** | **Conteúdo local / YouTube** | Vídeos/fotos adultas em `Fotos`/`Arquivos`, ou YouTube (não bloqueado) com conteúdo “adulto soft”. | — | **ESTÁTICO** (nada monitora apps de mídia local/YouTube) | **BAIXO/MÉDIO** | Escopo do app é URL/app social, não conteúdo de mídia | Aceitar; ou bloquear apps específicos por decisão | Inerente a um bloqueador local |
| **B-12** | **Split-screen / exceção de chamada** | O overlay do bloqueio diário é removido quando o pacote em foco está em `callPackages` (`DailyBlockController.kt:36-39,164-173`). Tentar: abrir “Chamadas” (botão do overlay) → split-screen com navegador. | diário ativo | **HIPÓTESE não confirmada** (a ordem dos eventos/foco provavelmente re-bloqueia) | **MÉDIO** (se confirmar) | A exceção de chamada é por pacote em foco, não por “há chamada em andamento” | Trocar por checagem de estado de chamada (`TelecomManager.isInCall`) em vez de pacote em foco; ver §7.3 | Baixo |

### Grupo C — Contornar os bloqueios temporais, o push e o estado

| ID | Vetor | Como se faz | Pré-requisitos | Resultado | Severidade | Por que a defesa falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|
| **C-01** | **Mudar data/hora/fuso** | Ajustes → **Data e hora** (ou desligar automático e adiantar). A página não contém o nome do app ⇒ o guard anti-tamper não dispara (`FocusAccessibilityService.kt:244-268`); no modo seletivo não há overlay. Efeitos: (1) encerra bloqueio diário (`shouldBlock` usa `LocalDateTime.now()`, `DailyBlockPolicy.kt:51-54`); (2) encerra janela adulta/Pomodoro do PC (`isAdultBlockActive`/`shouldBlockAt` usam `System.currentTimeMillis`, `:71-72,110-117`); (3) faz FCM novo parecer vencido (`acceptPomodoro` exige `end > now`, `:98-99`); (4) zera lockout da senha (`PasswordManager.kt:72-73,108-114`) | Ajustes acessíveis (seletivo: sim) | **ESTÁTICO comprovado** | **CRÍTICO** | Todo o estado temporal é wall-clock do aparelho, que o usuário controla; a página que muda isso é justamente uma que o guard não conhece | Reancorar bloqueios em `SystemClock.elapsedRealtime()` (monotônico) junto do wall-clock; detectar `ACTION_TIME_CHANGED/TIMEZONE_CHANGED` e, se um bloqueio estava ativo, manter pelo tempo monotônico e alertar; exigir que o PC reenvie/confirme; Device Owner `DISALLOW_CONFIG_DATE_TIME` (§7.6) | Bloqueio diário é “horário de parede” por conceito; sem DO, o relógio continua manipulável |
| **C-02** | **A frase de liberação é exibida (e automatizável)** | A tela de bloqueio mostra `DailyBlockPolicy.PHRASE` e o teclado interno impede colar/paste, mas **não impede digitar**. Via ADB/uiautomator dá para automatizar toques (`input tap`); o próprio doc admite “não impede automação de toques” (`docs/bloqueio-diario.md:40`). | diário ativo | **ESTÁTICO**; fricção intencional (`schedule/PhraseKeyboardView.kt:24-137`) | **ALTO** (para automação; MÉDIO para digitação manual) | É um *commitment device*, não um segredo: a frase está na tela e o teclado não valida coerção (a11y pode receber eventos globais? `FLAG_NOT_TOUCH_MODAL` no overlay de app bloqueado, mas o overlay diário é modal) | Frase mais longa/rotativa com timestamp, ou atraso obrigatório (ex.: 60 s de espera + digitar); para automação, não há defesa forte sem DO | Dono pode digitar; automação por ADB sempre possível |
| **C-03** | **PC não sabe se o celular está realmente bloqueado (sem ack)** | A ilha mostra “Celular bloqueado” com base no envio; o app nunca responde. Se a a11y estiver off/crash/force-stop, o push é gravado e o overlay nunca aparece (`PomodoroPushService.kt:14-24`; card só avisa quando aberto, `DailyBlockCard.kt:131-159`). | — | **ESTÁTICO comprovado**: não há envio do celular para o PC (nenhum HTTP/FCM upstream no código) | **ALTO** | Arquitetura 100% unidirecional; “bloqueado” é uma suposição do PC | Ack do celular (§7.4): payload com `blockId/nonce`, `a11yEnabled`, `a11yAlive`, `expiraEm`, via LAN HTTP com HMAC (fallback ntfy); PC só mostra “bloqueado” ao receber ack; alerta quando ack falta | Sem rede, ack atrasa; usar fila + retry |
| **C-04** | **Mensagens FCM forjadas/replay** | Quem tiver a chave FCM do projeto pode enviar `id/durationMs/endMs/adultEndMs`. O app não valida assinatura/remetente. | chave do servidor FCM (está no PC) | **ESTÁTICO**: `PomodoroPushService` só faz `toLongOrNull`; porém `acceptPomodoro` **nunca encurta/libera** e `acceptAdultBlock` só estende (máx. 4 h) (`DailyBlockPolicy.kt:93-115`) | **MÉDIO** (só DoS, não libera) | A ausência de auth é compensada pela monotonicidade; mensagem forjada só **aumenta** bloqueio | Validar `from`/`googleMessageId`/nonce e ignorar mensagens sem `ackNonce` esperado; manter política monotônica | Se a chave vazar, atacante pode bloquear o usuário (DoS) |
| **C-05** | **Agendamento sem acessibilidade** | O bloqueio diário é agendado mesmo sem a11y (alarme/exatidão), mas **nada aplica** se o serviço estiver off. O card avisa “proteção incompleta” só quando o app é aberto (`DailyBlockCard.kt:63,122-123,189-211`). | — | **ESTÁTICO** | **MÉDIO** | O alarme não falha; o efeito falha em silêncio (sem notificação/alerta proativo) | Notificação persistente de “proteção inativa” + reporter ao PC + watchdog | Se o usuário ignorar, nada muda |

### Grupo D — Integridade, instalação e self-control

| ID | Vetor | Como se faz | Pré-requisitos | Resultado | Severidade | Por que a defesa falha | Correção proposta | Risco residual |
|---|---|---|---|---|---|---|---|---|
| **D-01** | **Segundo usuário / visitante** | Ajustes → Sistema → **Vários usuários** → Adicionar visitante/usuário → trocar. O app só existe no user 0; serviços de acessibilidade são por usuário ⇒ **não há bloqueio nenhum** no outro perfil (Chrome é app de sistema e tende a existir no novo usuário). | aparelho | **OBSERVADO** que a ROM suporta (`Max users: 4`, `Supports switchable users: true`, restrições de visitante); **caveat**: durante o teste do `android-live` a criação/remoção do usuário 10 coincidiu com `am_crash` do `system_server` (`No UserInfo for 10`) — multi-usuário nesta ROM é instável e pode reiniciar o sistema | **ALTO** | Não há verificação de usuários nem restrição (só DO pode `DISALLOW_ADD_USER`) | Detectar `UserManager.userCount > 1` e alertar/reforçar; Device Owner + `DISALLOW_ADD_USER`; ack mostra ao PC que o perfil não está coberto | Sem DO não há como impedir a criação de usuário |
| **D-02** | **Perfil de trabalho (Island/Shelter etc.)** | Instalar app de “work profile” da Play Store → clonar Chrome/Telegram no perfil de trabalho → a a11y do user 0 não vê apps do perfil. | Play Store | **ESTÁTICO** (a11y é por usuário; nenhuma regra cobre perfis) | **ALTO** | O modelo não considera perfis gerenciados | Detectar `UserManager.getUserProfiles()` e alertar; DO para bloquear criação de perfil (`DISALLOW_ADD_MANAGED_PROFILE` no DO) | Sem DO, perfis gerenciados são livres |
| **D-03** | **Limite diário e “dívida” ajustáveis sem senha** | Dashboard → “Limite diário” → escolher 12 h (sem senha); cartão Dívida → “Zerar” (sem senha). Isso desliga também o P&B automático (que depende de `isLimitExceeded`). | app aberto | **ESTÁTICO**: `FocusDashboardScreen.kt:264-267` → `DashboardViewModel.setDailyLimit` (`:147-158`); `:300-305` → `resetMoneyOwed` (`:199-202`); nenhum `verifyPassword` no caminho | **ALTO** (o controle de tempo é o outro objetivo do app) | Só nível/desinstalação/update pedem senha; os parâmetros do próprio controle são livres | Exigir senha (ou atraso de 10 min) para subir limite, zerar dívida e desligar seletivo; manter “reduzir” livre | Dono sempre pode autenticar |
| **D-04** | **Build DEBUGGABLE + `debug.keystore` versionado** | `run-as com.example.focusapp` (ADB) lê/edita prefs (apagar bloqueio, remover senha, mudar nível); e o APK é assinado com a chave de debug versionada (`app/build.gradle.kts:22-40`, `app/debug.keystore` no git) — a validação de assinatura do updater (`update/ApkInstaller.kt:27-44`) aceita qualquer build feito desse repo/chave, e o instalado tem `flags=[DEBUGGABLE …]`. | ADB ou o próprio repo/fonte | **OBSERVADO** (`DEBUGGABLE` + `run-as` funciona) + **ESTÁTICO** (`signingConfig=debug` no release) | **MÉDIO** (para o dono técnico é total; para terceiros não, repo é privado) | Build de release reaproveita a chave de debug commitada e sai debuggable | Build release assinado com chave própria (guardada fora do repo), `debuggable=false`, e updater comparando também `versionCode` + assinatura (já faz) — mas a chave de debug no repo é aceitável só porque o repo é privado; documentar | Quem tem o repo/keystore substitui o app; é o “dono trapaceando” |
| **D-05** | **Relógio zera lockout da senha** | Adiantar a hora (C-01) → `lockoutRemainingMillis` (wall clock, `PasswordManager.kt:72-73`) zera → tentar senhas sem esperar o backoff. | Ajustes | **ESTÁTICO** | **BAIXO** (PBKDF2 310k + 6 chars tornam força bruta offline inviável; online é lenta e limitada) | Lockout por wall-clock | Lockout com `elapsedRealtime` + falhas persistidas (já persistidas); limite de tentativas por dia | — |
| **D-06** | **P&B (escala de cinza) desligável nos Ajustes** | Ajustes → Acessibilidade → **Correção de cor** → desligar (página sem o nome do app ⇒ guard não dispara). O FGS re-liga quando o limite efetivo estiver estourado (`ScreenTimeTrackerService.kt:303-323`, tick até 5 min quando excedido). | Ajustes | **ESTÁTICO** | **BAIXO** (cosmético, não bloqueia uso) | Guard por nome do app também não cobre essa página; mas o FGS persistente restaura | Guard de páginas de acessibilidade (inclui Correção de cor) no watchdog; repor em `SCREEN_ON` | Dono consegue alternar; P&B é só fricção |

---

## 4. Respostas às perguntas centrais (1–3)

### 4.1 Dá para fugir do bloqueio? Por quais caminhos hoje?
**Sim, com folga.** Em ordem de praticidade **no aparelho atual** (Moto G20/Android 11, modo seletivo ligado por padrão):

1. **Desligar a acessibilidade nos Ajustes** (A-02). Em modo seletivo não há overlay sobre os Ajustes e a proteção anti-tamper procura o nome errado (“Foco & Bem-Estar” *vs.* “Serviço de Foco e Proteção”). ~15 s, sem senha.
2. **Forçar parada** (A-03): o serviço fica “enabled” mas não bound; o app não religa e o card diz “Ativo”. (Observado: ~13 min nesse estado, só voltou por ação externa.)
3. **Um toque no “Modo seletivo”** (A-01) desliga o **bloqueio de conteúdo adulto** (o de Twitter/X continua no nível 2). Sem senha.
4. **Conteúdo por onde não há URL para ler** (B-02/B-03): navegador interno do **Telegram/Discord/Facebook** (todos instalados) ou mídia de canais do Telegram. Navegadores não mapeados (B-01) pela Play Store. Imagens de busca (B-04). Sites fora da lista (B-05).
5. **Mudar data/hora** (C-01): encerra bloqueio diário e janela adulta do PC e faz push novo parecer vencido. Página sem nome do app ⇒ guard não dispara.
6. **Caminhos com atrito/ferramenta:** modo seguro (A-04), ADB (A-05, já ligado), desinstalar após matar a a11y e desativar o admin (A-06 — o admin **não** bloqueia desinstalação), segundo usuário/visitante (D-01), perfil de trabalho (D-02), instalar APK/browser (B-09/B-01), fixar/limpar dados (A-08).
7. **VPN/DoH não ajuda** a furar *este* bloqueio (ele é local, por texto na tela); mas **também não atrapalha** quem quer pornografia: como não há camada de rede, DNS/VPN são irrelevantes para o app. (No PC, o SysGuard usa DNS; no celular, a única defesa de rede hoje é a `Private DNS` do sistema — veja §6.)
8. **Gerenciador de tarefas**: “limpar tudo” normalmente **não** mata serviço de acessibilidade+FGS de forma duradoura; **forçar parada** mata (e não volta). É mito achar que “limpar recentes” equivale a force-stop — e é mito achar que o app se recupera sozinho.

### 4.2 Dá para impedir o usuário de desligar a acessibilidade? De que formas (o que funciona de verdade no Android 11)?
**Impedir de forma absoluta, sem Device Owner: não.** O Android exige ação do usuário para desabilitar/abusar, e nenhum app normal pode capturar o toque no toggle. O que **funciona de verdade** no Android 11:

1. **Watchdog com `WRITE_SECURE_SETTINGS` (o app já tem a permissão)**: um FGS/alarme que verifica `enabled_accessibility_services` + **heartbeat real do serviço** e, ao detectar OFF/crash, reescreve o `Settings.Secure` (`ENABLED_ACCESSIBILITY_SERVICES`, `ACCESSIBILITY_ENABLED=1`). O `AccessibilityManagerService` observa esse setting e volta a fazer bind. É o mecanismo mais barato e eficaz enquanto o processo está vivo; funciona mesmo que o usuário desligue no toggle (religa em segundos). Detalhe em §7.1–7.2.
2. **Segundo pacote guardião** (assinatura compartilhada, `WRITE_SECURE_SETTINGS` concedido por ADB uma vez): sobrevive ao force-stop do app principal; reescreve o setting, relança o app (atividade/FGS; com `SYSTEM_ALERT_WINDOW` pode iniciar atividade em background) e pode mostrar **overlay de fallback** (`TYPE_APPLICATION_OVERLAY`) cobrindo a tela enquanto a a11y estiver morta — o usuário não consegue usar os Ajustes sob o overlay. É a única forma de “sobreviver ao force-stop” de verdade. Detalhe em §7.3.
3. **Guard de telas de Ajustes corrigido**: casar **todos** os rótulos (`app_name`, `accessibility_service_name`, package) e a tela do serviço; ao abrir, HOME + overlay/aviso. Aumenta muito o atrito (a página nem fica utilizável) mas não é “impedir” — é expulsar.
4. **Device Owner** (exige reset de fábrica/provisionamento; hoje **não** é DO): `setUninstallBlocked(true)` (real!), `DISALLOW_SAFE_BOOT`, `DISALLOW_ADD_USER`, `DISALLOW_ADD_MANAGED_PROFILE`, `DISALLOW_CONFIG_DATE_TIME`, `DISALLOW_DEBUGGING_FEATURES`, `DISALLOW_INSTALL_UNKNOWN_SOURCES`; pode também `setGlobalSetting`/`setSecureSetting` em chaves permitidas. É o único caminho para **prevenir** (não só reagir). O README já reconhece que o Nível 3 exige DO (`README.md:65-67`).
5. **Detecção de ADB**: `Settings.Global.ADB_ENABLED` / `DEVELOPMENT_SETTINGS_ENABLED`; ao detectar, revalidar o estado e avisar o PC. **Não** dá para desligar ADB sem DO (`DISALLOW_DEBUGGING_FEATURES`). Mas o watchdog neutraliza o bypass mais usado (`settings put ...`), porque religa.
6. **Alarmes/exatidão** sozinhos **não** religam acessibilidade: só disparam código; se o processo não roda (force-stop), o alarme é cancelado. Alarme só ajuda dentro do guardião (segundo pacote), para se reavaliar e religar.
7. **Mitos a evitar:** “serviço de acessibilidade volta sozinho depois de force-stop” — **não** (evidência A-03); “notificação persistente encerra o serviço se o usuário desligar” — não no Android 11; “overlay de tela cheia bloqueia o usuário de mexer nos Ajustes” — só se **estiver sendo exibido**; no modo seletivo não há overlay; “bloquear o botão Desativar do admin” — não é possível, o máximo é o texto de aviso.

### 4.3 Dá para o app sobreviver a `force-stop`/Gerenciador de Tarefas? O que é possível e o que é mito?
- **Sobreviver dentro do mesmo pacote: não.** `force-stop` coloca o pacote em estado *stopped*: processo morto, alarmes cancelados, FCM não entregue, receivers não chamados, a11y não rebindada (fica “enabled” no papel). O `START_STICKY` do FGS (`ScreenTimeTrackerService.kt:143`) só ajuda quando o sistema mata o processo por memória — **não** após force-stop. `BootReceiver` só age em boot/update.
- **O que é possível (e recomendado):**
  1. **Detectar + recuperar por fora** com um **segundo pacote** (guardião) que continua vivo (FGS próprio + `START_STICKY` + alarme exato + boot): verifica heartbeat do principal; se sumiu, re-liga a a11y via `WRITE_SECURE_SETTINGS` e relança o app (atividade via `SYSTEM_ALERT_WINDOW`/notificação de alta prioridade). Isso **não impede** o force-stop; **recupera** dele.
  2. **Fallback de overlay do guardião** (`TYPE_APPLICATION_OVERLAY`): enquanto o principal estiver morto, cobre a tela com um aviso “proteção desligada — reative”; sem tocar em Ajustes (que ficam sob o overlay). Isso é bloqueio **efetivo** enquanto o guardião viver.
  3. **Sinalizar para o PC** (ack/status): o PC passa a mostrar “não confirmado”/alerta quando o celular some, em vez de mentir “bloqueado”.
- **O que é mito:** “um app pode se auto-religar depois de force-stop” (não); “acessibilidade fica ligada mesmo com o app morto” (fica *enabled*, mas **não aplica** nada); “FCM acorda o app force-stopped” (não entrega); “Gerenciador de tarefas do fabricante não afeta” (se for force-stop, afeta; se for só “limpar recentes”, normalmente não).
- **Limite honesto:** com **ADB/root** (já ligado neste aparelho) o usuário sempre vence — pode force-stop os dois pacotes, apagar dados, desinstalar. A meta realista é: **o caminho preguiçoso (impulso) fica difícil**; o caminho “vou pegar o PC e usar ADB” fica **visível e alertado** (PC mostra “celular sem proteção”), criando atrito suficiente.

---

## 5. Defesas que já funcionam (não mexer)

1. **Bloqueios do PC são monotônicos**: mensagem nunca encurta nem libera bloqueio ativo; só estende (`DailyBlockPolicy.acceptPomodoro:93-104`, `acceptAdultBlock:110-115`). Forjar FCM só piora a vida do usuário.
2. **Tetos de segurança do PC**: Pomodoro ≤120 min por mensagem, adulto ≤4 h; `end` limitado a `now + duração` (tolera relógio do PC diferente) (`DailyBlockPolicy.kt:40-44,96-99,111`).
3. **Bloqueio adulto sem frase de liberação**: não há “escape” por frase; só o interruptor do card (que é o buraco A-01 — mas o *design* da frase está certo) (`DailyBlockStore.kt:71-78`; `DailyBlockCard.kt:151-158`).
4. **Bloqueio diário sobrevive a reboot/tela apagada**: record em SharedPreferences com `commit`, `released` persistido, alarmes exatos + reavaliação em `SCREEN_ON/USER_PRESENT` (`DailyBlockStore.kt:26-54,89-94`; `DailyBlockController.kt:70-87`; `DailyBlockScheduler.kt:19-49`).
5. **Sem liberação por mudança de relógio no record**: o record diário é data+minuto; adiantar o relógio encerra o bloqueio (buraco C-01), mas não há como “religar” depois; e apagar tela não libera (a liberação só grava `released` com frase correta).
6. **Update valida assinatura e versionCode**: rejeita APK de outro app, downgrade e assinatura diferente (`update/ApkInstaller.kt:27-44`). (A assinatura em si é fraca, D-04.)
7. **Senha PBKDF2-HMAC-SHA256** 310.000 iterações, salt 32 bytes, comparação em tempo constante, migração de hash antigo, salt/hash versionados; **lockout crescente** (30 s → 15 min); falhas/lockout persistidos (`security/PasswordManager.kt:33-151`; `PasswordLockoutPolicy.kt:1-17`).
8. **Backup exclui os prefs sensíveis**: hash/salt, token FCM/blocos, token GitHub (`res/xml/backup_rules.xml`, `res/xml/data_extraction_rules.xml`) — evita restaurar bloqueio velho ou vazar hash via `adb backup`.
9. **Componentes perigosos não são exportados**: FGS, widget service, receiver de alarme, receivers de boot/Now Bar, `PomodoroPushService` (`AndroidManifest.xml:50-127`); provider do updater `exported=false` com FileProvider (`:39-48`). A acessibilidade e o admin têm permissões de binding do sistema (`:130-157`).
10. **Overlay usa `TYPE_ACCESSIBILITY_OVERLAY`** (não depende de `SYSTEM_ALERT_WINDOW`, que o usuário poderia revogar em “Sobrepor outros apps”): `FocusAccessibilityService.kt:275-284`; `DailyBlockController.kt:205-207`.
11. **Exceção de chamada** para não prender o celular em emergência (`DailyBlockController.kt:36-39,164-173`) — bom para segurança; só é um possível vetor (B-12) em cenário não confirmado.
12. **Anti-tamper de Ajustes** (quando o rótulo casa — ex.: página de **admin**, de apps, de uso): HOME + abre o app (`FocusAccessibilityService.kt:244-268`), e a checagem só roda no nível 2+ (configurável só com senha).
13. **Senha para ações sensíveis**: desinstalar (`MainActivity.kt:205-274` — e se o usuário cancelar, restaura admin/grayscale, `:276-289`), instalar update (`ui/SettingsScreen.kt:181-198` + `AppUpdateViewModel.kt:104-113`, com janela de tolerância de 2 min), **rebaixar nível** (`DashboardViewModel.selectSecurityLevel:124-145` + `FocusDashboardScreen.kt:330-340`), abrir Ajustes quando a a11y já está ativa (`DailyBlockCard.kt:89-96,321-368`).
14. **FGS persistente do rastreio** (`START_STICKY`, notificação, reavaliação em mudanças de tela) e invalidação da janela de tolerância ao apagar a tela (`ScreenTimeTrackerService.kt:99-123,196-208`).
15. **P&B via `WRITE_SECURE_SETTINGS`** com reaplicação automática quando o limite efetivo é excedido (`ScreenTimeTrackerService.kt:303-323`) — defesa cosmética, mas persistente.

---

## 6. Defesas recomendadas (ordem de impacto / implementabilidade)

| # | Defesa | Impacto | Esforço | Onde |
|---|---|---|---|---|
| 1 | **Watchdog de acessibilidade no FGS** com heartbeat real e religamento via `WRITE_SECURE_SETTINGS`; corrigir `isEnabled()` para `isEnforcing()` (enabled **e** heartbeat vivo) e mostrar o estado real no card | Fecha A-02/A-03/A-07 (o maior buraco) | Baixo (~150 linhas; permissão já concedida) | §7.1, §7.2 |
| 2 | **Senha para desligar o “Modo seletivo”** e separar o bloqueio adulto do switch/nível (preferência própria, protegida) | Fecha A-01 (buraco de 1 toque) | Baixo | §7.9 |
| 3 | **Ack/status real do celular para o PC** (LAN HTTP + HMAC, fallback ntfy) com `a11yAlive`, `expiraEm`, `nonce`; PC só mostra “bloqueado” com ack e alerta se sumir | Fecha C-03 e dá visão real (Q5) | Médio | §7.4 |
| 4 | **Segundo pacote guardião** (FGS+alarme+overlay de fallback+relançar o app, sobrevive a force-stop do principal) | Recupera de A-03/A-05 e força o atrito | Alto (2º módulo Gradle + assinatura + ADB para permissão) | §7.3 |
| 5 | **Reancorar bloqueios em `elapsedRealtime`** e detectar mudança de relógio; manter bloqueio pelo tempo monotônico quando o wall-clock pular; alarme/aviso de “relógio alterado” | Fecha C-01 | Médio | §7.6 |
| 6 | **Gate de instalação** (senha/overlay no `PackageInstaller` e apps de “fontes desconhecidas”) e **gate da Play Store** durante janelas de bloqueio | Fecha B-01/B-09 | Médio | §7.5 |
| 7 | **Descobrir navegadores em runtime** via `PackageManager.queryIntentActivities(VIEW http/https)` e incluir no filtro; **varredura de título/URL para qualquer app** enquanto a janela adulta estiver ativa (WebView) | Fecha B-01/B-02 na parte que dá para fechar | Médio | §7.7 |
| 8 | **Camada de rede**: `Private DNS` em modo **strict** com provedor de filtro família (hoje está `opportunistic`, ou seja, sem efeito), monitorada/reposta pelo app; ideal = `VpnService` local com filtro DNS/SNI (pega navegador novo e WebView; não pega Telegram/MTProto) | Cobre B-04/B-05/B-07 parcialmente | Médio–Alto | §6 nota + §7.7 |
| 9 | **Corrigir o guard de Ajustes** para casar `accessibility_service_name` **e** `app_name` **e** package; incluir páginas de Acessibilidade/Correção de cor; detectar intenção e reagir | Fecha A-02 quando o watchdog ainda não religou | Baixo | §7.5 |
| 10 | **Detecção de ADB/desenvolvedor/usuários/safe-mode e alerta** (não previne sem DO, mas torna visível) | Reduz A-04/A-05/D-01/D-02 | Baixo | §7.8 |
| 11 | **Política honesta dos apps de mensagem**: bloquear Telegram/Discord/Facebook no modo seletivo (com senha para editar a lista) ou assumir explicitamente o risco residual de B-03 | Fecha B-03 por decisão | Baixo (decisão) | §6 |
| 12 | **Device Owner no próximo reset** (ou aparelho dedicado): uninstall block real, `DISALLOW_SAFE_BOOT/ADD_USER/ADD_MANAGED_PROFILE/CONFIG_DATE_TIME/DEBUGGING_FEATURES/INSTALL_UNKNOWN_SOURCES` | Prevenção de verdade de A-04/A-06/A-08/D-01/D-02 | Alto (reset/provisionamento) | §6 |
| 13 | **Build release próprio, não-debuggable**, keystore fora do repo; updater continua validando assinatura/versionCode | Fecha D-04 (self-tamper via `run-as`) | Baixo | `app/build.gradle.kts` |
| 14 | **Senha (ou atraso) para subir limite diário, zerar dívida, desligar P&B** | Conserta o self-control de tempo (D-03) | Baixo | §7.9 |

> Nota de rede (resposta a VPN/DoH): o bloqueio do app é **local e por texto**; VPN/DoH não o contornam — e também não o reforçam. Se quiser barreira de rede, `Private DNS` strict com filtro família (ex.: CleanBrowsing Family / AdGuard Family) cobre **navegadores mapeados e novos** por domínio, mas **não** cobre DoH embutido do Chrome/Firefox nem conteúdo de apps (Telegram). A combinação realista é app-level (a11y) **+** rede **+** ack/PC **+** guardião.

---

## 7. Proposta de código (esboços Kotlin concretos)

> Esboços para o módulo `app/` (não aplicados — repo do Android é somente leitura para este agente). Patch textual consolidado também em
> `docs/redteam/patches/android-codigo/`.

### 7.1 Estado real de enforcement (heartbeat) — `service/FocusEnforcementState.kt` (novo)

```kotlin
package com.example.focusapp.service

import android.content.Context
import android.os.SystemClock

/**
 * Liveness REAL do serviço de acessibilidade. "enabled" nos Ajustes não garante bind:
 * um crash/force-stop deixa o serviço habilitado e morto. O heartbeat é gravado pelo
 * próprio FocusAccessibilityService (em onServiceConnected e periodicamente) e lido
 * pelo FGS/guardião/card.
 */
object FocusEnforcementState {
    private const val PREFS = "focus_enforcement_state"
    private const val KEY_LAST_HEARTBEAT = "a11y_last_heartbeat_elapsed_ms"
    private const val KEY_LAST_BLOCK_EVENT = "a11y_last_block_event_elapsed_ms"
    private const val HEARTBEAT_TIMEOUT_MS = 90_000L

    private fun prefs(c: Context) = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun beat(context: Context, blockedEvent: Boolean = false) {
        val e = prefs(context).edit()
            .putLong(KEY_LAST_HEARTBEAT, SystemClock.elapsedRealtime())
        if (blockedEvent) e.putLong(KEY_LAST_BLOCK_EVENT, SystemClock.elapsedRealtime())
        e.commit()
    }

    fun isAccessibilityEnabled(context: Context): Boolean =
        FocusAccessibilityService.isEnabled(context)

    /** true somente se habilitado E com heartbeat recente (serviço realmente vivo). */
    fun isEnforcing(context: Context): Boolean {
        if (!isAccessibilityEnabled(context)) return false
        val last = prefs(context).getLong(KEY_LAST_HEARTBEAT, 0L)
        return last > 0L && SystemClock.elapsedRealtime() - last < HEARTBEAT_TIMEOUT_MS
    }

    fun clear(context: Context) = prefs(context).edit().clear().commit()
}
```

No `FocusAccessibilityService`:
```kotlin
override fun onServiceConnected() {
    super.onServiceConnected()
    FocusEnforcementState.beat(this)                 // liveness
    heartbeatHandler.postDelayed(heartbeatTick, 30_000L)  // prova de vida periódica
    // ... resto do onServiceConnected atual ...
}
private val heartbeatTick = object : Runnable {
    override fun run() {
        FocusEnforcementState.beat(this@FocusAccessibilityService)
        heartbeatHandler.postDelayed(this, 30_000L)
    }
}
override fun onDestroy() {
    heartbeatHandler.removeCallbacksAndMessages(null)
    super.onDestroy()
    // ... resto ...
}
```
E trocar o uso no card/ViewModel: `hasFocusAccessibility = FocusEnforcementState.isEnforcing(context)`
(em vez de `FocusAccessibilityService.isEnabled`, `DashboardViewModel.kt:104`, `DailyBlockCard.kt:51,83,135`).

### 7.2 Watchdog de acessibilidade no FGS — `service/AccessibilityWatchdog.kt` (novo)

```kotlin
package com.example.focusapp.service

import android.content.Context
import android.provider.Settings
import com.example.focusapp.display.GrayscaleController

object AccessibilityRepair {
    private const val COMPONENT =
        "com.example.focusapp/com.example.focusapp.service.FocusAccessibilityService"

    /** Religar acessibilidade sem UI. Requer WRITE_SECURE_SETTINGS (já concedido neste aparelho). */
    fun reEnable(context: Context): Boolean {
        if (!GrayscaleController.hasSecureSettingsPermission(context)) return false
        return try {
            val resolver = context.contentResolver
            val current = Settings.Secure.getString(
                resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
            ).orEmpty()
            val ids = current.split(':').filter { it.isNotBlank() }.toMutableSet()
            if (ids.add(COMPONENT)) {
                Settings.Secure.putString(
                    resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                    ids.joinToString(":")
                )
            }
            Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)
            true
        } catch (_: Exception) { false }
    }
}

/** Roda no ScreenTimeTrackerService (FGS): a cada 15 s e em SCREEN_ON/USER_PRESENT. */
internal class AccessibilityWatchdog(private val context: Context) {
    private var lastAttemptElapsed = 0L

    fun checkAndRepair(nowElapsed: Long = android.os.SystemClock.elapsedRealtime()) {
        if (FocusEnforcementState.isEnforcing(context)) {
            FocusEnforcementState.clearRepairNeeded(context)   // ver 7.1 (prefs)
            return
        }
        if (nowElapsed - lastAttemptElapsed < 5_000L) return     // backoff simples
        lastAttemptElapsed = nowElapsed
        val repaired = AccessibilityRepair.reEnable(context)
        FocusEnforcementState.onRepairAttempt(context, repaired)
        if (FocusEnforcementState.isAccessibilityEnabled(context) && !repaired) {
            // Sem WRITE_SECURE_SETTINGS: pelo menos notificar e pedir reativação (não silenciar).
            FocusNotifier.showProtectionOff(context)
        }
        BlockStatusReporter.reportAsync(context, reason = "watchdog")
    }
}
```
Integração: no `ScreenTimeTrackerService.startAdaptiveTicker()` chame
`watchdog.checkAndRepair()` a cada tick (mín. 15 s) e nos receivers `ACTION_SCREEN_ON/USER_PRESENT`.

### 7.3 Segundo pacote guardião (esboço de contrato, não é um único arquivo)

```
app-focus/            -> com.example.focusapp      (principal, já existe)
app-guard/            -> com.example.focusguard    (novo applicationId, mesma assinatura do principal)
  Manifest:
    <uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW"/>
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE"/>
    <uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED"/>
    <permission android:name="com.example.focusguard.permission.GUARD" android:protectionLevel="signature"/>
    <service android:name=".GuardianService" android:exported="false" android:foregroundServiceType="specialUse"/>
    <receiver android:name=".HeartbeatReceiver" android:exported="true"
              android:permission="com.example.focusguard.permission.GUARD"/>
    <receiver android:name=".BootReceiver" android:exported="false">
      <intent-filter><action android:name="android.intent.action.BOOT_COMPLETED"/></intent-filter>
    </receiver>
```

```kotlin
// app-guard/GuardianService.kt
class GuardianService : Service() {
    private val handler = Handler(Looper.getMainLooper())
    private var lastSeenElapsed = SystemClock.elapsedRealtime()

    private val tick = object : Runnable {
        override fun run() {
            val alive = (SystemClock.elapsedRealtime() - lastSeenElapsed) < 120_000L &&
                    FocusEnforcementState.isEnforcing(this@GuardianService)
            if (!alive) {
                // 1) tenta religar a acessibilidade por secure settings
                AccessibilityRepair.reEnable(this@GuardianService)
                // 2) relança o app principal (atividade) — permitido com SYSTEM_ALERT_WINDOW
                startActivity(Intent().apply {
                    setClassName("com.example.focusapp", "com.example.focusapp.MainActivity")
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED)
                })
                // 3) fallback: overlay cobre a tela até o heartbeat voltar
                FallbackOverlay.show(this@GuardianService, "Proteção desligada. Reative no app.")
            } else {
                FallbackOverlay.hide(this@GuardianService)
            }
            // 4) acorda via alarme exato para sobreviver a Doze
            val am = getSystemService(AlarmManager::class.java)
            am.setExactAndAllowWhileIdle(
                AlarmManager.ELAPSED_REALTIME_WAKEUP,
                SystemClock.elapsedRealtime() + 60_000L,
                PendingIntent.getService(
                    this, 0, Intent(this, GuardianService::class.java),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                )
            )
            handler.postDelayed(this, 60_000L)
        }
    }
    // HeartbeatReceiver.onReceive: lastSeenElapsed = SystemClock.elapsedRealtime()
    // FallbackOverlay: TYPE_APPLICATION_OVERLAY, MATCH_PARENT, com FLAG_NOT_FOCUSABLE? NÃO:
    //   para bloquear toques use sem FLAG_NOT_TOUCHABLE e com MATCH_PARENT; botão "Abrir app".
}
```
No principal: `HeartbeatSender` a cada 30 s e em toda mudança de bloqueio →
`sendBroadcast(Intent("com.example.focusapp.HEARTBEAT").setPackage("com.example.focusguard"))`
(assinatura compartilhada + receiver com permissão `signature`).

### 7.4 Ack/status real para o PC — `push/BlockStatusReporter.kt` (novo) + mudança no `PomodoroPushService`

```kotlin
// PomodoroPushService.kt (novo comportamento)
override fun onMessageReceived(message: RemoteMessage) {
    val data = message.data
    val store = DailyBlockStore(this)
    data["adultEndMs"]?.toLongOrNull()?.let { store.saveAdult(it) }
    val id = data["id"]
    val durationMs = data["durationMs"]?.toLongOrNull()
    val endMs = data["endMs"]?.toLongOrNull()
    if (id != null && durationMs != null && endMs != null) {
        if (store.savePomodoro(id, endMs, durationMs)) DailyBlockScheduler.refresh(this)
    }
    // ACK do que de fato foi aplicado (o PC só deve escrever "bloqueado" após isso).
    BlockStatusReporter.reportAsync(
        this, reason = "fcm", blockId = id, nonce = data["nonce"]
    )
}
```

```kotlin
package com.example.focusapp.push

import android.content.Context
import com.example.focusapp.schedule.DailyBlockStore
import com.example.focusapp.service.FocusEnforcementState
import com.google.firebase.messaging.FirebaseMessaging
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Relata o estado REAL de aplicação para o PC. LAN HTTP com HMAC (segredo compartilhado
 * com a ilha), fallback ntfy. Fila persistida: se a rede cair, reenvia depois.
 */
object BlockStatusReporter {
    private const val PREFS = "focus_ack_queue"
    private const val PC_URL_KEY = "ack_pc_url"       // ex.: http://192.168.0.10:8787/phone-ack
    private const val SECRET_KEY = "ack_hmac_secret"  // colado/derivado na config do celular
    private const val NTFY_TOPIC = "focus-ack-<deviceId>" // fallback público/privado

    fun reportAsync(context: Context, reason: String, blockId: String? = null, nonce: String? = null) {
        Thread {
            val payload = JSONObject()
                .put("type", "ack")
                .put("deviceId", deviceId(context))
                .put("reason", reason)
                .put("timestampMs", System.currentTimeMillis())
                .put("blockId", blockId)
                .put("nonce", nonce)
                .put("a11yEnabled", FocusEnforcementState.isAccessibilityEnabled(context))
                .put("a11yAlive", FocusEnforcementState.isEnforcing(context))
                .put("adbEnabled", adbEnabled(context))
                .put("userCount", userCount(context))
                .put("selective", DailyBlockStore(context).loadSelective())
                .put("adultEndMs", DailyBlockStore(context).loadAdultEnd())
                .put("pomodoroEndMs", DailyBlockStore(context).loadPomodoro()?.endMs)
            post(context, payload.toString())
        }.start()
    }

    private fun post(context: Context, body: String) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val url = prefs.getString(PC_URL_KEY, null) ?: return
        val sig = hmacSha256Hex(prefs.getString(SECRET_KEY, "").orEmpty(), body)
        try {
            val conn = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "POST"; connectTimeout = 5_000; readTimeout = 5_000
                setRequestProperty("Content-Type", "application/json")
                setRequestProperty("X-Focus-Signature", sig)
                doOutput = true
            }
            conn.outputStream.use { it.write(body.toByteArray()) }
            if (conn.responseCode !in 200..299) enqueue(prefs, body)
            conn.disconnect()
        } catch (_: Exception) { enqueue(prefs, body) }
    }
    // enqueue/flush com JSONArray; flush disparado no próximo reportAsync e em SCREEN_ON.
    // hmacSha256Hex: javax.crypto.Mac("HmacSHA256") + hex.
}
```

No PC, `FcmPhoneBlockNotifier` passa a expor `pendingAck` e só marca “Celular bloqueado” quando chegar
`type=ack, a11yAlive=true` para o `blockId/nonce`; se não chegar em 60 s → “não confirmado”.

### 7.5 Guard de Ajustes correto + gate de instalação (patch em `FocusAccessibilityService`/`FocusBlockRules`)

```kotlin
// FocusAccessibilityService.kt
private fun protectedLabels(): List<String> = listOf(
    getString(R.string.app_name),                    // "Foco & Bem-Estar"
    getString(R.string.accessibility_service_name),  // "Serviço de Foco e Proteção"  <-- faltava
    packageName
)

private fun checkSettingsTampering(packageName: String, rootNode: AccessibilityNodeInfo?): Boolean {
    val passwordManager = PasswordManager.getInstance(this)
    if (passwordManager.isGracePeriodActive()) return false
    if (rootNode == null) return false
    val hit = protectedLabels().any { label ->
        rootNode.findAccessibilityNodeInfosByText(label).orEmpty().isNotEmpty()
    }
    if (!hit) return false
    performGlobalAction(GLOBAL_ACTION_HOME)
    startActivity(Intent(this, MainActivity::class.java).apply {
        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
    })
    return true
}

/** Instalação de APK exige senha (ou janela de tolerância do update do próprio app). */
private fun checkInstallerTampering(): Boolean {
    val pm = PasswordManager.getInstance(this)
    if (pm.isGracePeriodActive() || !pm.isPasswordSet()) return false
    performGlobalAction(GLOBAL_ACTION_HOME)
    showBlockedOverlay("Instalação de aplicativos")   // overlay existente
    return true
}
```
Em `onAccessibilityEvent`, dentro do bloco `isSettingsPackage`:
```kotlin
if (reinforced) {
    val isInstaller = packageName in setOf(
        "com.google.android.packageinstaller", "com.android.packageinstaller",
        "com.samsung.android.packageinstaller"
    )
    if (isInstaller) checkInstallerTampering() else checkSettingsTampering(packageName, rootInActiveWindow)
}
```

### 7.6 Bloqueios imunes a mudança de relógio — `schedule/DailyBlockPolicy.kt` + controller

```kotlin
// Record ganha âncora monotônica (sobrevive a ajuste de relógio; reinício usa wall-clock).
internal data class PomodoroBlockRecord(
    val id: String, val startMs: Long, val endMs: Long, val released: Boolean = false,
    val endElapsedRealtime: Long = 0L   // SystemClock.elapsedRealtime() + (endMs - nowMs) no aceite
) {
    fun remainingMs(nowMs: Long, nowElapsed: Long): Long {
        val byWall = endMs - nowMs
        val byMono = if (endElapsedRealtime > 0L) endElapsedRealtime - nowElapsed else Long.MIN_VALUE
        return maxOf(byWall, byMono).coerceAtLeast(0L)   // relógio adiantado não encurta
    }
}
// acceptPomodoro: ao criar/estender, gravar endElapsedRealtime = nowElapsed + (end - nowMs)
// shouldBlockAt(record, nowMs, nowElapsed) = !released && record.remainingMs(nowMs, nowElapsed) > 0
```
E no `DailyBlockController`: no receiver, incluir `Intent.ACTION_TIME_CHANGED/TIMEZONE_CHANGED/DATE_CHANGED`
já registrados (`:74-82`); ao receber, se havia bloqueio ativo e o wall-clock “pulou” mais que ~90 s desde o
último tick monotônico, **reancorar** (`endMs = nowMs + remainingMono`) e mostrar aviso “relógio alterado durante
bloqueio”. Para o bloqueio diário (que é hora de parede por definição), no mínimo **detectar** a alteração e
exibir um aviso + reportar ao PC (C-01).

### 7.7 Descobrir navegadores e varrer WebView — `FocusBlockRules` + service

```kotlin
// FocusBlockRules.kt
fun discoverBrowserPackages(context: Context): Set<String> {
    val intent = Intent(Intent.ACTION_VIEW, Uri.parse("https://example.com"))
    return context.packageManager
        .queryIntentActivities(intent, PackageManager.MATCH_ALL)
        .map { it.activityInfo.packageName }
        .toSet()
}
```
No `onServiceConnected`, `monitoredPackages = monitoredPackages + discoverBrowserPackages(this)`; e para
WebView: quando `adultActive`, permitir a varredura de texto curto para **qualquer** pacote em foco
(mesmo caminho de `collectVisibleTexts`, com orçamento `MaxScanNodes` e throttle de 700 ms já existentes):

```kotlin
val inspect = FocusBlockRules.isBrowser(pkg) || (adultActive && deepScan)
if (inspect && shouldInspectBrowserEvent(event) &&
    checkBrowserUrl(pkg, rootInActiveWindow, selective, reinforced, adultActive, deepScan)) return
```
Custo: a varredura de 500 nós a cada 700 ms só enquanto há bloqueio adulto ativo; medir impacto.

### 7.8 Detecções de integridade/alerta — `service/IntegrityChecks.kt` (novo)

```kotlin
object IntegrityChecks {
    fun adbEnabled(context: Context): Boolean =
        Settings.Global.getInt(context.contentResolver, Settings.Global.ADB_ENABLED, 0) == 1

    fun developerOptionsEnabled(context: Context): Boolean =
        Settings.Global.getInt(context.contentResolver, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) == 1

    fun userCount(context: Context): Int =
        context.getSystemService(android.os.UserManager::class.java).userCount   // API 24+ (userCount); perfis: getUserProfiles()

    /** Se ADB/dev estiver ligado, NÃO silencie: marque o estado e alerte o PC. */
    fun reportIfRisky(context: Context) {
        if (adbEnabled(context) || developerOptionsEnabled(context) || userCount(context) > 1) {
            FocusNotifier.showRiskyState(context)
            BlockStatusReporter.reportAsync(context, reason = "integrity")
        }
    }
}
```
Obs.: sem Device Owner não é possível desligar ADB; a detecção serve para **tornar visível** e disparar
re-verificação/repair.

### 7.9 Senha nos gatilhos fracos — `ui/DailyBlockCard.kt`, `FocusDashboardScreen.kt`

```kotlin
// helper comum
suspend fun requirePassword(context: Context, action: () -> Unit): Boolean {
    val pm = PasswordManager.getInstance(context)
    if (!pm.isPasswordSet()) { action(); return true }
    // Preferível: componente Compose que abre PasswordConfirmDialog e chama action() no sucesso.
    // NUNCA aplicar a ação antes da verificação.
    return false // UI abre o diálogo; no callback verificado: action()
}
```
- Interruptor **Modo seletivo** (`DailyBlockCard.kt:178-188`): se `enabled==false` e `isPasswordSet`,
  abrir `PasswordConfirmDialog`; só então `store.saveSelective(false)`; se `enabled==true` aplicar direto.
- **Limite diário** (`FocusDashboardScreen.kt:264-267`): subir o limite exige senha (reduzir é livre).
- **Zerar/subtrair dívida** (`:300-305`): exige senha.
- Alternativa mais forte: mover **adulto** para um `enabled` separado e **não** depender do seletivo
  (`FocusBlockRules.shouldBlockWebAddress` → `adultActive = isAdultBlockActive() || adultAlwaysOn`, com
  `adultAlwaysOn` protegido por senha e não alterável por push).

---

## 8. Achados comprovados × hipóteses (para o `android-live` confirmar)

**Comprovado por código (estático):** A-01, A-06 (no-op fora de DO), A-07, C-01, C-03, C-04, D-03, D-04
(partes de build), B-01/B-05/B-06/B-07 (listas e fluxo), B-09 (sem gate de instalador), §5 inteiro.
**Comprovado por leitura ADB:** A-03 (enabled sem bound/crashed e recuperação só externa), A-05
(ADB/dev ligados; `run-as` funciona; APK debuggable), admin ativo sem DO, multi-usuário suportado,
modo seletivo atualmente OFF, DNS sem filtro efetivo.
**Hipótese/alta confiança a confirmar:** A-02 (rótulo na tela de Acessibilidade), A-04 (safe mode + Chrome),
B-02/B-03 (navegador interno do Telegram/Discord/Facebook — provável que sim), B-04 (imagens de busca),
B-08 (PWA standalone), B-10 (PiP), B-12 (exceção de chamada em split-screen), D-01/D-02 (usuário/perfil
nesta ROM; **cuidado**: o teste do usuário 10 no `android-live` coincidiu com crash do `system_server`).

**Pedidos ao `android-live` (em ordem de valor):**
1. Em Ajustes → Acessibilidade, ver se aparece “Foco & Bem-Estar” ou só “Serviço de Foco e Proteção” — decide A-02.
2. Force-stop do app (Ajustes → Forçar parada) e verificar `dumpsys accessibility` por 5 min: `Enabled` vs `Bound`/`Crashed` (A-03).
3. Abrir `xvideos.com` no navegador interno do Telegram (A-02/B-02) e no Chrome (controle).
4. Testar `brasileirinhas.com.br` e `sexlog.com` no Chrome (B-05) na janela adulta ativa e no seletivo.
5. Adiantar 2 h em Data e hora e ver se um bloqueio do PC/diário some (C-01).
6. Confirmar se “Modo seletivo” OFF desbloqueia sites adultos sem janela do PC (A-01).
7. Testar safe mode + Chrome (A-04) — cuidado: safe mode derruba a a11y; restaurar depois.

---

## 9. Anexo — mapa dos arquivos-chave

| Arquivo | Papel |
|---|---|
| `service/FocusAccessibilityService.kt` | detecção de apps/URL, overlay de app bloqueado, guard de Ajustes, `isEnabled()` |
| `service/FocusBlockRules.kt` | listas de apps/navegadores/hosts adultos, regex, matching de URL |
| `schedule/{DailyBlockPolicy,DailyBlockStore}.kt` | regras puras + persistência (diário, pomodoro, adulto, seletivo) |
| `schedule/DailyBlockController.kt` | overlay de tela cheia do diário/pomodoro, exceção de chamada, tick |
| `schedule/DailyBlockScheduler.kt` | alarmes exatos e refresh |
| `schedule/PhraseKeyboardView.kt` | teclado/frase de liberação (anti-paste, não anti-digitação) |
| `push/PomodoroPushService.kt` | entrada FCM (adulto + pomodoro) |
| `admin/AdminPolicyReceiver.kt` | admin; `setUninstallBlocked` só com DO |
| `receiver/BootReceiver.kt` | boot/update → FGS + alarmes |
| `service/ScreenTimeTrackerService.kt` | FGS 24/7, P&B, invalidação de grace |
| `security/PasswordManager.kt` | PBKDF2, lockout, grace |
| `update/*` | auto-update (assinatura/versão), token GitHub |
| `ui/{DailyBlockCard,FocusDashboardScreen,DashboardViewModel}.kt` | interruptores/ações sem senha, estado exibido |
| `AndroidManifest.xml`, `res/xml/*` | permissões, exportação, config de acessibilidade/admin, backup |
| `app/build.gradle.kts` | assinatura (reaproveita debug), minSdk 30, target 35, v1.6 |
