using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bastion.Core;

namespace Bastion.Core.Tests;

public class SshCompressionTests
{
    [Theory]
    [InlineData("shell")]
    [InlineData("command")]
    [InlineData("sftp")]
    public async Task ConnectionsOfferOnlyUncompressedTraffic(string kind)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = Host.Create("test", "127.0.0.1", "test");
        host.Port = (uint)((IPEndPoint)listener.LocalEndpoint).Port;
        host.Auth = new HostAuth.AskPassword();
        var knownHosts = new KnownHosts(null);

        var connect = Task.Run(() => Record.Exception(() =>
        {
            switch (kind)
            {
                case "shell": using (SshSession.Connect(host, "test", knownHosts)) { } break;
                case "command": SshSession.RunCommand(host, "test", knownHosts, "true"); break;
                case "sftp": using (SftpBrowserSession.Connect(host, "test", knownHosts)) { } break;
                default: throw new ArgumentException("Unknown connection type", nameof(kind));
            }
        }));

        using (var peer = await listener.AcceptTcpClientAsync(timeout.Token))
        using (var stream = peer.GetStream())
        {
            await stream.WriteAsync("SSH-2.0-test\r\n"u8.ToArray(), timeout.Token);
            var oneByte = new byte[1];
            do
            {
                await stream.ReadExactlyAsync(oneByte, timeout.Token);
            } while (oneByte[0] != (byte)'\n');

            var lengthBytes = new byte[4];
            await stream.ReadExactlyAsync(lengthBytes, timeout.Token);
            var packetLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            Assert.InRange(packetLength, 18u, 35000u);
            var packet = new byte[packetLength];
            await stream.ReadExactlyAsync(packet, timeout.Token);
            Assert.Equal(20, packet[1]); // SSH_MSG_KEXINIT

            var offset = 18; // padding length, message number, 16-byte cookie
            var lists = new List<string>();
            for (var i = 0; i < 8; i++)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset, 4)));
                offset += 4;
                lists.Add(Encoding.ASCII.GetString(packet, offset, length));
                offset += length;
            }

            Assert.Equal("none", lists[6]); // client to server
            Assert.Equal("none", lists[7]); // server to client
        }

        // The test peer ends the exchange after capturing the proposal.
        Assert.NotNull(await connect.WaitAsync(timeout.Token));
    }
}
