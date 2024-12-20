using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Client
{
    public static void Start()
    {
        Console.WriteLine("Введите IP сервера (по умолчанию 127.0.0.1):");
        string serverIp = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(serverIp)) serverIp = "127.0.0.1";

        Console.WriteLine("Введите порт TCP сервера (по умолчанию 12345):");
        if (!int.TryParse(Console.ReadLine(), out int tcpPort)) tcpPort = 12345;

        Console.WriteLine("Введите порт UDP сервера (по умолчанию 12346):");
        if (!int.TryParse(Console.ReadLine(), out int udpPort)) udpPort = 12346;

        // Запуск UDP пинга в фоновом потоке
        new Thread(() => SendUdpPing(serverIp, udpPort)) { IsBackground = true }.Start();

        Console.WriteLine("Введите сообщение для отправки (для выхода введите 'exit'):");
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
}
