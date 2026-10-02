using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The out-of-git per-project notepad store (ontwerp-v0.5.md item 8): path resolution next
/// to the loaded magnaflow.yml, filename sanitization, and hash-based optimistic concurrency.</summary>
public class ScratchpadStoreTests
{
    private static ScratchpadStore StoreAt(string configPath) =>
        new(new CockpitConfig { ConfigPath = configPath });

    [Fact]
    public void Directory_is_scratchpad_next_to_the_loaded_config()
    {
        using var project = new TempProject();
        var configPath = Path.Combine(project.Root, "magnaflow.yml");
        var store = StoreAt(configPath);

        Assert.Equal(Path.Combine(project.Root, "scratchpad"), store.Directory);
    }

    [Fact]
    public void Directory_resolves_even_when_the_config_file_does_not_exist_on_disk()
    {
        // Running on defaults: ConfigPath is the next-to-binary primary path, which may not exist.
        using var project = new TempProject();
        var configPath = Path.Combine(project.Root, "does-not-exist", "magnaflow.yml");
        var store = StoreAt(configPath);

        Assert.Equal(Path.Combine(project.Root, "does-not-exist", "scratchpad"), store.Directory);
    }

    [Theory]
    [InlineData("PiStart", "PiStart.md")]
    [InlineData("My Cool Project", "My-Cool-Project.md")]
    [InlineData("a/b\\c", "abc.md")]
    public void Filename_is_the_sanitized_project_name(string projectName, string expectedFile)
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        Assert.Equal(Path.Combine(store.Directory, expectedFile), store.PathFor(projectName));
    }

    [Fact]
    public void A_reserved_device_name_gets_a_safe_hashed_filename()
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        var path = store.PathFor("CON");
        var file = Path.GetFileName(path);
        Assert.StartsWith("_", file);
        Assert.EndsWith(".md", file);
        Assert.NotEqual("con.md", file.ToLowerInvariant());
    }

    [Fact]
    public void Read_of_a_never_written_note_is_empty_with_no_timestamp()
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        var snapshot = store.Read("proj");
        Assert.Equal("", snapshot.Content);
        Assert.Null(snapshot.SavedAt);
        Assert.Equal(ScratchpadStore.ComputeHash(""), snapshot.Hash);
    }

    [Fact]
    public void Save_then_read_round_trips_and_reports_a_timestamp()
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        var save = store.Save("proj", "hello notes", ScratchpadStore.ComputeHash(""));
        Assert.Equal(ScratchpadStore.SaveOutcome.Ok, save.Outcome);

        var snapshot = store.Read("proj");
        Assert.Equal("hello notes", snapshot.Content);
        Assert.NotNull(snapshot.SavedAt);
        Assert.Equal(save.Hash, snapshot.Hash);
    }

    [Fact]
    public void Save_with_a_stale_base_hash_conflicts_and_does_not_overwrite()
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        store.Save("proj", "first", ScratchpadStore.ComputeHash(""));
        // A second writer still holding the empty-file hash must be told to reload, not clobber.
        var conflict = store.Save("proj", "second", ScratchpadStore.ComputeHash(""));

        Assert.Equal(ScratchpadStore.SaveOutcome.Conflict, conflict.Outcome);
        Assert.Equal("first", store.Read("proj").Content);
    }

    [Fact]
    public void Save_over_the_size_cap_is_rejected()
    {
        using var project = new TempProject();
        var store = StoreAt(Path.Combine(project.Root, "magnaflow.yml"));

        var tooBig = new string('x', ScratchpadStore.MaxContentBytes + 1);
        var result = store.Save("proj", tooBig, ScratchpadStore.ComputeHash(""));

        Assert.Equal(ScratchpadStore.SaveOutcome.TooLarge, result.Outcome);
        Assert.False(File.Exists(store.PathFor("proj")));
    }
}
