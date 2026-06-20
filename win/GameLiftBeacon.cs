using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    /// <summary>
    /// Probes an AWS GameLift ping beacon directly (the same mechanism Dead by Queue uses), so we
    /// can detect whether a region's fleet is live or ramped down in real time instead of waiting on
    /// Dead by Queue's slow (20-60 min) all-region rotation.
    ///
    /// Protocol: send a 12-byte UDP packet to gamelift-ping.&lt;region&gt;.api.aws on port 443 — the
    /// "GLPL" magic (4 bytes) followed by an 8-byte big-endian Unix-ms timestamp. A live fleet echoes
    /// back 12 bytes starting with "GLPL"; a ramped-down fleet stays silent.
    ///
    /// This is UDP 443, not the 7770-7820 game-server range the hard lock blocks, and we only probe
    /// the user's selected region (which is never blocked), so it works with the lock on.
    /// </summary>
    public static class GameLiftBeacon
    {
        /// <summary>
        /// true  = beacon echoed (fleet online)
        /// false = resolved but no valid response before timeout (fleet ramped down / offline)
        /// null  = could not probe (DNS/socket failure) -> caller should fall back to another source
        /// </summary>
        public static async Task<bool?> IsFleetOnlineAsync(string pingHost, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(pingHost)) return null;

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(pingHost).ConfigureAwait(false);
            }
            catch
            {
                return null; // DNS failure -> inconclusive
            }
            if (addresses == null || addresses.Length == 0) return null;

            // Skip a host that's been hosts-blocked to 0.0.0.0 (can't tell anything from it).
            var target = Array.Find(addresses, a => !a.Equals(IPAddress.Any));
            if (target == null) return null;

            try
            {
                using var udp = new UdpClient(target.AddressFamily);
                udp.Client.ReceiveTimeout = timeoutMs;
                udp.Client.SendTimeout = timeoutMs;

                var packet = new byte[12];
                packet[0] = 0x47; // G
                packet[1] = 0x4C; // L
                packet[2] = 0x50; // P
                packet[3] = 0x4C; // L
                var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                for (int i = 0; i < 8; i++)
                    packet[4 + i] = (byte)(timestamp >> (56 - i * 8));

                await udp.SendAsync(packet, packet.Length, new IPEndPoint(target, 443)).ConfigureAwait(false);

                var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                byte[] response;
                try
                {
                    response = await Task.Run(() => udp.Receive(ref remoteEp)).ConfigureAwait(false);
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
                {
                    return false; // resolved, sent, no echo -> ramped down
                }

                return response.Length >= 4
                       && response[0] == 0x47
                       && response[1] == 0x4C
                       && response[2] == 0x50
                       && response[3] == 0x4C;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
            {
                return false;
            }
            catch
            {
                return null; // any other socket/network error -> inconclusive
            }
        }
    }
}
