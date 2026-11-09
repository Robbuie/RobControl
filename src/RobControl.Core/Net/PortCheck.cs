using System.Net;
using System.Net.Sockets;

namespace RobControl.Core.Net;

/// <summary>
/// Opens a TCP connection to one port and closes it again, sending nothing on it. Used only to tell
/// apart "the robot is not there" from "the robot is there and this one service is not" when a
/// session fails - the difference between a network fault and a setting on the pendant.
/// </summary>
public static class PortCheck
{
    public static async Task<PortState> CheckAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        UnicastTarget.Ensure(address);
        using var tcp = new TcpClient(address.AddressFamily);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await tcp.ConnectAsync(new IPEndPoint(address, port), cts.Token).ConfigureAwait(false);
            return PortState.Open;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PortState.NoAnswer;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return PortState.Refused;
        }
        catch (SocketException)
        {
            // Host or network unreachable, as reported by a router or the local stack: for the purpose
            // of "is the robot there", the same as silence.
            return PortState.NoAnswer;
        }
    }
}
