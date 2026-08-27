using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Coophead.Transport
{
    internal sealed class P2pInputTransport : IInputFrameTransport
    {
        private readonly Socket socket;
        private readonly bool host;
        private readonly string signalingUrl;
        private readonly string initialRoomCode;
        private readonly uint versionToken;
        private readonly EndPoint stunEndpoint;
        private readonly byte[] transactionId = new byte[12];
        private readonly byte[] receiveBuffer = new byte[512];
        private DateTime lastStunSentUtc;
        private volatile bool disposed;
        private volatile string asyncStatus;
        private volatile string asyncRoomCode;
        private volatile IPEndPoint discoveredPeer;
        private bool signalingStarted;
        private UdpInputTransport udp;

        public P2pInputTransport(string signalingUrl, bool host, string roomCode,
            string stunHost, int stunPort, uint versionToken)
        {
            this.host = host; this.signalingUrl = signalingUrl.TrimEnd('/');
            initialRoomCode = (roomCode ?? "").Trim().ToUpperInvariant(); this.versionToken = versionToken;
            if (!host && initialRoomCode.Length != 6) throw new ArgumentException("El código debe tener seis caracteres.");
            var addresses = Dns.GetHostAddresses(stunHost);
            IPAddress address = null;
            foreach (var candidate in addresses) if (candidate.AddressFamily == AddressFamily.InterNetwork) { address = candidate; break; }
            if (address == null) throw new InvalidOperationException("No se resolvió el servidor STUN.");
            stunEndpoint = new IPEndPoint(address, stunPort);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0)); socket.Blocking = false;
            RandomNumberGenerator.Create().GetBytes(transactionId);
            asyncStatus = "descubriendo endpoint público"; asyncRoomCode = initialRoomCode;
            Description = (host ? "P2P Internet host" : "P2P Internet client"); PingMilliseconds = -1;
        }

        public string Description { get; private set; }
        public string Status { get { return udp == null ? asyncStatus : udp.Status; } }
        public string RoomCode { get { return asyncRoomCode; } }
        public bool IsConnected { get { return udp != null && udp.IsConnected; } }
        public int PingMilliseconds { get { return udp == null ? -1 : udp.PingMilliseconds; } private set { } }

        public void Update()
        {
            if (udp != null) { udp.Update(); return; }
            var now = DateTime.UtcNow;
            if (now - lastStunSentUtc >= TimeSpan.FromSeconds(1))
            {
                try { socket.SendTo(StunPacketCodec.CreateBindingRequest(transactionId), stunEndpoint); }
                catch (SocketException) { }
                lastStunSentUtc = now;
            }
            ReceiveStun();
            if (discoveredPeer != null)
            {
                udp = UdpInputTransport.CreatePeer(socket, host, discoveredPeer, versionToken);
                asyncStatus = "abriendo ruta P2P";
                return;
            }
        }

        private void ReceiveStun()
        {
            if (signalingStarted) return;
            while (socket.Poll(0, SelectMode.SelectRead))
            {
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                int count;
                try { count = socket.ReceiveFrom(receiveBuffer, ref sender); }
                catch (SocketException) { return; }
                var packet = new byte[count]; Buffer.BlockCopy(receiveBuffer, 0, packet, 0, count);
                IPEndPoint publicEndpoint;
                if (!StunPacketCodec.TryReadBindingResponse(packet, transactionId, out publicEndpoint)) continue;
                signalingStarted = true; asyncStatus = "contactando señalización";
                var thread = new Thread(() => RunSignaling(publicEndpoint));
                thread.IsBackground = true; thread.Name = "Coophead P2P Signaling"; thread.Start();
                return;
            }
        }

        private void RunSignaling(IPEndPoint publicEndpoint)
        {
            try
            {
                var endpointJson = "{\"address\":\"" + publicEndpoint.Address + "\",\"port\":" +
                    publicEndpoint.Port + ",\"version\":" + versionToken + "}";
                if (host)
                {
                    var created = Request("POST", signalingUrl + "/rooms", endpointJson);
                    asyncRoomCode = MatchString(created, "code");
                    if (asyncRoomCode.Length != 6) throw new InvalidOperationException("Señalización no entregó código.");
                    asyncStatus = "sala " + asyncRoomCode + "; esperando jugador";
                    while (!disposed && discoveredPeer == null)
                    {
                        var response = Request("GET", signalingUrl + "/rooms/" + asyncRoomCode + "?role=host", null);
                        if (response.IndexOf("\"peer\"") >= 0) discoveredPeer = ParsePeer(response);
                        if (discoveredPeer == null) Thread.Sleep(750);
                    }
                }
                else
                {
                    var response = Request("POST", signalingUrl + "/rooms/" + initialRoomCode, endpointJson);
                    discoveredPeer = ParsePeer(response);
                }
                if (discoveredPeer != null) asyncStatus = "peer encontrado; abriendo UDP";
            }
            catch (Exception ex) { asyncStatus = "P2P error: " + ex.Message; }
        }

        private static string Request(string method, string url, string body)
        {
            var request = (HttpWebRequest)WebRequest.Create(url); request.Method = method;
            request.Timeout = 5000; request.ReadWriteTimeout = 5000; request.ContentType = "application/json";
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body); request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }

        private static IPEndPoint ParsePeer(string json)
        {
            var address = MatchString(json, "address"); var port = MatchInt(json, "port");
            IPAddress parsed;
            return IPAddress.TryParse(address, out parsed) && port > 0 ? new IPEndPoint(parsed, port) : null;
        }
        private static string MatchString(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + name + "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            return match.Success ? match.Groups[1].Value : "";
        }
        private static int MatchInt(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + name + "\\\"\\s*:\\s*(\\d+)"); int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : -1;
        }

        public void Reset() { if (udp != null) udp.Reset(); }
        public void Send(InputFrame frame) { if (udp != null) udp.Send(frame); }
        public bool TryReceive(uint tick, out InputFrame frame) { if (udp != null) return udp.TryReceive(tick, out frame); frame = default(InputFrame); return false; }
        public void SendScene(SceneCommand value) { if (udp != null) udp.SendScene(value); }
        public bool TryReceiveScene(out SceneCommand value) { if (udp != null) return udp.TryReceiveScene(out value); value = default(SceneCommand); return false; }
        public void SendContext(SessionContext value) { if (udp != null) udp.SendContext(value); }
        public bool TryReceiveContext(out SessionContext value) { if (udp != null) return udp.TryReceiveContext(out value); value = default(SessionContext); return false; }
        public void SendPlayerState(PlayerStateSnapshot value) { if (udp != null) udp.SendPlayerState(value); }
        public bool TryReceivePlayerState(out PlayerStateSnapshot value) { if (udp != null) return udp.TryReceivePlayerState(out value); value = default(PlayerStateSnapshot); return false; }
        public void Dispose() { disposed = true; if (udp != null) udp.Dispose(); else socket.Close(); }
    }
}
