using RobControl.Core.Help;
using Xunit;

namespace RobControl.Tests;

public sealed class MarkdownLiteTests
{
    private const string Repo = "https://github.com/Robbuie/RobControl/blob/main/";

    [Fact]
    public void Headings_paragraphs_and_lists()
    {
        IReadOnlyList<MarkdownBlock> blocks = MarkdownLite.Parse("""
            # RobControl

            A tool for
            FANUC robots.

            - one
              continued
            - two
            1. first
            2. second
            """);

        Assert.Equal(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("RobControl", blocks[0].PlainText);
        Assert.Equal("A tool for FANUC robots.", blocks[1].PlainText);
        Assert.Equal("one continued", blocks[2].PlainText);
        Assert.Equal(MarkdownBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MarkdownBlockKind.Numbered, blocks[5].Kind);
        Assert.Equal(2, blocks[5].Level);
        Assert.Equal(6, blocks.Count);
    }

    [Fact]
    public void Bold_code_links_and_italics_inline()
    {
        IReadOnlyList<MarkdownInline> inlines = MarkdownLite.ParseInline(
            "**Read-only** by `RETR`, see [NetControl](https://github.com/Robbuie/NetControl) and *Controller gotchas*.");

        Assert.Equal(new MarkdownInline(MarkdownInlineKind.Text, "Read-only", Bold: true), inlines[0]);
        Assert.Equal(MarkdownInlineKind.Code, inlines[2].Kind);
        Assert.Equal("RETR", inlines[2].Text);
        Assert.Equal(MarkdownInlineKind.Link, inlines[4].Kind);
        Assert.Equal("https://github.com/Robbuie/NetControl", inlines[4].Url);
        Assert.Contains(inlines, i => i.Italic && i.Text == "Controller gotchas");
        Assert.DoesNotContain(inlines, i => i.Text.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public void A_lone_asterisk_and_an_image_do_not_become_markup()
    {
        Assert.Equal("R[1] * 2", string.Concat(MarkdownLite.ParseInline("R[1] * 2").Select(i => i.Text)));
        Assert.Empty(MarkdownLite.Parse("![RobControl](assets/icon.png)"));
    }

    [Fact]
    public void A_table_without_a_header_keeps_only_its_rows_and_pipes_in_code_stay_in_the_cell()
    {
        MarkdownBlock table = Assert.Single(MarkdownLite.Parse("""
            | | |
            |---|---|
            | **Back up** | FTP copy of `md:` |
            | **Trend** | `R[1-10]` or `a|b` |
            """));

        Assert.Equal(MarkdownBlockKind.Table, table.Kind);
        Assert.False(table.HasHeader);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Back up", table.Rows[0][0][0].Text);
        Assert.Equal("a|b", table.Rows[1][1][^1].Text);
    }

    [Fact]
    public void A_table_with_a_header_says_so()
    {
        MarkdownBlock table = Assert.Single(MarkdownLite.Parse("| Transport | Port |\n|:--|--:|\n| FTP | 21 |"));

        Assert.True(table.HasHeader);
        Assert.Equal("Transport", table.Rows[0][0][0].Text);
    }

    [Fact]
    public void Code_fences_keep_their_lines_and_comments_vanish()
    {
        IReadOnlyList<MarkdownBlock> blocks = MarkdownLite.Parse("""
            <!-- a note
                 over two lines -->
            ```powershell
            dotnet build
            dotnet test
            ```
            """);

        MarkdownBlock code = Assert.Single(blocks);
        Assert.Equal("dotnet build\ndotnet test", code.Text);
    }

    [Fact]
    public void The_app_section_ends_at_the_marker()
    {
        string doc = "# Use it\n\nText.\n\n" + MarkdownLite.EndOfAppSection + "\n\n## Building\n";

        Assert.Equal(2, MarkdownLite.Parse(doc, stopAtAppSectionEnd: true).Count);
        Assert.Equal(3, MarkdownLite.Parse(doc).Count);
    }

    [Theory]
    [InlineData("https://github.com/Robbuie/NetControl", "https://github.com/Robbuie/NetControl")]
    [InlineData("CLAUDE.md#safety", Repo + "CLAUDE.md#safety")]
    [InlineData("#sites", Repo + "README.md#sites")]
    [InlineData("RELEASING.md", Repo + "RELEASING.md")]
    [InlineData("file:///C:/Windows/notepad.exe", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(@"\\server\share\x.exe", null)]
    [InlineData("../../../evil", null)]
    [InlineData("", null)]
    public void Only_web_links_are_ever_opened(string url, string? expected)
    {
        Assert.Equal(expected, MarkdownLite.ResolveLink(url, Repo)?.AbsoluteUri);
    }

    /// <summary>
    /// The README is what the app's Help > Read me shows. It must keep the marker, and the part
    /// before it must be the part for people using the app - not the build instructions.
    /// </summary>
    [Fact]
    public void The_real_README_has_an_in_app_section_for_users()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        Assert.Contains(MarkdownLite.EndOfAppSection, readme, StringComparison.Ordinal);
        List<string> headings = [.. MarkdownLite.Parse(readme, stopAtAppSectionEnd: true)
            .Where(b => b.Kind == MarkdownBlockKind.Heading).Select(b => b.PlainText)];
        Assert.Contains("Sites", headings);
        Assert.DoesNotContain("Building", headings);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RobControl.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("RobControl.sln not found above " + AppContext.BaseDirectory);
    }
}
