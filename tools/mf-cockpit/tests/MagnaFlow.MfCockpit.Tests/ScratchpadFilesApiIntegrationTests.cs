using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>/api/projects/{name}/files (docs/specs/cockpit/scratchpad.md#files): the upload →
/// list → download → delete round trip in-process, and the body-size behaviour on real Kestrel —
/// TestServer enforces no request body limit, so only Kestrel proves its 30 MB default was raised.</summary>
public class ScratchpadFilesApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configDir;
    private readonly string _configPath;

    public ScratchpadFilesApiIntegrationTests()
    {
        // Own config directory per test: the scratchpad/ folder sits next to the config, and a
        // shared one would let parallel test classes delete each other's files.
        _configDir = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + "-cfg");
        Directory.CreateDirectory(_configDir);
        _configPath = Path.Combine(_configDir, "magnaflow.yml");
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root));
        _project.InitGit();
    }

    public void Dispose()
    {
        _project.Dispose();
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best effort */ }
    }

    private string FilesDir => Path.Combine(_configDir, "scratchpad", "proj.files");

    [Fact]
    public async Task Upload_list_download_delete_round_trip()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();
        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

        var put = await client.PutAsync("/api/projects/proj/files/" + Uri.EscapeDataString("Rapport Q3.pdf"), new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var entry = await put.Content.ReadFromJsonAsync<ScratchpadFileDto>();
        Assert.Equal("Rapport Q3.pdf", entry!.Name);
        Assert.Equal(5000, entry.Size);

        var list = await client.GetFromJsonAsync<List<ScratchpadFileDto>>("/api/projects/proj/files");
        Assert.Equal("Rapport Q3.pdf", Assert.Single(list!).Name);

        var get = await client.GetAsync("/api/projects/proj/files/" + Uri.EscapeDataString("Rapport Q3.pdf"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/octet-stream", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", get.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync());

        var del = await client.DeleteAsync("/api/projects/proj/files/" + Uri.EscapeDataString("Rapport Q3.pdf"));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/proj/files/Rapport%20Q3.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/projects/proj/files/Rapport%20Q3.pdf")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<ScratchpadFileDto>>("/api/projects/proj/files"))!);
    }

    [Fact]
    public async Task Files_land_next_to_the_config_never_in_the_working_copy()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        await client.PutAsync("/api/projects/proj/files/x.txt", new StringContent("hi"));

        Assert.True(File.Exists(Path.Combine(FilesDir, "x.txt")));
        Assert.Empty(Directory.GetFiles(_project.Root, "x.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Non_ascii_name_downloads_under_its_name()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();
        const string name = "Ünïcödé €.txt";

        await client.PutAsync("/api/projects/proj/files/" + Uri.EscapeDataString(name), new StringContent("x"));
        var get = await client.GetAsync("/api/projects/proj/files/" + Uri.EscapeDataString(name));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(name, get.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/nope/files")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/projects/nope/files/a.txt", new StringContent("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/nope/files/a.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/projects/nope/files/a.txt")).StatusCode);
    }

    [Fact]
    public async Task Bad_name_is_400()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var put = await client.PutAsync("/api/projects/proj/files/CON.txt", new StringContent("x"));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.False(Directory.Exists(FilesDir) && Directory.EnumerateFileSystemEntries(FilesDir).Any());
    }

    [Fact]
    public async Task A_body_above_kestrels_30_MB_default_is_accepted_on_real_kestrel()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = StartOnKestrel(factory);
        const long size = 31L * 1024 * 1024;

        var put = await client.PutAsync("/api/projects/proj/files/big.bin", new StreamContent(new GeneratedStream(size)));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(size, (await put.Content.ReadFromJsonAsync<ScratchpadFileDto>())!.Size);
        Assert.Equal(size, new FileInfo(Path.Combine(FilesDir, "big.bin")).Length);
    }

    [Fact]
    public async Task Over_the_cap_is_413_on_real_kestrel_and_leaves_nothing()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = StartOnKestrel(factory);

        var put = await client.PutAsync("/api/projects/proj/files/huge.bin",
            new StreamContent(new GeneratedStream(ScratchpadFileStore.MaxFileBytes + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, put.StatusCode);
        Assert.Contains("200 MB cap", await put.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(FilesDir) && Directory.EnumerateFileSystemEntries(FilesDir).Any());
    }

    /// <summary>Real Kestrel on an ephemeral loopback port. An explicit Listen overrides Program's
    /// UseUrls (the configured port, 5210 by default — the live cockpit's port), which
    /// UseKestrel(0) alone does not.</summary>
    private static HttpClient StartOnKestrel(CockpitFactory factory)
    {
        factory.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
        factory.StartServer();
        return factory.CreateClient();
    }

    /// <summary>A seekable stream of deterministic bytes, so HttpClient sends a Content-Length
    /// without any fixture file or large buffer in memory.</summary>
    private sealed class GeneratedStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            for (var i = 0; i < n; i++)
                buffer[offset + i] = (byte)((_position + i) % 251);
            _position += n;
            return n;
        }
    }
}
