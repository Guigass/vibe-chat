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

public sealed class ConversationSequenceStore(VibeChatDbContext dbContext) : IConversationSequenceStore
{
    public async Task<long> NextAsync(TenantId tenantId, ChannelId conversationId, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.ConversationSequences
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ConversationId == conversationId, cancellationToken);

        if (sequence is null)
        {
            sequence = new ConversationSequence { TenantId = tenantId, ConversationId = conversationId, LastSequence = 1 };
            dbContext.ConversationSequences.Add(sequence);
            return 1;
        }

        sequence.LastSequence++;
        return sequence.LastSequence;
    }
}
