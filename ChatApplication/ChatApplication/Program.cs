using System;

class Program
{
    static void Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("Завершение работы...");
            Logger.Log("Программа завершена.");
            e.Cancel = true;
        };

        if (args.Length > 0 && args[0] == "--server")
        {
            Server.Start();
        }
        else if (args.Length > 0 && args[0] == "--client")
        {
            Client.Start();
        }
        else
        {
            Console.WriteLine("Использование:");
            Console.WriteLine("  --server : запустить в режиме сервера");
            Console.WriteLine("  --client : запустить в режиме клиента");
        }
    }
}
