using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinalFrontier.Launcher;

internal static class Program
{
    private const string SettingsFileName = "launcher.settings.json";

    public static async Task<int> Main(string[] args)
    {
        var settings = await LauncherSettings.LoadAsync(SettingsFileName);
        using var logger = LauncherLogger.Create(settings.Diagnostics.LogDirectoryName);

        logger.Info($"Starting {settings.Branding.ProductName}");
        logger.Info($"Target server: {settings.Server.Name} ({settings.Server.Address}:{settings.Server.Port})");

        if (args.Contains("--print-config", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(settings, LauncherSettings.JsonOptions));
            return 0;
        }

        var probeResult = await ProbeServerAsync(settings, logger);
        if (!probeResult)
        {
            logger.Error("Server probe failed. Client launch has been skipped.");
            Console.Error.WriteLine("Unable to verify The Final Frontier server status/build metadata. See launcher logs for details.");
            return 2;
        }

        var clientPath = Environment.GetEnvironmentVariable("FINAL_FRONTIER_CLIENT_PATH");
        if (string.IsNullOrWhiteSpace(clientPath))
        {
            logger.Info("FINAL_FRONTIER_CLIENT_PATH is not set. This scaffold verified server metadata only and did not launch a client.");
            Console.WriteLine("Server metadata check completed. Set FINAL_FRONTIER_CLIENT_PATH to launch a local client executable.");
            return 0;
        }

        return LaunchClient(clientPath, settings, logger);
    }

    private static async Task<bool> ProbeServerAsync(LauncherSettings settings, LauncherLogger logger)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        var success = true;

        success &= await ProbeEndpointAsync(http, settings.Server.StatusEndpoint, "status", logger, settings.Diagnostics.LogStatusChecks);
        success &= await ProbeEndpointAsync(http, settings.Server.InfoEndpoint, "info", logger, settings.Diagnostics.LogStatusChecks);

        return success;
    }

    private static async Task<bool> ProbeEndpointAsync(HttpClient http, string endpoint, string name, LauncherLogger logger, bool logBody)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.Warn($"No {name} endpoint configured.");
            return false;
        }

        try
        {
            logger.Info($"Checking {name} endpoint: {endpoint}");
            using var response = await http.GetAsync(endpoint);
            var body = await response.Content.ReadAsStringAsync();

            logger.Info($"{name} endpoint returned {(int) response.StatusCode} {response.ReasonPhrase}");

            if (logBody)
                logger.Debug($"{name} response: {body}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to query {name} endpoint: {ex}");
            return false;
        }
    }

    private static int LaunchClient(string clientPath, LauncherSettings settings, LauncherLogger logger)
    {
        if (!File.Exists(clientPath))
        {
            logger.Error($"Configured client executable does not exist: {clientPath}");
            Console.Error.WriteLine("Configured client executable does not exist. Check FINAL_FRONTIER_CLIENT_PATH.");
            return 3;
        }

        var arguments = $"connect {settings.Server.Address}:{settings.Server.Port}";

        if (settings.Diagnostics.LogLaunchArguments)
            logger.Info($"Launching client: {clientPath} {arguments}");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = clientPath,
                Arguments = arguments,
                UseShellExecute = false
            });

            if (process == null)
            {
                logger.Error("Process.Start returned null.");
                return 4;
            }

            process.WaitForExit();

            if (settings.Diagnostics.LogProcessExitCode)
                logger.Info($"Client exited with code {process.ExitCode}");

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to launch client: {ex}");
            return 5;
        }
    }
}

internal sealed record LauncherSettings(
    BrandingSettings Branding,
    ServerSettings Server,
    DiagnosticsSettings Diagnostics)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<LauncherSettings> LoadAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Launcher settings file was not found.", path);

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions);

        return settings ?? throw new InvalidOperationException("Launcher settings file was empty or invalid.");
    }
}

internal sealed record BrandingSettings(
    string ProductName,
    string ShortName,
    string WindowTitle,
    string Description,
    string SupportUrl,
    string DiscordUrl);

internal sealed record ServerSettings(
    string Name,
    string Address,
    int Port,
    string StatusEndpoint,
    string InfoEndpoint);

internal sealed record DiagnosticsSettings(
    string LogDirectoryName,
    bool LogStatusChecks,
    bool LogLaunchArguments,
    bool LogProcessExitCode);

internal sealed class LauncherLogger : IDisposable
{
    private readonly StreamWriter _writer;

    private LauncherLogger(StreamWriter writer)
    {
        _writer = writer;
    }

    public static LauncherLogger Create(string directoryName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            string.IsNullOrWhiteSpace(directoryName) ? "FinalFrontierLauncher" : directoryName,
            "logs");

        Directory.CreateDirectory(root);

        var file = Path.Combine(root, $"launcher-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        return new LauncherLogger(new StreamWriter(file, append: false) { AutoFlush = true });
    }

    public void Debug(string message) => Write("DEBUG", message);
    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";
        Console.WriteLine(line);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
