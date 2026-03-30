using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static List<TcpClient> tcpClients = new();
    private static Dictionary<TcpClient, DateTime> tcpLastSeen = new();
    private static Dictionary<TcpClient, string> tcpNames = new();

    private static HashSet<IPEndPoint> udpClients = new();
    private static Dictionary<IPEndPoint, DateTime> udpLastSeen = new();
    private static Dictionary<IPEndPoint, string> udpNames = new();

    private static readonly object tcpLock = new();
    private static readonly object udpLock = new();
    private static readonly object tcpSendLock = new();

    private static bool isRunning = true;
    private static UdpClient udpServer;

    public static void Start()
    {
        Console.Write("IP (Enter = 127.0.0.1): ");
        string ip = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(ip))
            ip = "127.0.0.1";

        Console.Write("TCP порт (Enter = 12345): ");
        int tcpPort = int.TryParse(Console.ReadLine(), out int tp) ? tp : 12345;

        Console.Write("UDP порт (Enter = 12346): ");
        int udpPort = int.TryParse(Console.ReadLine(), out int up) ? up : 12346;

        IPAddress ipAddr = IPAddress.Parse(ip);

        TcpListener tcpListener = new TcpListener(ipAddr, tcpPort);
        udpServer = new UdpClient(new IPEndPoint(ipAddr, udpPort));

        tcpListener.Start();

        Console.WriteLine($"TCP: {ip}:{tcpPort}");
        Console.WriteLine($"UDP: {ip}:{udpPort}");

        Console.CancelKeyPress += (s, e) =>
        {
            isRunning = false;
            tcpListener.Stop();
            udpServer.Close();
            e.Cancel = true;
        };

        new Thread(() => AcceptTcp(tcpListener)) { IsBackground = true }.Start();
        new Thread(ReceiveUdp) { IsBackground = true }.Start();
        new Thread(CheckAlive) { IsBackground = true }.Start();

        while (isRunning)
            Thread.Sleep(500);
    }

    // ================= TCP =================

    private static void AcceptTcp(TcpListener listener)
    {
        while (isRunning)
        {
            try
            {
                var client = listener.AcceptTcpClient();

                lock (tcpLock)
                {
                    tcpClients.Add(client);
                    tcpLastSeen[client] = DateTime.Now;
                }

                new Thread(() => HandleTcp(client)) { IsBackground = true }.Start();
            }
            catch { }
        }
    }

    private static void HandleTcp(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (isRunning)
            {
                string msg = reader.ReadLine();
                if (msg == null) break;

                ProcessMessage(msg, "TCP", client, null);
            }
        }
        catch { }
        finally
        {
            RemoveTcp(client);
        }
    }

    private static void RemoveTcp(TcpClient client)
    {
        string name = tcpNames.ContainsKey(client) ? tcpNames[client] : "unknown";

        lock (tcpLock)
        {
            tcpClients.Remove(client);
            tcpLastSeen.Remove(client);
            tcpNames.Remove(client);
        }

        client.Close();
        Console.WriteLine($"Клиент отключен [TCP] {name}");
    }

    // ================= UDP =================

    private static void ReceiveUdp()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint ep = null;
                byte[] data = udpServer.Receive(ref ep);

                string msg = Encoding.UTF8.GetString(data);

                ProcessMessage(msg, "UDP", null, ep);
            }
            catch { }
        }
    }

    // ================= ОБРАБОТКА =================

    private static void ProcessMessage(string msg, string protocol,
        TcpClient tcp = null, IPEndPoint udp = null)
    {
        // обновляем активность
        if (tcp != null)
        {
            lock (tcpLock)
                tcpLastSeen[tcp] = DateTime.Now;
        }

        if (udp != null)
        {
            lock (udpLock)
            {
                udpClients.Add(udp);
                udpLastSeen[udp] = DateTime.Now;
            }
        }

        // ===== HELLO =====
        if (msg.StartsWith("HELLO"))
        {
            string nick = msg.Contains("|") ? msg.Split('|')[1] : "Anonymous";

            if (tcp != null)
                lock (tcpLock) tcpNames[tcp] = nick;

            if (udp != null)
                lock (udpLock) udpNames[udp] = nick;

            Console.WriteLine($"Клиент подключен [{protocol}] {nick}");
            return;
        }

        // ===== HEARTBEAT =====
        if (msg == "PING")
        {
            if (tcp != null) SendTcp(tcp, "PONG");
            if (udp != null) SendUdp(udp, "PONG");
            return;
        }

        if (msg == "PONG")
            return;

        // ===== MSG с latency =====
        if (msg.StartsWith("MSG"))
        {
            var parts = msg.Split('|');

            if (parts.Length >= 3)
            {
                long ticks = long.Parse(parts[1]);
                string text = parts[2];

                long nowTicks = DateTime.UtcNow.Ticks;
                double ms = (nowTicks - ticks) / 10000.0;

                string name = tcp != null
                    ? tcpNames.GetValueOrDefault(tcp, "Unknown")
                    : udpNames.GetValueOrDefault(udp, "Unknown");

                string time = DateTime.Now.ToString("HH:mm:ss");

                string final = $"[{time}] [{protocol}] {name}: {text} (delay={ms:F1} ms)";

                Console.WriteLine(final);

                Broadcast(final, tcp, udp);
                return;
            }
        }

        // fallback
        Console.WriteLine(msg);
    }

    // ================= BROADCAST =================

    private static void Broadcast(string msg, TcpClient senderTcp, IPEndPoint senderUdp)
    {
        // TCP
        lock (tcpLock)
        {
            foreach (var c in tcpClients.ToList())
            {
                if (c == senderTcp) continue;
                SendTcp(c, msg);
            }
        }

        // UDP
        lock (udpLock)
        {
            foreach (var ep in udpClients.ToList())
            {
                if (senderUdp != null && ep.Equals(senderUdp)) continue;
                SendUdp(ep, msg);
            }
        }
    }

    // ================= SEND =================

    private static void SendTcp(TcpClient client, string msg)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");

            lock (tcpSendLock)
                client.GetStream().Write(data, 0, data.Length);
        }
        catch
        {
            RemoveTcp(client);
        }
    }

    private static void SendUdp(IPEndPoint ep, string msg)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);
            udpServer.Send(data, data.Length, ep);
        }
        catch
        {
            lock (udpLock)
            {
                udpClients.Remove(ep);
                udpLastSeen.Remove(ep);
                udpNames.Remove(ep);
            }

            Console.WriteLine($"UDP клиент удален {ep}");
        }
    }

    // ================= TIMEOUT =================

    private static void CheckAlive()
    {
        while (isRunning)
        {
            Thread.Sleep(10000);
            DateTime now = DateTime.Now;

            lock (tcpLock)
            {
                foreach (var c in tcpClients.ToList())
                {
                    if ((now - tcpLastSeen[c]).TotalSeconds > 15)
                    {
                        Console.WriteLine("TCP timeout");
                        RemoveTcp(c);
                    }
                }
            }

            lock (udpLock)
            {
                foreach (var ep in udpClients.ToList())
                {
                    if ((now - udpLastSeen[ep]).TotalSeconds > 15)
                    {
                        Console.WriteLine($"UDP timeout {ep}");
                        udpClients.Remove(ep);
                        udpLastSeen.Remove(ep);
                        udpNames.Remove(ep);
                    }
                }
            }
        }
    }
}