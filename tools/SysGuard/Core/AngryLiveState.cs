using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SysGuard;

/// <summary>
/// Estado vivo do Pomodoro Angry publicado pelo Dynamic Island em
/// <c>%LocalAppData%\DynamicIsland\angry-live.json</c> (um arquivo por perfil de usuário). O serviço
/// lê esse arquivo para reforçar o bloqueio de distrações no hosts durante a sessão; a ilha atualiza
/// o arquivo a cada mudança de estado e a cada poucos segundos.
/// <see cref="EndUnixMs"/> é o fim estimado da sessão inteira (fases restantes do plano incluídas).
/// <para>
/// Regra do bloqueio: durante o foco tudo fica bloqueado; durante o intervalo (a ilha marca
/// <c>locked=false</c>) o bloqueio continua, exceto para Twitter/X e Instagram nos 60 s finais de um
/// intervalo que esteja rodando. A liberação é sempre recalculada a partir de
/// <see cref="TimerPhase"/>/<see cref="TimerRemainingMs"/>/<see cref="TimerRunning"/> e de
/// <see cref="SavedAtUnixMs"/>: se a ilha morrer, o arquivo envelhece e a liberação fecha sozinha
/// quando a janela de 60 s passa — nunca fica aberta indefinidamente.
/// </para>
/// </summary>
internal sealed record AngryLiveState(
    bool Active,
    bool Locked,
    long? EndUnixMs,
    long? SavedAtUnixMs = null,
    string? TimerPhase = null,
    long? TimerRemainingMs = null,
    bool TimerRunning = false)
{
    public static readonly AngryLiveState Inactive = new(false, false, null);

    /// <summary>Janela final de um intervalo em que Twitter/X e Instagram ficam liberados.</summary>
    public static readonly TimeSpan BreakReleaseWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// True enquanto há uma sessão Angry para guardar agora: ativa e com o fim estimado ainda no
    /// futuro. Vale tanto no foco quanto no intervalo; o arquivo envelhecer não desliga a guarda
    /// sozinho — matar a ilha não pode desligar o bloqueio de rede durante a sessão.
    /// </summary>
    public bool IsActiveNow(DateTimeOffset now) =>
        Active && EndUnixMs is { } end && end > now.ToUnixTimeMilliseconds();

    /// <summary>
    /// True durante a janela de liberação: intervalo rodando (<c>phase == "Break"</c> e
    /// <c>running == true</c>), sessão ativa, não travada, e faltando entre 1 ms e 60 s para o
    /// intervalo terminar. O tempo restante é recalculado a partir do último salvamento, então um
    /// arquivo parado (ilha morta) fecha a janela quando o minuto passa.
    /// </summary>
    public bool IsBreakRelease(DateTimeOffset now)
    {
        if (!IsActiveNow(now) || Locked) return false;
        if (!TimerRunning || !IsBreakPhase) return false;
        if (SavedAtUnixMs is not { } savedAt || TimerRemainingMs is not { } remaining) return false;

        long remainingNow = remaining - (now.ToUnixTimeMilliseconds() - savedAt);
        return remainingNow > 0 && remainingNow <= (long)BreakReleaseWindow.TotalMilliseconds;
    }

    /// <summary>True enquanto a guarda do Angry deve rodar: a sessão inteira (foco e intervalo).</summary>
    public bool IsGuarding(DateTimeOffset now) => IsActiveNow(now);

    /// <summary>
    /// True quando o bloqueio extra completo deve estar aplicado agora: sessão ativa e fora da janela
    /// de liberação do intervalo. É o que a guarda de títulos usa para minimizar todas as distrações.
    /// </summary>
    public bool IsBlocking(DateTimeOffset now) => IsGuarding(now) && !IsBreakRelease(now);

    private bool IsBreakPhase => string.Equals(TimerPhase, "Break", StringComparison.OrdinalIgnoreCase);

    /// <summary>Idade do último heartbeat da ilha (ela grava a cada ≤5 s enquanto ativa); null se não veio.</summary>
    public TimeSpan? Age(DateTimeOffset now) =>
        SavedAtUnixMs is { } saved ? TimeSpan.FromMilliseconds(now.ToUnixTimeMilliseconds() - saved) : null;
}

/// <summary>
/// Leitura tolerante do arquivo do Angry: nomes de campo em camelCase ou PascalCase, booleanos como
/// bool/número/string, fim em epoch ms (<c>endMs</c>) ou ISO (<c>endUtc</c>), no topo ou dentro de
/// <c>timer</c>. Do <c>timer</c> também lê <c>phase</c>, <c>remainingMs</c> e <c>running</c>, que
/// alimentam a janela de liberação do intervalo. Qualquer coisa inesperada resulta em
/// <see cref="AngryLiveState.Inactive"/>: o bloqueio extra é reforço, então um arquivo corrompido
/// nunca pode manter o hosts preso.
/// </summary>
internal static class AngryLiveStateParser
{
    /// <summary>Tamanho máximo aceito para o arquivo; o formato real tem poucas centenas de bytes.</summary>
    public const int MaxFileBytes = 64 * 1024;

    private static readonly string[] EndMillisecondNames =
        ["endMs", "endUnixMs", "endEpochMs", "endsAtMs", "endAtMs", "sessionEndMs"];

    private static readonly string[] EndIsoNames = ["endUtc", "endsAtUtc", "endAtUtc", "endIso"];

    private static readonly string[] SavedAtMillisecondNames = ["savedAtMs", "savedMs", "heartbeatMs"];

    private static readonly string[] SavedAtIsoNames = ["savedAtUtc", "savedAt", "heartbeatUtc"];

    public static AngryLiveState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json!.Length > MaxFileBytes) return AngryLiveState.Inactive;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return AngryLiveState.Inactive;

            bool active = GetBoolean(root, "active", "isActive", "angryActive") ?? false;
            bool locked = GetBoolean(root, "locked", "isLocked", "angryLocked") ?? false;
            JsonElement? timer = GetObject(root, "timer", "state", "pomodoro", "session");

            long? end = GetUnixMilliseconds(root, EndMillisecondNames)
                        ?? GetUnixMilliseconds(timer, EndMillisecondNames)
                        ?? GetIsoMilliseconds(root, EndIsoNames)
                        ?? GetIsoMilliseconds(timer, EndIsoNames);

            long? savedAt = GetUnixMilliseconds(root, SavedAtMillisecondNames)
                            ?? GetUnixMilliseconds(timer, SavedAtMillisecondNames)
                            ?? GetIsoMilliseconds(root, SavedAtIsoNames)
                            ?? GetIsoMilliseconds(timer, SavedAtIsoNames);

            long? remaining = GetDurationMilliseconds(root, "remainingMs", "remaining")
                              ?? GetDurationMilliseconds(timer, "remainingMs", "remaining");

            // Fallback só para formatos futuros sem fim absoluto: captura + tempo restante informado.
            if (end is null && savedAt is { } saved && remaining is { } left) end = saved + left;

            string? phase = GetString(timer, "phase", "timerPhase") ?? GetString(root, "phase", "timerPhase");
            bool running = GetBoolean(timer, "running", "isRunning")
                           ?? GetBoolean(root, "running", "isRunning")
                           ?? false;

            return new AngryLiveState(active, locked, end, savedAt, phase, remaining, running);
        }
        catch (JsonException)
        {
            return AngryLiveState.Inactive;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return AngryLiveState.Inactive;
        }
    }

    /// <summary>Lê a flag booleana aceitando bool, número (0/1) ou texto ("true"/"1").</summary>
    private static bool? GetBoolean(JsonElement? element, params string[] names)
    {
        if (element is not { } container || !TryFind(container, names, out JsonElement value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetDouble(out double number) ? number != 0 : null,
            JsonValueKind.String => ParseBooleanText(value.GetString()),
            _ => null,
        };
    }

    private static bool? ParseBooleanText(string? text)
    {
        if (text is null) return null;
        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => null,
        };
    }

    /// <summary>Lê um texto (fase do timer, ex.: "Focus"/"Break"); vazio vira null.</summary>
    private static string? GetString(JsonElement? element, params string[] names)
    {
        if (element is not { } container || !TryFind(container, names, out JsonElement value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Lê um instante epoch em milissegundos, aceitando número ou texto numérico.</summary>
    private static long? GetUnixMilliseconds(JsonElement? element, params string[] names)
    {
        if (ReadNumber(element, names) is not { } number || number <= 0) return null;

        // Tolerância para segundos epoch (formato antigo/errado): abaixo de ~1973 em ms é segundos.
        return number < 100_000_000_000L ? number * 1000L : number;
    }

    /// <summary>Lê uma duração em milissegundos (não normaliza "segundos": durações são pequenas).</summary>
    private static long? GetDurationMilliseconds(JsonElement? element, params string[] names)
    {
        if (ReadNumber(element, names) is not { } number || number <= 0) return null;
        return number;
    }

    private static long? ReadNumber(JsonElement? element, params string[] names)
    {
        if (element is not { } container || !TryFind(container, names, out JsonElement value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long whole)
                ? whole
                : value.TryGetDouble(out double fraction) ? (long)Math.Round(fraction) : null,
            JsonValueKind.String => long.TryParse(
                value.GetString()?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed) ? parsed : null,
            _ => null,
        };
    }

    /// <summary>Lê um instante ISO-8601 (o formato que a ilha grava em <c>endUtc</c>/<c>savedAtUtc</c>).</summary>
    private static long? GetIsoMilliseconds(JsonElement? element, params string[] names)
    {
        if (element is not { } container || !TryFind(container, names, out JsonElement value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUnixTimeMilliseconds();
    }

    private static JsonElement? GetObject(JsonElement element, params string[] names) =>
        TryFind(element, names, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : null;

    /// <summary>Procura uma propriedade pelo nome (exato primeiro, depois sem diferenciar maiúsculas).</summary>
    private static bool TryFind(JsonElement element, string[] names, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var name in names)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
                value = property.Value;
                return true;
            }
        }

        foreach (var name in names)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Caminhos do estado vivo do Angry nos perfis de usuário desta máquina. O serviço roda como SYSTEM,
/// fora da sessão do dono da ilha, então precisa procurar o arquivo em todos os perfis — não só no
/// perfil do serviço.
/// </summary>
internal static class AngryLivePaths
{
    public const string AppFolderName = "DynamicIsland";
    public const string FileName = "angry-live.json";

    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    private static readonly object CacheGate = new();
    private static IReadOnlyList<string>? _cachedFiles;
    private static DateTimeOffset _cachedUntil;

    /// <summary>Arquivo do usuário atual (é o que a bandeja usa; no serviço SYSTEM não tem estado útil).</summary>
    public static string ForCurrentUser() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, FileName);

    /// <summary>Arquivo de um diretório de perfil de usuário.</summary>
    public static string ForUserProfile(string profileDirectory) =>
        Path.Combine(profileDirectory, "AppData", "Local", AppFolderName, FileName);

    /// <summary>
    /// Todos os arquivos candidatos desta máquina, um por perfil de usuário. A lista é cacheada por um
    /// minuto: perfis não nascem a cada ciclo do watchdog, e a leitura do arquivo continua limitada a 3 s.
    /// Nunca lança.
    /// </summary>
    public static IReadOnlyList<string> EnumerateUserFiles()
    {
        lock (CacheGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cachedFiles is not null && now < _cachedUntil) return _cachedFiles;
            _cachedFiles = EnumerateUserFilesCore();
            _cachedUntil = now + TimeSpan.FromMinutes(1);
            return _cachedFiles;
        }
    }

    private static IReadOnlyList<string> EnumerateUserFilesCore()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        foreach (var profile in EnumerateProfileDirectories())
        {
            var file = ForUserProfile(profile);
            if (seen.Add(file)) files.Add(file);
        }

        return files;
    }

    /// <summary>Perfis de usuário desta máquina (registro ProfileList, pasta de perfis e perfil atual).</summary>
    internal static IReadOnlyList<string> EnumerateProfileDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();

        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim()));
                if (seen.Add(full)) directories.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Caminho inválido vindo do registro: ignora.
            }
        }

        foreach (var profile in ReadProfileListPaths()) Add(profile);
        foreach (var profile in EnumerateDirectoryChildren(ReadProfilesDirectory())) Add(profile);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return directories;
    }

    private static IReadOnlyList<string> ReadProfileListPaths()
    {
        var paths = new List<string>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            if (root is null) return paths;

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var subKey = root.OpenSubKey(subKeyName);
                if (subKey?.GetValue("ProfileImagePath") is string path && !string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("Angry: falha ao ler a lista de perfis do registro.", ex);
        }

        return paths;
    }

    private static string? ReadProfilesDirectory()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            return key?.GetValue("ProfilesDirectory") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("Angry: falha ao ler a pasta de perfis do registro.", ex);
            return null;
        }
    }

    private static IReadOnlyList<string> EnumerateDirectoryChildren(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return Array.Empty<string>();
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(root);
            return Directory.Exists(expanded) ? Directory.GetDirectories(expanded) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>Lê o primeiro estado que ainda guarda a sessão entre os arquivos candidatos dos perfis.</summary>
internal static class AngryLiveStateReader
{
    /// <summary>
    /// Devolve o primeiro estado que pede guarda agora; <see cref="AngryLiveState.Inactive"/> quando
    /// nenhum arquivo existe, está legível ou pede guarda. Arquivo ilegível nunca bloqueia.
    /// A seleção usa <see cref="AngryLiveState.IsGuarding"/> (e não <c>IsBlocking</c>): durante a
    /// janela de liberação do intervalo o hosts continua composto com YouTube/Reddit, só sem as
    /// linhas de Twitter/X e Instagram.
    /// </summary>
    public static AngryLiveState Read(IReadOnlyList<string> files, DateTimeOffset now)
    {
        foreach (var file in files)
        {
            var state = ReadFile(file);
            if (state is not null && state.IsGuarding(now)) return state;
        }

        return AngryLiveState.Inactive;
    }

    private static AngryLiveState? ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > AngryLiveStateParser.MaxFileBytes) return null;

            // A ilha grava de forma atômica; FileShare.ReadWrite evita falhar se pegarmos a troca no meio.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return AngryLiveStateParser.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

/// <summary>
/// Cache do estado do Angry com janela de leitura de 3 segundos: o watchdog do serviço roda a cada
/// 3 s e nunca relê o JSON mais de uma vez por janela, nem quando as camadas são reaplicadas no mesmo
/// ciclo. A ilha grava no máximo a cada 5 s, então 3 s já é bem mais rápido que a fonte.
/// </summary>
internal sealed class AngryLiveCache
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly Func<IReadOnlyList<string>> _files;
    private readonly Func<IReadOnlyList<string>, DateTimeOffset, AngryLiveState> _reader;
    private readonly object _gate = new();
    private AngryLiveState _state = AngryLiveState.Inactive;
    private DateTimeOffset _validUntil = DateTimeOffset.MinValue;
    private int _reads;

    public AngryLiveCache(
        Func<IReadOnlyList<string>>? files = null,
        Func<IReadOnlyList<string>, DateTimeOffset, AngryLiveState>? reader = null)
    {
        _files = files ?? AngryLivePaths.EnumerateUserFiles;
        _reader = reader ?? AngryLiveStateReader.Read;
    }

    /// <summary>Estado atual; relê os arquivos no máximo a cada <see cref="RefreshInterval"/>.</summary>
    public AngryLiveState Read(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_reads > 0 && now < _validUntil) return _state;
            _reads++;
            _state = SafeRead(now);
            _validUntil = now + RefreshInterval;
            return _state;
        }
    }

    /// <summary>Quantas leituras de arquivo aconteceram (diagnóstico e testes).</summary>
    public int ReadCount
    {
        get { lock (_gate) return _reads; }
    }

    /// <summary>Usuário atual (bandeja): só o perfil de quem está logado.</summary>
    public static AngryLiveCache ForCurrentUser { get; } = new(() => [AngryLivePaths.ForCurrentUser()]);

    /// <summary>Serviço SYSTEM: procura o estado em todos os perfis de usuário.</summary>
    public static AngryLiveCache AllUsers { get; } = new();

    private AngryLiveState SafeRead(DateTimeOffset now)
    {
        try
        {
            return _reader(_files(), now);
        }
        catch (Exception ex)
        {
            Log.Error("Angry: falha ao ler o estado vivo.", ex);
            return AngryLiveState.Inactive;
        }
    }
}
