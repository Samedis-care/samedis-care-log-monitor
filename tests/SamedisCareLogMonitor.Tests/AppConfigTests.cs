using FluentAssertions;
using SamedisCare.Helper.Logging;
using SamedisCareLogMonitor;
using Xunit;

namespace SamedisCareLogMonitor.Tests;

/// <summary>
/// What the tool reads out of config.yml decides how the report mail leaves the host, so a
/// value that silently does not survive loading is a security defect rather than a nuisance:
/// with STARTTLS off, Mailer connects with SecureSocketOptions.None and the configured SMTP
/// credentials travel in plaintext.
/// </summary>
public class AppConfigTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lm-cfg-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private AppConfig Load(string yaml)
    {
        var path = Path.Combine(_folder, "config.yml");
        File.WriteAllText(path, yaml);
        return AppConfig.LoadFromYaml(path);
    }

    private const string SmtpConfig = """
        mail:
          enabled: true
          provider: "smtp"
          from: "monitor@example.org"
          smtp:
            server: "relay.example.org"
            port: 587
            username: "monitor"
            password: "secret"
            use_start_tls: {0}
        """;

    [Fact]
    public void Use_start_tls_true_survives_loading()
        => Load(SmtpConfig.Replace("{0}", "true")).Mail.Smtp.UseStartTls
            .Should().BeTrue("a config that asks for STARTTLS must get it -- otherwise the SMTP credentials go out in plaintext");

    [Fact]
    public void Use_start_tls_false_stays_false()
        => Load(SmtpConfig.Replace("{0}", "false")).Mail.Smtp.UseStartTls.Should().BeFalse();

    [Fact]
    public void The_rest_of_the_smtp_block_is_read_too()
    {
        var smtp = Load(SmtpConfig.Replace("{0}", "true")).Mail.Smtp;
        smtp.Server.Should().Be("relay.example.org");
        smtp.Port.Should().Be(587);
    }

    // The loader tolerated unknown keys before, and an operator's config.yml carries keys from
    // older versions. Turning that off would fail the run on a stale key instead of ignoring it.
    [Fact]
    public void An_unknown_key_does_not_fail_the_load()
        => ((Action)(() => Load("monitor:\n  levels: [\"ERROR\"]\nsomething_we_removed: 42\n")))
            .Should().NotThrow();

    [Fact]
    public void A_missing_file_is_reported_as_such()
        => ((Action)(() => AppConfig.LoadFromYaml(Path.Combine(_folder, "not-there.yml"))))
            .Should().Throw<FileNotFoundException>();
}

/// <summary>
/// There is deliberately no implicit default for use_ssl / use_start_tls, so the remaining
/// hazard is that a config which does not ask for encryption looks exactly like one that
/// does: the send succeeds and logs "sent successfully" while the SMTP password goes over
/// the wire. These pin the warning that closes that gap.
/// </summary>
public class SmtpPlaintextWarningTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lm-warn-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class CapturingLog : ISyncLog
    {
        public List<string> Warnings { get; } = new();
        public int Level => 2;
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? ex = null) { }
        public void Debug(string message) { }
    }

    private List<string> WarningsFor(string smtpBlock)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, "mail:\n  enabled: true\n  provider: \"smtp\"\n  smtp:\n"
                                + "    server: \"relay.example.org\"\n    port: 587\n"
                                + "    username: \"monitor\"\n    password: \"secret\"\n"
                                + smtpBlock);
        var log = new CapturingLog();
        Program.WarnIfSmtpCredentialsWouldGoOutInClear(AppConfig.LoadFromYaml(path).Mail, log);
        return log.Warnings;
    }

    // The three inputs that reach SecureSocketOptions.None with nothing to show for it.
    [Theory]
    // (1) the key omitted -- allowed, and it meant STARTTLS before the package migration
    [InlineData("")]
    // (2) the undocumented spelling the released main accepted via a YamlMember alias
    [InlineData("    use_starttls: true\n")]
    // (3) a typo, swallowed by ignoreUnmatchedProperties, landing on the false default
    [InlineData("    use_start_tsl: true\n")]
    public void A_config_that_does_not_ask_for_encryption_is_warned_about(string block)
        => WarningsFor(block).Should().ContainSingle()
            .Which.Should().Contain("use_start_tls").And.Contain("in the clear");

    [Theory]
    [InlineData("    use_start_tls: true\n")]
    [InlineData("    use_ssl: true\n")]
    public void An_encrypted_transport_is_not_warned_about(string block)
        => WarningsFor(block).Should().BeEmpty();

    // No credentials to leak, and an unauthenticated local relay is a legitimate setup.
    [Fact]
    public void A_blank_username_is_not_warned_about()
    {
        var path = Path.Combine(_folder, "nouser.yml");
        File.WriteAllText(path, "mail:\n  enabled: true\n  provider: \"smtp\"\n  smtp:\n"
                                + "    server: \"localhost\"\n    port: 1025\n    username: \"\"\n");
        var log = new CapturingLog();
        Program.WarnIfSmtpCredentialsWouldGoOutInClear(AppConfig.LoadFromYaml(path).Mail, log);
        log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void A_non_smtp_provider_is_not_warned_about()
    {
        var path = Path.Combine(_folder, "graph.yml");
        File.WriteAllText(path, "mail:\n  enabled: true\n  provider: \"graph\"\n  smtp:\n"
                                + "    username: \"monitor\"\n");
        var log = new CapturingLog();
        Program.WarnIfSmtpCredentialsWouldGoOutInClear(AppConfig.LoadFromYaml(path).Mail, log);
        log.Warnings.Should().BeEmpty();
    }
}

/// <summary>
/// A section header with nothing under it is a normal intermediate state while setting the
/// tool up, and YamlDotNet does not treat it like a missing section: it overwrites the
/// property initialiser with null. Measured on YamlDotNet 16.3.0 -- "logging:" alone gives
/// Logging == null, while omitting the key entirely leaves the initialiser intact. Every
/// consumer then has to null-check, or the run dies on a bare NullReferenceException with no
/// line saying which section is at fault. LoadFromYaml normalises instead, so an empty
/// section means "defaults" exactly like a missing one.
/// </summary>
public class EmptySectionTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lm-empty-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private AppConfig Load(string yaml)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, yaml);
        return AppConfig.LoadFromYaml(path);
    }

    [Fact]
    public void Every_empty_top_level_section_still_yields_defaults()
    {
        var config = Load("logging:\nmonitor:\nprograms:\nmail:\n");

        config.Logging.Should().NotBeNull();
        config.Monitor.Should().NotBeNull();
        config.Programs.Should().NotBeNull();
        config.Mail.Should().NotBeNull();

        // The defaults have to be the real ones, not just a non-null object.
        config.Logging.Level.Should().Be(1);
        config.Monitor.Levels.Should().BeEquivalentTo(new[] { "ERROR", "WARN" });
        config.Monitor.MaxEntriesPerProgram.Should().Be(500);
        config.Programs.Should().BeEmpty("an empty programs section is the 'nothing configured yet' state");
    }

    [Fact]
    public void An_empty_mail_transport_section_still_yields_defaults()
    {
        var mail = Load("mail:\n  enabled: true\n  provider: \"smtp\"\n  smtp:\n  graph:\n  gmail:\n").Mail;

        mail.Smtp.Should().NotBeNull();
        mail.Graph.Should().NotBeNull();
        mail.Gmail.Should().NotBeNull();
    }

    // Program.Main is not inside a try, so this is the difference between a diagnosis and
    // exit code 134 with nothing in the log.
    [Fact]
    public void An_empty_smtp_section_does_not_take_the_warning_down_with_it()
    {
        var mail = Load("mail:\n  enabled: true\n  provider: \"smtp\"\n  smtp:\n").Mail;

        ((Action)(() => Program.WarnIfSmtpCredentialsWouldGoOutInClear(mail, new ThrowawayLog())))
            .Should().NotThrow<NullReferenceException>();
    }

    // Same shape one level up: a half-filled mail block must not stop the scan from reporting.
    [Fact]
    public void A_config_that_is_only_section_headers_is_usable()
    {
        var config = Load("logging:\nmonitor:\nprograms:\nmail:\n");

        ((Action)(() =>
        {
            _ = config.Logging.Level;
            _ = config.Logging.Mode;
            _ = config.Programs.Count;
            _ = new LogScanner(new ThrowawayLog(), config.Monitor);
            _ = config.Mail.Subject;
            _ = config.Mail.Enabled;
        })).Should().NotThrow("these are the dereferences Program.Main makes before any try block");
    }

    private sealed class ThrowawayLog : ISyncLog
    {
        public int Level => 0;
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void Debug(string message) { }
    }
}
