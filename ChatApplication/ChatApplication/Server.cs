using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Server
{
    private static List<TcpClient> connectedClients = new List<TcpClient>();
    private static HashSet<IPEndPoint> udpClients = new HashSet<IPEndPoint>();

    private static readonly object clientListLock = new object();
    private static readonly object udpLock = new object();

    private static bool isRunning = true;
    private static UdpClient udpServer;

    public static void Start()
    {
        // ================= ВВОД НАСТРОЕК =================

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

        // ================= СОЗДАНИЕ СЕРВЕРА =================

        TcpListener tcpListener = new TcpListener(ipAddress, tcpPort);
        udpServer = new UdpClient(new IPEndPoint(ipAddress, udpPort));

        udpServer.Client.IOControl(
            (IOControlCode)0x9800000C,
            new byte[] { 0 },
            null
        );

        tcpListener.Start();

        Logger.Log($"TCP сервер запущен на {ipAddress}:{tcpPort}");
        Logger.Log($"UDP сервер запущен на {ipAddress}:{udpPort}");

        // ================= ОСТАНОВКА =================

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\nЗавершение работы сервера...");
            isRunning = false;

            tcpListener.Stop();
            udpServer.Close();

            lock (clientListLock)
            {
                foreach (var client in connectedClients)
                    client.Close();

                connectedClients.Clear();
            }

            e.Cancel = true;
        };

        // ================= ЗАПУСК ПОТОКОВ =================

        new Thread(() => AcceptTcpClients(tcpListener)) { IsBackground = true }.Start();
        new Thread(() => ReceiveUdpMessages()) { IsBackground = true }.Start();

        Console.WriteLine("Сервер запущен. Ctrl+C для выхода.");

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
                    connectedClients.Add(client);

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
                connectedClients.Remove(client);

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

                lock (udpLock)
                    udpClients.Add(remoteEP);

                ProcessMessage(message, "UDP", null, remoteEP);
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Logger.Log($"Ошибка UDP: {ex.Message}");
            }
        }
    }

    // ================= ОБЩАЯ ЛОГИКА =================

    private static void ProcessMessage(
        string message,
        string protocol,
        TcpClient senderTcp = null,
        IPEndPoint senderUdp = null)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string fullMessage = $"[{timestamp}] [{protocol}] {message}";

        Console.WriteLine(fullMessage);
        Logger.Log(fullMessage);

        BroadcastTcp(fullMessage, senderTcp);
        BroadcastUdp(fullMessage, senderUdp);
    }

    // ================= РАССЫЛКА =================

    private static void BroadcastTcp(string message, TcpClient sender)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (clientListLock)
        {
            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(data, 0, data.Length);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Ошибка отправки TCP: {ex.Message}");
                }
            }
        }
    }

    private static void BroadcastUdp(string message, IPEndPoint sender)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (udpLock)
        {
            foreach (var endpoint in udpClients)
            {
                if (sender != null && endpoint.Equals(sender))
                    continue;

                try
                {
                    udpServer.Send(data, data.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Ошибка отправки UDP: {ex.Message}");
                }
            }
        }
    }
}