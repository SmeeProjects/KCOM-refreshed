using System.Drawing;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // Smoke tests: KiwisCoOpModCore loads and runs on plain net10.0, System.Drawing.Color included.
    public class ChannelTests
    {
        [Fact]
        public void ToString_WrapsUpperCaseName()
        {
            Channel channel = new("chat", "Chat", Color.Cyan, "<", ">", ": ");

            Assert.Equal("CHAT", channel.GetName());
            Assert.Equal("Chat", channel.GetFriendlyName());
            Assert.Equal(Color.Cyan, channel.GetColor());
            Assert.Equal("<CHAT>: ", channel.ToString());
        }

        [Fact]
        public void DefaultChannel_IsGrayWithSquareBrackets()
        {
            Channel channel = new("info", "Info");

            Assert.Equal(Color.Gray, channel.GetColor());
            Assert.Equal("[INFO] ", channel.ToString());
        }

        [Fact]
        public void EmptyWrapper_LeavesTextAsIs()
        {
            Wrapper wrapper = new Wrapper("(", ")", " ").Empty();

            Assert.Equal("text", wrapper.Wrap("text"));
        }
    }
}
