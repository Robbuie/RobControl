namespace RobControl.Core.Help;

/// <summary>
/// One block of a document. Which properties mean anything depends on <see cref="Kind"/>:
/// <see cref="Level"/> for a heading (1-6) or a list item's number, <see cref="Inlines"/> for
/// headings, paragraphs and list items, <see cref="Rows"/> for a table, <see cref="Text"/> for code.
/// </summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind)
{
    public int Level { get; init; }

    public IReadOnlyList<MarkdownInline> Inlines { get; init; } = [];

    /// <summary>Table rows, each a list of cells. The first is the header when <see cref="HasHeader"/>.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows { get; init; } = [];

    public bool HasHeader { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>The plain text of <see cref="Inlines"/> - for a heading, what a link to it is made from.</summary>
    public string PlainText => string.Concat(Inlines.Select(i => i.Text));
}
