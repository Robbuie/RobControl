using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

public class SignalAddressTests
{
    [Theory]
    [InlineData("R[5]", SignalKind.NumericRegister, "R[5]")]
    [InlineData("r[ 12 ]", SignalKind.NumericRegister, "R[12]")]
    [InlineData("NUMREG[3]", SignalKind.NumericRegister, "R[3]")]
    [InlineData("DI[1]", SignalKind.Io, "DI[1]")]
    [InlineData("do[17]", SignalKind.Io, "DO[17]")]
    [InlineData("GI[2]", SignalKind.Io, "GI[2]")]
    [InlineData("UO[4]", SignalKind.Io, "UO[4]")]
    [InlineData("$scr.$num_group", SignalKind.SystemVariable, "$SCR.$NUM_GROUP")]
    [InlineData("$TIMER[1].$TIMER_VAL", SignalKind.SystemVariable, "$TIMER[1].$TIMER_VAL")]
    [InlineData("[MYPROG]COUNT", SignalKind.SystemVariable, "[MYPROG]COUNT")]
    public void PendantNotationIsUnderstood(string text, SignalKind kind, string normalised)
    {
        SignalAddress a = SignalAddress.Parse(text);
        Assert.Equal(kind, a.Kind);
        Assert.Equal(normalised, a.Text);
    }

    [Theory]
    [InlineData("PR[1]")]
    [InlineData("SR[1]")]
    [InlineData("R[0]")]
    [InlineData("XX[1]")]
    [InlineData("hello")]
    [InlineData("$VERSION; RUN MAIN")]
    [InlineData("$A RUN")]
    [InlineData("")]
    public void AnythingElseIsRefusedWithAReason(string text)
    {
        Assert.False(SignalAddress.TryParse(text, out _, out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void DigitalIsOnlyForBitIo()
    {
        Assert.True(SignalAddress.Parse("DI[1]").IsDigital);
        Assert.False(SignalAddress.Parse("GI[1]").IsDigital);
        Assert.False(SignalAddress.Parse("R[1]").IsDigital);
    }

    [Fact]
    public void ListsTakeRangesAndSeparators()
    {
        IReadOnlyList<SignalAddress> list = SignalAddress.ParseList("R[1-3], DI[1..2]; $SCR.$NUM_GROUP R[2]", out IReadOnlyList<string> problems);
        Assert.Empty(problems);
        Assert.Equal(["R[1]", "R[2]", "R[3]", "DI[1]", "DI[2]", "$SCR.$NUM_GROUP"], list.Select(a => a.Text).ToArray());
    }

    [Fact]
    public void OneBadEntryRejectsTheWholeList()
    {
        IReadOnlyList<SignalAddress> list = SignalAddress.ParseList("R[1], PR[2], DI[3]", out IReadOnlyList<string> problems);
        Assert.Empty(list);
        Assert.Single(problems);
    }

    [Fact]
    public void HugeRangesAreRefused()
    {
        SignalAddress.ParseList("R[1-100000]", out IReadOnlyList<string> problems);
        Assert.NotEmpty(problems);
    }
}
