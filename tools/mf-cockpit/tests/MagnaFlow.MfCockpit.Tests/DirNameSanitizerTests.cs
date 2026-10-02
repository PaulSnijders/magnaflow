using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Tests;

public class DirNameSanitizerTests
{
    [Fact]
    public void Trims_leading_and_trailing_whitespace()
    {
        Assert.Equal("hello", DirNameSanitizer.Sanitize("   hello   "));
    }

    [Fact]
    public void Strips_characters_invalid_in_windows_file_names()
    {
        Assert.Equal("abc", DirNameSanitizer.Sanitize("a<b>c:\"/\\|?*"));
    }

    [Fact]
    public void Converts_spaces_to_hyphens()
    {
        Assert.Equal("my-cool-project", DirNameSanitizer.Sanitize("my cool project"));
    }

    [Fact]
    public void Collapses_repeated_hyphens()
    {
        Assert.Equal("a-b", DirNameSanitizer.Sanitize("a   -  - b"));
    }

    [Fact]
    public void Trims_leading_and_trailing_hyphens_after_collapsing()
    {
        Assert.Equal("hello", DirNameSanitizer.Sanitize("  -hello-  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData(":::")]
    public void Empty_or_all_invalid_input_sanitizes_to_empty_string(string input)
    {
        Assert.Equal("", DirNameSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Recognizes_windows_reserved_device_names_case_insensitively(string reserved)
    {
        Assert.True(DirNameSanitizer.IsReservedName(reserved));
    }

    [Theory]
    [InlineData("CONsole")]
    [InlineData("NULL")]
    [InlineData("my-project")]
    public void Does_not_flag_names_that_merely_start_with_a_reserved_name(string notReserved)
    {
        Assert.False(DirNameSanitizer.IsReservedName(notReserved));
    }
}
