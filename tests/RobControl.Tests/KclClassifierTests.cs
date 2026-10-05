using RobControl.Core.Kcl;
using Xunit;

namespace RobControl.Tests;

public class KclClassifierTests
{
    [Theory]
    [InlineData("SHOW VAR $VERSION")]
    [InlineData("show var $numreg[2]")]
    [InlineData("SH VAR $SCR.$NUM_GROUP")]
    [InlineData("SHOW CLOCK")]
    [InlineData("DIRECTORY MD:")]
    [InlineData("dir fr:*.tp")]
    [InlineData("TYPE MD:SUMMARY.DG")]
    [InlineData("HELP")]
    public void ReadsAreAllowed(string command) =>
        Assert.Equal(KclCommandClass.Read, KclClassifier.Classify(command).Class);

    [Theory]
    [InlineData("SET VAR $NUMREG[2] = 0")]
    [InlineData("set var [MYPROG]COUNT = 5")]
    public void SetVarIsAWrite(string command) =>
        Assert.Equal(KclCommandClass.Write, KclClassifier.Classify(command).Class);

    [Theory]
    [InlineData("RUN MAIN")]
    [InlineData("CONTINUE")]
    [InlineData("ABORT ALL")]
    [InlineData("PAUSE")]
    [InlineData("HOLD")]
    [InlineData("RESET")]
    [InlineData("DELETE FILE MD:MAIN.TP")]
    [InlineData("CLEAR ALL")]
    [InlineData("LOAD PROG MAIN")]
    [InlineData("SAVE VARS")]
    [InlineData("SET PORT DOUT[1] = ON")]
    [InlineData("FORMAT MC:")]
    [InlineData("S")]
    [InlineData("SHO")] // not a listed abbreviation - unknown is Never
    [InlineData("SET")]
    [InlineData("")]
    [InlineData("   ")]
    public void EverythingElseIsNever(string command) =>
        Assert.Equal(KclCommandClass.Never, KclClassifier.Classify(command).Class);

    [Theory]
    [InlineData("SET VAR $DCSS_CPC[1].$ENABLE = 0")]
    [InlineData("SET VAR [*SYSTEM*]$DCSS_PSET[1].$X = 0")]
    [InlineData("SET VAR $MASTER_ENB = 1")]
    [InlineData("SET VAR $DMR_GRP[1].$MASTER_DONE = TRUE")]
    [InlineData("SET VAR $PASSWORD.$LEVEL = 0")]
    public void SafetyAndMasteringVariablesAreNeverEvenAsWrites(string command)
    {
        KclClassification result = KclClassifier.Classify(command);
        Assert.Equal(KclCommandClass.Never, result.Class);
        Assert.Contains("never writes", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SHOW VAR $VERSION\nRUN MAIN")]
    [InlineData("SHOW VAR $VERSION\rABORT")]
    [InlineData("SHOW VAR $VERSION; RUN MAIN")]
    [InlineData("SHOW VAR $VERSION | RUN")]
    [InlineData("SHOW VAR $VERSION > MD:X.TXT")]
    public void ASecondCommandCannotRideAlongWithARead(string command) =>
        Assert.Equal(KclCommandClass.Never, KclClassifier.Classify(command).Class);

    [Fact]
    public void ExtraSpacesAreCollapsed() =>
        Assert.Equal("SHOW VAR $VERSION", KclClassifier.Classify("  SHOW   VAR  $VERSION ").Normalised);
}
