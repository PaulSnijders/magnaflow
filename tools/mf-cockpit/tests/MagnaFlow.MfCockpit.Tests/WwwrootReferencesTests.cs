using System.Text.RegularExpressions;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// The pages are ES modules with no build step (docs/specs/cockpit/index.md#page-rendering), so a
/// typo in an import path or a script src is a blank page and no error anywhere else. These read
/// the source <c>wwwroot/</c> and check that every local reference resolves to a file.
/// </summary>
public class WwwrootReferencesTests
{
    private static readonly string ProjectDir = FindCockpitProjectDir();
    private static readonly string Wwwroot = Path.Combine(ProjectDir, "wwwroot");

    private static string FindCockpitProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MfCockpit.slnx")))
                return Path.Combine(dir.FullName, "src", "MagnaFlow.MfCockpit");
        }
        throw new InvalidOperationException("MfCockpit.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>wwwroot paths the csproj links in at build time (the logo and banner from the repo's
    /// assets/), which are not files in the source wwwroot/.</summary>
    private static HashSet<string> LinkedWwwrootFiles()
    {
        var csproj = File.ReadAllText(Path.Combine(ProjectDir, "MagnaFlow.MfCockpit.csproj"));
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(csproj, @"<Content\s+Include=""([^""]+)""\s+Link=""wwwroot[/\\]([^""]+)"""))
        {
            if (File.Exists(Path.GetFullPath(Path.Combine(ProjectDir, m.Groups[1].Value.Replace('\\', '/')))))
                linked.Add(m.Groups[2].Value.Replace('\\', '/'));
        }
        return linked;
    }

    private static bool ResolvesUnderWwwroot(string relativeToWwwroot, HashSet<string> linked) =>
        File.Exists(Path.Combine(Wwwroot, relativeToWwwroot)) || linked.Contains(relativeToWwwroot);

    public static TheoryData<string> HtmlPages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Wwwroot, "*.html").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(HtmlPages))]
    public void Every_local_src_and_href_in_a_page_resolves_to_a_file(string page)
    {
        var text = File.ReadAllText(Path.Combine(Wwwroot, page));
        var linked = LinkedWwwrootFiles();
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (Match m in Regex.Matches(text, @"\b(?:src|href)\s*=\s*""([^""]*)"""))
        {
            var value = m.Groups[1].Value;
            // Same-page anchors, absolute URLs and values an inline script builds at runtime.
            if (value.Length == 0 || value.StartsWith('#') || value.Contains("${") ||
                Regex.IsMatch(value, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase) || value.StartsWith("//"))
                continue;
            var path = value.Split('?', '#')[0].TrimStart('/');
            checkedCount++;
            if (!ResolvesUnderWwwroot(path, linked)) missing.Add(value);
        }

        Assert.True(checkedCount > 0, $"{page}: no local src/href found — the pattern no longer matches");
        Assert.True(missing.Count == 0, $"{page}: unresolved {string.Join(", ", missing)}");
    }

    // `import … from "./x.js"`, `export … from "../x.js"`, a bare `import "./x.js"`, and the
    // JSDoc `import("../types.js")` type references. Only relative specifiers are local files.
    private static readonly Regex StaticImport = new(@"\b(?:import|export)\b[^;""'`]*?\bfrom\s*[""']([^""']+)[""']", RegexOptions.Singleline);
    private static readonly Regex BareImport = new(@"\bimport\s*[""']([^""']+)[""']");
    private static readonly Regex TypeImport = new(@"\bimport\(\s*[""']([^""']+)[""']\s*\)");

    [Fact]
    public void Every_relative_import_in_an_asset_module_resolves_to_a_file()
    {
        var assets = Path.Combine(Wwwroot, "assets");
        var missing = new List<string>();
        var found = new List<string>();

        foreach (var file in Directory.GetFiles(assets, "*.js", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var specifiers = StaticImport.Matches(text).Concat(BareImport.Matches(text)).Concat(TypeImport.Matches(text))
                .Select(m => m.Groups[1].Value)
                .Where(s => s.StartsWith("./") || s.StartsWith("../"));
            var rel = Path.GetRelativePath(Wwwroot, file).Replace('\\', '/');
            foreach (var spec in specifiers)
            {
                found.Add($"{rel} -> {spec}");
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, spec));
                if (!File.Exists(target)) missing.Add($"{rel}: {spec}");
            }
        }

        // The pattern must still see the imports the pages are built on, or this test checks nothing.
        Assert.Contains("assets/ui.js -> ./vendor/preact-htm.js", found);
        Assert.Contains("assets/pages/project.js -> ../ui.js", found);
        Assert.Contains("assets/pages/index.js -> ../ui.js", found);
        Assert.Contains("assets/ui.js -> ./app.js", found);
        Assert.Contains("assets/ui.js -> ./md.js", found);
        foreach (var page in new[] { "index", "project", "specs", "command", "chat", "config", "scratchpad" })
            Assert.Contains($"assets/pages/{page}.js -> ../app.js", found);
        Assert.True(missing.Count == 0, "unresolved imports: " + string.Join("; ", missing));
    }

    /// <summary>Every page and every asset module except the vendored file and md.js, which builds
    /// HTML on purpose (its output is the one string Markdown injects).</summary>
    public static TheoryData<string> RenderingFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Wwwroot, "*.html").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        var assets = Path.Combine(Wwwroot, "assets");
        foreach (var file in Directory.GetFiles(assets, "*.js", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(Wwwroot, file).Replace('\\', '/');
            if (rel.StartsWith("assets/vendor/") || rel == "assets/md.js") continue;
            data.Add(rel);
        }
        return data;
    }

    [Fact]
    public void The_rendering_file_list_covers_every_page_module()
    {
        var files = RenderingFiles().Cast<object[]>().Select(row => (string)row[0]).ToList();
        foreach (var page in new[] { "index", "project", "specs", "command", "chat", "config", "scratchpad" })
        {
            Assert.Contains(page + ".html", files);
            Assert.Contains("assets/pages/" + page + ".js", files);
        }
        Assert.Contains("assets/app.js", files);
        Assert.Contains("assets/ui.js", files);
        Assert.DoesNotContain("assets/md.js", files);
        Assert.DoesNotContain("assets/vendor/preact-htm.js", files);
    }

    [Theory]
    [MemberData(nameof(RenderingFiles))]
    public void A_page_or_module_injects_no_html_strings(string file)
    {
        var text = File.ReadAllText(Path.Combine(Wwwroot, file));
        Assert.DoesNotContain("innerHTML", text);
        Assert.DoesNotContain("escapeHtml", text);
    }

    [Theory]
    [MemberData(nameof(HtmlPages))]
    public void Every_script_in_a_page_is_a_module(string page)
    {
        var text = File.ReadAllText(Path.Combine(Wwwroot, page));
        var scripts = Regex.Matches(text, @"<script\b([^>]*)>", RegexOptions.IgnoreCase);

        Assert.True(scripts.Count > 0, $"{page}: no <script> found — the pattern no longer matches");
        foreach (Match m in scripts)
            Assert.Matches(@"\btype\s*=\s*""module""", m.Groups[1].Value);
    }
}
