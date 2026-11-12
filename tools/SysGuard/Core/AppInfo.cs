using System.Reflection;

namespace SysGuard;

/// <summary>Nomes, caminhos e nomes de registro usados pelo app inteiro.</summary>
internal static class AppInfo
{
    public const string AppName = "SysGuard";

    /// <summary>Nome do serviço do Windows (sem espaços, como o SCM exige).</summary>
    public const string ServiceName = "SysGuardSvc";

    /// <summary>
    /// Serviço gêmeo pequeno: reinicia o <see cref="ServiceName"/> quando ele para (proteção em
    /// profundidade contra <c>sc stop</c> de admin). Os dois serviços se vigiam.
    /// </summary>
    public const string WatchdogServiceName = "SysGuardWatchdogSvc";

    /// <summary>Nome exibido em services.msc. Deliberadamente neutro.</summary>
    public const string ServiceDisplayName = "System Guard";

    /// <summary>Nome exibido do serviço gêmeo. Neutro: não menciona conteúdo bloqueado.</summary>
    public const string WatchdogServiceDisplayName = "System Guard Watchdog";

    /// <summary>Descrição exibida em services.msc. Neutra: não menciona conteúdo bloqueado.</summary>
    public const string ServiceDescription = "Mantém as configurações de proteção do sistema aplicadas.";

    /// <summary>Descrição neutra do serviço gêmeo.</summary>
    public const string WatchdogServiceDescription = "Mantém o serviço de proteção do sistema em execução.";

    /// <summary>Nome do pipe local usado pela bandeja para falar com o serviço.</summary>
    public const string PipeName = "SysGuard.Control";

    /// <summary>Chave Run (HKLM) que inicia a bandeja no logon.</summary>
    public const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public const string RunValueName = "SysGuard";

    /// <summary>Chave que o Gerenciador de Serviços usa para o serviço.</summary>
    public const string ServiceRegistryPath = $@"SYSTEM\CurrentControlSet\Services\{ServiceName}";

    /// <summary>Chave do serviço gêmeo.</summary>
    public const string WatchdogServiceRegistryPath = $@"SYSTEM\CurrentControlSet\Services\{WatchdogServiceName}";

    /// <summary>Caminho da chave de um serviço do SCM pelo nome.</summary>
    public static string ServiceRegistryPathFor(string serviceName) =>
        $@"SYSTEM\CurrentControlSet\Services\{serviceName}";

    /// <summary>Caminho do executável instalado (o exe se copia para cá na instalação).</summary>
    public static string InstalledExePath => Path.Combine(ProgramDataDirectory, AppName + ".exe");

    /// <summary>Pasta de dados, acessível somente a SYSTEM e Administradores.</summary>
    public static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);

    public static string ConfigPath => Path.Combine(StateDirectory, "config.dat");

    public static string StateDirectory => Path.Combine(ProgramDataDirectory, "state");

    public static string HostsBackupPath => Path.Combine(StateDirectory, "hosts.original");

    public static string DnsStatePath => Path.Combine(StateDirectory, "dns.dat");

    public static string PoliciesStatePath => Path.Combine(StateDirectory, "policies.dat");

    public static string InstallStatePath => Path.Combine(StateDirectory, "install.dat");

    public static string LogsDirectory => Path.Combine(ProgramDataDirectory, "logs");

    public static string ServiceLogPath => Path.Combine(LogsDirectory, "service.log");

    /// <summary>Log do serviço gêmeo, separado do principal para os dois processos não brigarem pelo arquivo.</summary>
    public static string WatchdogLogPath => Path.Combine(LogsDirectory, "watchdog.log");

    public static string PendingDeletePath => Path.Combine(ProgramDataDirectory, "pending-delete.dat");

    /// <summary>
    /// Marcador de instalação/reparo/remoção em andamento: os serviços se vigiam, então precisam saber
    /// que uma parada é intencional e não devem religar um ao outro no meio do procedimento.
    /// </summary>
    public static string InstallLockPath => Path.Combine(ProgramDataDirectory, "install.lock");

    /// <summary>Arquivo hosts do Windows.</summary>
    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static string ScExePath => Path.Combine(Environment.SystemDirectory, "sc.exe");

    public static string NsExePath => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    public static string PowerShellExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Versão do assembly, usada em logs e na UI.</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
