using Microsoft.Extensions.Options;
using VibeChat.Administration;

namespace VibeChat.Infrastructure;

public sealed class CapacitySloService(IOptions<CapacitySloOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    public CapacitySloResult Dashboard() =>
        Enabled ? CapacitySloResult.Ok(CapacitySloRules.Dashboard()) : CapacitySloResult.Fail(CapacitySloErrors.Disabled);

    public CapacitySloResult Evaluate(string? json) =>
        !Enabled
            ? CapacitySloResult.Fail(CapacitySloErrors.Disabled)
            : CapacitySloRules.TryEvaluate(json, out var evaluation, out var error)
                ? CapacitySloResult.Ok(evaluation)
                : CapacitySloResult.Fail(error);
}
