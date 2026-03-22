using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static List<TcpClient> tcpClients = new();
    private static Dictionary<TcpClient, DateTime> tcpLastSeen = new();

    private static HashSet<IPEndPoint> udpClients = new();
    private static Dictionary<IPEndPoint, DateTime> udpLastSeen = new();

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

                Console.WriteLine("TCP клиент подключен");

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
        lock (tcpLock)
        {
            tcpClients.Remove(client);
            tcpLastSeen.Remove(client);
        }

        client.Close();
        Console.WriteLine("TCP клиент отключен");
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

    // ================= ЛОГИКА =================

    private static void ProcessMessage(string msg, string protocol,
        TcpClient tcp = null, IPEndPoint udp = null)
    {
        // обновление активности
        if (protocol == "TCP" && tcp != null)
        {
            lock (tcpLock)
                tcpLastSeen[tcp] = DateTime.Now;
        }

        if (protocol == "UDP" && udp != null)
        {
            lock (udpLock)
            {
                udpClients.Add(udp);
                udpLastSeen[udp] = DateTime.Now;
            }
        }

        // регистрация UDP
        if (msg == "HELLO")
            return;

        // heartbeat
        if (msg == "PING")
        {
            if (tcp != null) SendTcp(tcp, "PONG");
            if (udp != null) SendUdp(udp, "PONG");
            return;
        }

        if (msg == "PONG")
            return;

        string time = DateTime.Now.ToString("HH:mm:ss");

        // 🔥 ЛОГ НА СЕРВЕРЕ (главное отличие)
        Console.WriteLine($"[{time}] {protocol} -> ALL: {msg}");

        // 🔥 отправка TCP
        lock (tcpLock)
        {
            foreach (var c in tcpClients.ToList())
            {
                if (c == tcp) continue;

                Console.WriteLine($"[{time}] {protocol} -> TCP");

                SendTcp(c, $"[{time}] {protocol}: {msg}");
            }
        }

        // 🔥 отправка UDP
        lock (udpLock)
        {
            foreach (var ep in udpClients.ToList())
            {
                if (udp != null && ep.Equals(udp)) continue;

                Console.WriteLine($"[{time}] {protocol} -> UDP");

                SendUdp(ep, $"[{time}] {protocol}: {msg}");
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
            {
                client.GetStream().Write(data, 0, data.Length);
            }
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
            }
        }
    }

    // ================= CHECK =================

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
                    if (!tcpLastSeen.ContainsKey(c) ||
                        (now - tcpLastSeen[c]).TotalSeconds > 15)
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
                    if (!udpLastSeen.ContainsKey(ep) ||
                        (now - udpLastSeen[ep]).TotalSeconds > 15)
                    {
                        Console.WriteLine("UDP timeout");
                        udpClients.Remove(ep);
                        udpLastSeen.Remove(ep);
                    }
                }
            }
        }
    }
}