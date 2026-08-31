using SamedisCare.Helper.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SamedisCareLogMonitor
{
  /// <summary>
  /// A single problem line extracted from a log file (ERROR/WARN), including any
  /// continuation lines (stack traces, JSON, HTTP headers) that followed it.
  /// </summary>
  public class ProblemEntry
  {
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
  }

  /// <summary>
  /// Scan result for a single configured program.
  /// </summary>
  public class ProgramResult
  {
    public string Name { get; set; } = "";
    public string ConfiguredFolder { get; set; } = "";
    public string? LogFilePath { get; set; }
    public DateTime? LogDate { get; set; }
    public List<ProblemEntry> Problems { get; set; } = new();

    /// <summary>Non-fatal notes about the scan itself (missing folder, stale log, truncation).</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>True when no current log file could be evaluated or it is not from today.</summary>
    public bool Stale { get; set; }

    public int ErrorCount => Problems.Count(p => string.Equals(p.Level, "ERROR", StringComparison.OrdinalIgnoreCase));
    // Everything that is a problem but not an ERROR is counted as a warning.
    public int WarningCount => Problems.Count - ErrorCount;

    /// <summary>A program has issues when it has problems or is stale.</summary>
    public bool HasIssues => Problems.Count > 0 || Stale;
  }

  public class LogScanner
  {
    private readonly ISyncLog log;
    private readonly MonitorConfig _config;
    private readonly HashSet<string> _levels;

    public LogScanner(ISyncLog syncLog, MonitorConfig config)
    {
      log = syncLog;
      _config = config;
      _levels = new HashSet<string>(
        (config.Levels ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()),
        StringComparer.OrdinalIgnoreCase);
      if (_levels.Count == 0)
      {
        _levels.Add("ERROR");
        _levels.Add("WARN");
      }
    }

    public ProgramResult Scan(string programName, string folder)
    {
      var result = new ProgramResult { Name = programName, ConfiguredFolder = folder };

      if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
      {
        result.Stale = true;
        result.Notes.Add($"Log-Ordner nicht gefunden: '{folder}'.");
        log.Warn($"[{programName}] Log-Ordner nicht gefunden: '{folder}'.");
        return result;
      }

      var newest = new DirectoryInfo(folder)
        .GetFiles("*.log", SearchOption.TopDirectoryOnly)
        .OrderByDescending(f => f.LastWriteTime)
        .FirstOrDefault();

      if (newest == null)
      {
        result.Stale = true;
        result.Notes.Add($"Keine Logdatei (*.log) in '{folder}' gefunden.");
        log.Warn($"[{programName}] Keine Logdatei in '{folder}' gefunden.");
        return result;
      }

      result.LogFilePath = newest.FullName;
      result.LogDate = ResolveLogDate(newest);

      if (_config.WarnIfNoRunToday && result.LogDate.HasValue && result.LogDate.Value.Date < DateTime.Now.Date)
      {
        result.Stale = true;
        result.Notes.Add($"Kein aktueller Lauf – neueste Logdatei ist vom {result.LogDate.Value:yyyy-MM-dd}.");
        log.Warn($"[{programName}] Kein aktueller Lauf (Logdatei vom {result.LogDate.Value:yyyy-MM-dd}).");
      }

      ParseFile(newest.FullName, result);

      log.Info($"[{programName}] {Path.GetFileName(newest.FullName)}: {result.ErrorCount} ERROR, {result.WarningCount} WARN.");

      return result;
    }

    private void ParseFile(string path, ProgramResult result)
    {
      var cap = _config.MaxEntriesPerProgram;
      var truncated = false;
      ProblemEntry? current = null; // the last emitted problem entry, for continuation lines

      IEnumerable<string> lines;
      try
      {
        lines = File.ReadLines(path);
      }
      catch (Exception ex)
      {
        result.Notes.Add($"Logdatei konnte nicht gelesen werden: {ex.Message}");
        log.Error($"[{result.Name}] Logdatei konnte nicht gelesen werden: {ex.Message}");
        return;
      }

      foreach (var line in lines)
      {
        // Through LogFormat, so the shape of a line lives in one place with the writer
        // that produces it. Getting this wrong is silent: a line that does not parse is not
        // an error here, it is the continuation of the entry above -- a format that had
        // drifted would fold every ERROR into the text before it and report nothing at all.
        if (!LogFormat.TryParse(line, out var entry))
        {
          // Continuation of the previous problem entry (stack trace / JSON / header).
          if (current != null)
            current.Message += "\n" + line;
          continue;
        }

        // A new proper log entry begins here; it is not a continuation anymore.
        var level = entry.Level;
        if (!_levels.Contains(level))
        {
          current = null;
          continue;
        }

        if (cap > 0 && result.Problems.Count >= cap)
        {
          truncated = true;
          current = null; // stop attaching continuations once capped
          continue;
        }

        current = new ProblemEntry
        {
          Timestamp = entry.At.ToString(LogFormat.TimeFormat),
          Level = level,
          Message = entry.Message
        };
        result.Problems.Add(current);
      }

      if (truncated)
        result.Notes.Add($"Detailmenge auf {cap} Einträge gekürzt (weitere vorhanden).");
    }

    private static DateTime? ResolveLogDate(FileInfo file)
    {
      // The six candidate formats that used to stand here were tolerance for a name the
      // tools built with ToShortDateString(), which follows the machine's culture. They all
      // write LogFormat.FileName now, so one is enough.
      if (LogFormat.TryParseFileName(file.Name, out var parsed))
        return parsed;

      // Fallback: last write time.
      return file.LastWriteTime.Date;
    }
  }
}
