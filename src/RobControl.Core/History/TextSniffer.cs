namespace RobControl.Core.History;

/// <summary>
/// Whether a controller file is text worth diffing line by line.
///
/// <para>By content, not only by extension. The listings a controller generates on <c>md:</c>
/// (<c>.LS</c>, <c>.VA</c>, <c>.DG</c>) are text; <c>.TP</c>, <c>.SV</c>, <c>.VR</c> are binary; but
/// extensions vary across applications and versions, so the extension is a hint and a look at the
/// first few kilobytes decides.</para>
/// </summary>
public static class TextSniffer
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".LS", ".VA", ".DG", ".TXT", ".CSV", ".XML", ".HTM", ".HTML", ".STM", ".CM", ".KL", ".DT",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".TP", ".SV", ".VR", ".PC", ".IO", ".ZIP", ".IMG", ".MN", ".DF",
    };

    public static bool LooksLikeText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string ext = Path.GetExtension(path);
        if (BinaryExtensions.Contains(ext))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> head = stackalloc byte[8192];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return LooksLikeText(head[..read]) || (read == 0 && TextExtensions.Contains(ext));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return false;
        }

        int odd = 0;
        foreach (byte b in head)
        {
            if (b == 0)
            {
                return false;
            }

            if (b < 32 && b is not (byte)'\r' and not (byte)'\n' and not (byte)'\t' and not 0x0C and not 0x1A)
            {
                odd++;
            }
        }

        return odd * 100 < head.Length;
    }
}
