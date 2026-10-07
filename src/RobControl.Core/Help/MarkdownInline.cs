namespace RobControl.Core.Help;

/// <summary>
/// A run of text inside a block. <see cref="Url"/> is set for a link and is exactly what the
/// document said; whether it may be opened is <see cref="MarkdownLite.ResolveLink"/>'s decision.
/// </summary>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text, bool Bold = false, string? Url = null, bool Italic = false);
