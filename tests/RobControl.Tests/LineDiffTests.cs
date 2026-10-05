using RobControl.Core.History;
using Xunit;

namespace RobControl.Tests;

public class LineDiffTests
{
    [Fact]
    public void IdenticalInputsAreAllSame()
    {
        string[] a = ["a", "b", "c"];
        Assert.All(LineDiff.Compute(a, a), l => Assert.Equal(DiffLineKind.Same, l.Kind));
    }

    [Fact]
    public void OneChangedLineIsOneRemoveAndOneAdd()
    {
        IReadOnlyList<DiffLine> diff = LineDiff.Compute(["a", "b", "c"], ["a", "B", "c"]);
        Assert.Equal(4, diff.Count);
        Assert.Single(diff, l => l.Kind == DiffLineKind.Removed && l.Text == "b" && l.OldNumber == 2);
        Assert.Single(diff, l => l.Kind == DiffLineKind.Added && l.Text == "B" && l.NewNumber == 2);
    }

    [Fact]
    public void EmptyToSomethingIsAllAdded()
    {
        IReadOnlyList<DiffLine> diff = LineDiff.Compute([], ["x", "y"]);
        Assert.All(diff, l => Assert.Equal(DiffLineKind.Added, l.Kind));
    }

    [Fact]
    public void RandomEditsAlwaysReconstructBothSides()
    {
        var random = new Random(1234);
        for (int round = 0; round < 300; round++)
        {
            List<string> a = [.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => ((char)('a' + random.Next(5))).ToString())];
            List<string> b = [.. a];
            for (int e = random.Next(0, 10); e > 0; e--)
            {
                int op = random.Next(3);
                if (op == 0 || b.Count == 0)
                {
                    b.Insert(random.Next(b.Count + 1), ((char)('a' + random.Next(5))).ToString());
                }
                else if (op == 1)
                {
                    b.RemoveAt(random.Next(b.Count));
                }
                else
                {
                    b[random.Next(b.Count)] = "z";
                }
            }

            IReadOnlyList<DiffLine> diff = LineDiff.Compute(a, b);
            Assert.Equal(a, diff.Where(l => l.Kind != DiffLineKind.Added).Select(l => l.Text).ToList());
            Assert.Equal(b, diff.Where(l => l.Kind != DiffLineKind.Removed).Select(l => l.Text).ToList());
            Assert.Equal(Enumerable.Range(1, a.Count).ToList(), diff.Where(l => l.OldNumber is not null).Select(l => l.OldNumber!.Value).ToList());
            Assert.Equal(Enumerable.Range(1, b.Count).ToList(), diff.Where(l => l.NewNumber is not null).Select(l => l.NewNumber!.Value).ToList());
        }
    }

    [Fact]
    public void HunksKeepContextAndSplitFarApartChanges()
    {
        List<string> a = [.. Enumerable.Range(1, 40).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        List<string> b = [.. a];
        b[2] = "changed early";
        b[35] = "changed late";
        IReadOnlyList<DiffHunk> hunks = LineDiff.Hunks(LineDiff.Compute(a, b));
        Assert.Equal(2, hunks.Count);
        Assert.Equal(1, hunks[0].OldStart);
        Assert.Contains(hunks[1].Lines, l => l.Text == "changed late");
        Assert.True(hunks[1].Lines.Count <= 3 + 2 + 3);
    }
}
