using Skua.Core.Utils;
using Xunit;

namespace Skua.Core.Tests;

public sealed class FlashBridgeJsonTests
{
    [Fact]
    public void BuildCallJsonSerializesPrimitives()
    {
        string json = FlashCallJson.Build(7, "getGameObject", new object[] { "world.myAvatar", 3, true, null! });

        Assert.Equal("""{"id":7,"fn":"getGameObject","args":["world.myAvatar",3,true,null]}""", json);
    }

    [Fact]
    public void BuildCallJsonEscapesStrings()
    {
        string json = FlashCallJson.Build(1, "sendClientPacket", new object[] { "say \"hi\"\n" });

        Assert.Equal("""{"id":1,"fn":"sendClientPacket","args":["say \"hi\"\n"]}""", json);
    }

    [Fact]
    public void NormalizeRuffleNumbersDropsTrailingPointZeroOutsideStrings()
    {
        Assert.Equal("{\"id\":897326396}", RuffleJson.NormalizeNumbers("{\"id\":897326396.0}"));
        Assert.Equal("\"keep.0\"", RuffleJson.NormalizeNumbers("\"keep.0\""));
        Assert.Equal("42", RuffleJson.NormalizeNumbers("42.0"));
    }
}
