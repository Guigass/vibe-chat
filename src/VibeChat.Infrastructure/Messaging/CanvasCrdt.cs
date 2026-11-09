using YDotNet.Document;
using YDotNet.Document.Options;
using YDotNet.Document.Transactions;

namespace VibeChat.Infrastructure.Messaging;

/// <summary>
/// Yjs/Yrs document merge (ADR-068). Updates are lib0 v1 and text offsets are UTF-16,
/// the same choices as the browser <c>yjs</c> client.
/// </summary>
public static class CanvasCrdt
{
    public static byte[] Create(string body)
    {
        using var doc = Open(VibeChat.Messaging.CanvasPolicies.ServerClientId);
        var text = doc.Text(VibeChat.Messaging.CanvasPolicies.TextName);
        var transaction = doc.WriteTransaction();
        if (body.Length > 0)
        {
            text.Insert(transaction, 0, body, null!);
        }

        var state = transaction.StateDiffV1(null!);
        transaction.Commit();
        return state;
    }

    public static string Read(byte[] state)
    {
        using var doc = Open(VibeChat.Messaging.CanvasPolicies.ServerClientId);
        var textHandle = doc.Text(VibeChat.Messaging.CanvasPolicies.TextName);
        var transaction = doc.WriteTransaction();
        if (state.Length > 0)
        {
            transaction.ApplyV1(state);
        }

        var text = textHandle.String(transaction);
        transaction.Commit();
        return text;
    }

    public static bool TryApply(byte[] state, byte[] update, out byte[] next, out string text)
    {
        next = state;
        text = string.Empty;
        try
        {
            using var doc = Open(VibeChat.Messaging.CanvasPolicies.ServerClientId);
            var textHandle = doc.Text(VibeChat.Messaging.CanvasPolicies.TextName);
            var transaction = doc.WriteTransaction();
            var committed = false;
            try
            {
                if (state.Length > 0 && transaction.ApplyV1(state) != TransactionUpdateResult.Ok)
                {
                    return false;
                }

                if (transaction.ApplyV1(update) != TransactionUpdateResult.Ok)
                {
                    return false;
                }

                text = textHandle.String(transaction);
                next = transaction.StateDiffV1(null!);
                transaction.Commit();
                committed = true;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    transaction.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static byte[] Insert(byte[] state, ulong clientId, uint index, string value)
    {
        using var doc = Open(clientId);
        var text = doc.Text(VibeChat.Messaging.CanvasPolicies.TextName);
        var transaction = doc.WriteTransaction();
        if (state.Length > 0)
        {
            transaction.ApplyV1(state);
        }

        var before = transaction.StateVectorV1();
        text.Insert(transaction, index, value, null!);
        var diff = transaction.StateDiffV1(before);
        transaction.Commit();
        return diff;
    }

    private static Doc Open(ulong clientId) =>
        new(new DocOptions
        {
            Id = clientId,
            Encoding = DocEncoding.Utf16,
        });
}
