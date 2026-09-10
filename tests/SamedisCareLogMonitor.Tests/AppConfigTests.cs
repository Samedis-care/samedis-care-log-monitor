using FluentAssertions;
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
