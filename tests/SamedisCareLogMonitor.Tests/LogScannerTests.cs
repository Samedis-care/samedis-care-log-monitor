using FluentAssertions;
using SamedisCare.Helper.Logging;
using SamedisCareLogMonitor;
using Xunit;

namespace SamedisCareLogMonitor.Tests;

/// <summary>
/// The seam this tool lives on: it reads logs the sync tools write. These tests write with
/// the real <see cref="FileSyncLog"/> and read with the real scanner, so a format that drifts
/// apart fails here instead of in production -- where it would not fail at all, because a
/// line the scanner cannot parse is not an error to it but the continuation of the entry
/// above. A monitor that has gone blind reports a clean run.
/// </summary>
public class LogScannerTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lm-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string WriteLog(Action<ISyncLog> write)
    {
        var path = Path.Combine(_folder, LogFormat.FileName(DateTime.Now));
        write(new FileSyncLog(2, LogMode.File, path));
        return path;
    }

    private static ProgramResult Scan(string folder, params string[] levels)
        => new LogScanner(new ConsoleSyncLog(0),
                          new MonitorConfig { Levels = levels.ToList(), MaxEntriesPerProgram = 50 })
           .Scan("sync", folder);

    [Fact]
    public void What_a_sync_tool_writes_is_what_the_monitor_reads()
    {
        WriteLog(log =>
        {
            log.Info("Inventories Upload sync starting.");
            log.Warn("Skipped inventory row because catalog_id is missing.");
            log.Error("Failed to create building");
            log.Info("Sync finised.");
        });

        var result = Scan(_folder, "ERROR", "WARN");

        result.Problems.Should().HaveCount(2);
        result.Problems.Select(p => p.Level).Should().Equal("WARN", "ERROR");
        result.Problems[0].Message.Should().Be("Skipped inventory row because catalog_id is missing.");
    }

    [Fact]
    public void Only_the_configured_levels_are_reported()
    {
        WriteLog(log =>
        {
            log.Error("boom");
            log.Warn("careful");
            log.Debug("noise");
        });

        Scan(_folder, "ERROR").Problems.Should().ContainSingle()
            .Which.Level.Should().Be("ERROR");
    }

    // A stack trace belongs to the entry above it, not to itself.
    [Fact]
    public void A_multi_line_message_stays_one_problem()
    {
        WriteLog(log => log.Error("Database query failed\n   at Program.Main()\n   at Runner.Run()"));

        var problem = Scan(_folder, "ERROR").Problems.Should().ContainSingle().Subject;
        problem.Message.Should().Contain("Database query failed").And.Contain("at Runner.Run()");
    }

    [Fact]
    public void The_run_date_is_taken_from_the_file_name()
        => Scan(Path.GetDirectoryName(WriteLog(log => log.Info("x")))!, "ERROR")
           .LogDate.Should().Be(DateTime.Today);

    [Fact]
    public void A_folder_without_logs_is_reported_rather_than_read()
        => Scan(_folder, "ERROR").Problems.Should().BeEmpty();
}
