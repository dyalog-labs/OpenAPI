using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Tests.Utils;

public class PathConverterTests
{
    [Fact]
    public void ToDyalogPath_NoParams_ReturnsSingleQuotedSegment()
    {
        Assert.Equal("'/users'", PathConverter.ToDyalogPath("/users"));
    }

    [Fact]
    public void ToDyalogPath_SingleParam_ReturnsConcatenation()
    {
        Assert.Equal("'/users/',(c.∆.HttpCommand.UrlEncode⍕argsNs.userId)", PathConverter.ToDyalogPath("/users/{userId}"));
    }

    [Fact]
    public void ToDyalogPath_MultipleParams_ReturnsFullConcatenation()
    {
        var result = PathConverter.ToDyalogPath("/users/{userId}/posts/{postId}");
        Assert.Equal("'/users/',(c.∆.HttpCommand.UrlEncode⍕argsNs.userId),'/posts/',(c.∆.HttpCommand.UrlEncode⍕argsNs.postId)", result);
    }

    [Fact]
    public void ToDyalogPath_EmptyString_ReturnsEmptyQuoted()
    {
        Assert.Equal("''", PathConverter.ToDyalogPath(""));
    }

    [Fact]
    public void ToDyalogPath_HyphenatedParam_EscapesViaToValidAplName()
    {
        // "item-id" contains a hyphen (UCS 45) which is invalid in APL names.
        // ToValidAplName encodes it, so the result must contain the encoded form.
        var result = PathConverter.ToDyalogPath("/items/{item-id}");
        Assert.StartsWith("'/items/',(c.∆.HttpCommand.UrlEncode⍕argsNs.⍙", result);
        Assert.Contains("45", result); // '45' is the UCS code for '-'
    }
}
