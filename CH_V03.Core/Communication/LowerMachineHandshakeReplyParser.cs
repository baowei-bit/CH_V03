namespace CH_V03.Core.Communication;

public enum HandshakeReplyStatus
{
    Pending,
    Accepted,
    Rejected
}

public readonly record struct HandshakeReplyParseResult(
    HandshakeReplyStatus Status,
    string Received,
    string RemainingPayload = "");

/// <summary>Reassembles the connection reply across serial reads without swallowing a following Item header.</summary>
public sealed class LowerMachineHandshakeReplyParser
{
    private const int MaximumReplyCharacters = 256;
    private readonly string _expected;
    private string _buffer = string.Empty;

    public LowerMachineHandshakeReplyParser(string expected)
    {
        _expected = Normalize(expected);
        if (string.IsNullOrWhiteSpace(_expected))
        {
            throw new ArgumentException("握手期望回复不能为空", nameof(expected));
        }
    }

    public HandshakeReplyParseResult Add(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return new HandshakeReplyParseResult(HandshakeReplyStatus.Pending, string.Empty);
        }

        _buffer += chunk;
        if (_buffer.Length > MaximumReplyCharacters)
        {
            return new HandshakeReplyParseResult(HandshakeReplyStatus.Rejected, "[握手回复过长]");
        }

        var received = Normalize(_buffer);
        if (_expected.StartsWith(received, StringComparison.Ordinal))
        {
            return received == _expected
                ? new HandshakeReplyParseResult(HandshakeReplyStatus.Accepted, received)
                : new HandshakeReplyParseResult(HandshakeReplyStatus.Pending, received);
        }

        if (received.StartsWith(_expected, StringComparison.Ordinal))
        {
            var remaining = received[_expected.Length..];
            if ("Item1,".StartsWith(remaining, StringComparison.OrdinalIgnoreCase)
                || remaining.StartsWith("Item1,", StringComparison.OrdinalIgnoreCase))
            {
                return new HandshakeReplyParseResult(HandshakeReplyStatus.Accepted, _expected, remaining);
            }
        }

        return new HandshakeReplyParseResult(HandshakeReplyStatus.Rejected, received);
    }

    private static string Normalize(string value) => (value ?? string.Empty)
        .Replace("\r", string.Empty)
        .Replace("\n", string.Empty)
        .Trim();
}
