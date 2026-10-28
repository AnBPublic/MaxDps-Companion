using System.Drawing;
using System.Text;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Ext5 (4.6.0 "Cadence") T0 contract: the 58-cell strip keeps cells 0-50
/// byte-identical to v5 + Ext2 + Ext3 + Ext4 and adds cells 51-57 (rank/delve,
/// interruptible + cast age + cast kind, GCD bucket, CC key, CC spell id,
/// commit). Presence is cell 50 R bit0 — a reserved-zero channel outside every
/// checksum — AND the Ext4 presence bit AND a >= 58-cell capture. Cell 57 G
/// covers cells 51-56 and its B is the heartbeat commit; a failure (or a torn
/// commit) drops ONLY Ext5. An invalid CC key forces the spell id to 0 (D4).
///
/// The macOS sampler/scanner and the Windows sampler/locator/learner consume the
/// SAME width ladder (<see cref="PixelProtocol.V5Widths"/>); the Mac project
/// targets net8.0 with osx RuntimeIdentifiers and is not referenced by this
/// net8.0-windows test project, so its width case is pinned here through the
/// shared ladder it reads rather than through a Mac-typed assertion.
/// </summary>
public class PixelProtocolExt5Tests
{
    private const byte VkQ = 0x51;
    private const int IntimidatingShout = 5246;   // warrior CC (identity only)

    private static Color N(int r, int g, int b) => Color.FromArgb(r * 17, g * 17, b * 17);
    private static int Nib(byte channel) => channel / 17;

    private static TestV5Frame Core() => new TestV5Frame()
        .Vitals(100)
        .Cast(PlayerCastState.None)
        .Slot(Slot.Main, 0x45)
        .SpellId(Slot.Main, 12294)
        .ClassSpec("WARRIOR", 1);

    /// <summary>The golden fixture: one fully-populated Ext5 block on a healthy core.</summary>
    private static TestV5Frame Golden() => Core().Ext5(
        rank: 5,
        delve: 2,
        interruptible: 2,
        castKind: 2,
        castAgeBucket: 9,
        ccKey: new KeyStroke(VkQ, false, false, false),
        ccSpellId: IntimidatingShout,
        gcdBucket: 8);

    private static int NibbleSum(Color[] cells, int first, int lastInclusive)
    {
        var sum = 0;
        for (var i = first; i <= lastInclusive; i++)
            sum += Nib(cells[i].R) + Nib(cells[i].G) + Nib(cells[i].B);
        return sum;
    }

    // ---- width contract ---------------------------------------------------

    [Fact]
    public void IsV5Length_Accepts_58_And_Keeps_Every_Family_Width()
    {
        Assert.Equal(58, PixelProtocol.CellCountExt5);
        foreach (var width in new[] { 35, 40, 43, 51, 58 })
            Assert.True(PixelProtocol.IsV5Length(width), $"width {width} must decode");
        // The two widths BETWEEN Ext4 and Ext5 are not protocol frames: a torn
        // capture must fail the gate instead of decoding a half Ext5 block.
        foreach (var width in new[] { 54, 56 })
            Assert.False(PixelProtocol.IsV5Length(width), $"width {width} must be rejected");
    }

    [Fact]
    public void Width_Gate_Still_Rejects_Legacy_And_Nonsense_Widths()
    {
        // Pre-existing assertions kept: none of these are v5-family frames.
        foreach (var width in new[] { 7, 8, 9, 34, 36, 39, 41, 42, 44, 50, 52, 57, 59 })
            Assert.False(PixelProtocol.IsV5Length(width), $"width {width} must be rejected");
    }

    [Fact]
    public void Hosts_Share_The_Ext5_Width_Ladder()
    {
        // ONE ladder: the Windows sampler/locator/learner and the macOS
        // sampler/scanner all read this array (a local copy is how the Mac once
        // lost Ext4). Widest first, every narrower stale width still present.
        Assert.Equal(new[] { 58, 51, 43, 40, 35 }, PixelProtocol.V5Widths);
        Assert.Same(PixelProtocol.V5Widths, ScreenSampler.CaptureWidths);
        Assert.Equal(PixelProtocol.CellCountExt5, ScreenSampler.CaptureWidths[0]);
    }

    [Fact]
    public void Test_Frame_Builder_Emits_A_58_Cell_Frame_With_Ext5_Set()
    {
        // The shared test encoder must be able to produce the wire frame the
        // feature tests need: 58 cells, Ext4 present and a decoded Ext5 block.
        var cells = Golden().Build(heartbeat: 7, cellCount: PixelProtocol.CellCountExt5);
        Assert.Equal(58, cells.Length);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext5Present);
        Assert.NotNull(frame.Ext5);
        Assert.Equal(5, frame.Ext5!.Rank);
        Assert.Equal(IntimidatingShout, frame.Ext5.CcSpellId);
        // The 35-cell legacy demand is untouched by the new optional width.
        Assert.Equal(PixelProtocol.CellCount, Golden().Build(heartbeat: 7).Length);
    }

    [Fact]
    public void Windows_Sampler_Captures_The_58_Cell_Width()
    {
        // Minimized: the array is returned without a BitBlt, so the width is
        // asserted without screen content (see CaptureDiagnosticsTests).
        using var sampler = new ScreenSampler { IsMinimizedProbe = _ => true };
        var cells = sampler.Sample(new Point(10, 10), 8, new IntPtr(0x1234));

        Assert.Equal(PixelProtocol.CellCountExt5, cells.Length);
        Assert.Equal(58, cells.Length);
    }

    // ---- golden packing ---------------------------------------------------

    [Fact]
    public void Ext5_Golden_Packing_Matches_The_Frozen_Layout()
    {
        var cells = Golden().BuildExt5(heartbeat: 7);

        Assert.Equal(PixelProtocol.CellCountExt5, cells.Length);
        // 51: R = rank (3 bits), G = delve (2 bits), B reserved 0.
        Assert.Equal(N(5, 2, 0), cells[PixelProtocol.Ext5RankCellIndex]);
        // 52: R = interruptible (2 bits), G = cast age bucket, B = cast kind (2 bits).
        Assert.Equal(N(2, 9, 2), cells[PixelProtocol.Ext5CastCellIndex]);
        // 53: R = GCD bucket, G/B reserved 0.
        Assert.Equal(N(8, 0, 0), cells[PixelProtocol.Ext5GcdCellIndex]);
        // 54: vk hi/lo + key flags (bit3 valid), identical packing to Ext4.
        Assert.Equal(N(0x5, 0x1, 8), cells[PixelProtocol.Ext5CcKeyCellIndex]);
        // 55: idTop (bits 21-20, masked & 3) / id19-16 / id15-12. 5246 = 0x00147E.
        Assert.Equal(N(0, 0, 1), cells[PixelProtocol.Ext5CcSpellIdCellIndex]);
        // 56: id11-8 / id7-4 / id3-0.
        Assert.Equal(N(4, 7, 0xE), cells[PixelProtocol.Ext5CcSpellIdCellIndex + 1]);
        // 57: R reserved 0, G = nibble sum of 51-56 mod 16, B = heartbeat.
        var expected = NibbleSum(cells, PixelProtocol.Ext5RankCellIndex, PixelProtocol.Ext5CcSpellIdCellIndex + 1) & 0xF;
        Assert.Equal(4, expected);
        Assert.Equal(N(0, expected, 7), cells[PixelProtocol.Ext5CommitCellIndex]);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext5Present);
        Assert.NotNull(frame.Ext5);
        Assert.True(frame.Ext4Present);
        Assert.NotNull(frame.Ext4);

        var ext5 = frame.Ext5!;
        Assert.Equal(5, ext5.Rank);
        Assert.Equal(2, ext5.Delve);
        Assert.Equal(2, ext5.Interruptible);
        Assert.Equal(2, ext5.CastKind);
        Assert.Equal(9, ext5.CastAgeBucket);
        Assert.Equal(2000, ext5.CastAgeMinMs);                 // (9-1) * 250
        Assert.Equal(new KeyStroke(VkQ, false, false, false), ext5.CcKey);
        Assert.Equal(IntimidatingShout, ext5.CcSpellId);
        Assert.True(ext5.CcValid);
        Assert.Equal(8, ext5.GcdBucket);
        Assert.Equal(600, ext5.GcdRemainMinMs);                // (8-2) * 100
        Assert.False(ext5.BossGrade);
        // The frozen core is untouched.
        Assert.Equal(12294, frame.SpellId(Slot.Main));
        Assert.Equal(7, frame.Heartbeat);
    }

    [Fact]
    public void Ext5_Bucket_Math_And_Unknown_Are_Closed_Form()
    {
        static Ext5Block With(int rank = 0, int castAge = 0, int gcd = 0, KeyStroke? key = null, int id = 0, int kind = 0, int intr = 0)
            => new(rank, 0, intr, kind, castAge, key ?? default, id, gcd);

        Assert.True(With(rank: 6).BossGrade);                  // worldboss
        Assert.True(With(rank: 7).BossGrade);                  // elite-skull
        Assert.False(With(rank: 3).BossGrade);                 // elite
        Assert.False(With(rank: 0).BossGrade);                 // unknown

        Assert.Equal(-1, With(castAge: 0).CastAgeMinMs);       // unknown/none
        Assert.Equal(0, With(castAge: 1).CastAgeMinMs);
        Assert.Equal(3250, With(castAge: 14).CastAgeMinMs);    // saturates
        Assert.Equal(-1, With(castAge: 15).CastAgeMinMs);      // reserved

        Assert.Equal(-1, With(gcd: 0).GcdRemainMinMs);         // unknown
        Assert.Equal(0, With(gcd: 1).GcdRemainMinMs);          // not on GCD
        Assert.Equal(0, With(gcd: 2).GcdRemainMinMs);
        Assert.Equal(1300, With(gcd: 15).GcdRemainMinMs);      // saturates

        Assert.False(Ext5Block.Unknown.CcValid);
        Assert.False(Ext5Block.Unknown.BossGrade);
        Assert.Equal(-1, Ext5Block.Unknown.CastAgeMinMs);
        Assert.Equal(-1, Ext5Block.Unknown.GcdRemainMinMs);
    }

    // ---- integrity --------------------------------------------------------

    [Fact]
    public void Ext5_Cell57_Checksum_Failure_Drops_Only_Ext5()
    {
        var cells = Golden().BuildExt5(heartbeat: 7);
        var commit = cells[PixelProtocol.Ext5CommitCellIndex];
        cells[PixelProtocol.Ext5CommitCellIndex] =
            Color.FromArgb(commit.R, (Nib(commit.G) + 1) % 16 * 17, commit.B);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext5Present);
        Assert.Null(frame.Ext5);
        // Drop ONLY Ext5: Ext4 and the core survive untouched.
        Assert.NotNull(frame.Ext4);
        Assert.True(frame.Ext4Present);
        Assert.Equal(12294, frame.SpellId(Slot.Main));
    }

    [Fact]
    public void Ext5_Cell57_Torn_Heartbeat_Drops_Only_Ext5()
    {
        var cells = Golden().BuildExt5(heartbeat: 7);
        cells[PixelProtocol.Ext5CommitCellIndex] = N(0, Nib(cells[PixelProtocol.Ext5CommitCellIndex].G), 15);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext5Present);
        Assert.Null(frame.Ext5);
        Assert.NotNull(frame.Ext4);
        Assert.NotNull(frame.Ext3);
    }

    [Fact]
    public void Ext5_51_Cell_Frame_Reads_Null()
    {
        // A pre-Ext5 addon renders 51 cells even when the encoder left cell
        // 50 R bit0 set: the length gate is authoritative.
        var wide = Golden().BuildExt5(heartbeat: 7);
        Assert.Equal(1, Nib(wide[PixelProtocol.Ext5PresenceCellIndex].R));   // bit0 set

        var narrow = new Color[PixelProtocol.CellCountExt4];
        for (var i = 0; i < narrow.Length; i++) narrow[i] = wide[i];

        var frame = PixelProtocol.Decode(narrow);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext5Present);
        Assert.Null(frame.Ext5);
        // Ext4 is unaffected: cell 50 R is outside its checksum/commit.
        Assert.True(frame.Ext4Present);
        Assert.NotNull(frame.Ext4);
        Assert.Equal(12294, frame.SpellId(Slot.Main));
    }

    [Fact]
    public void Ext5_Presence_Clear_Ignores_Junk_In_Cells_51_To_57()
    {
        var cells = Golden().Ext5(rank: 7, gcdBucket: 15).BuildExt5(heartbeat: 7, ext5Present: false);
        Assert.Equal(0, Nib(cells[PixelProtocol.Ext5PresenceCellIndex].R));

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext5Present);
        Assert.Null(frame.Ext5);
        Assert.NotNull(frame.Ext4);
        Assert.Equal(12294, frame.SpellId(Slot.Main));
    }

    [Fact]
    public void Ext5_Presence_Requires_The_Ext4_Presence_Bit()
    {
        // Ext5 rides on Ext4's block: a frame whose cell 28 B bit3 is clear
        // never surfaces Ext5, even with the cell-50 R bit and a valid commit.
        var cells = Golden().BuildExt5(heartbeat: 7);
        var cast = cells[PixelProtocol.CastCellIndex];
        cells[PixelProtocol.CastCellIndex] = Color.FromArgb(
            cast.R, cast.G, (byte)((Nib(cast.B) & ~PixelProtocol.CastFlagExt4Present) * 17));
        // Repair the cell-34 extension checksum (cell 28 is inside its scope).
        cells[PixelProtocol.ExtensionCellIndex] = N(
            0,
            NibbleSum(cells, PixelProtocol.SpellIdCellBase, PixelProtocol.ClassSpecCellIndex) & 0xF,
            7);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext4Present);
        Assert.False(frame.Ext5Present);
        Assert.Null(frame.Ext5);
    }

    [Fact]
    public void Ext5_Invalid_Cc_Key_Forces_The_Spell_Id_To_Zero()
    {
        // The decoder must not surface half an entry: key flags bit3 clear =>
        // CcSpellId 0 and an invalid stroke, whatever cells 55/56 say.
        var cells = Golden().BuildExt5(heartbeat: 7);
        cells[PixelProtocol.Ext5CcKeyCellIndex] = N(0x5, 0x1, 0);          // valid bit clear
        cells[PixelProtocol.Ext5CcSpellIdCellIndex] = N(0, 0, 1);          // id bytes intact
        cells[PixelProtocol.Ext5CcSpellIdCellIndex + 1] = N(4, 7, 0xE);
        cells[PixelProtocol.Ext5CommitCellIndex] = N(0,
            NibbleSum(cells, PixelProtocol.Ext5RankCellIndex, PixelProtocol.Ext5CcSpellIdCellIndex + 1) & 0xF,
            7);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        var ext5 = frame!.Ext5;
        Assert.NotNull(ext5);
        Assert.Equal(0, ext5!.CcSpellId);
        Assert.False(ext5.CcKey.Valid);
        Assert.False(ext5.CcValid);
        // The rest of the block is still readable.
        Assert.Equal(5, ext5.Rank);
        Assert.Equal(8, ext5.GcdBucket);
    }

    [Fact]
    public void Ext5_Core_Fault_Still_Fails_The_Whole_Frame()
    {
        var cells = Golden().BuildExt5(heartbeat: 7);
        cells[2] = Color.FromArgb(0, 255, 0);   // break the core checksum

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }

    // ---- context + replay -------------------------------------------------

    [Fact]
    public void CombatContext_Carries_Ext5_Through_FromFrame_WithTtk_And_WithSlotRange()
    {
        var frame = PixelProtocol.Decode(Golden().Ext5(rank: 7).BuildExt5(heartbeat: 7));
        Assert.NotNull(frame);

        var combat = CombatContext.FromFrame(frame!);
        Assert.NotNull(combat.Ext5);
        Assert.True(combat.Ext5!.BossGrade);

        // WithTtk and WithSlotRange are hand-written copies: neither may drop
        // the field (the historical bug class this assertion exists for).
        var withTtk = combat.WithTtk(TtkEstimate.Invalid, historyDurFactor: 0.25);
        Assert.NotNull(withTtk.Ext5);
        Assert.Equal(combat.Ext5, withTtk.Ext5);

        var withRange = withTtk.WithSlotRange((int)Slot.SelfHeal, TriState.Yes);
        Assert.NotNull(withRange.Ext5);
        Assert.Equal(combat.Ext5, withRange.Ext5);
        Assert.Equal(TriState.Yes, withRange.SlotRange[(int)Slot.SelfHeal]);
    }

    [Fact]
    public void Frame_Without_Ext5_Yields_A_Context_Without_Ext5()
    {
        var frame = PixelProtocol.Decode(Core().BuildExt2(heartbeat: 3));
        Assert.NotNull(frame);
        Assert.Null(frame!.Ext5);

        var combat = CombatContext.FromFrame(frame);
        Assert.Null(combat.Ext5);
        Assert.Null(CombatContext.Unknown().Ext5);
    }

    [Fact]
    public void Ext5_Policy_Record_Replays_With_Zero_Mismatches_And_Keeps_The_Timing()
    {
        var builder = Golden()
            .Target(inMelee: true, casting: true, interruptible: true)
            .Ranges((Slot.Main, TriState.Yes), (Slot.Interrupt, TriState.Yes));
        var frame = PixelProtocol.Decode(builder.BuildExt5(heartbeat: 1));
        Assert.NotNull(frame);

        var tracker = new CandidateTracker();
        tracker.Update(frame!, 1000);
        var combat = CombatContext.FromFrame(frame!);
        var scheduler = new ActionScheduler();
        var plan = scheduler.Advance(new ScheduleInput
        {
            Frame = frame!,
            OutOfCombatPermitted = true,
            Candidates = tracker.Snapshot([true, true, true, true, true, true, true, true]),
            NowMs = 1000,
            MinKeyIntervalMs = 120,
            StaleAfterMs = 1500,
            HeartbeatTimeoutMs = 500,
            RepeatSuppressMs = 900,
            Context = combat,
            Options = PolicyOptions.Standard,
            Catalog = AbilityCatalog.Default,
            CollectPolicyVerdicts = true,
        });
        var timing = new TelemetryTiming
        {
            Kind = "landing",
            Reason = "Armed",
            PredictedGcdEndMs = 2500,
            LeadMs = 40,
            CandidateSpell = 12294,
        };
        var policy = TelemetryEvent.BuildPolicy(plan, true, combat, PolicyOptions.Standard, timing);
        Assert.NotNull(policy.Ext5);
        Assert.Equal(5, policy.Ext5!.Rank);
        Assert.Equal(IntimidatingShout, policy.Ext5.CcSpellId);

        var events = new List<TelemetryEvent>
        {
            TelemetryEvent.Session(0, "4.6.0", PixelProtocol.SupportedVersion, 1000, true, "start",
                AbilityCatalog.CatalogVersion),
            TelemetryEvent.Tick(1000, frame, DecodeFault.None, null, null, true,
                tracker.Snapshot([true, true, true, true, true, true, true, true]), "ext5 tick", true, policy),
        };

        var path = Path.Combine(Path.GetTempPath(), $"mdb-ext5-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path,
                events.Select(evt => Encoding.UTF8.GetString(TelemetryJson.Serialize(evt))), new UTF8Encoding(false));
            var (read, badLines) = TelemetryReader.Read(path);
            var result = ReplayRunner.Run(read, badLines, source: path);

            Assert.Equal(0, badLines);
            Assert.Equal(0, result.Errors);
            Assert.Equal(0, result.Mismatches);
            Assert.Equal(0, result.VerdictMismatches);
            Assert.Equal(0, result.HistMismatches);
            Assert.Contains("cadence : landing Armed", result.Report);
            Assert.Contains("0 mismatch(es)", result.Report);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
