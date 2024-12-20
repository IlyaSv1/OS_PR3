using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

class Client
{
    public static void Start()
    {
        var config = LoadConfig("config.json");

        string serverIp = config.ServerIp ?? "127.0.0.1";
        int tcpPort = config.TcpPort ?? 12345;
        int udpPort = config.UdpPort ?? 12346;

        Console.WriteLine("Введите сообщение для отправки (для выхода введите 'exit'):");

        // Запуск UDP пинга в фоновом потоке
        new Thread(() => SendUdpPing(serverIp, udpPort)) { IsBackground = true }.Start();

        while (true)
        {
            string message = Console.ReadLine();
            if (message.ToLower() == "exit") break;

            // Отправка TCP сообщения
            SendMessageTcp(serverIp, tcpPort, message);
        }
    }

    private static void SendMessageTcp(string serverIp, int tcpPort, string message)
    {
        try
        {
            using var client = new TcpClient(serverIp, tcpPort);
            NetworkStream stream = client.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);

            byte[] response = new byte[1024];
            int bytesRead = stream.Read(response, 0, response.Length);
            string responseMessage = Encoding.UTF8.GetString(response, 0, bytesRead);
            Console.WriteLine($"[TCP] Ответ от сервера: {responseMessage}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка TCP клиента: {ex.Message}");
            Console.WriteLine("Ошибка TCP клиента.");
        }
    }

    private static void SendUdpPing(string serverIp, int udpPort)
    {
        using var udpClient = new UdpClient();
        while (true)
        {
            try
            {
                string pingMessage = "Я в сети";
                byte[] data = Encoding.UTF8.GetBytes(pingMessage);
                udpClient.Send(data, data.Length, serverIp, udpPort);
                Logger.Log("[UDP] Пинг отправлен.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка UDP клиента: {ex.Message}");
            }

            Thread.Sleep(1000000);
        }
    }

    private static Config LoadConfig(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Config>(json);
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
