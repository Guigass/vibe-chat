using Microsoft.Extensions.DependencyInjection;
using VibeChat.Notifications;

namespace VibeChat.Infrastructure;

internal static class EmailServiceCollectionExtensions
{
    internal static IServiceCollection AddEmailInfrastructure(this IServiceCollection services)
    {
        // D-10 / B-043 / B-069 / ADR-020: email off by default; runtime tenant overrides via EmailSettingsResolver.
        services.AddScoped<EmailSettingsResolver>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
