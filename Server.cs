using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Server
{
    public static void Start()
    {
        int tcpPort = 12345;
        int udpPort = 12346;

        Console.WriteLine($"Запуск TCP сервера на порту {tcpPort}...");
        Console.WriteLine($"Запуск UDP сервера на порту {udpPort}...");

        TcpListener tcpListener = new TcpListener(IPAddress.Any, tcpPort);
        UdpClient udpServer = new UdpClient(udpPort);

        tcpListener.Start();
        Logger.Log($"TCP сервер запущен на порту {tcpPort}");
        Logger.Log($"UDP сервер запущен на порту {udpPort}");

        // Запуск потоков для обработки TCP и UDP
        new Thread(() => AcceptTcpClients(tcpListener)) { IsBackground = true }.Start();
        new Thread(() => ReceiveUdpMessages(udpServer)) { IsBackground = true }.Start();

        Console.WriteLine("Серверы запущены. Нажмите Ctrl+C для завершения.");
        Thread.Sleep(Timeout.Infinite);
    }

    private static void AcceptTcpClients(TcpListener tcpListener)
    {
        while (true)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                new Thread(() => HandleTcpClient(client)) { IsBackground = true }.Start();
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
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            Console.WriteLine($"[TCP] Получено сообщение: {message}");
            Logger.Log($"[TCP] Получено сообщение: {message}");

            string response = "Сообщение получено!";
            byte[] responseData = Encoding.UTF8.GetBytes(response);
            stream.Write(responseData, 0, responseData.Length);
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка обработки TCP клиента: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private static void ReceiveUdpMessages(UdpClient udpServer)
    {
        while (true)
        {
            try
            {
                IPEndPoint remoteEP = null;
                byte[] receivedData = udpServer.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(receivedData);
                Console.WriteLine($"[UDP] Получено сообщение от {remoteEP}: {message}");
                Logger.Log($"[UDP] Получено сообщение от {remoteEP}: {message}");

                string response = "Принято!";
                byte[] responseData = Encoding.UTF8.GetBytes(response);
                udpServer.Send(responseData, responseData.Length, remoteEP);
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка UDP: {ex.Message}");
            }
        }
    }
}
