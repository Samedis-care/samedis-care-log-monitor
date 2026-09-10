using SamedisCare.Mail;
using SamedisCare.Helper.Config;

namespace SamedisCareLogMonitor
{
  public class AppConfig
  {
    public LoggingConfig Logging { get; set; } = new LoggingConfig();

    /// <summary>
    /// Hash: program name -> folder that contains that program's log files.
    /// Keys are used verbatim (naming convention does not touch dictionary keys).
    /// </summary>
    public Dictionary<string, string> Programs { get; set; } = new();

    public MonitorConfig Monitor { get; set; } = new MonitorConfig();
    public MailSettings Mail { get; set; } = new();

    /// <summary>
    /// Reads config.yml. Unknown keys are tolerated, as they were before, so a key left over
    /// from an older version does not fail the run.
    /// </summary>
    public static AppConfig LoadFromYaml(string filePath)
      => ConfigStore.Load<AppConfig>(filePath, ignoreUnmatchedProperties: true);
  }

  public class LoggingConfig
  {
    public int Level { get; set; } = 1;
    public int Mode { get; set; } = 3;
  }

  public class MonitorConfig
  {
    /// <summary>
    /// Log level tokens that count as a problem (matched case-insensitively on
    /// the level field, not by text search). Defaults to ERROR and WARN.
    /// </summary>
    public List<string> Levels { get; set; } = new() { "ERROR", "WARN" };

    /// <summary>
    /// When true, a program whose newest log file is not from today is reported
    /// as a warning ("no recent run").
    /// </summary>
    public bool WarnIfNoRunToday { get; set; } = true;

    /// <summary>
    /// Cap on how many problem entries per program are put into the detail
    /// attachment. Truncation is noted in the report. 0 or negative = no cap.
    /// </summary>
    public int MaxEntriesPerProgram { get; set; } = 500;
  }

  // MailConfig, SmtpConfig, GraphMailConfig and GmailConfig used to live here -- property for
  // property the same ones as in samedis-care-requests-to-mail. They now come from
  // SamedisCare.Mail, where the transports read them. An existing config.yml keeps working:
  // the keys this tool has ever documented are use_ssl / use_start_tls, and those map onto
  // the package properties unchanged.
}
