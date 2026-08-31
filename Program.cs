using System.Text;
using SamedisCare.Helper.Logging;
using SamedisCare.Mail;

namespace SamedisCareLogMonitor;

internal class Program
{
  static async Task Main(string[] args)
  {
    // The name goes through LogFormat. It used to be built with ToShortDateString(), which
    // follows the machine's culture -- which is why the scanner below carried six candidate
    // date formats to find a file it had written itself.
    //
    // Bootstrapped at the previous defaults; the configured level and mode are applied once
    // config.yml has been read.
    ISyncLog log = new FileSyncLog(1, LogMode.Both,
                                   Path.Combine("log", LogFormat.FileName(DateTime.Now)));

    // read config
    var ymlFilePath = args.Length > 0 ? args[0] : "config.yml";
    if (!File.Exists(ymlFilePath))
      Abort.With(log, $"The file {ymlFilePath} does not exists. Stopping log monitor.");

    AppConfig config;
    try
    {
      config = AppConfig.LoadFromYaml(ymlFilePath);
    }
    catch (YamlDotNet.Core.YamlException ex)
    {
      var where = ex.Start.Line > 0 ? $" (Zeile {ex.Start.Line}, Spalte {ex.Start.Column})" : "";
      var hint = ex.Message.Contains("escape", StringComparison.OrdinalIgnoreCase)
        ? " Hinweis: Windows-Pfade mit Backslash in EINFACHE Anführungszeichen setzen, z. B. 'D:\\samedis\\...\\log' bzw. '\\\\server\\d$\\...\\log' – in doppelten Anführungszeichen ist \"\\\" ein Escape-Zeichen."
        : "";
      Abort.With(log, $"Konfiguration {ymlFilePath} konnte nicht gelesen werden{where}: {ex.Message}.{hint}");
      return;
    }
    catch (Exception ex)
    {
      Abort.With(log, $"Konfiguration {ymlFilePath} konnte nicht gelesen werden: {ex.Message}");
      return;
    }

    log = new FileSyncLog(config.Logging.Level, (LogMode)config.Logging.Mode,
                          Path.Combine("log", LogFormat.FileName(DateTime.Now)));

    log.Info("Log monitor started.");

    if (config.Programs.Count == 0)
    {
      log.Warn("No programs configured under 'programs' in config.yml. Nothing to check.");
    }

    // scan every configured program's newest log file
    var scanner = new LogScanner(log, config.Monitor);
    var results = new List<ProgramResult>();
    foreach (var (name, folder) in config.Programs)
    {
      results.Add(scanner.Scan(name, folder));
    }

    // build report
    var report = ReportBuilder.Build(results, DateTime.Now);
    var subject = ReportBuilder.BuildSubject(config.Mail.Subject, report);
    var htmlBody = ReportBuilder.BuildHtmlBody(report);
    var textBody = ReportBuilder.BuildTextBody(report);
    var detailText = ReportBuilder.BuildDetailText(report);

    log.Info($"Report: status={report.Status}, programs={report.Programs.Count}, errors={report.TotalErrors}, warnings={report.TotalWarnings}.");

    // always keep a local copy of the detail report next to the log
    Directory.CreateDirectory("log");
    var reportFileName = $"log-monitor-report_{report.GeneratedAt:yyyy-MM-dd}.log";
    var localReportPath = Path.Combine("log", reportFileName);
    await File.WriteAllTextAsync(localReportPath, detailText, Encoding.UTF8);
    log.Debug($"Detail report written to {localReportPath}");

    // send mail (once), also on "all OK"
    if (config.Mail.Enabled)
    {
      var attachment = new MailAttachment(
        reportFileName,
        Encoding.UTF8.GetBytes(detailText),
        "text/plain; charset=utf-8");

      var mailer = new Mailer(config.Mail, log, "SamedisCareLogMonitor");
      var sent = await mailer.SendAsync(
        new MailMessage(config.Mail.From ?? string.Empty, mailer.Recipients(),
                        subject, htmlBody, textBody, new[] { attachment }),
        "log monitor report");
      if (!sent)
        log.Warn("Report mail was not sent (see previous messages).");
    }
    else
    {
      log.Info("Mail disabled in config.yml – skipping send.");
    }

    log.Info("Log monitor finished.");
  }
}
