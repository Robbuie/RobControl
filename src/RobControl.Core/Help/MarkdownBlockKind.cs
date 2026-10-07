namespace RobControl.Core.Help;

/// <summary>The kinds of block <see cref="MarkdownLite"/> understands - what the README actually uses.</summary>
public enum MarkdownBlockKind
{
    Heading,
    Paragraph,
    Bullet,
    Numbered,
    Table,
    Code,
    Rule,
}
