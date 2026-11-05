#if !IS_WINDOWS
using Skua.App.Avalonia.Flash;
using Xunit;

namespace Skua.Core.Tests;

public sealed class FlashBridgeJsonTests
{
    [Fact]
    public void BuildCallJsonSerializesPrimitives()
    {
        string json = FlashUtil.BuildCallJson(7, "getGameObject", new object[] { "world.myAvatar", 3, true, null! });

        Assert.Equal("""{"id":7,"fn":"getGameObject","args":["world.myAvatar",3,true,null]}""", json);
    }

    [Fact]
    public void BuildCallJsonEscapesStrings()
    {
        string json = FlashUtil.BuildCallJson(1, "sendClientPacket", new object[] { "say \"hi\"\n" });

        Assert.Equal("""{"id":1,"fn":"sendClientPacket","args":["say \"hi\"\n"]}""", json);
    }

    [Fact]
    public void NormalizeRuffleNumbersDropsTrailingPointZeroOutsideStrings()
    {
        Assert.Equal("{\"id\":897326396}", FlashUtil.NormalizeRuffleNumbers("{\"id\":897326396.0}"));
        Assert.Equal("\"keep.0\"", FlashUtil.NormalizeRuffleNumbers("\"keep.0\""));
        Assert.Equal("42", FlashUtil.NormalizeRuffleNumbers("42.0"));
    }
}
#endif
