using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using System.Linq;

namespace MakeYourChoice
{
    public class TrafficSniffer : IDisposable
    {
        private Socket _socket;
        private byte[] _buffer = new byte[65535];
        private bool _stopped;
        private Thread _workerThread;

        // (remoteIp, remotePort, localPort) — localPort lets the handler ignore our own beacon probes.
        public event Action<string, int, int> TrafficDetected;
        public IPAddress ListeningIP { get; private set; }

        public void Start()
        {
            try
            {
                var localIp = GetLocalIP();
                if (localIp == null)
                {
                    MessageBox.Show("Sniffer Error: Could not find a valid local IPv4 address with an active gateway.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ListeningIP = localIp;

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);

                // SIO_RCVALL REQUIRES binding to a specific local IP (not IPAddress.Any)
                _socket.Bind(new IPEndPoint(localIp, 0));

                // SIO_RCVALL = 0x98000001
                byte[] inValue = new byte[] { 1, 0, 0, 0 };
                byte[] outValue = new byte[] { 0, 0, 0, 0 };
                try
                {
                    _socket.IOControl(IOControlCode.ReceiveAll, inValue, outValue);
                }
                catch (SocketException)
                {
                     // This often happens if not running as admin or on some specific network setups
                     MessageBox.Show("Sniffer Error: Could not set IOControl ReceiveAll. Ensure you are running as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                     return;
                }

                _stopped = false;
                _workerThread = new Thread(Listen);
                _workerThread.IsBackground = true;
                _workerThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sniffer failed to start: {ex.Message}\n\nEnsure you are running as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private IPAddress GetLocalIP()
        {
            // 1. Try to find the interface with a gateway (Primary Internet adapter)
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             !ni.Description.ToLower().Contains("virtual") &&
                             !ni.Description.ToLower().Contains("pseudo") &&
                             !ni.Description.ToLower().Contains("vmware") &&
                             !ni.Description.ToLower().Contains("vbox") &&
                             !ni.Description.ToLower().Contains("hyper-v") &&
                             !ni.Name.ToLower().Contains("wsl"))
                .ToList();

            foreach (var ni in interfaces)
            {
                var props = ni.GetIPProperties();
                if (props.GatewayAddresses.Any())
                {
                    var addr = props.UnicastAddresses
                        .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (addr != null) return addr.Address;
                }
            }

            // 2. Fallback to any active non-loopback IPv4
            foreach (var ni in interfaces)
            {
                var addr = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);
                if (addr != null) return addr.Address;
            }

            return null;
        }

        private void Listen()
        {
            while (!_stopped)
            {
                try
                {
                    int received = _socket.Receive(_buffer);
                    if (received > 0)
                    {
                        ParsePacket(_buffer, received);
                    }
                }
                catch
                {
                    if (_stopped) break;
                }
            }
        }

        private void ParsePacket(byte[] buffer, int length)
        {
            // IP Header: byte 0 is version and header length
            // Header length is in 32-bit words, bits 0-3
            int ipHeaderLength = (buffer[0] & 0x0F) * 4;

            // Protocol: byte 9
            int protocol = buffer[9];

            if (protocol == 17) // UDP
            {
                // UDP Header starts after IP Header
                // Source Port: Bytes 0-1
                // Dest Port: Bytes 2-3
                int srcPort = (buffer[ipHeaderLength] << 8) + buffer[ipHeaderLength + 1];
                int dstPort = (buffer[ipHeaderLength + 2] << 8) + buffer[ipHeaderLength + 3];

                // Check for port range 7777-7780
                bool isSourceInRange = srcPort >= 7777 && srcPort <= 7820;
                bool isDestInRange = dstPort >= 7777 && dstPort <= 7820;

                if (isSourceInRange || isDestInRange)
                {
                    // Source IP: Bytes 12-15
                    // Dest IP: Bytes 16-19
                    string remoteIp;
                    int remotePort;
                    int localPort;

                    if (isSourceInRange)
                    {
                        // Inbound from a server: remote = source, our local port = destination.
                        remoteIp = $"{buffer[12]}.{buffer[13]}.{buffer[14]}.{buffer[15]}";
                        remotePort = srcPort;
                        localPort = dstPort;
                    }
                    else
                    {
                        // Outbound to a server: remote = destination, our local port = source.
                        remoteIp = $"{buffer[16]}.{buffer[17]}.{buffer[18]}.{buffer[19]}";
                        remotePort = dstPort;
                        localPort = srcPort;
                    }

                    TrafficDetected?.Invoke(remoteIp, remotePort, localPort);
                }
            }
        }

        public void Stop()
        {
            _stopped = true;
            _socket?.Close();
            _workerThread?.Join(100);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
