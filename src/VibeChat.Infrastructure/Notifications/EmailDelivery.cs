using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

/// <summary>Resolves effective email/SMTP settings (B-069 / ADR-020): DB non-secrets + password from envelope or env.</summary>
public sealed class EmailSettingsResolver(
    VibeChatDbContext dbContext,
    IConfiguration configuration,
    RuntimeSecretProtector protector,
    ILogger<EmailSettingsResolver> logger)
{
    public async Task<bool> IsEnabledAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var row = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        return row?.Enabled ?? false;
    }

    public async Task<EffectiveSmtpSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var row = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        var envPassword = configuration["Email:Smtp:Password"] ?? configuration["SMTP_PASSWORD"] ?? string.Empty;

        if (row is null)
        {
            return new EffectiveSmtpSettings(
                false,
                "localhost",
                1025,
                string.Empty,
                envPassword,
                "noreply@localhost",
                false,
                Source: SecretMasking.IsConfigured(envPassword) ? "env" : ProcessSettingsDefaults.SourceDefault,
                PasswordSource: SecretMasking.IsConfigured(envPassword) ? "env" : "none",
                PasswordKeyVersion: null,
                PasswordRotatedAt: null,
                PasswordMask: SecretMasking.Mask(envPassword));
        }

        var password = envPassword;
        var passwordSource = SecretMasking.IsConfigured(envPassword) ? "env" : "none";
        int? passwordKeyVersion = null;
        DateTimeOffset? passwordRotatedAt = null;
        string? passwordMask = SecretMasking.Mask(envPassword);

        if (row.SmtpPassword.IsPresent)
        {
            try
            {
                password = protector.Unprotect(
                    row.SmtpPassword,
                    RuntimeSecretKinds.SmtpPassword,
                    tenantId,
                    workspaceId: null,
                    tenantId.Value.ToString("D"));
                passwordSource = "database";
                passwordKeyVersion = row.SmtpPassword.KeyVersion;
                passwordRotatedAt = row.SmtpPassword.RotatedAt;
                passwordMask = SecretMasking.MaskFromSuffix(row.SmtpPassword.MaskSuffix);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Failed to decrypt SMTP password for tenant {TenantId}", tenantId.Value);
                password = string.Empty;
                passwordSource = "unavailable";
                passwordMask = SecretMasking.MaskFromSuffix(row.SmtpPassword.MaskSuffix);
                passwordKeyVersion = row.SmtpPassword.KeyVersion;
                passwordRotatedAt = row.SmtpPassword.RotatedAt;
            }
        }

        return new EffectiveSmtpSettings(
            row.Enabled,
            string.IsNullOrWhiteSpace(row.Host) ? "localhost" : row.Host,
            row.Port > 0 ? row.Port : 1025,
            string.IsNullOrWhiteSpace(row.Username) ? string.Empty : row.Username,
            password,
            string.IsNullOrWhiteSpace(row.From) ? "noreply@localhost" : row.From,
            row.UseStartTls,
            Source: "tenant",
            PasswordSource: passwordSource,
            PasswordKeyVersion: passwordKeyVersion,
            PasswordRotatedAt: passwordRotatedAt,
            PasswordMask: passwordMask);
    }
}

public sealed record EffectiveSmtpSettings(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string Password,
    string From,
    bool UseStartTls,
    string Source,
    string PasswordSource = "none",
    int? PasswordKeyVersion = null,
    DateTimeOffset? PasswordRotatedAt = null,
    string? PasswordMask = null);

public sealed class SmtpEmailSender(
    EmailSettingsResolver settingsResolver,
    ProcessSettingsResolver processSettings,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public string Name => "Smtp";
    public bool IsEnabled => true;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var process = await processSettings.ResolveAsync(cancellationToken);
        if (!process.EmailEnabled)
        {
            return;
        }

        if (message.TenantId is not { } tenantGuid || tenantGuid == Guid.Empty)
        {
            return;
        }

        var smtp = await settingsResolver.ResolveAsync(new TenantId(tenantGuid), cancellationToken);
        if (!smtp.Enabled)
        {
            return;
        }

        var from = message.From ?? smtp.From;
        using var client = new System.Net.Mail.SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseStartTls,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network
        };

        if (SecretMasking.IsConfigured(smtp.Username))
        {
            client.Credentials = new System.Net.NetworkCredential(smtp.Username, smtp.Password);
        }

        using var mail = new System.Net.Mail.MailMessage(from, message.To, message.Subject, message.BodyText)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP send failed via {Host}:{Port}", smtp.Host, smtp.Port);
            throw;
        }
    }
}
