namespace ComfyGenReader.Core.Tests;

public class ErrorFormatterTests
{
    [Fact]
    public void Format_PutsMessageBeforeStackAndIncludesInner()
    {
        Exception ex;
        try { try { throw new InvalidOperationException("inner boom"); }
              catch (Exception i) { throw new IOException("outer boom", i); } }
        catch (Exception e) { ex = e; }
        var text = ErrorFormatter.Format(ex);
        Assert.StartsWith("System.IO.IOException: outer boom", text);
        Assert.True(text.IndexOf("at ") > text.IndexOf("outer boom"));
        Assert.Contains("System.InvalidOperationException: inner boom", text);
    }

    [Fact]
    public void Format_UnthrownExceptionHasNoStackSection()
    {
        Assert.Equal("System.InvalidOperationException: x", ErrorFormatter.Format(new InvalidOperationException("x")));
    }
}
