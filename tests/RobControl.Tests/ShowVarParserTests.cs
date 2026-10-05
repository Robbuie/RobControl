using RobControl.Core.Controllers;
using Xunit;

namespace RobControl.Tests;

public class ShowVarParserTests
{
    [Theory]
    [InlineData("$VERSION  Storage: SHADOW  Access: RO  : STRING[37] = 'V9.40P/23'", "V9.40P/23")]
    [InlineData("$VERSION = 'V9.40P/23'", "V9.40P/23")]
    [InlineData("[*NUMREG*]$NUMREG[2]  Storage: CMOS  Access: RW  : NUMREG_T = 1287  'Weld count'", "1287  'Weld count'")]
    [InlineData("$SCR.$NUM_GROUP  : INTEGER = 1", "1")]
    public void ValueIsAfterTheLastEquals(string output, string expected) =>
        Assert.Equal(expected, ShowVarParser.Value(output));

    [Fact]
    public void NoEqualsIsNoValue() => Assert.Null(ShowVarParser.Value("VARS-023 Variable not found"));
}
