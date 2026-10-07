using System.Windows;
using System.Windows.Documents;
using RobControl.Core.Help;

namespace RobControl.App.Views;

/// <summary>
/// Turns <see cref="MarkdownLite"/> blocks into a WPF <see cref="FlowDocument"/>. Colours and fonts
/// are resource references to the theme tokens, so the Read me window follows the theme - light,
/// dark, accent - like everything else, including a theme change while it is open.
/// </summary>
internal static class MarkdownRenderer
{
    public static FlowDocument Render(IReadOnlyList<MarkdownBlock> blocks, Func<string?, Uri?> resolveLink, Action<Uri> openLink)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(resolveLink);
        ArgumentNullException.ThrowIfNull(openLink);

        var document = new FlowDocument
        {
            PagePadding = new Thickness(28, 18, 28, 28),
            FontSize = 13.5,
            LineHeight = 20,
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(TextElement.FontFamilyProperty, "font");
        document.SetResourceReference(TextElement.ForegroundProperty, "txt-0");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "bg-1");

        List? list = null;
        MarkdownBlockKind listKind = MarkdownBlockKind.Paragraph;

        foreach (MarkdownBlock block in blocks)
        {
            bool isItem = block.Kind is MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered;
            if (!isItem || block.Kind != listKind)
            {
                list = null;
            }

            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                    Paragraph heading = Inlines(block.Inlines, resolveLink, openLink);
                    heading.FontSize = block.Level switch { 1 => 24, 2 => 18, 3 => 15, _ => 13.5 };
                    heading.FontWeight = FontWeights.SemiBold;
                    heading.Margin = new Thickness(0, block.Level == 1 ? 0 : 18, 0, 6);
                    if (block.Level <= 2)
                    {
                        heading.BorderThickness = new Thickness(0, 0, 0, 1);
                        heading.Padding = new Thickness(0, 0, 0, 4);
                        heading.SetResourceReference(Block.BorderBrushProperty, "line");
                    }

                    document.Blocks.Add(heading);
                    break;

                case MarkdownBlockKind.Paragraph:
                    Paragraph paragraph = Inlines(block.Inlines, resolveLink, openLink);
                    paragraph.Margin = new Thickness(0, 0, 0, 10);
                    document.Blocks.Add(paragraph);
                    break;

                case MarkdownBlockKind.Bullet:
                case MarkdownBlockKind.Numbered:
                    if (list is null)
                    {
                        list = new List
                        {
                            MarkerStyle = block.Kind == MarkdownBlockKind.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(0, 0, 0, 10),
                            Padding = new Thickness(22, 0, 0, 0),
                        };
                        if (block.Kind == MarkdownBlockKind.Numbered && block.Level > 0)
                        {
                            list.StartIndex = block.Level;
                        }

                        listKind = block.Kind;
                        document.Blocks.Add(list);
                    }

                    Paragraph item = Inlines(block.Inlines, resolveLink, openLink);
                    item.Margin = new Thickness(0, 0, 0, 4);
                    list.ListItems.Add(new ListItem(item));
                    break;

                case MarkdownBlockKind.Table:
                    document.Blocks.Add(TableOf(block, resolveLink, openLink));
                    break;

                case MarkdownBlockKind.Code:
                    var code = new Paragraph(new Run(block.Text))
                    {
                        FontSize = 12.5,
                        LineHeight = 18,
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 12),
                    };
                    code.SetResourceReference(TextElement.FontFamilyProperty, "mono");
                    code.SetResourceReference(TextElement.BackgroundProperty, "bg-3");
                    document.Blocks.Add(code);
                    break;

                case MarkdownBlockKind.Rule:
                    var rule = new Paragraph { Margin = new Thickness(0, 6, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 };
                    rule.SetResourceReference(Block.BorderBrushProperty, "line");
                    document.Blocks.Add(rule);
                    break;
            }
        }

        return document;
    }

    private static Table TableOf(MarkdownBlock block, Func<string?, Uri?> resolveLink, Action<Uri> openLink)
    {
        int columns = block.Rows.Count == 0 ? 1 : block.Rows.Max(r => r.Count);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };
        for (int c = 0; c < columns; c++)
        {
            // A two-column table is a list of terms and what they mean: keep the term narrow.
            table.Columns.Add(new TableColumn { Width = new GridLength(columns == 2 && c == 0 ? 1 : 3, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (int r = 0; r < block.Rows.Count; r++)
        {
            var row = new TableRow();
            bool header = block.HasHeader && r == 0;
            foreach (IReadOnlyList<MarkdownInline> cellInlines in block.Rows[r])
            {
                Paragraph content = Inlines(cellInlines, resolveLink, openLink);
                content.Margin = new Thickness(0);
                if (header)
                {
                    content.FontWeight = FontWeights.SemiBold;
                }

                var cell = new TableCell(content) { Padding = new Thickness(6, 5, 12, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "line");
                if (header)
                {
                    cell.SetResourceReference(TextElement.BackgroundProperty, "bg-2");
                }

                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        return table;
    }

    private static Paragraph Inlines(IReadOnlyList<MarkdownInline> inlines, Func<string?, Uri?> resolveLink, Action<Uri> openLink)
    {
        var paragraph = new Paragraph();
        foreach (MarkdownInline part in inlines)
        {
            Inline inline;
            switch (part.Kind)
            {
                case MarkdownInlineKind.Code:
                    var code = new Run(part.Text) { FontSize = 12.5 };
                    code.SetResourceReference(TextElement.FontFamilyProperty, "mono");
                    code.SetResourceReference(TextElement.BackgroundProperty, "bg-3");
                    inline = code;
                    break;

                case MarkdownInlineKind.Link when resolveLink(part.Url) is { } uri:
                    var link = new Hyperlink(new Run(part.Text)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
                    link.SetResourceReference(TextElement.ForegroundProperty, "accent");
                    link.RequestNavigate += (_, e) =>
                    {
                        openLink(e.Uri);
                        e.Handled = true;
                    };
                    inline = link;
                    break;

                default:
                    // Plain text - and a link that may not be opened, shown as its words only.
                    inline = new Run(part.Text);
                    break;
            }

            if (part.Bold)
            {
                inline.FontWeight = FontWeights.SemiBold;
            }

            if (part.Italic)
            {
                inline.FontStyle = FontStyles.Italic;
            }

            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }
}
