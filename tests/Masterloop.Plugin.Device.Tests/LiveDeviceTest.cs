using System;
using System.Threading;
using Masterloop.Core.Types.Commands;
using Xunit;

namespace Masterloop.Plugin.Device.Tests
{
    public class LiveDeviceTest : DeviceTestBase
    {
        [Fact]
        public void ConnectDisconnect()
        {
            using (LiveDevice device = GetLiveDevice())
            {
                Assert.NotNull(device);
                Assert.False(device.IsConnected());
                Assert.True(device.Connect());
                Assert.True(device.IsConnected());
                device.Disconnect();
                Assert.False(device.IsConnected());
            }
        }

        [Fact]
        public void ReceiveCommand()
        {
            using (LiveDevice device = GetLiveDevice())
            {
                device.RegisterCommandHandler(1, OnCommandReceived);
                Assert.NotNull(device);
                Assert.False(device.IsConnected());
                Assert.True(device.Connect());
                Assert.True(device.IsConnected());
                Thread.Sleep(15 * 1000);
                device.Disconnect();
                Assert.False(device.IsConnected());
            }
        }

        private void OnCommandReceived(string mid, Command command)
        {
            Assert.NotNull(mid);
            Assert.NotNull(command);
            Assert.Equal(1, command.Id);
            using (LiveDevice device = GetLiveDevice())
            {
                Assert.True(device.Connect());
                Assert.True(device.PublishCommandResponse(command, true, DateTime.UtcNow, 1, "Test OK"));
                device.Disconnect();
            }
        }
    }
}
