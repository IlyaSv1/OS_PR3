using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Server
{
    private static List<TcpClient> connectedClients = new List<TcpClient>();
    private static readonly object clientListLock = new object();
    private static bool isRunning = true;

    public static void Start()
    {
        var config = LoadConfig("config.json");

        string serverIp = config.ServerIp ?? "127.0.0.1";
        int tcpPort = config.TcpPort ?? 12345;
        int udpPort = config.UdpPort ?? 12346;

        Console.WriteLine($"Запуск TCP сервера на порту {tcpPort}...");
        Console.WriteLine($"Запуск UDP сервера на порту {udpPort}...");

        TcpListener tcpListener = new TcpListener(IPAddress.Any, tcpPort);
        UdpClient udpServer = new UdpClient(udpPort);

        tcpListener.Start();
        Logger.Log($"TCP сервер запущен на порту {tcpPort}");
        Logger.Log($"UDP сервер запущен на порту {udpPort}");

        // Обработчик завершения работы
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\nЗавершение работы сервера...");
            Logger.Log("Сервер завершает работу...");
            isRunning = false;

            // Остановить серверы
            tcpListener.Stop();
            udpServer.Close();

            lock (clientListLock)
            {
                foreach (var client in connectedClients)
                {
                    client.Close();
                }
                connectedClients.Clear();
            }

            e.Cancel = true;
        };

        // Запуск потоков для обработки TCP и UDP
        new Thread(() => AcceptTcpClients(tcpListener)) { IsBackground = true }.Start();
        new Thread(() => ReceiveUdpMessages(udpServer)) { IsBackground = true }.Start();

        Console.WriteLine("Серверы запущены. Нажмите Ctrl+C для завершения.");

        while (isRunning)
        {
            Thread.Sleep(500);
        }

        Logger.Log("Сервер завершил работу.");
        Console.WriteLine("Сервер завершил работу.");
    }

    private static void AcceptTcpClients(TcpListener tcpListener)
    {
        while (isRunning)
        {
            try
            {
                if (!tcpListener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }

                TcpClient client = tcpListener.AcceptTcpClient();
                lock (clientListLock)
                {
                    connectedClients.Add(client);
                }
                new Thread(() => HandleTcpClient(client)) { IsBackground = true }.Start();
            }
            catch (SocketException ex) when (!isRunning)
            {
                Logger.Log("TCP сервер остановлен.");
            }
            catch (Exception ex)
            {
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
                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Клиент отключился

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Console.WriteLine($"[{timestamp}] [TCP] Получено сообщение: {message}");
                    Logger.Log($"[{timestamp}] [TCP] Получено сообщение: {message}");

                    // Отправка сообщения всем клиентам
                    BroadcastMessage(message, client);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка обработки TCP клиента: {ex.Message}");
        }
        finally
        {
            lock (clientListLock)
            {
                connectedClients.Remove(client);
            }
            client.Close();
        }
    }


    private static void BroadcastMessage(string message, TcpClient sender)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (clientListLock)
        {
            foreach (var client in connectedClients)
            {
                if (client != sender)
                {
                    try
                    {
                        // Проверяем, подключен ли клиент перед отправкой сообщения
                        if (client.Connected)
                        {
                            NetworkStream stream = client.GetStream();
                            stream.Write(data, 0, data.Length);
                        }
                        else
                        {
                            Logger.Log("Не удается отправить сообщение. Клиент не подключен.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Ошибка отправки сообщения клиенту: {ex.Message}");
                    }
                }
            }
        }
    }



    private static HashSet<IPEndPoint> receivedUdpMessages = new HashSet<IPEndPoint>();

    private static void ReceiveUdpMessages(UdpClient udpServer)
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEP = null;
                byte[] receivedData = udpServer.Receive(ref remoteEP);

                // Проверяем, пришло ли сообщение с того же адреса и порта
                if (!receivedUdpMessages.Contains(remoteEP))
                {
                    receivedUdpMessages.Add(remoteEP);
                    string message = Encoding.UTF8.GetString(receivedData);
                    Console.WriteLine($"[UDP] Получено сообщение от {remoteEP}: {message}");
                    Logger.Log($"[UDP] Получено сообщение от {remoteEP}: {message}");
                }
            }
            catch (SocketException ex) when (!isRunning)
            {
                // Исключение ожидаемо при остановке сервера
                Logger.Log("UDP сервер остановлен.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка UDP: {ex.Message}");
            }
        }
    }


    private static Config LoadConfig(string filePath)
    {
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                string json = System.IO.File.ReadAllText(filePath);
                return System.Text.Json.JsonSerializer.Deserialize<Config>(json);
            }
            else
            {
                Logger.Log("Файл конфигурации не найден. Используются значения по умолчанию.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка чтения конфигурации: {ex.Message}");
        }

        return new Config(); // Возврат значений по умолчанию
    }
}

class Config
{
    public string ServerIp { get; set; }
    public int? TcpPort { get; set; }
    public int? UdpPort { get; set; }
}
