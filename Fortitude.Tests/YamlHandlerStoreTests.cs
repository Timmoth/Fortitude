using Fortitude.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

/// <summary>
///     Covers the on-disk YAML handler store: where it reads from, what it accepts, how it
///     survives bad input, and the guard on file names supplied by the live UI.
/// </summary>
public sealed class YamlHandlerStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fortitude-store-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private YamlHandlerStore CreateStore(bool hotReload = false)
    {
        Directory.CreateDirectory(_dir);

        return new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance,
            Options.Create(new Settings { ConfigDirectory = _dir, HotReload = hotReload }));
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private const string ValidHandler = """
                                        handlers:
                                          - match:
                                              methods: [GET]
                                              route: /ping
                                            response:
                                              status: 200
                                              body:
                                                text: pong
                                        """;

    // ---------------------------------------------------------------- directory

    [Fact]
    public void ConfigDirectory_IsConfigurable()
    {
        using var store = CreateStore();

        Assert.Equal(_dir, store.Directory);
    }

    [Fact]
    public void ConfigDirectory_DefaultsToConfigBesideTheExecutable()
    {
        using var store = new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance, Options.Create(new Settings()));

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "config"), store.Directory);
    }

    [Fact]
    public void ARelativeConfigDirectory_ResolvesAgainstTheApplicationDirectory()
    {
        using var store = new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance,
            Options.Create(new Settings { ConfigDirectory = "handlers" }));

        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "handlers")), store.Directory);
    }

    [Fact]
    public void AMissingDirectory_LoadsNothingRatherThanThrowing()
    {
        using var store = new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance,
            Options.Create(new Settings { ConfigDirectory = Path.Combine(_dir, "does-not-exist") }));

        Assert.Empty(store.Files);
    }

    // ---------------------------------------------------------------- discovery

    [Fact]
    public void BothYamlAndYmlExtensions_AreLoaded()
    {
        WriteFile("a.yaml", ValidHandler);
        WriteFile("b.yml", ValidHandler);

        using var store = CreateStore();

        Assert.Equal(2, store.Files.Count);
        Assert.All(store.Files, f => Assert.Single(f.Handlers));
    }

    [Fact]
    public void SubdirectoriesAreSearched()
    {
        WriteFile(Path.Combine("nested", "deep", "c.yaml"), ValidHandler);

        using var store = CreateStore();

        Assert.Single(store.Files);
        Assert.Single(store.Files[0].Handlers);
    }

    [Fact]
    public void NonYamlFiles_AreIgnored()
    {
        WriteFile("notes.txt", "not yaml");
        WriteFile("data.json", "{}");
        WriteFile("real.yaml", ValidHandler);

        using var store = CreateStore();

        Assert.Single(store.Files);
        Assert.Equal("real.yaml", store.Files[0].FileName);
    }

    [Fact]
    public void FilesAreLoadedInADeterministicOrder()
    {
        WriteFile("b.yaml", ValidHandler);
        WriteFile("a.yaml", ValidHandler);
        WriteFile("c.yaml", ValidHandler);

        using var store = CreateStore();

        Assert.Equal(["a.yaml", "b.yaml", "c.yaml"], store.Files.Select(f => f.FileName));
    }

    // ---------------------------------------------------------------- bad input

    [Fact]
    public void AMalformedFile_IsReportedAndDoesNotHideItsSiblings()
    {
        WriteFile("broken.yaml", "handlers: [ this is not valid: yaml: at all");
        WriteFile("good.yaml", ValidHandler);

        using var store = CreateStore();

        var broken = store.Files.Single(f => f.FileName == "broken.yaml");
        var good = store.Files.Single(f => f.FileName == "good.yaml");

        Assert.True(broken.HasError);
        Assert.NotNull(broken.Error);
        Assert.Empty(broken.Handlers);

        // The valid file still loaded — one bad file must not take the rest down.
        Assert.False(good.HasError);
        Assert.Single(good.Handlers);
    }

    [Fact]
    public void AnEmptyFile_LoadsAsZeroHandlersRatherThanFailing()
    {
        WriteFile("empty.yaml", "");
        WriteFile("comment-only.yaml", "# nothing here\n");

        using var store = CreateStore();

        Assert.All(store.Files, f =>
        {
            Assert.False(f.HasError);
            Assert.Empty(f.Handlers);
        });
    }

    // ---------------------------------------------------------------- path traversal

    [Theory]
    [InlineData("../escape.yaml")]
    [InlineData("../../appsettings.yaml")]
    [InlineData("nested/child.yaml")]
    [InlineData("/etc/passwd.yaml")]
    public void AFileNameThatEscapesTheDirectory_IsRejected(string fileName)
    {
        using var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ResolveFilePath(fileName));
        Assert.Throws<ArgumentException>(() => store.Save(fileName, "handlers:"));
        Assert.Throws<ArgumentException>(() => store.Delete(fileName));
    }

    [Theory]
    [InlineData("..%2F..%2Fescape.yaml")]
    [InlineData("%2e%2e%2fescape.yaml")]
    public void APercentEncodedFileName_IsRejected(string fileName)
    {
        // ASP.NET leaves %2F encoded in a route value, so such a name is contained rather
        // than traversing - but it creates an unmanageable file. Refuse it outright.
        using var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ResolveFilePath(fileName));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("appsettings.json")]
    [InlineData("script.sh")]
    [InlineData("noextension")]
    public void AFileNameWithoutAYamlExtension_IsRejected(string fileName)
    {
        using var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ResolveFilePath(fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyFileName_IsRejected(string fileName)
    {
        using var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ResolveFilePath(fileName));
    }

    [Fact]
    public void ARejectedSave_WritesNothingToDisk()
    {
        using var store = CreateStore();

        var outside = Path.GetFullPath(Path.Combine(_dir, "..", "escaped.yaml"));
        if (File.Exists(outside)) File.Delete(outside);

        Assert.Throws<ArgumentException>(() => store.Save("../escaped.yaml", "handlers:"));
        Assert.False(File.Exists(outside), "A rejected save must not reach the filesystem.");
    }

    [Theory]
    [InlineData("handlers.yaml")]
    [InlineData("handlers.yml")]
    [InlineData("Handlers.YAML")]
    public void APlainYamlFileName_IsAccepted(string fileName)
    {
        using var store = CreateStore();

        var resolved = store.ResolveFilePath(fileName);

        Assert.Equal(Path.Combine(_dir, fileName), resolved);
    }

    // ---------------------------------------------------------------- mutation

    [Fact]
    public void SavingAFile_MakesItsHandlersLive()
    {
        using var store = CreateStore();
        Assert.Empty(store.Files);

        store.Save("new.yaml", ValidHandler);

        Assert.Single(store.Files);
        Assert.Single(store.Files[0].Handlers);
        Assert.True(File.Exists(Path.Combine(_dir, "new.yaml")));
    }

    [Fact]
    public void DeletingAFile_RemovesItsHandlers()
    {
        WriteFile("gone.yaml", ValidHandler);

        using var store = CreateStore();
        Assert.Single(store.Files);

        store.Delete("gone.yaml");

        Assert.Empty(store.Files);
        Assert.False(File.Exists(Path.Combine(_dir, "gone.yaml")));
    }

    [Fact]
    public void DeletingAFileThatIsAlreadyGone_IsNotAnError()
    {
        using var store = CreateStore();

        var ex = Record.Exception(() => store.Delete("never-existed.yaml"));

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------- hot reload

    [Fact]
    public async Task AFileAppearingOnDisk_IsPickedUpWithoutARestart()
    {
        using var store = CreateStore(hotReload: true);
        Assert.Empty(store.Files);

        var reloaded = new TaskCompletionSource();
        store.OnReloaded += () =>
        {
            if (store.Files.Count > 0) reloaded.TrySetResult();
        };

        File.WriteAllText(Path.Combine(_dir, "late.yaml"), ValidHandler);

        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.True(completed == reloaded.Task, "Expected the watcher to reload within 10s.");
        Assert.Single(store.Files);
    }

    [Fact]
    public void WithHotReloadOff_ChangesAreNotPickedUpUntilReloadIsCalled()
    {
        using var store = CreateStore(hotReload: false);

        File.WriteAllText(Path.Combine(_dir, "manual.yaml"), ValidHandler);

        Assert.Empty(store.Files);

        store.Reload();

        Assert.Single(store.Files);
    }

    [Fact]
    public async Task ReloadingWhileServingRequests_DoesNotThrow()
    {
        // Regression: the handler list used to be cleared and refilled in place while request
        // threads iterated it. It is now swapped atomically.
        WriteFile("a.yaml", ValidHandler);
        using var store = CreateStore();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var serving = Task.Run(async () =>
        {
            var request = new Fortitude.Client.FortitudeRequest
            {
                Method = "GET", BaseUrl = "http://localhost", Route = "/ping",
                Url = "http://localhost/ping", Protocol = "HTTP/1.1"
            };

            while (!cts.IsCancellationRequested)
                await store.HandleIncomingAsync(request);
        }, cts.Token);

        var reloading = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                store.Reload();
        }, cts.Token);

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(serving, reloading));

        Assert.True(ex is null or OperationCanceledException, $"Unexpected exception: {ex}");
    }
}
