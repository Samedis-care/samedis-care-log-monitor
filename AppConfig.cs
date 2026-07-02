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
    public MailConfig Mail { get; set; } = new MailConfig();

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

  public class MailConfig
  {
    public bool Enabled { get; set; } = false;
    public string? Provider { get; set; } = "smtp";
    public string? From { get; set; }
    public List<string> Recipients { get; set; } = new();
    public string? Subject { get; set; }
    public SmtpConfig Smtp { get; set; } = new SmtpConfig();
    public GraphMailConfig Graph { get; set; } = new GraphMailConfig();
    public GmailConfig Gmail { get; set; } = new GmailConfig();
  }

  public class SmtpConfig
  {
    public string? Server { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; } = false;
    public bool UseStartTls { get; set; } = true;
    public bool IgnoreCertificateErrors { get; set; } = false;
    [YamlMember(Alias = "use_starttls")]
    public bool? UseStartTlsLegacy { get; set; }
  }

  public class GraphMailConfig
  {
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? SenderUserPrincipalName { get; set; }
  }

  public class GmailConfig
  {
    public string? ServiceAccountJsonPath { get; set; }
    public string? ImpersonatedUser { get; set; }
  }
}
