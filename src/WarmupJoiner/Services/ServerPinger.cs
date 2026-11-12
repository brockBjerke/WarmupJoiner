using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WarmupJoiner.Services;

/// <summary>
/// Measures real round-trip latency to a CS2 (Source engine) game server.
/// Primary method is a raw A2S_INFO query straight to the game port - the same
/// request real server browsers use - since that's what actually matters for
/// "ping" in-game. Falls back to a plain ICMP ping to the host if the server
/// has query packets firewalled off.
/// </summary>
public static class ServerPinger
{
    private static readonly byte[] A2SInfoRequest =
    {
        0xFF, 0xFF, 0xFF, 0xFF,
        (byte)'T', (byte)'S', (byte)'o', (byte)'u', (byte)'r', (byte)'c', (byte)'e', (byte)' ',
        (byte)'E', (byte)'n', (byte)'g', (byte)'i', (byte)'n', (byte)'e', (byte)' ',
        (byte)'Q', (byte)'u', (byte)'e', (byte)'r', (byte)'y', 0x00,
    };

    public static async Task<int?> MeasureAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        var a2sPing = await TryA2SPingAsync(host, port, timeoutMs, ct).ConfigureAwait(false);
        if (a2sPing is not null)
        {
            return a2sPing;
        }

        return await TryIcmpPingAsync(host, timeoutMs, ct).ConfigureAwait(false);
    }

    private static async Task<int?> TryA2SPingAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            var remote = new IPEndPoint((await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false))[0], port);

            var stopwatch = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            await udp.SendAsync(A2SInfoRequest, remote, timeoutCts.Token).ConfigureAwait(false);
            var result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);

            // Newer Source servers reply with a challenge (header byte 'A' / 0x41)
            // before answering for real - redo the request with the challenge appended.
            if (result.Buffer.Length >= 5 && result.Buffer[4] == (byte)'A')
            {
                var challenge = result.Buffer[^4..];
                var requestWithChallenge = A2SInfoRequest.Concat(challenge).ToArray();
                await udp.SendAsync(requestWithChallenge, remote, timeoutCts.Token).ConfigureAwait(false);
                await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static async Task<int?> TryIcmpPingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs).WaitAsync(ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is PingException or SocketException or OperationCanceledException)
        {
            return null;
        }
    }
}
