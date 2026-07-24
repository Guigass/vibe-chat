namespace VibeChat.Files;

public interface IObjectStorage
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
