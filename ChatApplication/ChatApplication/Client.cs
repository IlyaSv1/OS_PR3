using System;
using System.Net.Sockets;
using System.Text;

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

        Console.WriteLine("Введите сообщение для отправки (для выхода введите 'exit'):");

        using (var udpClient = new UdpClient())
        {
            while (true)
            {
                string message = Console.ReadLine();
                if (message.ToLower() == "exit") break;

                // Отправка TCP сообщения
                SendMessageTcp(serverIp, tcpPort, message);

                // Отправка UDP сообщения
                byte[] data = Encoding.UTF8.GetBytes(message);
                udpClient.Send(data, data.Length, serverIp, udpPort);
                Console.WriteLine("[UDP] Сообщение отправлено.");
            }
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
}
