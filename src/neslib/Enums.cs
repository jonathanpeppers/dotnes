namespace NES;

/// <summary>
/// Return value of <see cref="NESLib.pad_poll(byte)"/>
/// </summary>
public enum PAD : byte
{
    A = 0x01,
    B = 0x02,
    SELECT = 0x04,
    START = 0x08,
    UP = 0x10,
    DOWN = 0x20,
    LEFT = 0x40,
    RIGHT = 0x80,
}
