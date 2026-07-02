using System.Text;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;

namespace SamedisCareLogMonitor
{
  internal class Mailer
  {
    private readonly AppConfig _config;
    private readonly Helper _helper;

    public Mailer(AppConfig config, Helper helper)
    {
      _config = config;
      _helper = helper;
    }

    public record MailAttachment(string FileName, byte[] Content, string ContentType);
    private record MailMessageData(string From, List<string> To, string Subject, string Body, bool IsHtml, List<MailAttachment>? Attachments = null);

    private List<string> BuildRecipients()
    {
      return (_config.Mail.Recipients ?? new List<string>())
        .Where(r => !string.IsNullOrWhiteSpace(r))
        .Select(r => r.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    public async Task<bool> SendReportEmailAsync(string subject, string htmlBody, MailAttachment? attachment = null)
    {
      if (!_config.Mail.Enabled)
        return false;

      var from = _config.Mail.From?.Trim();
      var recipients = BuildRecipients();
      if (string.IsNullOrWhiteSpace(from) || recipients.Count == 0)
      {
        _helper.Message("Mail configuration invalid (from/recipients missing).", 1, "ERROR");
        return false;
      }

      var attachments = attachment != null ? new List<MailAttachment> { attachment } : null;
      var message = new MailMessageData(from, recipients, subject, htmlBody, true, attachments);
      return await SendAsync(message, "log monitor report");
    }

    private async Task<bool> SendAsync(MailMessageData message, string label)
    {
      var provider = (_config.Mail.Provider ?? "smtp").Trim().ToLowerInvariant();
      _helper.Message($"Sending {label} using provider: {provider}", 1);

      try
      {
        switch (provider)
        {
          case "smtp":
            await SendViaSmtpAsync(message);
            break;
          case "graph":
            await SendViaGraphAsync(message);
            break;
          case "gmail":
            await SendViaGmailAsync(message);
            break;
          default:
            _helper.Message($"Unknown mail provider '{provider}'.", 1, "ERROR");
            return false;
        }
      }
      catch (Exception ex)
      {
        _helper.Message($"Mail send failed: {ex.Message}", 1, "ERROR");
        return false;
      }

      _helper.Message($"{label} sent successfully.", 1);
      return true;
    }

    private async Task SendViaSmtpAsync(MailMessageData message)
    {
      var smtp = _config.Mail.Smtp;
      if (string.IsNullOrWhiteSpace(smtp.Server) || smtp.Port <= 0)
        throw new InvalidOperationException("SMTP server/port not configured.");

      var mimeMessage = BuildMimeMessage(message);

      var secureSocketOptions = SecureSocketOptions.Auto;
      if (smtp.UseSsl)
        secureSocketOptions = SecureSocketOptions.SslOnConnect;
      else if (smtp.UseStartTls)
        secureSocketOptions = SecureSocketOptions.StartTls;
      else
        secureSocketOptions = SecureSocketOptions.None;

      using var client = new SmtpClient();
      if (smtp.IgnoreCertificateErrors)
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;
      await client.ConnectAsync(smtp.Server, smtp.Port, secureSocketOptions);

      if (!string.IsNullOrWhiteSpace(smtp.Username))
      {
        await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty);
      }

      await client.SendAsync(mimeMessage);
      await client.DisconnectAsync(true);
    }

    private async Task SendViaGraphAsync(MailMessageData message)
    {
      var graph = _config.Mail.Graph;
      if (string.IsNullOrWhiteSpace(graph.TenantId)
        || string.IsNullOrWhiteSpace(graph.ClientId)
        || string.IsNullOrWhiteSpace(graph.ClientSecret)
        || string.IsNullOrWhiteSpace(graph.SenderUserPrincipalName))
      {
        throw new InvalidOperationException("Graph mail configuration is incomplete.");
      }

      var credential = new ClientSecretCredential(graph.TenantId, graph.ClientId, graph.ClientSecret);
      var graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

      var graphMessage = new Microsoft.Graph.Models.Message
      {
        Subject = message.Subject,
        Body = new ItemBody
        {
          ContentType = message.IsHtml ? BodyType.Html : BodyType.Text,
          Content = message.Body,
        },
        ToRecipients = message.To
          .Select(to => new Recipient { EmailAddress = new EmailAddress { Address = to } })
          .ToList(),
      };

      if (message.Attachments != null && message.Attachments.Count > 0)
      {
        graphMessage.Attachments = new List<Attachment>();
        foreach (var attachment in message.Attachments)
        {
          graphMessage.Attachments.Add(new FileAttachment
          {
            OdataType = "#microsoft.graph.fileAttachment",
            Name = attachment.FileName,
            ContentType = attachment.ContentType,
            ContentBytes = attachment.Content
          });
        }
      }

      var requestBody = new SendMailPostRequestBody
      {
        Message = graphMessage,
        SaveToSentItems = true,
      };

      await graphClient.Users[graph.SenderUserPrincipalName].SendMail.PostAsync(requestBody);
    }

    private async Task SendViaGmailAsync(MailMessageData message)
    {
      var gmail = _config.Mail.Gmail;
      if (string.IsNullOrWhiteSpace(gmail.ServiceAccountJsonPath)
        || string.IsNullOrWhiteSpace(gmail.ImpersonatedUser))
      {
        throw new InvalidOperationException("Gmail mail configuration is incomplete.");
      }

      if (!File.Exists(gmail.ServiceAccountJsonPath))
        throw new FileNotFoundException("Gmail service account JSON not found.", gmail.ServiceAccountJsonPath);

      var credential = CredentialFactory.FromFile<ServiceAccountCredential>(gmail.ServiceAccountJsonPath)
        .ToGoogleCredential()
        .CreateScoped(GmailService.Scope.GmailSend)
        .CreateWithUser(gmail.ImpersonatedUser);

      var gmailService = new GmailService(new BaseClientService.Initializer
      {
        HttpClientInitializer = credential,
        ApplicationName = "SamedisCareLogMonitor",
      });

      var mimeMessage = BuildMimeMessage(message);
      var rawMessage = mimeMessage.ToString();
      var rawBytes = Encoding.UTF8.GetBytes(rawMessage);
      var base64Raw = Convert.ToBase64String(rawBytes)
        .Replace("+", "-")
        .Replace("/", "_")
        .Replace("=", "");

      var gmailMessage = new Google.Apis.Gmail.v1.Data.Message { Raw = base64Raw };
      await gmailService.Users.Messages.Send(gmailMessage, "me").ExecuteAsync();
    }

    private static MimeMessage BuildMimeMessage(MailMessageData message)
    {
      var mimeMessage = new MimeMessage();
      mimeMessage.From.Add(MailboxAddress.Parse(message.From));
      foreach (var to in message.To)
        mimeMessage.To.Add(MailboxAddress.Parse(to));

      mimeMessage.Subject = message.Subject;

      var bodyBuilder = new BodyBuilder();
      if (message.IsHtml)
        bodyBuilder.HtmlBody = message.Body;
      else
        bodyBuilder.TextBody = message.Body;

      if (message.Attachments != null)
      {
        foreach (var attachment in message.Attachments)
        {
          bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, MimeKit.ContentType.Parse(attachment.ContentType));
        }
      }

      mimeMessage.Body = bodyBuilder.ToMessageBody();
      return mimeMessage;
    }
  }
}
