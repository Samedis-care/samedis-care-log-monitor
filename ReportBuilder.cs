using System.Text;

namespace SamedisCareLogMonitor
{
  /// <summary>
  /// Aggregated report over all scanned programs, ready to be mailed.
  /// </summary>
  public class Report
  {
    public List<ProgramResult> Programs { get; set; } = new();
    public DateTime GeneratedAt { get; set; }

    public int TotalErrors => Programs.Sum(p => p.ErrorCount);
    public int TotalWarnings => Programs.Sum(p => p.WarningCount);
    public bool HasIssues => Programs.Any(p => p.HasIssues);
    public string Status => HasIssues ? "PROBLEME" : "OK";
  }

  public static class ReportBuilder
  {
    public static Report Build(IEnumerable<ProgramResult> results, DateTime generatedAt)
    {
      return new Report
      {
        Programs = results.ToList(),
        GeneratedAt = generatedAt
      };
    }

    public static string BuildSubject(string? template, Report report)
    {
      template ??= "Samedis Log-Monitor {{Date}} – {{Status}} ({{ErrorCount}} Fehler, {{WarningCount}} Warnungen)";
      return template
        .Replace("{{Date}}", report.GeneratedAt.ToString("yyyy-MM-dd"))
        .Replace("{{Status}}", report.Status)
        .Replace("{{ErrorCount}}", report.TotalErrors.ToString())
        .Replace("{{WarningCount}}", report.TotalWarnings.ToString());
    }

    public static string BuildHtmlBody(Report report)
    {
      var sb = new StringBuilder();
      sb.AppendLine("<!DOCTYPE html>");
      sb.AppendLine("<html lang=\"de\"><head><meta charset=\"utf-8\">");
      sb.AppendLine("<style>");
      sb.AppendLine("body,table,th,td,p{font-family:Aptos, Verdana, Arial, sans-serif;font-size:14px;}");
      sb.AppendLine("table{border-collapse:collapse;margin-top:12px;}");
      sb.AppendLine("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left;}");
      sb.AppendLine("th{background:#f2f2f2;}");
      sb.AppendLine(".ok{color:#1a7f37;font-weight:bold;}");
      sb.AppendLine(".bad{color:#b00020;font-weight:bold;}");
      sb.AppendLine(".num-bad{color:#b00020;font-weight:bold;text-align:right;}");
      sb.AppendLine(".num{text-align:right;}");
      sb.AppendLine("</style></head><body>");

      var statusClass = report.HasIssues ? "bad" : "ok";
      sb.AppendLine($"<h2>Samedis Log-Monitor – <span class=\"{statusClass}\">{HtmlEscape(report.Status)}</span></h2>");
      sb.AppendLine($"<p>Erstellt: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}<br>");
      sb.AppendLine($"Programme geprüft: {report.Programs.Count} &middot; " +
                    $"Fehler gesamt: {report.TotalErrors} &middot; Warnungen gesamt: {report.TotalWarnings}</p>");

      if (!report.HasIssues)
      {
        sb.AppendLine("<p class=\"ok\">Alles OK – keine Fehler oder Warnungen in den geprüften Logs.</p>");
      }

      sb.AppendLine("<table><thead><tr>");
      sb.AppendLine("<th>Programm</th><th>Fehler</th><th>Warnungen</th><th>Status / Logdatei</th>");
      sb.AppendLine("</tr></thead><tbody>");

      foreach (var p in report.Programs)
      {
        var errClass = p.ErrorCount > 0 ? "num-bad" : "num";
        var warnClass = p.WarningCount > 0 ? "num-bad" : "num";
        var statusText = new StringBuilder();
        statusText.Append(HtmlEscape(p.LogFilePath ?? "—"));
        foreach (var note in p.Notes)
          statusText.Append($"<br><em>{HtmlEscape(note)}</em>");

        sb.AppendLine("<tr>");
        sb.AppendLine($"<td>{HtmlEscape(p.Name)}</td>");
        sb.AppendLine($"<td class=\"{errClass}\">{p.ErrorCount}</td>");
        sb.AppendLine($"<td class=\"{warnClass}\">{p.WarningCount}</td>");
        sb.AppendLine($"<td>{statusText}</td>");
        sb.AppendLine("</tr>");
      }

      sb.AppendLine("</tbody></table>");
      sb.AppendLine("<p>Details siehe angehängte Logdatei.</p>");
      sb.AppendLine("</body></html>");
      return sb.ToString();
    }

    /// <summary>
    /// Plain-text detail report used as mail attachment.
    /// </summary>
    public static string BuildDetailText(Report report)
    {
      var sb = new StringBuilder();
      sb.AppendLine("Samedis Log-Monitor – Detailbericht");
      sb.AppendLine($"Erstellt: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
      sb.AppendLine($"Status: {report.Status}");
      sb.AppendLine($"Programme geprüft: {report.Programs.Count} | Fehler gesamt: {report.TotalErrors} | Warnungen gesamt: {report.TotalWarnings}");
      sb.AppendLine();

      foreach (var p in report.Programs)
      {
        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"Programm : {p.Name}");
        sb.AppendLine($"Ordner   : {p.ConfiguredFolder}");
        sb.AppendLine($"Logdatei : {p.LogFilePath ?? "—"}");
        if (p.LogDate.HasValue)
          sb.AppendLine($"Log-Datum: {p.LogDate.Value:yyyy-MM-dd}");
        sb.AppendLine($"Ergebnis : {p.ErrorCount} ERROR, {p.WarningCount} WARN{(p.Stale ? ", VERALTET/OHNE LAUF" : "")}");
        foreach (var note in p.Notes)
          sb.AppendLine($"Hinweis  : {note}");
        sb.AppendLine(new string('-', 80));

        if (p.Problems.Count == 0)
        {
          sb.AppendLine(p.Stale ? "(kein aktueller Lauf – keine Meldungen ausgewertet)" : "(keine Auffälligkeiten)");
        }
        else
        {
          foreach (var entry in p.Problems)
            sb.AppendLine($"{entry.Timestamp} {entry.Level} {entry.Message}");
        }
        sb.AppendLine();
      }

      return sb.ToString();
    }

    /// <summary>
    /// Concise plain-text body used as the multipart/alternative text part, so
    /// clients that do not render HTML still show a readable summary. The full
    /// details remain in the attachment.
    /// </summary>
    public static string BuildTextBody(Report report)
    {
      var sb = new StringBuilder();
      sb.AppendLine($"Samedis Log-Monitor – Status: {report.Status}");
      sb.AppendLine($"Erstellt: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
      sb.AppendLine($"Programme geprüft: {report.Programs.Count} | Fehler gesamt: {report.TotalErrors} | Warnungen gesamt: {report.TotalWarnings}");
      sb.AppendLine();

      if (!report.HasIssues)
        sb.AppendLine("Alles OK – keine Fehler oder Warnungen in den geprüften Logs.");

      foreach (var p in report.Programs)
      {
        var flags = p.Stale ? " [kein aktueller Lauf]" : "";
        sb.AppendLine($"- {p.Name}: {p.ErrorCount} ERROR, {p.WarningCount} WARN{flags}");
        foreach (var note in p.Notes)
          sb.AppendLine($"    Hinweis: {note}");
      }

      sb.AppendLine();
      sb.AppendLine("Details siehe angehängte Logdatei.");
      return sb.ToString();
    }

    private static string HtmlEscape(string input) => System.Net.WebUtility.HtmlEncode(input);
  }
}
