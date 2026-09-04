using SamedisCare.Mail;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

    public static AppConfig LoadFromYaml(string filePath)
    {
      using var input = File.OpenText(filePath);
      var deserializerBuilder = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties();
      var deserializer = deserializerBuilder.Build();
      var result = deserializer.Deserialize<AppConfig>(input) ?? new AppConfig();
      if (result.Mail?.Smtp?.UseStartTlsLegacy is bool legacyValue)
        result.Mail.Smtp.UseStartTls = legacyValue;
      return result;
    }
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

  // MailConfig, SmtpConfig, GraphMailConfig und GmailConfig standen hier -- Eigenschaft
  // fuer Eigenschaft dieselben wie in samedis-care-requests-to-mail. Sie kommen jetzt aus
  // SamedisCare.Mail, wo die Transporte sie lesen. Ein bestehendes config.yml passt
  // unveraendert weiter.
}
