package com.example.focusapp.service

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import java.net.URI
import java.text.Normalizer
import java.util.Locale

internal object FocusBlockRules {
    /** Twitter/X: o único app social bloqueado no modo seletivo (os outros dependem do Nível 2). */
    private val twitterApps = setOf(
        "com.twitter.android",
        "com.twitter.android.lite"
    )

    /** Barra do Firefox/Fenix: ids estáveis e o testTag da toolbar Compose. */
    private val firefoxAddressBars = listOf(
        "org.mozilla.firefox:id/mozac_browser_toolbar_url_view",
        "org.mozilla.firefox_beta:id/mozac_browser_toolbar_url_view",
        "org.mozilla.fenix:id/mozac_browser_toolbar_url_view",
        "ADDRESSBAR_URL_BOX"
    )

    private val blockedApps = twitterApps + setOf(
        "com.instagram.android",
        "com.instagram.lite",
        "com.reddit.frontpage",
        "org.quantumbadger.redreader",
        "ml.docilealligator.infinityforreddit",
        "free.reddit.news",
        "com.rubenmayayo.reddit",
        "com.laurencedawson.reddit_sync",
        "com.andrewshu.android.reddit"
    )

    /**
     * Navegadores conhecidos e o id da barra de endereço. A lista fixa cobre o que já foi visto em uso;
     * a descoberta em runtime (`queryIntentActivities(ACTION_VIEW http/https)`) complementa com qualquer
     * navegador instalado depois (ver [discoverBrowserPackages]) — B-01 do red-team.
     */
    private val browserAddressBars = mapOf(
        "com.android.chrome" to listOf("com.android.chrome:id/url_bar"),
        "com.chrome.beta" to listOf("com.chrome.beta:id/url_bar"),
        "com.chrome.dev" to listOf("com.chrome.dev:id/url_bar"),
        "com.chrome.canary" to listOf("com.chrome.canary:id/url_bar"),
        "org.chromium.chrome" to listOf("org.chromium.chrome:id/url_bar"),
        "com.sec.android.app.sbrowser" to listOf("com.sec.android.app.sbrowser:id/location_bar_edit_text"),
        "com.microsoft.emmx" to listOf("com.microsoft.emmx:id/url_bar"),
        "com.brave.browser" to listOf("com.brave.browser:id/url_bar"),
        // Firefox: a barra muda de id entre versões (Compose expõe o testTag sem pacote).
        "org.mozilla.firefox" to firefoxAddressBars,
        "org.mozilla.firefox_beta" to firefoxAddressBars,
        "org.mozilla.fenix" to firefoxAddressBars,
        "org.mozilla.focus" to listOf("org.mozilla.focus:id/url_bar", "ADDRESSBAR_URL_BOX"),
        "com.opera.browser" to listOf("com.opera.browser:id/url_field"),
        "com.opera.mini.native" to listOf("com.opera.mini.native:id/url_field"),
        "com.opera.gx" to listOf("com.opera.gx:id/url_field"),
        "com.vivaldi.browser" to listOf("com.vivaldi.browser:id/url_bar"),
        "com.duckduckgo.mobile.android" to listOf("com.duckduckgo.mobile.android:id/search_bar_text"),
        "com.ecosia.android" to listOf("com.ecosia.android:id/url_bar"),
        "com.kiwibrowser.browser" to listOf("com.kiwibrowser.browser:id/url_bar"),
        "com.yandex.browser" to listOf("com.yandex.browser:id/url_bar"),
        "com.UCMobile.intl" to listOf("com.UCMobile.intl:id/url_bar"),
        "org.bromite.bromite" to listOf("org.bromite.bromite:id/url_bar")
    )

    private val settingsPackages = setOf(
        "com.android.settings",
        "com.samsung.android.settings",
        "com.google.android.packageinstaller",
        "com.samsung.android.packageinstaller",
        "com.android.packageinstaller"
    )

    private val twitterHosts = setOf("twitter.com", "x.com")
    private val blockedHosts = twitterHosts + setOf("reddit.com", "instagram.com")
    val monitoredPackages: Set<String> = blockedApps + browserAddressBars.keys + settingsPackages

    /**
     * Navegadores descobertos em runtime (B-01). Vazio até o serviço de acessibilidade perguntar ao
     * PackageManager; é protegido por `@Volatile` porque o loop do watchdog também lê.
     */
    @Volatile
    private var discoveredBrowsers: Set<String> = emptySet()

    fun updateDiscoveredBrowsers(packages: Set<String>) {
        discoveredBrowsers = packages.asSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .toSet()
    }

    fun discoveredBrowserPackages(): Set<String> = discoveredBrowsers

    /** Pacotes que o serviço de acessibilidade precisa observar (lista fixa + navegadores descobertos). */
    fun runtimeMonitoredPackages(): Set<String> = monitoredPackages + discoveredBrowsers

    fun isBlockedApp(packageName: String): Boolean = packageName in blockedApps

    fun isTwitterApp(packageName: String): Boolean = packageName in twitterApps

    fun isBrowser(packageName: String): Boolean =
        packageName in browserAddressBars || packageName in discoveredBrowsers

    fun isSettingsPackage(packageName: String): Boolean = packageName in settingsPackages

    fun addressBarViewIds(packageName: String): List<String> = browserAddressBars[packageName].orEmpty()

    /**
     * Descobre em runtime quem trata `ACTION_VIEW` de http/https — pega navegador novo instalado pela
     * Play Store sem precisar de lista fechada. Roda em thread de background (consulta ao PackageManager).
     */
    fun discoverBrowserPackages(context: Context): Set<String> = runCatching {
        val packageManager = context.packageManager
        val found = LinkedHashSet<String>()
        for (scheme in listOf("http", "https")) {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse("$scheme://example.com"))
            packageManager.queryIntentActivities(intent, PackageManager.MATCH_ALL).forEach { info ->
                val name = info.activityInfo?.packageName
                if (!name.isNullOrBlank() && name != context.packageName) found.add(name)
            }
        }
        found
    }.getOrDefault(emptySet())

    /**
     * Modo seletivo (preferência do aparelho): só Twitter/X e conteúdo adulto, sempre — Instagram e Reddit
     * ficam liberados e o Pomodoro Angry do PC não trava a tela. Sem ele, vale o comportamento antigo por nível.
     */
    fun shouldBlockApp(packageName: String, selective: Boolean, reinforced: Boolean): Boolean =
        if (selective) isTwitterApp(packageName) else reinforced && isBlockedApp(packageName)

    /** No seletivo o navegador é inspecionado sempre; no modo antigo só no Nível 2+ ou com janela adulta ativa. */
    fun shouldInspectBrowser(selective: Boolean, reinforced: Boolean, adultWindowActive: Boolean): Boolean =
        selective || reinforced || adultWindowActive

    /**
     * Com o bloqueio adulto (ou o seletivo) valendo, o serviço precisa receber eventos de TODOS os apps
     * para pegar WebView/custom tabs de apps fora da lista (B-02). Fora disso, basta a lista monitorada.
     */
    fun shouldScanAllPackages(selective: Boolean, adultWindowActive: Boolean): Boolean =
        selective || adultWindowActive

    /** Endereço bloqueado na barra: Twitter/X + adulto no seletivo; sites + adulto no modo antigo. */
    fun shouldBlockWebAddress(
        address: String,
        selective: Boolean,
        reinforced: Boolean,
        adultWindowActive: Boolean
    ): Boolean = if (selective) {
        isTwitterWebAddress(address) || isAdultWebAddress(address)
    } else {
        (reinforced && isBlockedWebAddress(address)) || (adultWindowActive && isAdultWebAddress(address))
    }

    /**
     * Rede de segurança para textos visíveis no navegador (barra, título da aba, título da página, links).
     * Cobre navegador cuja barra de endereço muda de id entre versões e conteúdo adulto cujo endereço não
     * cita o termo. Textos longos são ignorados para não varrer o corpo inteiro da página.
     */
    fun shouldBlockBrowserText(
        text: String,
        selective: Boolean,
        reinforced: Boolean,
        adultWindowActive: Boolean
    ): Boolean {
        val value = text.trim()
        if (value.isEmpty() || value.length > MAX_BROWSER_TEXT) return false
        if ((selective || adultWindowActive) && isAdultWebAddress(value)) return true
        if (!selective && !reinforced) return false
        val host = hostOf(value) ?: return false
        val hosts = if (selective) twitterHosts else blockedHosts
        return hosts.any { host == it || host.endsWith(".$it") }
    }

    /**
     * Varredura conservadora em app que NÃO é navegador (WebView/custom tab de Telegram, Discord,
     * Facebook etc.): só bloqueia quando aparece uma URL cujo **host** está na lista adulta. Palavra
     * solta ("porn", "sexo") não conta — evita bloquear conversa/tela legítima (B-02).
     */
    fun containsAdultDomain(text: String): Boolean = firstAdultDomainIn(text) != null

    fun containsAnyAdultDomain(texts: Collection<String>): Boolean = texts.any(::containsAdultDomain)

    /** Host adulto encontrado no texto, para o aviso de bloqueio; null se não houver. */
    fun firstAdultDomainIn(text: String): String? {
        if (text.isEmpty() || text.length > MAX_GENERIC_TEXT) return null
        for (token in URL_TOKEN_SEPARATORS.split(text)) {
            val candidate = token.trim().trim('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '>', '<')
            if (candidate.isEmpty() || !candidate.contains('.')) continue
            val host = hostOf(candidate) ?: continue
            if (isAdultHost(host)) return host
        }
        return null
    }

    /**
     * Apps onde a varredura genérica de URL adulta faz sentido. Exclui o próprio app (UI legítima),
     * o sistema/Settings/instalador (tratados por outro caminho) e apps de chamada (segurança: nunca
     * bloquear a tela de telefone em andamento).
     */
    fun shouldScanAnyAppPackage(packageName: String, ownPackage: String): Boolean {
        if (packageName.isBlank() || packageName == ownPackage) return false
        if (packageName in genericScanExcludedPackages) return false
        if (packageName.contains("telecom") || packageName.contains("dialer") ||
            packageName.contains("incallui") || packageName.contains("phone")) return false
        return true
    }

    fun isBlockedWebAddress(address: String): Boolean {
        val host = hostOf(address) ?: return false
        return blockedHosts.any { host == it || host.endsWith(".$it") }
    }

    fun isTwitterWebAddress(address: String): Boolean {
        val host = hostOf(address) ?: return false
        return twitterHosts.any { host == it || host.endsWith(".$it") }
    }

    /** Domínios de conteúdo adulto. Subdomínios entram (m.xvideos.com). */
    private val adultHosts = setOf(
        "xvideos.com", "xvideos2.com", "xvideos.com.br", "xvideos.es", "xnxx.com", "xhamster.com", "xhamster.desi",
        "pornhub.com", "redtube.com", "youporn.com", "tube8.com", "spankbang.com", "eporner.com", "chaturbate.com",
        "stripchat.com", "bongacams.com", "camsoda.com", "livejasmin.com", "livejasmine.com", "brazzers.com",
        "onlyfans.com", "fansly.com", "justfor.fans", "rule34.xxx", "nhentai.net", "nhentai.to", "e-hentai.org",
        "hentaifox.com", "hanime.tv", "redgifs.com", "erome.com", "motherless.com", "beeg.com", "porn.com",
        "sex.com", "txxx.com", "pornone.com", "hqporner.com", "porn300.com", "thumbzilla.com", "drtuber.com",
        "porntrex.com", "tnaflix.com", "empflix.com", "adultfriendfinder.com", "jerkmate.com", "cam4.com",
        "myfreecams.com", "xlovecam.com", "flirt4free.com", "nudevista.com", "imagefap.com", "8muses.com",
        "pornobr.com", "caiunanet.com", "brasileirinhas.com.br", "sexlog.com", "missav.com", "javdb.com",
        "fatalmodel.com", "coelhinhas.tv", "porntube.com", "redwap.me", "pornodoido.com", "putaria.com.br"
    )

    /** Palavras-chave em qualquer parte do endereço/texto (ex.: google.com/search?q=porn). "xxx" só como termo inteiro. */
    private val adultKeywords = Regex(
        "porn|hentai|nsfw|putaria|brasileirinhas|sexlog|missav|javdb|fatalmodel|coelhinhas|\\bxxx\\b|" +
            "sexo\\s+expl[ií]cito|\\bmilf\\b|\\bcamgirl\\b|\\bcamsex\\b|\\bsexcam\\b|onlyfans|rule34|" +
            "\\bfap\\b|\\bnudes\\b|\\bsextape\\b|brazzers|xvideos"
    )

    /** Conteúdo adulto: domínio da lista ou palavra-chave no texto do endereço. */
    fun isAdultWebAddress(address: String): Boolean {
        val text = address.trim()
        if (text.isEmpty()) return false
        val normalized = text.lowercase(Locale.ROOT).replace('+', ' ').replace("%20", " ")
        if (adultKeywords.containsMatchIn(normalized)) return true
        val host = hostOf(text) ?: return false
        return isAdultHost(host)
    }

    private fun isAdultHost(host: String): Boolean =
        adultHosts.any { domain -> host == domain || host.endsWith(".$domain") }

    private fun hostOf(address: String): String? {
        val value = address.trim().substringBefore(' ')
        if (value.isEmpty()) return null
        if (!value.contains('.')) return null

        val uri = runCatching {
            URI(if (value.contains("://")) value else "https://$value")
        }.getOrNull() ?: return null
        return uri.host?.lowercase(Locale.ROOT)?.trimEnd('.')
    }

    /**
     * Anti-tamper das telas que desligam a proteção (A-02). Casa por três caminhos, sempre conservador:
     * 1. className de atividade do Settings (AccessibilitySettings/DeviceAdminSettings);
     * 2. os rótulos do próprio bloqueador — `app_name`, `accessibility_service_name` ("Serviço de Foco
     *    e Proteção") e o packageName — em qualquer tela de Settings;
     * 3. títulos conhecidos de Acessibilidade/Administrador do dispositivo (texto curto, sem acento).
     * "Detalhes do app" só entra quando o rótulo/package do app aparece; não bloqueia a ficha de apps
     * alheios. Quem chama deve respeitar a janela de tolerância por senha.
     */
    fun isProtectionTamperScreen(
        settingsPackage: String,
        windowClass: String?,
        texts: Collection<String>,
        ownLabels: Collection<String>,
        ownPackage: String
    ): Boolean {
        if (!isSettingsPackage(settingsPackage)) return false
        // A classe da janela basta para reconhecer as telas críticas, mesmo quando a árvore de texto
        // ainda não chegou no evento de acessibilidade.
        val window = windowClass.orEmpty()
        if (window.contains("Accessibility", ignoreCase = true)) return true
        if (window.contains("DeviceAdmin", ignoreCase = true)) return true
        // Tela de "Acesso aos dados de uso" NÃO é tamper: ela lista o nome do app e o anti-tamper
        // genérico expulsava o usuário da tela (bug reportado em 2026-11-11).
        if (window.contains("UsageAccess", ignoreCase = true)) return false
        // Outras telas legítimas de permissão que também mostram o nome do app: não são tamper.
        listOf("BatteryOptimization", "HighPowerApplications", "NotificationAppList", "AppBatteryUsage")
            .firstOrNull { window.contains(it, ignoreCase = true) }?.let { return false }
        val shortTexts = texts.asSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() && it.length <= MAX_TAMPER_TEXT }
            .map(::normalizeForMatch)
            .toList()
        if (shortTexts.isEmpty()) return false

        val labels = (ownLabels + ownPackage).asSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .map(::normalizeForMatch)
            .toList()

        // A-02: telas cujo className é genérico (ex.: "SubSettings") ainda precisam casar o rótulo do app/serviço.
        // Telas legítimas de PERMISSÃO usam className genérica (SubSettings) e mostram o nome do app:
        // allowlist pelo título antes do match por rótulo (bug do "Acesso aos dados de uso", 2026-11-11).
        if (shortTexts.any { text -> permissionScreenTitleHints.any { hint -> text == hint || text.startsWith("$hint ") } }) return false

        // A-02: só o rótulo do SERVIÇO ("Serviço de Foco e Proteção") é sinal forte de tela de
        // acessibilidade. O nome do app aparece em várias telas legítimas de permissão (Acesso aos
        // dados de uso, Otimização da bateria, ...) e por isso NÃO pode ser gatilho sozinho.
        val serviceLabels = labels.filter { label ->
            label.contains("servico") || label.contains("service") ||
                label.contains("protecao") || label.contains("protection")
        }
        if (serviceLabels.any { label -> shortTexts.any { it.contains(label) } }) return true
        if (shortTexts.any { text -> accessibilityTitleHints.any { text == it || text.startsWith("$it ") } }) return true
        if (shortTexts.any { text -> deviceAdminTitleHints.any { text == it || text.startsWith("$it ") } }) return true

        // "Detalhes do app" do próprio bloqueador. O rótulo do app já casa acima; as ações destrutivas
        // ("Forçar parada", "Limpar dados", "Desativar") só contam na ficha do nosso app — nunca na de
        // um app alheio, para não impedir o gerenciamento legítimo dos outros apps.
        if (isAppInfoWindow(window)) {
            val ownAppShown = labels.any { label -> shortTexts.any { it.contains(label) } }
            val destructiveShown = shortTexts.any { text ->
                destructiveActionHints.any { hint -> text == hint || text.startsWith("$hint ") }
            }
            return ownAppShown && destructiveShown
        }
        return false
    }

    /** Títulos de telas que aparecem nas configurações de acessibilidade. */
    private val accessibilityTitleHints = listOf(
        "servicos instalados", "installed services",
        "correcao de cor", "color correction",
        "atalhos de acessibilidade", "accessibility shortcuts",
        "aplicativos instalados", "downloaded apps"
    )

    /** Títulos de telas do administrador do dispositivo. */
    private val deviceAdminTitleHints = listOf(
        "administrador do dispositivo", "administradores do dispositivo",
        "administracao do dispositivo", "device administrators", "device admin apps",
        "apps de administracao do dispositivo", "administradores"
    )

    /** Títulos de telas legítimas de permissão (nunca são tamper; mostram o nome do app). */
    private val permissionScreenTitleHints = listOf(
        "acesso aos dados de uso", "usage access", "uso de dados", "data usage",
        "acesso especial", "special access", "otimizacao da bateria", "battery optimization",
        "notificacoes do app", "app notifications"
    )

    /** Ações destrutivas exibidas na ficha do app (usadas só junto de app-info do nosso pacote). */
    private val destructiveActionHints = listOf(
        "forcar parada", "force stop", "desinstalar", "uninstall",
        "limpar dados", "clear data", "limpar armazenamento", "clear storage", "desativar", "disable"
    )

    private fun isAppInfoWindow(windowClass: String): Boolean {
        val cls = windowClass.lowercase(Locale.ROOT)
        return cls.contains("installedappdetails") || cls.contains("appinfo") ||
            cls.contains("applicationdetails") || cls.contains("applicationinfo")
    }

    /** Normaliza para comparação: minúsculas, sem acentos, espaços colapsados. */
    private fun normalizeForMatch(value: String): String {
        val decomposed = Normalizer.normalize(value.lowercase(Locale.ROOT), Normalizer.Form.NFD)
        return decomposed.replace(Regex("\\p{M}+"), "")
            .replace(Regex("\\s+"), " ")
            .trim()
    }

    /** Acima disso o texto é corpo de página, não barra/título/legenda: não vale varrer. */
    private const val MAX_BROWSER_TEXT = 500

    /** Mesmo limite para a varredura genérica: texto de corpo não deve casar URL solta. */
    private const val MAX_GENERIC_TEXT = 2_000

    /** Título/label de tela é curto; texto longo não identifica a página. */
    private const val MAX_TAMPER_TEXT = 80

    private val URL_TOKEN_SEPARATORS = Regex("[\\s<>\\[\\]{}()\"'`|]+")

    private val genericScanExcludedPackages = setOf(
        "android",
        "com.android.systemui",
        "com.android.settings",
        "com.android.packageinstaller",
        "com.google.android.packageinstaller",
        "com.samsung.android.packageinstaller",
        "com.android.permissioncontroller",
        "com.google.android.permissioncontroller"
    )
}
