using System.Globalization;

namespace RobControl.Core.Trending;

/// <summary>
/// One I/O point as the controller listed it: <c>DIN[ 1] OFF  *IMSTP</c> becomes type <c>DI</c>,
/// index 1, value 0, comment "*IMSTP".
/// </summary>
/// <param name="Type">Normalised to the pendant's name: DI, DO, GI, F, CSI...</param>
/// <param name="Index">The number in brackets.</param>
/// <param name="Value">1/0 for a digital point, the number for a group or analog one.</param>
/// <param name="Comment">The I/O comment, or empty.</param>
/// <param name="Listed">The type as the page wrote it (DIN, DOUT, FLG), for the person who goes looking in the file.</param>
public sealed record IoPoint(string Type, int Index, double Value, string Comment, string Listed)
{
    /// <summary><c>DI[1]</c> - the same key the trend recorder stores samples under.</summary>
    public string Key => $"{Type}[{Index.ToString(CultureInfo.InvariantCulture)}]";

    public bool IsDigital => IoStateParser.IsDigital(Type);

    public bool IsOn => IsDigital && Value != 0;

    /// <summary>"ON"/"OFF" for a digital point, the number otherwise.</summary>
    public string State => IsDigital ? (Value != 0 ? "ON" : "OFF") : Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Digital", "UOP", "Safe I/O (DCS / CIP Safety)"... - what the pendant's I/O menu groups it under.</summary>
    public string Category => IoStateParser.CategoryOf(Type);

    /// <summary>"Input", "Output" or "Internal" (flags, markers, safe internal relays).</summary>
    public string Direction => IoStateParser.DirectionOf(Type);
}
