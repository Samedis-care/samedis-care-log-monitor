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
    // Matches a real log entry start: "yyyy-MM-dd HH:mm:ss <LEVEL> <message>".
    // Lines that do not match are treated as continuation of the previous entry.
    private static readonly Regex LineRegex = new(
      @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+(\S+)\s?(.*)$",
      RegexOptions.Compiled);

    private static readonly string[] FileNameDateFormats =
    {
      "dd.MM.yyyy", "d.M.yyyy", "MM.dd.yyyy", "yyyy-MM-dd", "M/d/yyyy", "dd-MM-yyyy"
    };

    private readonly Helper _helper;
    private readonly MonitorConfig _config;
    private readonly HashSet<string> _levels;

    public LogScanner(Helper helper, MonitorConfig config)
    {
      _helper = helper;
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
        _helper.Message($"[{programName}] Log-Ordner nicht gefunden: '{folder}'.", 1, "WARN");
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
        _helper.Message($"[{programName}] Keine Logdatei in '{folder}' gefunden.", 1, "WARN");
        return result;
      }

      result.LogFilePath = newest.FullName;
      result.LogDate = ResolveLogDate(newest);

      if (_config.WarnIfNoRunToday && result.LogDate.HasValue && result.LogDate.Value.Date < DateTime.Now.Date)
      {
        result.Stale = true;
        result.Notes.Add($"Kein aktueller Lauf – neueste Logdatei ist vom {result.LogDate.Value:yyyy-MM-dd}.");
        _helper.Message($"[{programName}] Kein aktueller Lauf (Logdatei vom {result.LogDate.Value:yyyy-MM-dd}).", 1, "WARN");
      }

      ParseFile(newest.FullName, result);

      _helper.Message(
        $"[{programName}] {Path.GetFileName(newest.FullName)}: {result.ErrorCount} ERROR, {result.WarningCount} WARN.",
        1);

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
        _helper.Message($"[{result.Name}] Logdatei konnte nicht gelesen werden: {ex.Message}", 1, "ERROR");
        return;
      }

      foreach (var line in lines)
      {
        var match = LineRegex.Match(line);
        if (!match.Success)
        {
          // Continuation of the previous problem entry (stack trace / JSON / header).
          if (current != null)
            current.Message += "\n" + line;
          continue;
        }

        // A new proper log entry begins here; it is not a continuation anymore.
        var level = match.Groups[2].Value;
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
          Timestamp = match.Groups[1].Value,
          Level = level,
          Message = match.Groups[3].Value
        };
        result.Problems.Add(current);
      }

      if (truncated)
        result.Notes.Add($"Detailmenge auf {cap} Einträge gekürzt (weitere vorhanden).");
    }

    private static DateTime? ResolveLogDate(FileInfo file)
    {
      // Expected pattern: Logfile_dd.MM.yyyy.log — extract the date part.
      var name = Path.GetFileNameWithoutExtension(file.Name);
      var underscore = name.LastIndexOf('_');
      if (underscore >= 0 && underscore < name.Length - 1)
      {
        var datePart = name[(underscore + 1)..];
        foreach (var fmt in FileNameDateFormats)
        {
          if (DateTime.TryParseExact(datePart, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;
        }
      }

      // Fallback: last write time.
      return file.LastWriteTime.Date;
    }
  }
}
