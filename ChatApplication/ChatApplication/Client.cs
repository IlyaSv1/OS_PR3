using System.Net;
using System.Net.Sockets;
using System.Text;

class Client
{
    private static readonly object tcpSendLock = new object();
    private static string nickname;

    public static void Start()
    {
        Console.Write("Введите ник: ");
        nickname = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(nickname))
            nickname = "Anonymous";

        Console.WriteLine("Выберите протокол:");
        Console.WriteLine("1 - TCP");
        Console.WriteLine("2 - UDP");

        string protocol = Console.ReadLine();

        if (protocol != "1" && protocol != "2")
        {
            Console.WriteLine("Неверный выбор");
            return;
        }

        Console.Write("IP (Enter = 127.0.0.1): ");
        string ip = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(ip))
            ip = "127.0.0.1";

        int defaultPort = protocol == "1" ? 12345 : 12346;

        Console.Write($"Порт (Enter = {defaultPort}): ");
        int port = int.TryParse(Console.ReadLine(), out int p) ? p : defaultPort;

        Console.Write("Локальный порт (Enter = авто): ");
        int localPort = int.TryParse(Console.ReadLine(), out int lp) ? lp : 0;

        if (protocol == "1")
            StartTcp(ip, port, localPort);
        else
            StartUdp(ip, port, localPort);
    }

    // ================= TCP =================

    private static void StartTcp(string ip, int port, int localPort)
    {
        try
        {
            TcpClient client = localPort == 0
                ? new TcpClient(ip, port)
                : new TcpClient(new IPEndPoint(IPAddress.Any, localPort));

            if (localPort != 0)
                client.Connect(ip, port);

            var stream = client.GetStream();

            // 🔥 регистрация
            SendTcp(stream, $"HELLO|{nickname}");

            new Thread(() => ReceiveTcp(stream)) { IsBackground = true }.Start();
            new Thread(() => HeartbeatTcp(stream)) { IsBackground = true }.Start();

            while (true)
            {
                string msg = Console.ReadLine();
                if (msg?.ToLower() == "exit") break;

                SendTcp(stream, msg);
            }

            client.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TCP ошибка: {ex.Message}");
        }
    }

    private static void SendTcp(NetworkStream stream, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message + "\n");

            lock (tcpSendLock)
            {
                stream.Write(data, 0, data.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка TCP отправки: {ex.Message}");
        }
    }

    private static void ReceiveTcp(NetworkStream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (true)
            {
                string msg = reader.ReadLine();
                if (msg == null) break;

                if (msg == "PING" || msg == "PONG")
                    continue;

                Console.WriteLine(msg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка TCP приема: {ex.Message}");
        }
    }

    private static void HeartbeatTcp(NetworkStream stream)
    {
        try
        {
            while (true)
            {
                Thread.Sleep(5000);

                lock (tcpSendLock)
                {
                    byte[] data = Encoding.UTF8.GetBytes("PING\n");
                    stream.Write(data, 0, data.Length);
                }
            }
        }
        catch { }
    }

    // ================= UDP =================

    private static void StartUdp(string ip, int port, int localPort)
    {
        try
        {
            UdpClient client = localPort == 0
                ? new UdpClient(0)
                : new UdpClient(localPort);

            IPEndPoint serverEP = new IPEndPoint(IPAddress.Parse(ip), port);

            // 🔥 регистрация
            SendUdp(client, serverEP, $"HELLO|{nickname}");

            new Thread(() => ReceiveUdp(client)) { IsBackground = true }.Start();
            new Thread(() => HeartbeatUdp(client, serverEP)) { IsBackground = true }.Start();

            while (true)
            {
                string msg = Console.ReadLine();
                if (msg?.ToLower() == "exit") break;

                SendUdp(client, serverEP, msg);
            }

            client.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UDP ошибка: {ex.Message}");
        }
    }

    private static void SendUdp(UdpClient client, IPEndPoint ep, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, ep);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка UDP отправки: {ex.Message}");
        }
    }

    private static void ReceiveUdp(UdpClient client)
    {
        try
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] data = client.Receive(ref remote);
                string msg = Encoding.UTF8.GetString(data);

                if (msg == "PING" || msg == "PONG")
                    continue;

                Console.WriteLine(msg);
            }
        }
        catch (ObjectDisposedException)
        {

        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
        {

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка UDP приема: {ex.Message}");
        }
    }

    private static void HeartbeatUdp(UdpClient client, IPEndPoint ep)
    {
        try
        {
            while (true)
            {
                Thread.Sleep(5000);
                SendUdp(client, ep, "PING");
            }
        }
        catch { }
    }
}