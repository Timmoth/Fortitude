using Fortitude.Client;
using Microsoft.Extensions.Options;

namespace Fortitude.Server;

/// <summary>
///     Owns the YAML handler definitions on disk: where they live, loading them, serving
///     requests from them, and the edits made through the live UI.
/// </summary>
/// <remarks>
///     Handlers defined here are matched before a request is forwarded to any connected client,
///     so a YAML definition takes precedence over a client-registered handler for the same route.
/// </remarks>
public sealed class YamlHandlerStore : IDisposable
{
    private readonly ILogger<YamlHandlerStore> _logger;
    private readonly object _reloadLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>
    ///     The current handler set. Replaced wholesale on reload rather than mutated, so request
    ///     threads always iterate a stable snapshot.
    /// </summary>
    private volatile IReadOnlyList<FortitudeHandler> _handlers = [];

    /// <summary>Per-file load results, for display and diagnostics.</summary>
    private volatile IReadOnlyList<YamlHandlerFile> _files = [];

    /// <summary>
    ///     Scenario state shared across every definition file, so handlers in different files
    ///     can take part in the same flow. Survives a reload: resetting it on every file save
    ///     would make scenarios unusable with hot reload on.
    /// </summary>
    public FortitudeScenarios Scenarios { get; } = new();

    public YamlHandlerStore(ILogger<YamlHandlerStore> logger, IOptions<Settings> settings)
    {
        _logger = logger;

        Directory = ResolveDirectory(settings.Value);

        Reload();

        if (settings.Value.HotReload)
            StartWatching();
    }

    /// <summary>Absolute path of the directory handler definitions are read from.</summary>
    public string Directory { get; }

    /// <summary>Per-file load results, including any that failed to parse.</summary>
    public IReadOnlyList<YamlHandlerFile> Files => _files;

    /// <summary>Raised after the handler set is reloaded, so the UI can refresh.</summary>
    public event Action? OnReloaded;

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private static string ResolveDirectory(Settings settings)
    {
        var configured = settings.ConfigDirectory;

        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(AppContext.BaseDirectory, "config");

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    /// <summary>
    ///     Matches a request against the YAML handlers, newest definition first.
    /// </summary>
    /// <returns>The response, or null if no YAML handler matched.</returns>
    public async Task<FortitudeResponse?> HandleIncomingAsync(FortitudeRequest request)
    {
        // Snapshot once: a concurrent reload swaps the reference, it never mutates this list.
        var handler = FortitudeHandler.SelectFor(_handlers, request);

        if (handler == null)
            return null;

        var response = await handler.HandleRequestAsync(request);

        _logger.LogInformation("[Incoming]: {Request}", request.ToString());
        _logger.LogInformation("[Handled] {Response}", response);

        return response;
    }

    /// <summary>
    ///     Re-reads every definition file and atomically swaps in the result.
    /// </summary>
    public void Reload()
    {
        lock (_reloadLock)
        {
            var files = FortitudeYamlLoader.LoadHandlerFiles(Directory, Scenarios);

            _files = files;
            _handlers = files.SelectMany(f => f.Handlers).ToList();

            foreach (var failed in files.Where(f => f.HasError))
                _logger.LogError(
                    "Handler file {File} could not be parsed and was skipped: {Error}",
                    failed.Path, failed.Error);

            _logger.LogInformation(
                "Loaded {HandlerCount} YAML handler(s) from {FileCount} file(s) in {Directory}",
                _handlers.Count, files.Count, Directory);
        }

        OnReloaded?.Invoke();
    }

    /// <summary>
    ///     Resolves a user-supplied file name to a full path inside <see cref="Directory" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown if the name is empty, carries any directory component, is not a recognised
    ///     YAML extension, or would otherwise resolve outside the configured directory.
    /// </exception>
    /// <remarks>
    ///     The live UI accepts this name from the browser, so it is untrusted input: without
    ///     this check a name like <c>../../appsettings.json</c> would let the editor write
    ///     anywhere the server process can reach.
    /// </remarks>
    public string ResolveFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        // Reject anything that is not a bare file name before touching the filesystem.
        if (fileName != Path.GetFileName(fileName))
            throw new ArgumentException(
                $"File name must not contain a path: '{fileName}'.", nameof(fileName));

        // Percent-encoding never belongs in a bare file name. ASP.NET leaves %2F encoded in
        // a route value, so without this a name like "..%2F..%2Fx.yaml" is contained but
        // creates an unmanageable file instead of being refused outright.
        if (fileName.Contains('%'))
            throw new ArgumentException(
                $"File name must not contain percent-encoding: '{fileName}'.", nameof(fileName));

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"File name contains characters that are not valid in a file name: '{fileName}'.",
                nameof(fileName));

        if (!FortitudeYamlLoader.YamlExtensions.Contains(
                Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"File name must end in {string.Join(" or ", FortitudeYamlLoader.YamlExtensions)}: '{fileName}'.",
                nameof(fileName));

        var root = Path.GetFullPath(Directory);
        var full = Path.GetFullPath(Path.Combine(root, fileName));

        // Belt and braces: confirm the resolved path really is under the root. Catches
        // platform-specific quirks that slip past the name check above.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(Path.GetDirectoryName(full), root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
            throw new ArgumentException(
                $"File name resolves outside the handler directory: '{fileName}'.", nameof(fileName));

        return full;
    }

    /// <summary>Writes a definition file and reloads.</summary>
    public void Save(string fileName, string content)
    {
        var path = ResolveFilePath(fileName);

        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, content ?? string.Empty);

        _logger.LogInformation("Saved handler file {File}", path);
        Reload();
    }

    /// <summary>Deletes a definition file and reloads.</summary>
    public void Delete(string fileName)
    {
        var path = ResolveFilePath(fileName);

        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted handler file {File}", path);
        }

        Reload();
    }

    /// <summary>
    ///     Watches the directory and reloads on change, debounced because editors commonly
    ///     produce several events for one save.
    /// </summary>
    private void StartWatching()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(Directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;

            _logger.LogInformation("Watching {Directory} for handler changes", Directory);
        }
        catch (Exception ex)
        {
            // Hot reload is a convenience; never let it stop the server starting.
            _logger.LogWarning(ex, "Could not watch {Directory} for handler changes", Directory);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!FortitudeYamlLoader.YamlExtensions.Contains(
                Path.GetExtension(e.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase))
            return;

        _debounce?.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
    }
}
