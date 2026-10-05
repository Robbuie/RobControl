using RobControl.Core.Transports.Ftp;
using Xunit;

namespace RobControl.Tests;

public class FtpReplyReaderTests
{
    [Fact]
    public void SingleLineReplyCompletesImmediately()
    {
        var reader = new FtpReplyReader();
        FtpReply? reply = reader.Push("230 User logged in [NORM].");
        Assert.NotNull(reply);
        Assert.Equal(230, reply.Code);
        Assert.Equal("User logged in [NORM].", reply.Text);
        Assert.True(reply.IsCompletion);
    }

    [Fact]
    public void MultiLineReplyEndsOnlyAtSameCodeAndSpace()
    {
        var reader = new FtpReplyReader();
        Assert.Null(reader.Push("220-R-30iB Plus FTP server ready."));
        Assert.Null(reader.Push("150 a line inside that looks like another code"));
        Assert.Null(reader.Push("220-still going"));
        FtpReply? reply = reader.Push("220 Ready.");
        Assert.NotNull(reply);
        Assert.Equal(220, reply.Code);
        Assert.Equal(4, reply.Lines.Count);
    }

    [Fact]
    public void BareCodeEndsAMultiLineReply()
    {
        var reader = new FtpReplyReader();
        Assert.Null(reader.Push("211-Features:"));
        Assert.NotNull(reader.Push("211"));
    }

    [Fact]
    public void GarbageIsAProtocolErrorNotAHang()
    {
        var reader = new FtpReplyReader();
        Assert.Throws<FtpProtocolException>(() => reader.Push("HTTP/1.1 400 Bad Request"));
    }

    [Theory]
    [InlineData(150, true, false, false, false)]
    [InlineData(226, false, true, false, false)]
    [InlineData(331, false, false, true, false)]
    [InlineData(550, false, false, false, true)]
    public void ClassesFollowTheFirstDigit(int code, bool preliminary, bool completion, bool intermediate, bool failure)
    {
        var reply = new FtpReply(code, []);
        Assert.Equal(preliminary, reply.IsPreliminary);
        Assert.Equal(completion, reply.IsCompletion);
        Assert.Equal(intermediate, reply.IsIntermediate);
        Assert.Equal(failure, reply.IsFailure);
    }
}
