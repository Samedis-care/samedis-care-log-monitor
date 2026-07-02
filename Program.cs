using System.Text;

namespace SamedisCareLogMonitor;

internal class Program
{
  static async Task Main(string[] args)
  {
    // set log
    var helper = new Helper
    {
      LogFile = "Logfile_" + DateTime.Now.ToShortDateString() + ".log",
    };

    // read config
    var ymlFilePath = args.Length > 0 ? args[0] : "config.yml";
    if (!File.Exists(ymlFilePath))
      helper.MessageAndExit($"The file {ymlFilePath} does not exists. Stopping log monitor.");

    var config = AppConfig.LoadFromYaml(ymlFilePath);

    helper.LogLevel = config.Logging.Level;
    helper.LogMode = config.Logging.Mode;

    helper.Message("Log monitor started.", 1);

    if (config.Programs.Count == 0)
    {
      helper.Message("No programs configured under 'programs' in config.yml. Nothing to check.", 1, "WARN");
    }

    // scan every configured program's newest log file
    var scanner = new LogScanner(helper, config.Monitor);
    var results = new List<ProgramResult>();
    foreach (var (name, folder) in config.Programs)
    {
      results.Add(scanner.Scan(name, folder));
    }

    // build report
    var report = ReportBuilder.Build(results, DateTime.Now);
    var subject = ReportBuilder.BuildSubject(config.Mail.Subject, report);
    var htmlBody = ReportBuilder.BuildHtmlBody(report);
    var detailText = ReportBuilder.BuildDetailText(report);

    helper.Message(
      $"Report: status={report.Status}, programs={report.Programs.Count}, errors={report.TotalErrors}, warnings={report.TotalWarnings}.",
      1);

    // always keep a local copy of the detail report next to the log
    Directory.CreateDirectory("log");
    var reportFileName = $"log-monitor-report_{report.GeneratedAt:yyyy-MM-dd}.log";
    var localReportPath = Path.Combine("log", reportFileName);
    await File.WriteAllTextAsync(localReportPath, detailText, Encoding.UTF8);
    helper.Message($"Detail report written to {localReportPath}", 2);

    // send mail (once), also on "all OK"
    if (config.Mail.Enabled)
    {
      var attachment = new Mailer.MailAttachment(
        reportFileName,
        Encoding.UTF8.GetBytes(detailText),
        "text/plain; charset=utf-8");

      var mailer = new Mailer(config, helper);
      var sent = await mailer.SendReportEmailAsync(subject, htmlBody, attachment);
      if (!sent)
        helper.Message("Report mail was not sent (see previous messages).", 1, "WARN");
    }
    else
    {
      helper.Message("Mail disabled in config.yml – skipping send.", 1);
    }

    helper.Message("Log monitor finished.", 1);
  }
}
