using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    /// <summary>
    /// EXPERIMENT — active liveness probing of real DBD game servers, to detect a region's fleet
    /// state in real time instead of waiting on Dead by Queue's lagged data.
    ///
    /// HOW WE KNOW THIS WORKS (from Wireshark captures of two separate sessions hitting two
    /// different Ohio servers, 18.225.57.244:7792 and 18.224.135.149:7788):
    /// DBD's dedicated servers run on Unreal Engine and use UE's "stateless connect handshake".
    /// The very first client->server packet is a connectionless InitialConnect; the server answers
    /// with a Challenge *before any auth*, purely from the packet's magic/version. Captured initials:
    ///   client:  b8 01 02 80 00 | c9 1e f8 11 | 00..00 (empty cookie) | &lt;client nonce&gt; | 01
    ///   server:  b8 01 82 80 00 | c9 1e f8 11 | ...challenge...                         (0x80 = reply)
    /// The 9-byte prefix incl. magic c9 1e f8 11 was IDENTICAL across both sessions/servers, so it's
    /// a build constant we can replay. A live server replies with a challenge; a ramped-down fleet
    /// (instance terminated) is silent. => Replaying the captured InitialConnect is our beacon.
    ///
    /// Connected-UDP also lets us read ICMP errors the OS surfaces:
    ///   • Replied         — server sent a handshake challenge  => fleet UP, process listening
    ///   • PortUnreachable — ICMP 3/3 (ConnectionReset 10054)   => EC2 instance UP, no process on port
    ///   • HostUnreachable — ICMP 3/1 etc (10065/10051)         => instance/route gone  => likely DOWN
    ///   • NoResponse      — timeout                            => gone / dropped / firewalled
    ///
    /// NOTE: the magic (c9 1e f8 11) is tied to DBD's network build; a game netcode update may change
    /// it and require a fresh capture. Update UeHandshakeHex below from a new InitialConnect packet.
    /// </summary>
    public static class LiveProbe
    {
        public enum Outcome
        {
            Replied,          // got data back -> definitely alive & talking
            PortUnreachable,  // ICMP port unreachable -> host up, no process on that port
            HostUnreachable,  // ICMP host/net unreachable -> instance/route gone
            NoResponse,       // timeout -> nothing there
            Error             // socket/other failure -> inconclusive
        }

        // A captured UE InitialConnect packet (from the Ohio capture, frame 415). Replayed verbatim;
        // the trailing client nonce being "stale" does not stop the server issuing a fresh challenge.
        private const string UeHandshakeHex =
            "b801028000c91ef81100000000000000000000000000000000000000000000000000000000000001e05886665b064cc46901";
        // Bytes 5..8 of any handshake packet (client or server) — the build magic. A reply carrying
        // this is a confirmed UE handshake challenge (strongest "fleet up" signal).
        private static readonly byte[] Magic = { 0xc9, 0x1e, 0xf8, 0x11 };

        private static readonly byte[] UeHandshake = FromHex(UeHandshakeHex);

        public readonly struct ProbeReport
        {
            public ProbeReport(string method, string ip, int port, Outcome outcome, int rttMs, bool magicMatch, string detail)
            {
                Method = method; Ip = ip; Port = port; Outcome = outcome; RttMs = rttMs; MagicMatch = magicMatch; Detail = detail;
            }
            public string Method { get; }
            public string Ip { get; }
            public int Port { get; }
            public Outcome Outcome { get; }
            public int RttMs { get; }
            public bool MagicMatch { get; }   // reply carried the UE handshake magic -> real DBD server
            public string Detail { get; }

            // Did this probe prove a live DBD server? A handshake challenge (magic reply) is definitive.
            public bool IsLiveServer => Outcome == Outcome.Replied && MagicMatch;

            public override string ToString()
                => $"{Method,-10} {Ip}:{Port,-5} -> {Outcome,-15}{(MagicMatch ? " [DBD-challenge]" : "")} {RttMs,4}ms  {Detail}";
        }

        // Local UDP ports currently (or very recently) used by our own probe sockets, with an expiry
        // tick. The traffic sniffer consults this so it never mistakes the beacon's own probe packets
        // (or the server replies to them) for a real game connection. Fixes the phantom "Connected
        // to: <region>" that appeared with no game running.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _beaconPorts = new();

        private static void MarkBeaconPort(int port)
        {
            if (port > 0) _beaconPorts[port] = DateTime.UtcNow.AddSeconds(8).Ticks;
        }

        /// <summary>True if this local port belongs to one of our in-flight/recent beacon probes.</summary>
        public static bool IsBeaconLocalPort(int port)
        {
            if (_beaconPorts.TryGetValue(port, out var expiry))
            {
                if (expiry > DateTime.UtcNow.Ticks) return true;
                _beaconPorts.TryRemove(port, out _);
            }
            return false;
        }

        private static byte[] FromHex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        private static bool HasMagic(byte[] buf, int n)
        {
            if (n < 9) return false;
            for (int i = 0; i < 4; i++) if (buf[5 + i] != Magic[i]) return false;
            return true;
        }

        // Steam A2S_INFO request, kept as a secondary probe for the log.
        private static byte[] BuildA2sInfo()
        {
            var prefix = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
            var payload = Encoding.ASCII.GetBytes("Source Engine Query\0");
            var packet = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, packet, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, packet, prefix.Length, payload.Length);
            return packet;
        }

        /// <summary>
        /// Send one UDP payload to ip:port on a connected socket and classify the result. Connected
        /// UDP surfaces ICMP errors as socket exceptions, letting us tell host-up from host-gone.
        /// </summary>
        public static async Task<ProbeReport> ProbeUdpAsync(string method, byte[] payload, string ip, int port, int timeoutMs = 1200)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!IPAddress.TryParse(ip, out var addr))
                    return new ProbeReport(method, ip, port, Outcome.Error, 0, false, "bad ip");

                using var udp = new Socket(addr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                udp.Connect(new IPEndPoint(addr, port));
                // Tag our own local port so the sniffer ignores this probe (and the server's reply).
                MarkBeaconPort((udp.LocalEndPoint as IPEndPoint)?.Port ?? 0);
                await udp.SendAsync(new ReadOnlyMemory<byte>(payload), SocketFlags.None).ConfigureAwait(false);

                var buf = new byte[2048];
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                try
                {
                    int n = await udp.ReceiveAsync(new Memory<byte>(buf), SocketFlags.None, cts.Token).ConfigureAwait(false);
                    sw.Stop();
                    bool magic = HasMagic(buf, n);
                    var head = BitConverter.ToString(buf, 0, Math.Min(n, 16));
                    return new ProbeReport(method, ip, port, Outcome.Replied, (int)sw.ElapsedMilliseconds, magic, $"{n}B {head}");
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    return new ProbeReport(method, ip, port, Outcome.NoResponse, (int)sw.ElapsedMilliseconds, false, "timeout");
                }
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                sw.Stop();
                return new ProbeReport(method, ip, port, Outcome.PortUnreachable, (int)sw.ElapsedMilliseconds, false, "ICMP port unreachable");
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.HostUnreachable
                                          || se.SocketErrorCode == SocketError.NetworkUnreachable)
            {
                sw.Stop();
                return new ProbeReport(method, ip, port, Outcome.HostUnreachable, (int)sw.ElapsedMilliseconds, false, se.SocketErrorCode.ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ProbeReport(method, ip, port, Outcome.Error, (int)sw.ElapsedMilliseconds, false, ex.Message);
            }
        }

        /// <summary>Replay the UE InitialConnect handshake to a known game-server endpoint.</summary>
        public static Task<ProbeReport> ProbeHandshakeAsync(string ip, int port, int timeoutMs = 1200)
            => ProbeUdpAsync("UE-HS", UeHandshake, ip, port, timeoutMs);

        /// <summary>Secondary Steam query probe (logged for comparison).</summary>
        public static Task<ProbeReport> ProbeA2sAsync(string ip, int port, int timeoutMs = 1200)
            => ProbeUdpAsync("A2S", BuildA2sInfo(), ip, port, timeoutMs);

        /// <summary>
        /// Handshake every port in [start,end] on one IP and return the ports that answer as a live
        /// DBD server. Called when we first see an instance (from a real connection) to harvest all
        /// of its server ports into the pool — one connection then yields ~5 endpoints, not 1.
        /// </summary>
        public static async Task<List<int>> HarvestLivePortsAsync(string ip, int start = 7777, int end = 7820,
            int timeoutMs = 800, int maxConcurrent = 32)
        {
            var live = new List<int>();
            var sync = new object();
            var ports = Enumerable.Range(start, end - start + 1).ToList();
            try
            {
                await Parallel.ForEachAsync(ports, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrent },
                    async (p, _) =>
                    {
                        var r = await ProbeUdpAsync("HARVEST", UeHandshake, ip, p, timeoutMs).ConfigureAwait(false);
                        if (r.IsLiveServer) lock (sync) live.Add(p);
                    }).ConfigureAwait(false);
            }
            catch { /* best effort */ }
            live.Sort();
            return live;
        }

        public sealed class SweepSummary
        {
            public int Total, Replied, PortUnreach, HostUnreach, Timeout, Errored;
            public bool AnyLive;
            public ProbeReport FirstLive;
            public override string ToString()
                => $"{Total} probed: {Replied} replied, {PortUnreach} port-unreach, {HostUnreach} host-unreach, " +
                   $"{Timeout} timeout, {Errored} err" + (AnyLive ? $"  LIVE={FirstLive.Ip}:{FirstLive.Port}" : "");
        }

        /// <summary>
        /// Probe many endpoints with the UE handshake at bounded concurrency. If stopOnLive, cancels
        /// the remaining work the moment a confirmed DBD challenge is seen (fast "up" detection).
        /// </summary>
        public static async Task<SweepSummary> ProbeBatchAsync(IReadOnlyList<(string ip, int port)> targets,
            int timeoutMs, int maxConcurrent, bool stopOnLive)
        {
            var s = new SweepSummary { Total = targets.Count };
            if (targets.Count == 0) return s;
            using var cts = new CancellationTokenSource();
            var opts = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrent, CancellationToken = cts.Token };
            var sync = new object();
            try
            {
                await Parallel.ForEachAsync(targets, opts, async (t, _) =>
                {
                    var r = await ProbeUdpAsync("UE-HS", UeHandshake, t.ip, t.port, timeoutMs).ConfigureAwait(false);
                    lock (sync)
                    {
                        switch (r.Outcome)
                        {
                            case Outcome.Replied: s.Replied++; break;
                            case Outcome.PortUnreachable: s.PortUnreach++; break;
                            case Outcome.HostUnreachable: s.HostUnreach++; break;
                            case Outcome.NoResponse: s.Timeout++; break;
                            default: s.Errored++; break;
                        }
                        if (r.IsLiveServer && !s.AnyLive) { s.AnyLive = true; s.FirstLive = r; }
                    }
                    if (r.IsLiveServer && stopOnLive) { try { cts.Cancel(); } catch { } }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* stopped early after a live hit */ }
            return s;
        }

        /// <summary>
        /// Sweep a /24 (prefix like "18.225.57") across the given ports with the UE handshake, to find
        /// a live DBD server even when GameLift has churned the exact IPs we'd previously seen.
        /// </summary>
        public static Task<SweepSummary> SweepSubnetAsync(string prefix24, IReadOnlyList<int> ports,
            int timeoutMs = 600, int maxConcurrent = 256)
        {
            var targets = new List<(string, int)>(254 * Math.Max(1, ports.Count));
            for (int host = 1; host <= 254; host++)
                foreach (var p in ports)
                    targets.Add(($"{prefix24}.{host}", p));
            return ProbeBatchAsync(targets, timeoutMs, maxConcurrent, stopOnLive: true);
        }
    }
}
