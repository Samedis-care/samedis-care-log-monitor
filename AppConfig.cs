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
      => Normalize(ConfigStore.Load<AppConfig>(filePath, ignoreUnmatchedProperties: true));

    /// <summary>
    /// Makes an empty section mean "defaults", the same as a missing one.
    /// </summary>
    /// <remarks>
    /// A section header with nothing under it -- the normal intermediate state while setting
    /// the tool up -- is not the same as an absent one to YamlDotNet: it sets the property, and
    /// the value it sets is null, overwriting the initialiser above. Measured on YamlDotNet
    /// 16.3.0: "logging:" alone gives Logging == null, while omitting the key leaves the
    /// initialiser intact.
    ///
    /// Every consumer would otherwise need its own null check. Program.Main is not inside a
    /// try, so the alternative is a bare NullReferenceException and exit code 134 with nothing
    /// in the log naming the section at fault -- a poor neighbour to the care taken over YAML
    /// syntax errors, which report line, column and a hint about Windows paths.
    ///
    /// This is not a hidden default: a missing section already means defaults, and the whole
    /// point here is that the two spellings stop behaving differently.
    /// </remarks>
    private static AppConfig Normalize(AppConfig config)
    {
      config.Logging ??= new LoggingConfig();
      config.Monitor ??= new MonitorConfig();
      config.Programs ??= new();
      config.Mail ??= new();

      // The transports come from SamedisCare.Mail and carry the same kind of initialiser.
      config.Mail.Smtp ??= new();
      config.Mail.Graph ??= new();
      config.Mail.Gmail ??= new();

      return config;
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

  // MailConfig, SmtpConfig, GraphMailConfig and GmailConfig used to live here -- property for
  // property the same ones as in samedis-care-requests-to-mail. They now come from
  // SamedisCare.Mail, where the transports read them. An existing config.yml keeps working
  // for every key this tool has ever documented -- use_ssl / use_start_tls map onto the
  // package properties unchanged.
  //
  // The one thing that does not survive: the undocumented use_starttls spelling, which the
  // released main accepted through a [YamlMember] alias. Under UnderscoredNamingConvention
  // the package property maps to use_start_tls_legacy, and ignoreUnmatchedProperties eats the
  // old name, so such a key now loads as "unset". It has to be renamed to use_start_tls.
  // WarnIfSmtpCredentialsWouldGoOutInClear in Program.cs is what makes that audible.
}
