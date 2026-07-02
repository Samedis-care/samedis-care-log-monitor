namespace SamedisCareLogMonitor
{
  public class Helper
  {
    /// <summary>
    /// LogLevel 0: turned off
    /// LogLevel 1: normal output
    /// LogLevel 2: debug output
    /// </summary>
    public int LogLevel = 1;
    /// <summary>
    /// LogMode 0: no output
    /// LogMode 1: Console Output
    /// LogMode 2: LogFile
    /// LogMode 3: Console and Logfile
    /// </summary>
    public int LogMode = 3;
    public string LogFile = "debug.log";

    public void Message(string message, int logLevel = 1, string logType = "INFO")
    {
      if (logLevel > LogLevel) return;
      const string format = "yyyy-MM-dd HH:mm:ss";

      if (LogMode == 1 || LogMode == 3)
      {
        Console.WriteLine(new string('*', 80));
        Console.WriteLine(DateTime.Now.ToString(format) + " " + message);
      }

      if (LogMode < 2) return;
      Directory.CreateDirectory("log");
      var logContent = string.Empty;
      logContent += DateTime.Now.ToString(format) + " ";
      logContent += logType + " ";
      if (!string.IsNullOrEmpty(message))
        logContent += message;
      File.AppendAllText(Path.Combine("log", LogFile), logContent + "\n");
    }

    internal void MessageAndExit(string errorMessage)
    {
      Message(errorMessage, 1, "ERROR");
      Environment.Exit(1);
    }
  }
}
