using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Server
{
    private static List<TcpClient> connectedClients = new List<TcpClient>();
    private static HashSet<IPEndPoint> udpClients = new HashSet<IPEndPoint>();

    private static Dictionary<TcpClient, DateTime> tcpLastSeen = new();
    private static Dictionary<IPEndPoint, DateTime> udpLastSeen = new();

    private static readonly object clientListLock = new object();
    private static readonly object udpLock = new object();

    private static bool isRunning = true;
    private static UdpClient udpServer;

    public static void Start()
    {
        // ================= ВВОД =================

        Console.Write("Введите IP сервера (Enter = 127.0.0.1): ");
        string ipInput = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(ipInput))
            ipInput = "127.0.0.1";

        IPAddress ipAddress = IPAddress.Parse(ipInput);

        Console.Write("Введите TCP порт (Enter = 12345): ");
        string tcpInput = Console.ReadLine();
        int tcpPort = string.IsNullOrWhiteSpace(tcpInput) ? 12345 : int.Parse(tcpInput);

        Console.Write("Введите UDP порт (Enter = 12346): ");
        string udpInput = Console.ReadLine();
        int udpPort = string.IsNullOrWhiteSpace(udpInput) ? 12346 : int.Parse(udpInput);

        // ================= СЕРВЕР =================

        TcpListener tcpListener = new TcpListener(ipAddress, tcpPort);
        udpServer = new UdpClient(new IPEndPoint(ipAddress, udpPort));

        tcpListener.Start();

        Logger.Log($"TCP сервер запущен на {ipAddress}:{tcpPort}");
        Logger.Log($"UDP сервер запущен на {ipAddress}:{udpPort}");

        // ================= ОСТАНОВКА =================

        Console.CancelKeyPress += (sender, e) =>
        {
            Logger.Log("Завершение работы сервера...");
            isRunning = false;

            tcpListener.Stop();
            udpServer.Close();

            lock (clientListLock)
            {
                foreach (var client in connectedClients)
                    client.Close();

                connectedClients.Clear();
                tcpLastSeen.Clear();
            }

            e.Cancel = true;
        };

        // ================= ПОТОКИ =================

        new Thread(() => AcceptTcpClients(tcpListener)) { IsBackground = true }.Start();
        new Thread(() => ReceiveUdpMessages()) { IsBackground = true }.Start();
        new Thread(CheckClientsAlive) { IsBackground = true }.Start();

        Logger.Log("Сервер запущен. Ctrl+C для выхода.");

        while (isRunning)
            Thread.Sleep(500);
    }

    // ================= TCP =================

    private static void AcceptTcpClients(TcpListener tcpListener)
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();

                lock (clientListLock)
                {
                    connectedClients.Add(client);
                    tcpLastSeen[client] = DateTime.Now;
                }

                Logger.Log("Новый TCP клиент подключен");

                new Thread(() => HandleTcpClient(client)) { IsBackground = true }.Start();
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Logger.Log($"Ошибка TCP: {ex.Message}");
            }
        }
    }

    private static void HandleTcpClient(TcpClient client)
    {
        try
        {
            using NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (isRunning)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                ProcessMessage(message, "TCP", client, null);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка TCP клиента: {ex.Message}");
        }
        finally
        {
            lock (clientListLock)
            {
                connectedClients.Remove(client);
                tcpLastSeen.Remove(client);
            }

            client.Close();
        }
    }

    // ================= UDP =================

    private static void ReceiveUdpMessages()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEP = null;
                byte[] data = udpServer.Receive(ref remoteEP);

                string message = Encoding.UTF8.GetString(data);

                ProcessMessage(message, "UDP", null, remoteEP);
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Logger.Log($"Ошибка UDP: {ex.Message}");
            }
        }
    }

    // ================= ЛОГИКА =================

    private static void ProcessMessage(
        string message,
        string protocol,
        TcpClient senderTcp = null,
        IPEndPoint senderUdp = null)
    {
        // 🔥 СНАЧАЛА обновляем lastSeen
        if (protocol == "TCP" && senderTcp != null)
        {
            lock (clientListLock)
                tcpLastSeen[senderTcp] = DateTime.Now;
        }

        if (protocol == "UDP" && senderUdp != null)
        {
            lock (udpLock)
            {
                udpClients.Add(senderUdp);
                udpLastSeen[senderUdp] = DateTime.Now;
            }
        }

        // 🔥 heartbeat обработка
        if (message == "PING")
        {
            if (protocol == "TCP" && senderTcp != null)
                SendTcp(senderTcp, "PONG");

            if (protocol == "UDP" && senderUdp != null)
                SendUdp(senderUdp, "PONG");

            return;
        }

        if (message == "PONG")
            return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string fullMessage = $"[{timestamp}] [{protocol}] {message}";

        Logger.Log(fullMessage);

        BroadcastTcp(fullMessage, senderTcp);
        BroadcastUdp(fullMessage, senderUdp);
    }

    // ================= ОТПРАВКА =================

    private static void SendTcp(TcpClient client, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.GetStream().Write(data, 0, data.Length);
        }
        catch { }
    }

    private static void SendUdp(IPEndPoint endpoint, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpServer.Send(data, data.Length, endpoint);
        }
        catch { }
    }

    // ================= BROADCAST =================

    private static void BroadcastTcp(string message, TcpClient sender)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (clientListLock)
        {
            foreach (var client in connectedClients.ToList())
            {
                if (client == sender) continue;

                try
                {
                    if (client.Connected)
                        client.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    client.Close();
                    connectedClients.Remove(client);
                    tcpLastSeen.Remove(client);
                }
            }
        }
    }

    private static void BroadcastUdp(string message, IPEndPoint sender)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (udpLock)
        {
            foreach (var endpoint in udpClients.ToList())
            {
                if (sender != null && endpoint.Equals(sender))
                    continue;

                try
                {
                    udpServer.Send(data, data.Length, endpoint);
                }
                catch
                {
                    udpClients.Remove(endpoint);
                    udpLastSeen.Remove(endpoint);
                }
            }
        }
    }

    // ================= CHECK =================

    private static void CheckClientsAlive()
    {
        while (isRunning)
        {
            Thread.Sleep(10000);
            DateTime now = DateTime.Now;

            // TCP
            lock (clientListLock)
            {
                foreach (var client in connectedClients.ToList())
                {
                    if (!tcpLastSeen.ContainsKey(client) ||
                        (now - tcpLastSeen[client]).TotalSeconds > 15)
                    {
                        Logger.Log("TCP клиент отключен (timeout)");
                        client.Close();
                        connectedClients.Remove(client);
                        tcpLastSeen.Remove(client);
                    }
                }
            }

            // UDP
            lock (udpLock)
            {
                foreach (var ep in udpClients.ToList())
                {
                    if (!udpLastSeen.ContainsKey(ep) ||
                        (now - udpLastSeen[ep]).TotalSeconds > 15)
                    {
                        Logger.Log("UDP клиент удален (timeout)");
                        udpClients.Remove(ep);
                        udpLastSeen.Remove(ep);
                    }
                }
            }
        }
    }
}