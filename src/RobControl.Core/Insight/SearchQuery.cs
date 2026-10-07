namespace RobControl.Core.Insight;

/// <summary>
/// What to look for. Plain text by default; a register or port (<c>R[45]</c>, <c>DO[120]</c>) is
/// matched however the line writes it; <see cref="IsRegex"/> takes a .NET regular expression.
/// </summary>
public sealed record SearchQuery(string Text, SearchScope Scope = SearchScope.Programs, bool IsRegex = false, bool CaseSensitive = false);
