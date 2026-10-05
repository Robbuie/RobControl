namespace RobControl.Core.History;

/// <summary>
/// Line diff, Myers' O((N+M)D) algorithm ("An O(ND) Difference Algorithm and Its Variations",
/// 1986), with the common prefix and suffix trimmed first.
///
/// <para>The trim matters more than the algorithm here: two backups of the same robot a day apart
/// are usually identical except for a handful of lines, so after trimming there is very little
/// left to diff. The <see cref="MaxEditDistance"/> cap is for the other case - a <c>.VA</c> where
/// half the values moved - where an exact minimal diff would cost memory for no benefit: past the
/// cap the remaining middle is reported as one removed block and one added block, which is still
/// correct, just not minimal.</para>
/// </summary>
public static class LineDiff
{
    public const int MaxEditDistance = 2000;

    public static IReadOnlyList<DiffLine> Compute(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);

        int prefix = 0;
        while (prefix < oldLines.Count && prefix < newLines.Count && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldLines.Count - prefix && suffix < newLines.Count - prefix
            && oldLines[oldLines.Count - 1 - suffix] == newLines[newLines.Count - 1 - suffix])
        {
            suffix++;
        }

        var result = new List<DiffLine>(Math.Max(oldLines.Count, newLines.Count) + 16);
        for (int i = 0; i < prefix; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Same, i + 1, i + 1, oldLines[i]));
        }

        int oldMid = oldLines.Count - prefix - suffix;
        int newMid = newLines.Count - prefix - suffix;
        Middle(oldLines, newLines, prefix, oldMid, prefix, newMid, result);

        for (int i = 0; i < suffix; i++)
        {
            int o = oldLines.Count - suffix + i;
            int n = newLines.Count - suffix + i;
            result.Add(new DiffLine(DiffLineKind.Same, o + 1, n + 1, oldLines[o]));
        }

        return result;
    }

    /// <summary>Groups changes into hunks with <paramref name="context"/> unchanged lines either side.</summary>
    public static IReadOnlyList<DiffHunk> Hunks(IReadOnlyList<DiffLine> lines, int context = 3)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var hunks = new List<DiffHunk>();
        int i = 0;

        while (i < lines.Count)
        {
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Same)
            {
                i++;
            }

            if (i >= lines.Count)
            {
                break;
            }

            int start = Math.Max(0, i - context);
            int end = i;
            int sameRun = 0;

            while (end < lines.Count)
            {
                if (lines[end].Kind == DiffLineKind.Same)
                {
                    sameRun++;
                    if (sameRun > context * 2)
                    {
                        break;
                    }
                }
                else
                {
                    sameRun = 0;
                }

                end++;
            }

            // Trailing context only: back off the unchanged lines past it.
            int trailingSame = 0;
            for (int k = end - 1; k >= start && lines[k].Kind == DiffLineKind.Same; k--)
            {
                trailingSame++;
            }

            end -= Math.Max(0, trailingSame - context);

            List<DiffLine> slice = [.. lines.Skip(start).Take(end - start)];
            int oldStart = slice.FirstOrDefault(l => l.OldNumber is not null)?.OldNumber ?? 0;
            int newStart = slice.FirstOrDefault(l => l.NewNumber is not null)?.NewNumber ?? 0;
            hunks.Add(new DiffHunk(
                oldStart, slice.Count(l => l.Kind != DiffLineKind.Added),
                newStart, slice.Count(l => l.Kind != DiffLineKind.Removed),
                slice));
            i = end;
        }

        return hunks;
    }

    private static void Middle(
        IReadOnlyList<string> a, IReadOnlyList<string> b, int aStart, int n, int bStart, int m, List<DiffLine> result)
    {
        if (n == 0 && m == 0)
        {
            return;
        }

        List<int[]>? trace = Trace(a, b, aStart, n, bStart, m);
        if (trace is null)
        {
            for (int i = 0; i < n; i++)
            {
                result.Add(new DiffLine(DiffLineKind.Removed, aStart + i + 1, null, a[aStart + i]));
            }

            for (int j = 0; j < m; j++)
            {
                result.Add(new DiffLine(DiffLineKind.Added, null, bStart + j + 1, b[bStart + j]));
            }

            return;
        }

        // Walk the trace backwards to recover the path, then emit it forwards.
        var path = new List<DiffLine>(n + m);
        int x = n;
        int y = m;

        for (int d = trace.Count - 1; d >= 0 && (x > 0 || y > 0); d--)
        {
            int[] v = trace[d];
            int k = x - y;
            int prevK = k == -d || (k != d && At(v, d, k - 1) < At(v, d, k + 1)) ? k + 1 : k - 1;
            int prevX = d == 0 ? 0 : At(v, d, prevK);
            int prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                path.Add(new DiffLine(DiffLineKind.Same, aStart + x + 1, bStart + y + 1, a[aStart + x]));
            }

            if (d > 0)
            {
                if (x == prevX)
                {
                    y--;
                    path.Add(new DiffLine(DiffLineKind.Added, null, bStart + y + 1, b[bStart + y]));
                }
                else
                {
                    x--;
                    path.Add(new DiffLine(DiffLineKind.Removed, aStart + x + 1, null, a[aStart + x]));
                }
            }
        }

        path.Reverse();
        result.AddRange(path);
    }

    /// <summary>
    /// The forward pass. <c>trace[d]</c> is the slice of the V array for diagonals -d-1..d+1
    /// <em>before</em> step d was applied, which is all the backward walk reads - storing whole
    /// arrays would cost (N+M) per step, and a .VA file can be tens of thousands of lines. Null when
    /// the edit distance exceeds <see cref="MaxEditDistance"/>.
    /// </summary>
    private static List<int[]>? Trace(IReadOnlyList<string> a, IReadOnlyList<string> b, int aStart, int n, int bStart, int m)
    {
        int max = n + m;
        // One spare slot each side so the slice for diagonals -d-1..d+1 is always in range.
        int offset = max + 1;
        int[] v = new int[(2 * max) + 4];
        var trace = new List<int[]>();

        for (int d = 0; d <= Math.Min(max, MaxEditDistance); d++)
        {
            trace.Add(v.AsSpan(offset - d - 1, (2 * d) + 3).ToArray());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                int y = x - k;

                while (x < n && y < m && a[aStart + x] == b[bStart + y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    return trace;
                }
            }
        }

        return null;
    }

    /// <summary>Reads diagonal <paramref name="k"/> out of a trace slice taken at step <paramref name="d"/>.</summary>
    private static int At(int[] slice, int d, int k)
    {
        int index = k + d + 1;
        return index >= 0 && index < slice.Length ? slice[index] : 0;
    }
}
