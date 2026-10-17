namespace MaxDpsCompanion;

/// <summary>Human-readable names for virtual-key codes, for the status display only.</summary>
internal static class KeyNames
{
    public static string Describe(byte vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),
        // Mouse: real VK codes for the buttons, two undefined values for the wheel.
        0x01 => "Mouse1",
        0x02 => "Mouse2",
        0x04 => "Mouse3",
        0x05 => "Mouse4",
        0x06 => "Mouse5",
        0x07 => "WheelUp",
        0x0B => "WheelDown",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Escape",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0x6A => "Num*",
        0x6B => "Num+",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"VK{vk:X2}",
    };
}
