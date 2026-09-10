using System.Globalization;
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
    // Invariant, not interpolated with the ambient culture: an interpolated hole formats with
    // CurrentCulture even with a fixed specifier, which yields log-monitor-report_2569-09-10
    // on th-TH. The name is the mail attachment name and the local copy, so it should stay ISO.
    var reportFileName =
      $"log-monitor-report_{report.GeneratedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";
    var localReportPath = Path.Combine("log", reportFileName);
    await File.WriteAllTextAsync(localReportPath, detailText, Encoding.UTF8);
    log.Debug($"Detail report written to {localReportPath}");

    // send mail (once), also on "all OK"
    if (config.Mail.Enabled)
    {
      WarnIfSmtpCredentialsWouldGoOutInClear(config.Mail, log);

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

  /// <summary>
  /// Says out loud when the configured SMTP credentials are about to travel unencrypted.
  /// </summary>
  /// <remarks>
  /// There is no implicit default for use_ssl / use_start_tls, which is deliberate -- but it
  /// means an omitted key, the undocumented use_starttls spelling, or a typo (swallowed by
  /// ignoreUnmatchedProperties) all end in SecureSocketOptions.None with AUTH on top, and the
  /// log line is the ordinary "sent successfully". The defect behind issue #2867 stayed hidden
  /// for exactly that reason, so this is the same class of problem left unattended.
  ///
  /// The provider is normalised the way Mailer.SendAsync normalises it, so this warns for the
  /// runs that actually go over SMTP. A blank username is not warned about: there are then no
  /// credentials to leak, and an unauthenticated local relay is a legitimate setup.
  ///
  /// This belongs in SamedisCare.Mail.Mailer, where it would cover every consumer and sit at
  /// the actual send. It lives here until the package is released again.
  /// </remarks>
  internal static void WarnIfSmtpCredentialsWouldGoOutInClear(MailSettings mail, ISyncLog log)
  {
    var smtp = mail.Smtp;
    if ((mail.Provider ?? "smtp").Trim().ToLowerInvariant() != "smtp") return;
    if (string.IsNullOrWhiteSpace(smtp.Username)) return;
    if (smtp.UseSsl || smtp.UseStartTls) return;

    log.Warn("mail.smtp: neither use_ssl nor use_start_tls is set, so the connection to "
             + $"{smtp.Server}:{smtp.Port} is unencrypted and the configured username/password "
             + "will be sent in the clear. Set use_start_tls: true (port 587) or use_ssl: true "
             + "(port 465).");
  }
}
