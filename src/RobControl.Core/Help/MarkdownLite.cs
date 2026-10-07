using System.Globalization;
using System.Text;

namespace RobControl.Core.Help;

/// <summary>
/// Just enough Markdown to show the README and the changelog inside the app: headings, paragraphs,
/// bullet and numbered lists, pipe tables, fenced code, rules, and inline bold, code and links.
///
/// <para><b>Why not a package:</b> the documents it reads are ours, written in a small subset, and a
/// Markdown library is one more thing to justify to plant IT for a Help window. Anything it does not
/// understand comes out as plain text rather than failing - the worst case is a line that looks like
/// source, never a window that will not open.</para>
///
/// <para>Images are dropped (the in-app window has the icon already), and HTML comments are dropped
/// - which is how the README marks where the part for users ends: <see cref="EndOfAppSection"/>.</para>
/// </summary>
public static class MarkdownLite
{
    /// <summary>A line containing this ends what the app shows; the rest of the README is for people building it.</summary>
    public const string EndOfAppSection = "<!-- end of in-app readme -->";

    public static IReadOnlyList<MarkdownBlock> Parse(string markdown, bool stopAtAppSectionEnd = false)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var blocks = new List<MarkdownBlock>();
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph) { Inlines = ParseInline(paragraph.ToString()) });
                paragraph.Clear();
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (stopAtAppSectionEnd && trimmed.Contains(EndOfAppSection, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                // Skip the comment, however many lines it runs to.
                while (i < lines.Length && !lines[i].Contains("-->", StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code) { Text = string.Join("\n", code) });
                continue;
            }

            if (HeadingLevel(trimmed) is int level and > 0)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading)
                {
                    Level = level,
                    Inlines = ParseInline(trimmed[level..].Trim().TrimEnd('#').TrimEnd()),
                });
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule));
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                FlushParagraph();
                var tableLines = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('|'); i++)
                {
                    tableLines.Add(lines[i].Trim());
                }

                i--;
                blocks.Add(ParseTable(tableLines));
                continue;
            }

            if (ListItem(trimmed, out MarkdownBlockKind kind, out int number, out string first))
            {
                FlushParagraph();
                var item = new StringBuilder(first);

                // Continuation: indented lines that are not themselves a new item.
                while (i + 1 < lines.Length && lines[i + 1].Length > 0 && char.IsWhiteSpace(lines[i + 1][0])
                    && lines[i + 1].Trim().Length > 0 && !ListItem(lines[i + 1].Trim(), out _, out _, out _))
                {
                    item.Append(' ').Append(lines[++i].Trim());
                }

                blocks.Add(new MarkdownBlock(kind) { Level = number, Inlines = ParseInline(item.ToString()) });
                continue;
            }

            if (trimmed.StartsWith("![", StringComparison.Ordinal) && trimmed.EndsWith(')'))
            {
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>
    /// Bold, code and links in one line of text. <c>**</c> toggles bold for whatever follows,
    /// including code and links inside it. An image is dropped. Anything unmatched is text.
    /// </summary>
    public static IReadOnlyList<MarkdownInline> ParseInline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inlines = new List<MarkdownInline>();
        var run = new StringBuilder();
        bool bold = false;
        bool italic = false;

        void FlushRun()
        {
            if (run.Length > 0)
            {
                inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, run.ToString(), bold, Italic: italic));
                run.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                FlushRun();
                bold = !bold;
                i++;
                continue;
            }

            // *italic*: an opener must be followed by a non-space and closed later in the line, so
            // "R[1-10] * 2" stays text.
            if (c == '*' && (italic || (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && text.IndexOf('*', i + 1) > 0)))
            {
                FlushRun();
                italic = !italic;
                continue;
            }

            if (c == '`' && text.IndexOf('`', i + 1) is int close and > 0)
            {
                FlushRun();
                inlines.Add(new MarkdownInline(MarkdownInlineKind.Code, text[(i + 1)..close], bold, Italic: italic));
                i = close;
                continue;
            }

            bool image = c == '!' && i + 1 < text.Length && text[i + 1] == '[';
            if ((c == '[' || image) && TryLink(text, image ? i + 1 : i, out string label, out string url, out int end))
            {
                FlushRun();
                if (!image)
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Link, label, bold, url, italic));
                }

                i = end;
                continue;
            }

            if (c == '\\' && i + 1 < text.Length && "\\`*_[]()#|!".Contains(text[i + 1], StringComparison.Ordinal))
            {
                run.Append(text[++i]);
                continue;
            }

            run.Append(c);
        }

        FlushRun();
        return inlines;
    }

    /// <summary>
    /// The address a link should open, or null if it should not be clickable.
    ///
    /// <para>Only http and https leave the app. A relative link (<c>CLAUDE.md#safety</c>) means a file
    /// in the repository, so it becomes that file on GitHub under <paramref name="repositoryBase"/>; an
    /// anchor on its own (<c>#sites</c>) is a heading in <paramref name="document"/>. Anything else -
    /// <c>file:</c>, <c>javascript:</c>, a UNC path - is shown as text and never opened.</para>
    /// </summary>
    public static Uri? ResolveLink(string? url, string repositoryBase, string document = "README.md")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryBase);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string target = url.Trim();
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? absolute))
        {
            return absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp ? absolute : null;
        }

        if (target.StartsWith("//", StringComparison.Ordinal) || target.StartsWith('\\') || target.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        if (target.StartsWith('#'))
        {
            target = document + target;
        }

        string root = repositoryBase.EndsWith('/') ? repositoryBase : repositoryBase + "/";
        return Uri.TryCreate(new Uri(root), target.TrimStart('/'), out Uri? resolved)
            && resolved.Scheme == Uri.UriSchemeHttps
            && resolved.AbsoluteUri.StartsWith(root, StringComparison.Ordinal)
            ? resolved
            : null;
    }

    private static int HeadingLevel(string trimmed)
    {
        int level = 0;
        while (level < trimmed.Length && level < 7 && trimmed[level] == '#')
        {
            level++;
        }

        return level is >= 1 and <= 6 && level < trimmed.Length && trimmed[level] == ' ' ? level : 0;
    }

    private static bool ListItem(string trimmed, out MarkdownBlockKind kind, out int number, out string rest)
    {
        kind = MarkdownBlockKind.Bullet;
        number = 0;
        rest = string.Empty;

        if (trimmed.Length > 2 && (trimmed[0] is '-' or '*' or '+') && trimmed[1] == ' ')
        {
            rest = trimmed[2..].Trim();
            return true;
        }

        int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dot is > 0 and <= 3 && int.TryParse(trimmed[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            kind = MarkdownBlockKind.Numbered;
            rest = trimmed[(dot + 2)..].Trim();
            return true;
        }

        return false;
    }

    private static MarkdownBlock ParseTable(List<string> lines)
    {
        var rows = new List<IReadOnlyList<IReadOnlyList<MarkdownInline>>>();
        bool hasHeader = false;

        for (int r = 0; r < lines.Count; r++)
        {
            List<string> cells = SplitRow(lines[r]);
            if (cells.All(IsSeparatorCell))
            {
                // The line under the header. A header with nothing in it (README's "| | |") is not one.
                hasHeader = r == 1 && rows.Count == 1 && rows[0].Any(cell => cell.Count > 0);
                if (r == 1 && !hasHeader && rows.Count == 1)
                {
                    rows.Clear();
                }

                continue;
            }

            rows.Add([.. cells.Select(ParseInline)]);
        }

        return new MarkdownBlock(MarkdownBlockKind.Table) { Rows = rows, HasHeader = hasHeader };
    }

    private static List<string> SplitRow(string line)
    {
        string inner = line.Trim();
        inner = inner.StartsWith('|') ? inner[1..] : inner;
        inner = inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal) ? inner[..^1] : inner;

        var cells = new List<string>();
        var cell = new StringBuilder();
        bool inCode = false;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\\' && i + 1 < inner.Length && inner[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (c == '`')
            {
                inCode = !inCode;
                cell.Append(c);
            }
            else if (c == '|' && !inCode)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static bool IsSeparatorCell(string cell) =>
        cell.Length > 0 && cell.Trim(':').Length > 0 && cell.Trim(':').All(c => c == '-');

    private static bool TryLink(string text, int open, out string label, out string url, out int end)
    {
        label = url = string.Empty;
        end = open;
        int closeLabel = text.IndexOf(']', open + 1);
        if (closeLabel < 0 || closeLabel + 1 >= text.Length || text[closeLabel + 1] != '(')
        {
            return false;
        }

        int closeUrl = text.IndexOf(')', closeLabel + 2);
        if (closeUrl < 0)
        {
            return false;
        }

        label = text[(open + 1)..closeLabel];
        url = text[(closeLabel + 2)..closeUrl].Trim();
        end = closeUrl;
        return true;
    }
}
