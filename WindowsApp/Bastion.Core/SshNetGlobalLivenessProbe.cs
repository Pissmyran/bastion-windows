using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Connection;

namespace Bastion.Core;

// Beteendeneutral CodeQL-trigger: håll C# i PR-diffen så default setup producerar csharp-konfigurationen som main-rulesetet kräver.

/// <summary>
/// Smal adapter för SSH.NET 2026.0.0:s svarsbärande globala SSH-request.
///
/// OpenSSH använder själv SSH_MSG_GLOBAL_REQUEST med namnet
/// <c>keepalive@openssh.com</c> och <c>want-reply=true</c> för client-alive/
/// server-alive-kontroller. Requesten öppnar ingen kanal och kör inget
/// fjärrkommando. SSH.NET exponerar svarseventen publikt på <see cref="Session"/>,
/// men den aktuella sessionen på <see cref="BaseClient"/>, sändmetoden och den
/// användbara <see cref="GlobalRequestMessage"/>-konstruktorn är inte publika.
/// De tre reflektionspunkterna kapslas därför här och kontraktsvalideras
/// synkront innan bakgrundsbevakningen startar.
/// </summary>
internal static class SshNetGlobalLivenessProbe
{
    private static readonly byte[] KeepAliveRequestName = Encoding.ASCII.GetBytes("keepalive@openssh.com");

    private static readonly PropertyInfo ClientSessionProperty =
        typeof(BaseClient).GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("SSH.NET BaseClient saknar det förväntade Session-kontraktet");

    private static readonly MethodInfo SendMessageMethod =
        ClientSessionProperty.PropertyType.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new NotSupportedException("SSH.NET ISession saknar SendMessage(Message)");

    private static readonly FieldInfo SessionSocketField =
        typeof(Session).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("SSH.NET Session saknar transportens socket");

    private static readonly ConstructorInfo GlobalRequestConstructor =
        typeof(GlobalRequestMessage).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(byte[]), typeof(bool) },
            modifiers: null)
        ?? throw new NotSupportedException("SSH.NET GlobalRequestMessage saknar konstruktorn (byte[], bool)");

    static SshNetGlobalLivenessProbe()
    {
        if (!ClientSessionProperty.PropertyType.IsAssignableFrom(typeof(Session)))
        {
            throw new NotSupportedException("SSH.NET BaseClient.Session har ändrat typ");
        }

        if (SessionSocketField.FieldType != typeof(Socket))
        {
            throw new NotSupportedException("SSH.NET Session har ändrat sockettyp");
        }

        var sendParameters = SendMessageMethod.GetParameters();
        if (SendMessageMethod.ReturnType != typeof(void) || sendParameters.Length != 1)
        {
            throw new NotSupportedException("SSH.NET ISession.SendMessage har ändrat signatur");
        }
    }

    /// <summary>Forcerar statisk kontraktsvalidering innan bakgrundsbevakningen startas.</summary>
    public static void ValidateContract()
    {
        _ = ClientSessionProperty;
        _ = SendMessageMethod;
        _ = GlobalRequestConstructor;
    }

    /// <summary>
    /// Sends an OpenSSH no-op global request and waits for a success or failure
    /// reply for the remaining deadline. Either response proves the peer is alive.
    /// Reply timeouts leave the session connected. A blocked send is aborted by
    /// the caller's deadline, since SSH.NET cannot cancel its synchronous write.
    /// </summary>
    public static bool SendAndWaitForReply(SshClient client, Func<TimeSpan> remainingAfterSend)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (GetValueUnwrapped(ClientSessionProperty, client) is not Session session)
        {
            throw new InvalidOperationException("SSH.NET-klienten saknar en aktiv Session");
        }

        // TCS behöver ingen Dispose och TrySetResult är säker även om ett svar
        // hinner in parallellt med timeout/unsubscribe. Ett WaitHandle här skulle
        // annars kunna träffas av ett sent event efter att handtaget disponerats.
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<MessageEventArgs<RequestSuccessMessage>> success = (_, _) => reply.TrySetResult(true);
        EventHandler<MessageEventArgs<RequestFailureMessage>> failure = (_, _) => reply.TrySetResult(true);

        session.RequestSuccessReceived += success;
        session.RequestFailureReceived += failure;
        try
        {
            var request = (GlobalRequestMessage)InvokeUnwrapped(
                GlobalRequestConstructor,
                new object[] { KeepAliveRequestName, true });
            InvokeUnwrapped(SendMessageMethod, session, new object[] { request });
            var remaining = remainingAfterSend();
            return remaining > TimeSpan.Zero && reply.Task.Wait(remaining);
        }
        finally
        {
            session.RequestSuccessReceived -= success;
            session.RequestFailureReceived -= failure;
        }
    }

    public static Socket? GetSocket(SshClient client) =>
        GetValueUnwrapped(ClientSessionProperty, client) is Session session
            ? (Socket?)SessionSocketField.GetValue(session)
            : null;

    public static void Abort(Socket? socket)
    {
        try { socket?.Close(0); }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private static object? GetValueUnwrapped(PropertyInfo property, object target)
    {
        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static object InvokeUnwrapped(ConstructorInfo constructor, object[] parameters)
    {
        try
        {
            return constructor.Invoke(parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvokeUnwrapped(MethodInfo method, object target, object[] parameters)
    {
        try
        {
            _ = method.Invoke(target, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
