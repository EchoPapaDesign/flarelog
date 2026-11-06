using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AeroflyAcars.Core.Sim;

/// <summary>Empfängt die UDP-Pakete von Aerofly (oder vom Simulator-Ersatz).</summary>
public sealed class SimUdpListener : IDisposable
{
    private readonly UdpClient _client;

    public SimUdpListener(int port = ForeFlightProtocol.DefaultPort)
    {
        Port = port;
        _client = new UdpClient(AddressFamily.InterNetwork);
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    public int Port { get; }

    public async Task RunAsync(Action<SimMessage> onMessage, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // Unter Windows kann z. B. ein ICMP "port unreachable" hier landen – einfach weiter.
                continue;
            }

            var text = Encoding.ASCII.GetString(result.Buffer);
            foreach (var message in ForeFlightProtocol.ParsePacket(text))
                onMessage(message);
        }
    }

    public void Dispose() => _client.Dispose();
}
