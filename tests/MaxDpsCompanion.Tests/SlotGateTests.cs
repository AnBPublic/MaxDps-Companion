using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.6: the shared per-slot OS gate. The pin is that a focus-blocked
/// KEYBOARD slot yields <see cref="SlotVerdict.SkipFocusKeyboard"/> (skip this
/// slot, try lower ranks) — before 4.1 the scheduled/legacy loops returned
/// from the whole scan there, starving every lower-ranked candidate.
/// </summary>
public class SlotGateTests
{
    private static readonly KeyStroke KeyE = new(0x45, false, false, false);
    private static readonly KeyStroke KeyQ = new(0x51, false, false, false);
    private static readonly KeyStroke Mouse5 = new(0x06, false, false, true);

    private static SlotVerdict Evaluate(KeyStroke stroke, bool foreground, bool isGameWindow,
        bool allowBackgroundKeys, bool held = false) =>
        SlotGate.Evaluate(stroke, foreground, isGameWindow, allowBackgroundKeys, _ => held);

    [Fact]
    public void Movement_Keys_Are_Never_Sent()
    {
        Assert.Equal(SlotVerdict.SkipMovementBound, Evaluate(new KeyStroke(0x57, false, false, false), true, true, true));
        Assert.Equal(SlotVerdict.SkipMovementBound, Evaluate(new KeyStroke(0x20, false, false, false), true, true, true));
        Assert.Equal(SlotVerdict.SkipMovementBound, Evaluate(new KeyStroke(0x25, false, false, false), true, true, true));
    }

    [Fact]
    public void Q_And_E_Are_Spell_Binds_Not_Movement()
    {
        Assert.Equal(SlotVerdict.Send, Evaluate(KeyQ, true, true, allowBackgroundKeys: false));
        Assert.Equal(SlotVerdict.Send, Evaluate(KeyE, true, true, allowBackgroundKeys: false));
    }

    [Fact]
    public void A_Physically_Held_Key_Is_Skipped()
    {
        Assert.Equal(SlotVerdict.SkipPhysicalHold, Evaluate(KeyE, true, true, false, held: true));
    }

    [Fact]
    public void Focus_Blocked_Keyboard_Slot_Skips_Instead_Of_Aborting()
    {
        Assert.Equal(SlotVerdict.SkipFocusKeyboard, Evaluate(KeyE, foreground: false, isGameWindow: false,
            allowBackgroundKeys: false));
    }

    [Fact]
    public void Background_Keys_Allowed_Send_Without_Focus()
    {
        Assert.Equal(SlotVerdict.Send, Evaluate(KeyE, foreground: false, isGameWindow: false,
            allowBackgroundKeys: true));
    }

    [Fact]
    public void Keyboard_Slot_Sends_When_Focused()
    {
        Assert.Equal(SlotVerdict.Send, Evaluate(KeyE, foreground: true, isGameWindow: true,
            allowBackgroundKeys: false));
    }

    [Fact]
    public void Mouse_Slots_Always_Need_The_Game_Foreground()
    {
        Assert.Equal(SlotVerdict.SkipFocusMouse, Evaluate(Mouse5, foreground: false, isGameWindow: false, true));
        // Foreground but NOT the attached game window: still blocked.
        Assert.Equal(SlotVerdict.SkipFocusMouse, Evaluate(Mouse5, foreground: true, isGameWindow: false, true));
        Assert.Equal(SlotVerdict.Send, Evaluate(Mouse5, foreground: true, isGameWindow: true, true));
    }

    [Fact]
    public void Movement_Bound_Outranks_Focus_And_Hold()
    {
        Assert.Equal(SlotVerdict.SkipMovementBound,
            Evaluate(new KeyStroke(0x57, false, false, false), false, false, false, held: true));
    }
}
