using System.Net;
using System.Net.Sockets;
using System.Text;

class Client
{
    public static void Start()
    {
        Console.WriteLine("Выберите протокол:");
        Console.WriteLine("1 - TCP");
        Console.WriteLine("2 - UDP");

        string protocol = Console.ReadLine();

        if (protocol != "1" && protocol != "2")
        {
            Console.WriteLine("Неверный выбор протокола");
            return;
        }

        // ================= ВВОД С ДЕФОЛТАМИ =================

        Console.Write("Введите IP сервера (Enter = 127.0.0.1): ");
        string serverIp = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(serverIp))
            serverIp = "127.0.0.1";

        int defaultPort = protocol == "1" ? 12345 : 12346;

        Console.Write($"Введите порт сервера (Enter = {defaultPort}): ");
        string portInput = Console.ReadLine();
        int serverPort = string.IsNullOrWhiteSpace(portInput)
            ? defaultPort
            : int.Parse(portInput);

        Console.Write("Введите локальный порт (Enter = авто): ");
        string localInput = Console.ReadLine();
        int localPort = string.IsNullOrWhiteSpace(localInput)
            ? 0
            : int.Parse(localInput);

        Console.WriteLine("Введите сообщение (exit для выхода):");

        // ================= TCP =================
        if (protocol == "1")
        {
            try
            {
                TcpClient client = localPort == 0
                    ? new TcpClient(serverIp, serverPort)
                    : new TcpClient(new IPEndPoint(IPAddress.Any, localPort));

                if (localPort != 0)
                    client.Connect(serverIp, serverPort);

                NetworkStream stream = client.GetStream();

                // 🔥 прием сообщений
                new Thread(() => ReceiveTcpMessages(stream))
                {
                    IsBackground = true
                }.Start();

                // 🔥 heartbeat
                new Thread(() => SendHeartbeatTcp(stream))
                {
                    IsBackground = true
                }.Start();

                while (true)
                {
                    string message = Console.ReadLine();
                    if (message?.ToLower() == "exit") break;

                    SendMessageTcp(stream, message);
                }

                client.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка TCP клиента: {ex.Message}");
            }
        }

        // ================= UDP =================
        else
        {
            try
            {
                UdpClient udpClient = localPort == 0
                    ? new UdpClient(0)
                    : new UdpClient(localPort);

                IPEndPoint serverEP = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

                // 🔥 прием
                new Thread(() => ReceiveUdpMessages(udpClient))
                {
                    IsBackground = true
                }.Start();

                // 🔥 heartbeat
                new Thread(() => SendHeartbeatUdp(udpClient, serverEP))
                {
                    IsBackground = true
                }.Start();

                while (true)
                {
                    string message = Console.ReadLine();
                    if (message?.ToLower() == "exit") break;

                    SendMessageUdp(udpClient, serverEP, message);
                }

                udpClient.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка UDP клиента: {ex.Message}");
            }
        }
    }

    // ================= HEARTBEAT =================

    private static void SendHeartbeatTcp(NetworkStream stream)
    {
        try
        {
            while (true)
            {
                Thread.Sleep(5000);
                byte[] data = Encoding.UTF8.GetBytes("PING");
                stream.Write(data, 0, data.Length);
            }
        }
        catch { }
    }

    private static void SendHeartbeatUdp(UdpClient client, IPEndPoint serverEP)
    {
        try
        {
            while (true)
            {
                Thread.Sleep(5000);
                byte[] data = Encoding.UTF8.GetBytes("PING");
                client.Send(data, data.Length, serverEP);
            }
        }
        catch { }
    }

    // ================= TCP =================

    private static void SendMessageTcp(NetworkStream stream, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);

            PrintLocal("TCP", message);
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка отправки TCP: {ex.Message}");
        }
    }

    private static void ReceiveTcpMessages(NetworkStream stream)
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // ❗ игнор heartbeat
                if (message == "PING" || message == "PONG")
                    continue;

                PrintReceived("TCP", message);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка при получении TCP: {ex.Message}");
        }
    }

    // ================= UDP =================

    private static void SendMessageUdp(UdpClient client, IPEndPoint serverEP, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, serverEP);

            PrintLocal("UDP", message);
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка UDP отправки: {ex.Message}");
        }
    }

    private static void ReceiveUdpMessages(UdpClient client)
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] data = client.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data);

                // ❗ игнор heartbeat
                if (message == "PING" || message == "PONG")
                    continue;

                PrintReceived("UDP", message);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка при получении UDP: {ex.Message}");
        }
    }

    // ================= ВЫВОД =================

    private static void PrintLocal(string protocol, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"[{timestamp}] Вы ({protocol}): {message}");
    }

    private static void PrintReceived(string transport, string message)
    {
        try
        {
            int firstBracketEnd = message.IndexOf(']');
            int secondBracketStart = message.IndexOf('[', firstBracketEnd + 1);
            int secondBracketEnd = message.IndexOf(']', secondBracketStart + 1);

            if (secondBracketStart == -1 || secondBracketEnd == -1)
                return;

            string source = message.Substring(
                secondBracketStart + 1,
                secondBracketEnd - secondBracketStart - 1);

            string text = message.Substring(secondBracketEnd + 2);

            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            Console.WriteLine($"[{timestamp}] {source} -> {transport}: {text}");
        }
        catch
        {
            // игнор
        }
    }
}