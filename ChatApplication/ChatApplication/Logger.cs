using System;
using System.IO;

class Logger
{
    private static readonly string logFile = "chat.log";

    public static void Log(string message)
    {
        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}";
        Console.WriteLine(logMessage);
        File.AppendAllText(logFile, logMessage + Environment.NewLine);
    }
}
