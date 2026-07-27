using Hazel;

namespace TimeMachine;

internal static class MessageReaderExtensions
{
    // A packed int32 is at most five 7-bit groups; a longer continuation chain is malformed.
    private const int MaxPackedShift = 28;

    // A message header declares its own payload length, so a remote peer can claim more bytes than the datagram
    // actually carries. Clamp the declared length to what was really received.
    public static int BytesRemaining(this MessageReader messageReader)
    {
        var available = Math.Min(messageReader.Length, messageReader.Buffer.Length - messageReader.Offset);
        return Math.Max(0, available - messageReader.Position);
    }

    public static bool TryReadPackedInt32(this MessageReader messageReader, out int value)
    {
        value = 0;

        for (var shift = 0; shift <= MaxPackedShift; shift += 7)
        {
            if (messageReader.BytesRemaining() < sizeof(byte)) return false;

            var current = messageReader.ReadByte();
            value |= (current & 0x7f) << shift;

            if (current < 0x80) return true;
        }

        return false;
    }
}
