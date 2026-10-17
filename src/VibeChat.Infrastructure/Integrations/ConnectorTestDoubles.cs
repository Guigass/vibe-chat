using VibeChat.Integrations;

namespace VibeChat.Infrastructure;

/// <summary>
/// Test seam for the connector egress client. Production leaves both null.
/// The integration host points them at an in-memory double before the first request.
/// </summary>
public static class ConnectorTestDoubles
{
    public static IConnectorTransport? Transport { get; set; }

    public static IConnectorAddressLookup? Lookup { get; set; }
}
