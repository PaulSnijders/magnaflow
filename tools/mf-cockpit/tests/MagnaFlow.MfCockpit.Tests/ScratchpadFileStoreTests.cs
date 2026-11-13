using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The scratchpad file drop store (docs/specs/cockpit/scratchpad.md#files): name
/// sanitization, the temp-file write, list order, overwrite, the size cap and delete.</summary>
public class ScratchpadFileStoreTests : IDisposable
{
    private readonly TempProject _dir = new();

    public void Dispose() => _dir.Dispose();

    private ScratchpadFileStore Store(long? maxBytes = null)
    {
        var notes = new ScratchpadStore(new CockpitConfig { ConfigPath = Path.Combine(_dir.Root, "magnaflow.yml") });
        return maxBytes is { } m ? new ScratchpadFileStore(notes) { MaxBytes = m } : new ScratchpadFileStore(notes);
    }

    private static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    private static string[] FilesIn(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).ToArray() : [];

    [Theory]
    [InlineData("a/b.txt", "b.txt")]
    [InlineData("..\\x", "x")]
    [InlineData("a:b?.pdf", "a_b_.pdf")]
    [InlineData("report.", "report")]
    [InlineData("notes. . ", "notes")]
    [InlineData("tab\there.txt", "tab_here.txt")]
    [InlineData("<>|\"*.bin", "_____.bin")]
    [InlineData(".bashrc", ".bashrc")]
    [InlineData("Ünïcödé naam.docx", "Ünïcödé naam.docx")]
    public void Sanitize_keeps_a_safe_last_segment(string raw, string expected) =>
        Assert.Equal(expected, ScratchpadFileStore.SanitizeFileName(raw));

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("dir/")]
    [InlineData("CON.txt")]
    [InlineData("con")]
    [InlineData("nul.tar.gz")]
    [InlineData("COM1")]
    [InlineData(".upload-0123abcd.tmp")]
    public void Sanitize_refuses(string raw) =>
        Assert.Null(ScratchpadFileStore.SanitizeFileName(raw));

    [Fact]
    public void Sanitize_caps_the_name_at_200_chars_keeping_the_extension()
    {
        var name = ScratchpadFileStore.SanitizeFileName(new string('a', 300) + ".pdf");

        Assert.Equal(200, name!.Length);
        Assert.EndsWith("a.pdf", name);
    }

    [Fact]
    public void Directory_is_the_note_name_plus_files_next_to_the_note()
    {
        var store = Store();
        var notes = new ScratchpadStore(new CockpitConfig { ConfigPath = Path.Combine(_dir.Root, "magnaflow.yml") });

        Assert.Equal(Path.ChangeExtension(notes.PathFor("My Project"), ".files"), store.DirectoryFor("My Project"));
        Assert.Equal(Path.Combine(_dir.Root, "scratchpad", "My-Project.files"), store.DirectoryFor("My Project"));
    }

    [Fact]
    public void List_of_an_absent_directory_is_empty() =>
        Assert.Empty(Store().List("proj"));

    [Fact]
    public async Task Save_then_list_newest_first_with_size_and_time()
    {
        var store = Store();
        await store.SaveAsync("proj", "old.txt", Bytes("12345"), CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(store.DirectoryFor("proj"), "old.txt"), DateTime.UtcNow.AddMinutes(-5));
        var saved = await store.SaveAsync("proj", "sub/new.txt", Bytes("ab"), CancellationToken.None);

        Assert.Equal(ScratchpadFileStore.SaveOutcome.Ok, saved.Outcome);
        Assert.Equal("new.txt", saved.Entry!.Name);
        var list = store.List("proj");
        Assert.Equal(["new.txt", "old.txt"], list.Select(e => e.Name));
        Assert.Equal([2L, 5L], list.Select(e => e.Size));
        Assert.True(DateTimeOffset.TryParse(list[0].UploadedAt, out _));
        Assert.EndsWith("+00:00", list[0].UploadedAt);
    }

    [Fact]
    public async Task Same_name_overwrites()
    {
        var store = Store();
        await store.SaveAsync("proj", "a.txt", Bytes("first"), CancellationToken.None);
        await store.SaveAsync("proj", "a.txt", Bytes("second!"), CancellationToken.None);

        var entry = Assert.Single(store.List("proj"));
        Assert.Equal(7, entry.Size);
        using var reader = new StreamReader(store.OpenRead("proj", "a.txt")!);
        Assert.Equal("second!", reader.ReadToEnd());
    }

    [Fact]
    public async Task Bad_name_is_refused_and_writes_nothing()
    {
        var store = Store();
        var result = await store.SaveAsync("proj", "CON.txt", Bytes("x"), CancellationToken.None);

        Assert.Equal(ScratchpadFileStore.SaveOutcome.BadName, result.Outcome);
        Assert.Empty(FilesIn(store.DirectoryFor("proj")));
    }

    [Fact]
    public async Task Over_cap_save_leaves_no_file_and_no_temp_file()
    {
        var store = Store(maxBytes: 10);
        var result = await store.SaveAsync("proj", "big.bin", new MemoryStream(new byte[11]), CancellationToken.None);

        Assert.Equal(ScratchpadFileStore.SaveOutcome.TooLarge, result.Outcome);
        Assert.Empty(FilesIn(store.DirectoryFor("proj")));
    }

    [Fact]
    public async Task Exactly_the_cap_is_accepted()
    {
        var store = Store(maxBytes: 10);
        var result = await store.SaveAsync("proj", "edge.bin", new MemoryStream(new byte[10]), CancellationToken.None);

        Assert.Equal(ScratchpadFileStore.SaveOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Cancelled_save_leaves_no_temp_file()
    {
        var store = Store();
        using var cts = new CancellationTokenSource();
        var body = new CancelAfterFirstReadStream(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync("proj", "half.bin", body, cts.Token));

        Assert.Empty(FilesIn(store.DirectoryFor("proj")));
    }

    [Fact]
    public async Task Failing_body_leaves_no_temp_file_and_keeps_the_old_version()
    {
        var store = Store();
        await store.SaveAsync("proj", "keep.txt", Bytes("old"), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() =>
            store.SaveAsync("proj", "keep.txt", new ThrowingStream(), CancellationToken.None));

        Assert.Equal(["keep.txt"], FilesIn(store.DirectoryFor("proj")));
        Assert.Equal(3, store.List("proj")[0].Size);
    }

    [Fact]
    public async Task Delete_removes_the_file_and_unknown_is_false()
    {
        var store = Store();
        await store.SaveAsync("proj", "a.txt", Bytes("x"), CancellationToken.None);

        Assert.True(store.Delete("proj", "a.txt"));
        Assert.Empty(store.List("proj"));
        Assert.False(store.Delete("proj", "a.txt"));
        Assert.Null(store.OpenRead("proj", "a.txt"));
    }

    [Fact]
    public void Temp_files_are_not_listed_downloadable_or_deletable()
    {
        var store = Store();
        var dir = store.DirectoryFor("proj");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".upload-abc.tmp"), "partial");
        File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");

        Assert.Equal(["done.txt"], store.List("proj").Select(e => e.Name));
        Assert.Null(store.OpenRead("proj", ".upload-abc.tmp"));
        Assert.False(store.Delete("proj", ".upload-abc.tmp"));
        Assert.True(File.Exists(Path.Combine(dir, ".upload-abc.tmp")));
    }

    [Fact]
    public async Task Unsanitized_names_do_not_resolve_on_read_or_delete()
    {
        var store = Store();
        await store.SaveAsync("proj", "b.txt", Bytes("x"), CancellationToken.None);

        Assert.Null(store.OpenRead("proj", "a/b.txt"));
        Assert.Null(store.OpenRead("proj", "../proj.files/b.txt"));
        Assert.False(store.Delete("proj", "..\\b.txt"));
        Assert.Single(store.List("proj"));
    }

    [Fact]
    public async Task Two_projects_that_sanitize_alike_share_the_directory()
    {
        // Documented, like the note: "My Project" and "My-Project" both map to My-Project.files.
        var store = Store();
        await store.SaveAsync("My Project", "shared.txt", Bytes("x"), CancellationToken.None);

        Assert.Equal(store.DirectoryFor("My Project"), store.DirectoryFor("My-Project"));
        Assert.Equal(["shared.txt"], store.List("My-Project").Select(e => e.Name));
    }

    private sealed class CancelAfterFirstReadStream(CancellationTokenSource cts) : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().Result;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                buffer.Span[..Math.Min(100, buffer.Length)].Fill(1);
                return ValueTask.FromResult(Math.Min(100, buffer.Length));
            }
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }

    private sealed class ThrowingStream : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_first)
                throw new IOException("connection reset");
            _first = false;
            buffer[offset] = 42;
            return 1;
        }
    }
}
