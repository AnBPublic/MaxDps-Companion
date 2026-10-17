using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Ext4 (v4.0.3) decode + assembler: the 51-cell frame keeps cells 0-42
/// byte-identical to v5+Ext2+Ext3 and adds the hotbar-map stream (cells 43-50,
/// presence = cell 28 B bit3). A cell-50 checksum/commit failure drops ONLY the
/// Ext4 frame; a 43-cell (or narrower) addon decodes exactly as before with
/// cells 43-50 ignored, and the typing veto (cell 41 R bit2) reads only from a
/// valid Ext3 block. The <see cref="Ext4Assembler"/> pins the gen/seq cycle
/// state machine (contiguous seqs, gen change, gap, atomic LAST swap, stale).
/// </summary>
public class PixelProtocolExt4Tests
{
    private const byte VkF = 0x46;      // F  -> Shockwave in the user's example
    private const byte VkFive = 0x35;   // 5
    private const int ShockwaveId = 46968;
    private const int HearthstoneId = 5512;

    private static Color N(int r, int g, int b) => Color.FromArgb(r * 17, g * 17, b * 17);
    private static int Nib(byte channel) => channel / 17;

    private sealed record EntrySpec(byte Vk, int Id, bool IsItem, bool Ready, bool Shift = false, bool Ctrl = false, bool Alt = false);

    /// <summary>
    /// Builds a full 51-cell capture: the real TestV5Frame core (with the cell-34
    /// extension checksum recomputed after the cell-28 presence bits change),
    /// an optional valid Ext3 block (cell 41 R bit2 = typing) and an optional
    /// Ext4 frame (header + up to two entries + cell-50 commit).
    /// </summary>
    private static Color[] BuildCapture(
        int heartbeat = 7,
        bool ext3 = true,
        bool typing = false,
        bool presence = true,
        int gen = 3,
        int seq = 0,
        bool last = true,
        bool empty = false,
        EntrySpec? a = null,
        EntrySpec? b = null)
    {
        var core = new TestV5Frame()
            .Slot(Slot.Main, 0x31)
            .SpellId(Slot.Main, 12294)
            .ClassSpec("WARRIOR", 1)
            .Build(heartbeat);

        var cells = new Color[PixelProtocol.CellCountExt4];
        for (var i = 0; i < core.Length; i++) cells[i] = core[i];

        // Cell 28 B: bits 0-1 stay 0 in this fixture, bit2 = Ext3, bit3 = Ext4.
        var presBits = (ext3 ? PixelProtocol.CastFlagExt3Present : 0)
            | (presence ? PixelProtocol.CastFlagExt4Present : 0);
        cells[PixelProtocol.CastCellIndex] = Color.FromArgb(
            cells[PixelProtocol.CastCellIndex].R,
            cells[PixelProtocol.CastCellIndex].G,
            (byte)(presBits * 17));

        // Cell 28 B is inside the cell-34 extension checksum (cells 11-33):
        // recompute it after the presence edit or the whole frame fails decode.
        var extSum = 0;
        for (var i = PixelProtocol.SpellIdCellBase; i <= PixelProtocol.ClassSpecCellIndex; i++)
            extSum += Nib(cells[i].R) + Nib(cells[i].G) + Nib(cells[i].B);
        cells[PixelProtocol.ExtensionCellIndex] = N(0, extSum & 0xF, heartbeat);

        if (ext3)
        {
            var mhi = typing ? PixelProtocol.Ext3FlagTyping : 0;
            cells[PixelProtocol.Ext3MaskCellIndex] = N(0, 0, 0);
            cells[PixelProtocol.Ext3FlagsCellIndex] = N(mhi, 0, 0);
            cells[PixelProtocol.Ext3CommitCellIndex] = N(0, mhi & 0xF, heartbeat);
        }

        if (presence)
        {
            var hdrFlags = (last ? 1 : 0) | (empty ? 2 : 0);
            var sum = gen + seq + hdrFlags;
            cells[PixelProtocol.Ext4HeaderCellIndex] = N(gen, seq, hdrFlags);
            sum += PaintEntry(cells, PixelProtocol.Ext4EntryACellIndex, a);
            sum += PaintEntry(cells, PixelProtocol.Ext4EntryBCellIndex, b);
            cells[PixelProtocol.Ext4CommitCellIndex] = N(0, sum & 0xF, heartbeat);
        }

        return cells;
    }

    private static int PaintEntry(Color[] cells, int baseCell, EntrySpec? e)
    {
        if (e is null)
        {
            cells[baseCell] = N(0, 0, 0);
            cells[baseCell + 1] = N(0, 0, 0);
            cells[baseCell + 2] = N(0, 0, 0);
            return 0;
        }

        var keyFlags = 8 | (e.Shift ? 1 : 0) | (e.Ctrl ? 2 : 0) | (e.Alt ? 4 : 0);
        var c1 = N(e.Vk >> 4, e.Vk & 0x0F, keyFlags);
        var r2 = (e.Id >> 20) & 0x03;
        if (e.IsItem) r2 |= 8;
        if (e.Ready) r2 |= 4;
        var c2 = N(r2, (e.Id >> 16) & 0x0F, (e.Id >> 12) & 0x0F);
        var c3 = N((e.Id >> 8) & 0x0F, (e.Id >> 4) & 0x0F, e.Id & 0x0F);
        cells[baseCell] = c1;
        cells[baseCell + 1] = c2;
        cells[baseCell + 2] = c3;
        return Nib(c1.R) + Nib(c1.G) + Nib(c1.B)
            + Nib(c2.R) + Nib(c2.G) + Nib(c2.B)
            + Nib(c3.R) + Nib(c3.G) + Nib(c3.B);
    }

    private static BridgeFrame WireFrame(Ext4Frame? ext4, bool present = true) => new()
    {
        State = BridgeState.Active,
        Heartbeat = 1,
        Version = PixelProtocol.SupportedVersion,
        StatusFlags = PixelProtocol.StatusFlagContextValid,
        Slots = new KeyStroke?[PixelProtocol.SlotCount],
        Ext4Present = present,
        Ext4 = ext4,
    };

    private static Ext4Frame Ext4FrameOf(int gen, int seq, bool last, bool empty, params Ext4Entry[] entries) => new()
    {
        Gen = gen,
        Seq = seq,
        Last = last,
        Empty = empty,
        Entries = entries,
    };

    // ---- wire decode ------------------------------------------------------

    [Fact]
    public void Ext4_Valid_Decodes_Both_Entries()
    {
        var cells = BuildCapture(
            gen: 3, seq: 0, last: true,
            a: new EntrySpec(VkF, ShockwaveId, IsItem: false, Ready: true),
            b: new EntrySpec(VkFive, HearthstoneId, IsItem: true, Ready: false));

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext4Present);
        Assert.NotNull(frame.Ext4);

        var ext4 = frame.Ext4!;
        Assert.Equal(3, ext4.Gen);
        Assert.Equal(0, ext4.Seq);
        Assert.True(ext4.Last);
        Assert.False(ext4.Empty);
        Assert.Equal(2, ext4.Entries.Count);

        var a = ext4.Entries[0];
        Assert.Equal(new KeyStroke(VkF, false, false, false), a.Stroke);
        Assert.Equal(ShockwaveId, a.Id);
        Assert.False(a.IsItem);
        Assert.True(a.Ready);

        var b = ext4.Entries[1];
        Assert.Equal(new KeyStroke(VkFive, false, false, false), b.Stroke);
        Assert.Equal(HearthstoneId, b.Id);
        Assert.True(b.IsItem);
        Assert.False(b.Ready);
    }

    [Fact]
    public void Ext4_Golden_Packing_Matches_The_Frozen_Layout()
    {
        var cells = BuildCapture(
            a: new EntrySpec(VkF, ShockwaveId, IsItem: false, Ready: true),
            b: new EntrySpec(VkFive, HearthstoneId, IsItem: true, Ready: false));

        // 46968 = 0x00B778: G2 = bits19-16 = 0, B2 = bits15-12 = 0xB,
        // R3 = bits11-8 = 7, G3 = bits7-4 = 7, B3 = bits3-0 = 8.
        Assert.Equal(N(0x4, 0x6, 8), cells[PixelProtocol.Ext4EntryACellIndex]);
        Assert.Equal(N(0x4, 0x0, 0xB), cells[PixelProtocol.Ext4EntryACellIndex + 1]);
        Assert.Equal(N(7, 7, 8), cells[PixelProtocol.Ext4EntryACellIndex + 2]);

        // Item entry: idTop = isItem(8), 5512 = 0x1588, vk 0x35 ('5').
        Assert.Equal(N(0x3, 0x5, 8), cells[PixelProtocol.Ext4EntryBCellIndex]);
        Assert.Equal(N(8, 0, 1), cells[PixelProtocol.Ext4EntryBCellIndex + 1]);
        Assert.Equal(N(5, 8, 8), cells[PixelProtocol.Ext4EntryBCellIndex + 2]);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame!.Ext4);
        Assert.Equal((VkF, ShockwaveId), (frame.Ext4!.Entries[0].Stroke.VirtualKey, frame.Ext4.Entries[0].Id));
    }

    [Fact]
    public void Ext4_Typing_Bit_Reads_Only_From_A_Valid_Ext3_Block()
    {
        var typing = PixelProtocol.Decode(BuildCapture(ext3: true, typing: true));
        Assert.NotNull(typing);
        Assert.True(typing!.TypingActive);
        Assert.NotNull(typing.Ext3);
        Assert.Equal(0, typing.Ext3!.Value.Mask);   // bits 0-1 stay mask bits, untouched

        var clean = PixelProtocol.Decode(BuildCapture(ext3: true, typing: false));
        Assert.NotNull(clean);
        Assert.False(clean!.TypingActive);

        // Ext3 absent (bit2 clear): a stale bit2 in cell 41 must never read as typing.
        var absent = BuildCapture(ext3: false);
        absent[PixelProtocol.Ext3FlagsCellIndex] = N(PixelProtocol.Ext3FlagTyping, 0, 0);
        var frame = PixelProtocol.Decode(absent);
        Assert.NotNull(frame);
        Assert.False(frame!.TypingActive);
        Assert.Null(frame.Ext3);
    }

    [Fact]
    public void Ext4_Checksum_Failure_Drops_Only_The_Ext4_Frame()
    {
        var cells = BuildCapture(
            a: new EntrySpec(VkF, ShockwaveId, IsItem: false, Ready: true));
        var commit = cells[PixelProtocol.Ext4CommitCellIndex];
        cells[PixelProtocol.Ext4CommitCellIndex] = N(0, (Nib(commit.G) + 1) % 16, Nib(commit.B));

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext4Present);
        Assert.Null(frame.Ext4);
        Assert.Equal(12294, frame.SpellId(Slot.Main));   // core intact
    }

    [Fact]
    public void Ext4_Torn_Commit_Drops_Only_The_Ext4_Frame()
    {
        var cells = BuildCapture(
            a: new EntrySpec(VkF, ShockwaveId, IsItem: false, Ready: true));
        cells[PixelProtocol.Ext4CommitCellIndex] = N(0, Nib(cells[PixelProtocol.Ext4CommitCellIndex].G), 15);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext4Present);
        Assert.Null(frame.Ext4);
    }

    [Fact]
    public void Ext4_Presence_Bit_Clear_Ignores_Junk_In_Cells_43_To_50()
    {
        var cells = BuildCapture(presence: false);
        for (var i = PixelProtocol.Ext4HeaderCellIndex; i <= PixelProtocol.Ext4CommitCellIndex; i++)
            cells[i] = Color.FromArgb(255, 0, 255);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext4Present);
        Assert.Null(frame.Ext4);
        Assert.Equal(12294, frame.SpellId(Slot.Main));
    }

    [Fact]
    public void Ext4_43_Cell_Capture_Decodes_As_Ext3_Only()
    {
        var wide = BuildCapture(presence: true, typing: true);
        var narrow = new Color[PixelProtocol.CellCountExt3];
        for (var i = 0; i < narrow.Length; i++) narrow[i] = wide[i];

        var frame = PixelProtocol.Decode(narrow);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext4Present);
        Assert.Null(frame.Ext4);
        Assert.True(frame.Ext3Present);
        Assert.True(frame.TypingActive);
        Assert.Equal(12294, frame.SpellId(Slot.Main));
    }

    // ---- assembler: gen/seq cycle state machine ---------------------------

    [Fact]
    public void Assembler_Completes_Cycle_On_Last_And_Is_Searchable_By_Stroke()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);

        assembler.Feed(WireFrame(Ext4FrameOf(1, 0, last: false, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true),
            new Ext4Entry(new KeyStroke(VkFive, false, false, false), HearthstoneId, true, false))), 1000);
        Assert.Equal(HotbarMapStatus.Stale, assembler.Map.Status);   // partial cycle only

        assembler.Feed(WireFrame(Ext4FrameOf(1, 1, last: true, empty: false,
            new Ext4Entry(new KeyStroke(0x52, false, false, false), 1719, false, true))), 1100);

        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);
        Assert.Equal(3, assembler.Map.Count);
        var hit = assembler.Map.Lookup(f);
        Assert.Single(hit);
        Assert.Equal(ShockwaveId, hit[0].Id);
        Assert.True(hit[0].Ready);
        Assert.Empty(assembler.Map.Lookup(new KeyStroke(0x5A, false, false, false)));
    }

    [Fact]
    public void Assembler_Lookup_Returns_All_Matches_For_A_Multi_Bound_Stroke()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);
        assembler.Feed(WireFrame(Ext4FrameOf(0, 0, last: true, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true),
            new Ext4Entry(f, 6552, false, true))), 0);

        Assert.Equal(2, assembler.Map.Lookup(f).Count);
    }

    [Fact]
    public void Assembler_Seq_Gap_Discards_The_Partial_Cycle()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);

        assembler.Feed(WireFrame(Ext4FrameOf(4, 0, last: false, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true))), 0);
        assembler.Feed(WireFrame(Ext4FrameOf(4, 2, last: true, empty: false,
            new Ext4Entry(f, 999, false, true))), 50);   // gap: dropped
        Assert.Equal(HotbarMapStatus.Stale, assembler.Map.Status);

        assembler.Feed(WireFrame(Ext4FrameOf(4, 0, last: false, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true))), 100);
        assembler.Feed(WireFrame(Ext4FrameOf(4, 1, last: true, empty: false,
            new Ext4Entry(f, 6552, false, true))), 150);

        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);
        Assert.Equal(2, assembler.Map.Count);
        var ids = assembler.Map.Lookup(f).Select(e => e.Id).ToList();
        Assert.Contains(ShockwaveId, ids);          // re-fed seq 0 entry
        Assert.Contains(6552, ids);                 // cycle's LAST entry
        Assert.DoesNotContain(999, ids);            // the gap frame was discarded
    }

    [Fact]
    public void Assembler_Gen_Change_Restarts_The_Cycle()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);

        assembler.Feed(WireFrame(Ext4FrameOf(1, 0, last: false, empty: false,
            new Ext4Entry(f, 111, false, true))), 0);
        assembler.Feed(WireFrame(Ext4FrameOf(2, 0, last: true, empty: false,
            new Ext4Entry(f, 222, false, true))), 50);

        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);
        Assert.Equal(222, assembler.Map.Lookup(f).Single().Id);
    }

    [Fact]
    public void Assembler_Repeat_Reads_Of_A_Held_Frame_Are_Idempotent()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);
        var frame = WireFrame(Ext4FrameOf(1, 0, last: true, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true)));

        assembler.Feed(frame, 0);
        assembler.Feed(frame, 33);   // held >= 2 paint ticks

        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);
        Assert.Equal(1, assembler.Map.Count);
    }

    [Fact]
    public void Assembler_Empty_Frame_Is_A_Valid_Empty_Map()
    {
        var assembler = new Ext4Assembler();
        assembler.Feed(WireFrame(Ext4FrameOf(0, 0, last: true, empty: true)), 0);

        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);
        Assert.Equal(0, assembler.Map.Count);
    }

    [Fact]
    public void Assembler_Old_Addon_And_Stale_Map_Statuses()
    {
        var assembler = new Ext4Assembler();
        var f = new KeyStroke(VkF, false, false, false);
        assembler.Feed(WireFrame(Ext4FrameOf(0, 0, last: true, empty: false,
            new Ext4Entry(f, ShockwaveId, false, true))), 0);
        Assert.Equal(HotbarMapStatus.Ok, assembler.Map.Status);

        // 5 s without a completed cycle: the map drops to Stale (torn frames keep
        // the last map only until the freshness window lapses).
        assembler.Feed(WireFrame(ext4: null, present: true), Ext4Assembler.StaleAfterMs + 1);
        Assert.Equal(HotbarMapStatus.Stale, assembler.Map.Status);
        Assert.Equal(0, assembler.Map.Count);

        // An Ext3-only addon (no presence bit) reports OldAddon.
        assembler.Feed(WireFrame(ext4: null, present: false), 20_000);
        Assert.Equal(HotbarMapStatus.OldAddon, assembler.Map.Status);
    }
}
