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
        var config = LoadConfig("config.json");

        int tcpPort = config.TcpPort ?? 12345;
        int udpPort = config.UdpPort ?? 12346;

        Console.WriteLine($"TCP порт: {tcpPort}");
        Console.WriteLine($"UDP порт: {udpPort}");

        TcpListener tcpListener = new TcpListener(IPAddress.Any, tcpPort);
        udpServer = new UdpClient(udpPort);

        udpServer.Client.IOControl(
            (IOControlCode)0x9800000C,
            new byte[] { 0 },
            null
        );

        tcpListener.Start();

        Logger.Log($"TCP сервер запущен на порту {tcpPort}");
        Logger.Log($"UDP сервер запущен на порту {udpPort}");

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

                // 👇 передаём отправителя
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
        BroadcastUdp(fullMessage, senderUdp); // 👈 теперь передаём отправителя
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
                // ❗ ГЛАВНОЕ ИСПРАВЛЕНИЕ
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

    // ================= CONFIG =================

    private static Config LoadConfig(string filePath)
    {
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                string json = System.IO.File.ReadAllText(filePath);
                return System.Text.Json.JsonSerializer.Deserialize<Config>(json);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка конфигурации: {ex.Message}");
        }

        return new Config();
    }
}

class Config
{
    public string ServerIp { get; set; }
    public int? TcpPort { get; set; }
    public int? UdpPort { get; set; }
}