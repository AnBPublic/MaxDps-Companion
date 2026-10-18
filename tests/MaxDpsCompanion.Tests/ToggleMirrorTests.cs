using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.1 A2.4: the Ext3 toggle mirror is published as ONE immutable reference.
/// Before 4.1 the engine set <c>_mirrorValid = true</c> before writing the mask
/// / epoch / blocked fields, so the UI's ToggleSync could read a valid mirror
/// with a stale mask for one tick.
/// </summary>
public class ToggleMirrorTests
{
    [Fact]
    public void Empty_Mirror_Reads_Invalid_With_No_Block()
    {
        var mirror = new ToggleMirror();

        Assert.False(mirror.TryRead(out var ext3, out var inCombat));
        Assert.Null(ext3);
        Assert.False(inCombat);
        Assert.False(mirror.Valid);
        Assert.Same(ToggleMirrorState.Empty, mirror.State);
    }

    [Fact]
    public void Publish_Replaces_The_Snapshot_By_Reference()
    {
        var mirror = new ToggleMirror();
        var empty = mirror.State;

        mirror.Publish(new Ext3Block(0x2A, 3, 1), inCombat: true);

        var published = mirror.State;
        Assert.NotSame(empty, published);
        Assert.True(mirror.TryRead(out var ext3, out var inCombat));
        Assert.Equal(new Ext3Block(0x2A, 3, 1), ext3);
        Assert.True(inCombat);
    }

    [Fact]
    public void Publish_With_A_Valid_Block_Carries_Validity_And_Mask_Together()
    {
        // The torn-read regression: a mask of 0 is a legal value, so a reader
        // must never infer validity from the mask and never see valid=true with
        // a mask written by an earlier frame.
        var mirror = new ToggleMirror();
        mirror.Publish(new Ext3Block(0x2A, 3, 1), inCombat: true);
        mirror.Publish(new Ext3Block(0, 0, 0), inCombat: true);

        Assert.True(mirror.TryRead(out var ext3, out _));
        Assert.Equal(new Ext3Block(0, 0, 0), ext3);
    }

    [Fact]
    public void A_Missing_Block_Clears_Validity_But_Keeps_The_Last_Nibble()
    {
        var mirror = new ToggleMirror();
        mirror.Publish(new Ext3Block(0x2A, 3, 1), inCombat: true);

        mirror.Publish(null, inCombat: false);

        Assert.False(mirror.TryRead(out var ext3, out var inCombat));
        Assert.Null(ext3);
        Assert.False(inCombat);
        Assert.Equal(0x2A, mirror.Mask);   // pre-4.1 consumers still see the last mask
    }

    [Fact]
    public void Every_Publish_Creates_A_Fresh_Snapshot_Instance()
    {
        var mirror = new ToggleMirror();
        mirror.Publish(new Ext3Block(1, 1, 1), true);
        var first = mirror.State;
        mirror.Publish(new Ext3Block(2, 2, 2), true);
        var second = mirror.State;

        Assert.NotSame(first, second);
        Assert.Equal(1, first.Mask);       // the captured snapshot never mutates
        Assert.Equal(2, second.Mask);
    }
}
