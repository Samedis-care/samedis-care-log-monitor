# Samedis Care Log Monitor

Liest die Logdateien der übrigen Samedis-Client-Tools aus, sucht nach `ERROR`- und
`WARN`-Einträgen und verschickt einmal täglich eine zusammenfassende E-Mail an ein
Postfach – inklusive einer Detail-Logdatei im Anhang. Gibt es keine Auffälligkeiten,
wird eine „Alles OK"-Mail versendet, damit man weiß, dass der Monitor selbst lief.

Alle Samedis-Tools schreiben ihre Logs im identischen Format
`yyyy-MM-dd HH:mm:ss <LEVEL> <message>` nach `log/Logfile_dd.MM.yyyy.log` (eine
Datei pro Tag). Der Monitor nutzt genau dieses Format.

## Shared libraries

This tool no longer carries its own copy of the API layer. It consumes:

| Package | What comes from it |
| --- | --- |
| `SamedisCare.Helper` | Logging and — the point of this tool — `LogFormat`, the shape of a log line |
| `SamedisCare.Mail` | Sending over SMTP, Microsoft Graph or the Gmail API |

The packages live in [samedis-care-dotnet](https://github.com/Samedis-care/samedis-care-dotnet).
Their versions are pinned in the `.csproj`; a local folder feed for trying an unpublished
change is described in that repository's README.

## Features
- Konfigurierbare Programm→Log-Ordner-Zuordnung (Hash in `config.yml`)
- Wertet pro Programm die **neueste** Logdatei aus (alle Läufe des Tages)
- Erkennt Probleme am **Level-Token** (nicht per Textsuche) – `WARN WARNING: …`
  wird also einmal gezählt, und `INFO/DEBUG`-Zeilen mit dem Wort „ERROR"/„WARNING"
  im Text lösen keinen Fehlalarm aus
- Mehrzeilige Meldungen (Stacktraces/JSON/HTTP-Header) werden korrekt an ihre
  Ausgangsmeldung angehängt
- Meldet ausgefallene Jobs: ist die neueste Logdatei nicht von heute (oder fehlt der
  Ordner ganz), wird das als Warnung im Bericht aufgeführt
- E-Mail-Versand via SMTP, Microsoft Graph oder Gmail (Service Account)
- Detailbericht als `text/plain`-Anhang; zusätzlich lokal unter `log/` abgelegt

## Tests

```bash
dotnet test -c Release
```

Die Tests schreiben mit dem echten `FileSyncLog` und lesen mit dem echten Scanner. Das ist die
Nahtstelle, auf der dieses Programm sitzt: es liest ein Format, das eine andere Anwendung
schreibt. Läuft das Format auseinander, meldet der Monitor **keinen Fehler** — eine Zeile, die
er nicht erkennt, gilt ihm als Fortsetzung des Eintrags darüber, und jedes `ERROR` verschwindet
im Text davor. Deshalb liegt das Format in `SamedisCare.Helper.Logging.LogFormat`, und beide
Seiten gehen darüber.

Aus demselben Grund führt der Scanner nur noch **ein** Datumsformat für Logdateinamen: die
sechs früheren waren Toleranz gegen einen Namen, den die Tools mit `ToShortDateString()`
bauten, also kulturabhängig.

## Installation

### 1) Konfigurieren
```bash
cp config.yml.example config.yml
# config.yml anpassen (Programm-Pfade, Empfänger, Mail-Provider)
```

### 2) Ausführen (dev)
```bash
dotnet run
# optional: abweichende Config angeben
dotnet run -- /pfad/zu/config.yml
```

### 3) Release-Build (Beispiel Windows)
```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

## Konfiguration (config.yml)

### logging
Log-Einstellungen des Monitors selbst (schreibt nach `log/Logfile_<Datum>.log`).
```yaml
logging:
  level: 1   # 0: off  1: on  2: debug
  mode: 3    # 0: none 1: console 2: logfile 3: console & logfile
```

### programs
Hash: Programmname → Ordner mit dessen Logdateien. Der Name erscheint so im Bericht.
```yaml
programs:
  staff-sync: "/opt/samedis/samedis-care-staff-sync/log"
  external-sync: "/opt/samedis/samedis-care-external-sync/log"
  requests-to-mail: "/opt/samedis/samedis-care-requests-to-mail/log"
```

> **Windows-Pfade:** Backslash-Pfade **immer in einfache** Anführungszeichen setzen.
> In doppelten Anführungszeichen ist `\` ein Escape-Zeichen, `\d`/`\s` usw. lösen
> beim Start `unknown escape character` aus. Einfache Anführungszeichen nehmen den
> Pfad wörtlich (Forward Slashes gehen ebenfalls):
> ```yaml
> programs:
>   log-monitoring: '\\server\d$\samedis\log-monitoring\log'   # richtig
>   staff-sync:     '//server/d$/samedis/staff-sync/log'       # ebenso ok
> ```

### monitor
```yaml
monitor:
  levels: ["ERROR", "WARN"]      # Level-Token, die als Problem zählen (case-insensitive)
  warn_if_no_run_today: true     # veraltete/fehlende Logs als Warnung melden
  max_entries_per_program: 500   # Kappung der Detailmenge (0 = unbegrenzt)
```

### mail
Identisch aufgebaut wie in `samedis-care-requests-to-mail`. Platzhalter im `subject`:
`{{Date}}`, `{{Status}}`, `{{ErrorCount}}`, `{{WarningCount}}`.
```yaml
mail:
  enabled: true
  provider: "smtp"   # smtp | graph | gmail
  from: "log-monitor@samedis.care"
  subject: "Samedis Log-Monitor {{Date}} - {{Status}} ({{ErrorCount}} Fehler, {{WarningCount}} Warnungen)"
  recipients:
    - "ops@samedis.care"
  smtp:
    server: "localhost"
    port: 587
    username: ""
    password: ""
    use_ssl: false
    use_start_tls: true
    ignore_certificate_errors: false
  graph:
    tenant_id: ""
    client_id: ""
    client_secret: ""
    sender_user_principal_name: ""
  gmail:
    service_account_json_path: ""
    impersonated_user: ""
```
Zur Transportverschluesselung gibt es **keinen impliziten Default**: sind weder `use_ssl`
noch `use_start_tls` gesetzt, verbindet sich der Mailer unverschluesselt und schickt
`username`/`password` im Klartext. Auf Port 587 gehoert `use_start_tls: true`, auf Port 465
`use_ssl: true`.

## Einmal täglich ausführen (Scheduling)

Das Programm läuft einmal durch und beendet sich – die Taktung erfolgt extern.

**Linux (cron), täglich 07:00:**
```cron
0 7 * * * cd /opt/samedis/samedis-care-log-monitor && /usr/bin/dotnet SamedisCareLogMonitor.dll >> /var/log/samedis-log-monitor.cron.log 2>&1
```

**Windows (Aufgabenplanung):** täglichen Trigger anlegen, der die veröffentlichte
`SamedisCareLogMonitor.exe` im Programmverzeichnis startet.

> Hinweis: Der Monitor sollte **nach** den überwachten Tools laufen, damit die
> jeweils aktuelle Tageslogdatei bereits existiert.
