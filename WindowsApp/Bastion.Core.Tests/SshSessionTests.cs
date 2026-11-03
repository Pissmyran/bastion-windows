using Bastion.Core;
using System.Net.Sockets;
using System.Reflection;
using Renci.SshNet;

namespace Bastion.Core.Tests;

/// <summary>
/// Riktiga integrationstester mot localhosts sshd — kräver
/// BASTION_TEST_SSH_KEY (en nyckel redan tillagd i ~/.ssh/authorized_keys,
/// sätts upp/rivs manuellt av testskriptet, inte av testerna själva). Körs
/// inte i vanlig `dotnet test` (ingen nyckel satt i CI), samma mönster som
/// LinuxApp/src/ssh.rs #[ignore]-testerna.
/// </summary>
public class SshSessionTests
{
    private static bool HasTestKey => Environment.GetEnvironmentVariable("BASTION_TEST_SSH_KEY") is not null;

    private static Host BuildTestHost()
    {
        var keyPath = Environment.GetEnvironmentVariable("BASTION_TEST_SSH_KEY")!;
        var user = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
        var host = Host.Create("test", "127.0.0.1", user);
        host.Auth = new HostAuth.KeyFile(keyPath);
        return host;
    }

    [Fact]
    public void ConnectsToRealLocalhostSshdAndGetsAShellPrompt()
    {
        if (!HasTestKey) return; // se klassdoc
        var host = BuildTestHost();
        var knownHosts = new KnownHosts(null); // i-minne, rör inte den riktiga filen i detta test
        using var session = SshSession.Connect(host, null, knownHosts);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        var gotData = false;
        while (DateTime.UtcNow < deadline)
        {
            if (session.Shell.DataAvailable) { gotData = true; break; }
            Thread.Sleep(50);
        }
        Assert.True(gotData, "fick aldrig någon data tillbaka från fjärrskalet");
    }

    [Fact]
    public void TypingExitInTheShellClosesTheSession()
    {
        if (!HasTestKey) return;
        var host = BuildTestHost();
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);

        // Vänta in första skalpromptens data innan vi skriver något.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !session.Shell.DataAvailable) Thread.Sleep(50);
        Assert.True(session.Shell.DataAvailable, "fick aldrig en initial prompt från skalet");
        _ = session.Shell.Read();

        var closed = false;
        session.Shell.Closed += (_, _) => closed = true;
        session.Shell.WriteLine("exit");

        deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !closed) Thread.Sleep(100);
        Assert.True(closed, "sessionen stängdes aldrig efter att exit skrevs i skalet (ShellStream.Closed-eventet fyrade inte)");
    }

    [Fact]
    public void RejectsConnectionWhenHostKeyHasChanged()
    {
        if (!HasTestKey) return;
        var host = BuildTestHost();
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-cs-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var knownHostsPath = Path.Combine(dir, "known_hosts");
        File.WriteAllText(knownHostsPath, "127.0.0.1:22 ssh-ed25519 FALSKT-INTE-DEN-RIKTIGA-NYCKELN\n");
        var knownHosts = new KnownHosts(knownHostsPath);

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var session = SshSession.Connect(host, null, knownHosts);
        });
        Assert.Contains("HAR ÄNDRATS", ex.ToString());
        Directory.Delete(dir, true);
    }

    [Fact]
    public void RunCommandExecutesARealReadonlyCommandOverSsh()
    {
        if (!HasTestKey) return;
        var host = BuildTestHost();
        var knownHosts = new KnownHosts(null);
        var output = SshSession.RunCommand(host, null, knownHosts, "echo bastion-run-command-ok");
        Assert.Equal("bastion-run-command-ok", output.Trim());
    }
}

/// <summary>
/// Samma tester som ovan, men mot en FRISTÅENDE test-sshd (<see cref="TestSshd"/>)
/// istället för den nyckel-gatade riktiga systemtjänsten — körbara här och i CI
/// utan `BASTION_TEST_SSH_KEY`.
/// </summary>
public class SshSessionStandaloneTests
{
    [Fact]
    public async Task ReplacementDoesNotAbortWhilePreviousProbeWaitsForReply()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var proxy = BlackholeTcpProxy.Start(sshd.Port);
        var host = BuildHost(sshd);
        host.Port = proxy.Port;
        using var session = SshSession.Connect(host, null, new KnownHosts(null));
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var client = (SshClient)typeof(SshSession).GetField("_client", fields)!.GetValue(session)!;
        var transport = typeof(BaseClient).GetProperty("Session", fields)!.GetValue(client)!;
        var socket = (Socket)typeof(Session).GetField("_socket", fields)!.GetValue(transport)!;

        proxy.Blackhole();
        session.StartKeepAlive(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2), maxMissed: 1);
        Assert.True(proxy.WaitForDroppedClientTraffic(TimeSpan.FromSeconds(2)));
        session.StartKeepAlive(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(200), maxMissed: 10);
        await Task.Delay(600);
        Assert.False(socket.SafeHandle.IsClosed, "A pending reply must not be treated as a blocked send.");
        session.StopKeepAlive();
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("replacement")]
    [InlineData("dispose")]
    public async Task BlockedTransportWriteIsAborted(string action)
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var proxy = BlackholeTcpProxy.Start(sshd.Port);
        var host = BuildHost(sshd);
        host.Port = proxy.Port;
        using var session = SshSession.Connect(host, null, new KnownHosts(null));
        session.StopKeepAlive();

        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var client = (SshClient)typeof(SshSession).GetField("_client", fields)!.GetValue(session)!;
        var transport = typeof(BaseClient).GetProperty("Session", fields)!.GetValue(client)!;
        var socket = (Socket)typeof(Session).GetField("_socket", fields)!.GetValue(transport)!;
        var disposeLock = typeof(Session).GetField("_socketDisposeLock", fields)!.GetValue(transport)!;
        var success = typeof(Session).GetField("RequestSuccessReceived", fields)!;
        var failure = typeof(Session).GetField("RequestFailureReceived", fields)!;
        var initialSuccess = success.GetValue(transport);
        var initialFailure = failure.GetValue(transport);
        socket.SendBufferSize = 4096;
        proxy.PauseClientReads();

        using var started = new ManualResetEventSlim();
        using var lost = new ManualResetEventSlim();
        void OnConnectionLost(SshSession candidate)
        {
            if (ReferenceEquals(candidate, session)) lost.Set();
        }
        SshSession.SessionConnectionLost += OnConnectionLost;
        var writer = Task.Run(() =>
        {
            lock (disposeLock)
            {
                started.Set();
                try
                {
                    var buffer = new byte[64 * 1024];
                    while (true) socket.Send(buffer);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
            }
        });

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(proxy.WaitForPausedClientReads(TimeSpan.FromSeconds(2)));
            Assert.NotSame(writer, await Task.WhenAny(writer, Task.Delay(300)));
            Assert.False(writer.IsCompleted, "The socket write should be blocked by backpressure.");

            Task? disposal = null;
            if (action == "dispose")
            {
                disposal = Task.Run(session.Dispose);
            }
            else
            {
                session.StartKeepAlive(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(300), maxMissed: 1);
                if (action == "replacement")
                {
                    Assert.True(SpinWait.SpinUntil(() => !Equals(initialSuccess, success.GetValue(transport)), TimeSpan.FromSeconds(2)));
                    session.StopKeepAlive();
                    await Task.Delay(500);
                    Assert.False(socket.SafeHandle.IsClosed, "A stopped monitor aborted the connection.");
                    session.StartKeepAlive(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(300), maxMissed: 1);
                }
            }
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposal is not null) await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            else Assert.True(lost.Wait(TimeSpan.FromSeconds(5)), "The stalled probe did not report the lost connection.");
            Assert.False(session.Shell.CanWrite);
            Assert.Equal(initialSuccess, success.GetValue(transport));
            Assert.Equal(initialFailure, failure.GetValue(transport));
        }
        finally
        {
            socket.Close(0);
            SshSession.SessionConnectionLost -= OnConnectionLost;
        }
    }

    private static Host BuildHost(TestSshd sshd)
    {
        var host = Host.Create("test", "127.0.0.1", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName);
        host.Port = sshd.Port;
        host.Auth = new HostAuth.KeyFile(sshd.ClientKeyPath);
        return host;
    }

    [Fact]
    public void ConnectsToAStandaloneTestSshdAndGetsAShellPrompt()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        var host = BuildHost(sshd);
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        var gotData = false;
        while (DateTime.UtcNow < deadline)
        {
            if (session.Shell.DataAvailable) { gotData = true; break; }
            Thread.Sleep(50);
        }
        Assert.True(gotData, "fick aldrig någon data tillbaka från fjärrskalet");
    }

    [Fact]
    public void TypingExitInTheShellClosesTheSession()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        var host = BuildHost(sshd);
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !session.Shell.DataAvailable) Thread.Sleep(50);
        Assert.True(session.Shell.DataAvailable, "fick aldrig en initial prompt från skalet");
        _ = session.Shell.Read();

        var closed = false;
        session.Shell.Closed += (_, _) => closed = true;
        session.Shell.WriteLine("exit");

        deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !closed) Thread.Sleep(100);
        Assert.True(closed, "sessionen stängdes aldrig efter att exit skrevs i skalet (ShellStream.Closed-eventet fyrade inte)");
    }

    [Fact]
    public void RejectsConnectionWhenHostKeyHasChanged()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        var host = BuildHost(sshd);
        var dir = Path.Combine(Path.GetTempPath(), $"bastion-cs-known-hosts-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var knownHostsPath = Path.Combine(dir, "known_hosts");
        File.WriteAllText(knownHostsPath, $"127.0.0.1:{sshd.Port} ssh-ed25519 FALSKT-INTE-DEN-RIKTIGA-NYCKELN\n");
        var knownHosts = new KnownHosts(knownHostsPath);

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var session = SshSession.Connect(host, null, knownHosts);
        });
        Assert.Contains("HAR ÄNDRATS", ex.ToString());
        Directory.Delete(dir, true);
    }

    [Fact]
    public void RunCommandExecutesARealCommandOverSsh()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        var host = BuildHost(sshd);
        var knownHosts = new KnownHosts(null);
        var output = SshSession.RunCommand(host, null, knownHosts, "echo bastion-run-command-ok");
        Assert.Equal("bastion-run-command-ok", output.Trim());
    }

    [Fact]
    public void KeepAliveDetectsASilentBlackholedConnectionWithoutFalsePositives()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var proxy = BlackholeTcpProxy.Start(sshd.Port);

        var host = BuildHost(sshd);
        host.Port = proxy.Port;
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);
        using var lost = new ManualResetEventSlim(false);
        void OnConnectionLost(SshSession candidate)
        {
            if (ReferenceEquals(candidate, session)) lost.Set();
        }

        SshSession.SessionConnectionLost += OnConnectionLost;
        try
        {
            session.StartKeepAlive(
                interval: TimeSpan.FromMilliseconds(100),
                probeTimeout: TimeSpan.FromSeconds(1),
                maxMissed: 2);

            // Kontrollarmen väntar längre än två kompletta timeoutcykler. En
            // implementation vars varje friska sond timeoutar måste därför
            // hinna signalera fel här, samtidigt som CI-scheduling får en
            // realistisk marginal jämfört med produktionens 10 s timeout.
            Assert.False(
                lost.Wait(TimeSpan.FromMilliseconds(2500)),
                "en levande SSH-session markerades felaktigt som död");

            // Sluta vidarebefordra trafik men håll TCP-socketarna öppna. Det här
            // är exakt fallet där IsConnected/SSH_MSG_IGNORE inte räcker.
            proxy.Blackhole();
            Assert.True(
                proxy.WaitForDroppedClientTraffic(TimeSpan.FromSeconds(2)),
                "ingen keepalive-trafik nådde den svart-hålade proxyn");
            Assert.True(
                lost.Wait(TimeSpan.FromSeconds(5)),
                "sessionen signalerade inte anslutningsförlust när svarsbärande keepalive-requester timeoutade över en svart-hålad TCP-anslutning");
            Assert.False(session.Shell.CanWrite, "sessionens shell var fortfarande skrivbart efter verifierad anslutningsförlust");
        }
        finally
        {
            SshSession.SessionConnectionLost -= OnConnectionLost;
        }
    }

    [Fact]
    public void StopKeepAlivePreventsAnInFlightTimeoutFromClosingTheSession()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var proxy = BlackholeTcpProxy.Start(sshd.Port);

        var host = BuildHost(sshd);
        host.Port = proxy.Port;
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);
        using var lost = new ManualResetEventSlim(false);
        void OnConnectionLost(SshSession candidate)
        {
            if (ReferenceEquals(candidate, session)) lost.Set();
        }

        SshSession.SessionConnectionLost += OnConnectionLost;
        try
        {
            proxy.Blackhole();
            session.StartKeepAlive(
                interval: TimeSpan.FromMilliseconds(20),
                probeTimeout: TimeSpan.FromMilliseconds(300),
                maxMissed: 1);

            Assert.True(
                proxy.WaitForDroppedClientTraffic(TimeSpan.FromSeconds(1)),
                "ingen in-flight keepalive-request observerades före StopKeepAlive");
            session.StopKeepAlive();
            Assert.False(
                lost.Wait(TimeSpan.FromMilliseconds(600)),
                "StopKeepAlive tillät en gammal monitor att signalera anslutningsförlust efter retur");
        }
        finally
        {
            SshSession.SessionConnectionLost -= OnConnectionLost;
        }
    }

    [Fact]
    public void ReplacingAnInFlightKeepAliveThenStoppingDoesNotLetTheOldMonitorCloseTheSession()
    {
        using var sshd = TestSshd.Start();
        if (sshd is null) return;
        using var proxy = BlackholeTcpProxy.Start(sshd.Port);

        var host = BuildHost(sshd);
        host.Port = proxy.Port;
        var knownHosts = new KnownHosts(null);
        using var session = SshSession.Connect(host, null, knownHosts);
        using var lost = new ManualResetEventSlim(false);
        void OnConnectionLost(SshSession candidate)
        {
            if (ReferenceEquals(candidate, session)) lost.Set();
        }

        SshSession.SessionConnectionLost += OnConnectionLost;
        try
        {
            proxy.Blackhole();
            session.StartKeepAlive(
                interval: TimeSpan.FromMilliseconds(20),
                probeTimeout: TimeSpan.FromMilliseconds(300),
                maxMissed: 1);

            Assert.True(
                proxy.WaitForDroppedClientTraffic(TimeSpan.FromSeconds(1)),
                "ingen in-flight keepalive-request observerades före monitorbytet");

            // Ersätt monitorn medan den gamla väntar på sitt uteblivna svar och
            // stoppa sedan den nya. Den gamla får aldrig disponera sessionen
            // efter att ersättningen accepterats.
            session.StartKeepAlive(
                interval: TimeSpan.FromSeconds(5),
                probeTimeout: TimeSpan.FromMilliseconds(300),
                maxMissed: 1);
            session.StopKeepAlive();

            Assert.False(
                lost.Wait(TimeSpan.FromMilliseconds(600)),
                "en ersatt monitor stängde sessionen efter att den nya monitorn stoppats");
        }
        finally
        {
            SshSession.SessionConnectionLost -= OnConnectionLost;
        }
    }
}
