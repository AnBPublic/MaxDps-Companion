using System.Globalization;
using System.Text;

namespace MaxDpsCompanion;

internal sealed record ReplayResult
{
    public required string Report { get; init; }
    public string ReportPath { get; init; } = "";
    public required int Events { get; init; }
    public required int Ticks { get; init; }
    public required int Decisions { get; init; }
    public required int Sends { get; init; }
    public required int Errors { get; init; }
    public required int Mismatches { get; init; }
    public required int BadLines { get; init; }

    /// <summary>Policy verdicts recomputed by the replay (0 for legacy recordings).</summary>
    public int Verdicts { get; init; }

    /// <summary>Recomputed policy verdicts that differ from the recording.</summary>
    public int VerdictMismatches { get; init; }
}

/// <summary>
/// Replays recorded decision contexts through the deterministic
/// <see cref="DecisionEngine"/>. No clock, no I/O during evaluation — every
/// input comes from the recorded event, so the same file always produces the
/// same report (the determinism contract).
///
/// When the recording was made with rotation intelligence enabled, the
/// recomputed decision is compared field-for-field with the recorded one
/// (mismatches must be 0 for a same-build replay). For legacy recordings the
/// evaluator result is labelled "would-select" — that is the diagnostic value
/// of the tool: what the deterministic layer would have done on a session
/// that ran the legacy priority loop.
/// </summary>
internal static class ReplayRunner
{
    public static ReplayResult RunFile(string path, bool allTicks = false)
    {
        var (events, badLines) = TelemetryReader.Read(path);
        var result = Run(events, badLines, allTicks, path);
        var outPath = path + ".replay.txt";
        File.WriteAllText(outPath, result.Report, new UTF8Encoding(false));
        return result with { ReportPath = outPath };
    }

    public static ReplayResult Run(IReadOnlyList<TelemetryEvent> events, int badLines = 0, bool allTicks = false, string source = "")
    {
        var body = new StringBuilder();
        int ticks = 0, decisions = 0, sends = 0, errors = 0, mismatches = 0, links = 0, sessions = 0;
        int policyVerdicts = 0, policyVerdictMismatches = 0, legacyPolicyRecords = 0;
        var reasonCounts = new int[Enum.GetValues<DecisionReason>().Length];
        var sendCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var faultCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var noteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var policyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        long spanStart = 0, spanEnd = 0;
        var haveSpan = false;
        long lastSendMs = long.MinValue;
        var lastSendDesc = "-";

        // Policy replay state: the memory is rebuilt from the recorded sends
        // so defensive sequencing / trinket lockout verdicts recompute exactly
        // as they ran. ORDERING (measured against the live engine): the engine
        // appends the send event for a tick BEFORE that tick's event, because
        // TrySendOne runs before Report. The tick's verdicts were computed
        // pre-send, so sends are buffered and applied AFTER evaluating the
        // tick they precede — never before.
        var policyMemory = new PolicyMemory();
        AbilityCatalog? replayCatalog = null;

        // v3.2.0: the TTK estimator is stateful, so replay reconstructs it by
        // feeding the RECORDED (ttkMs, hasTarget, targetHpPct) series in order.
        // The feed timestamp is the exact engine value, so the rebuilt estimate
        // is identical to the live one and every TTK-gated verdict recomputes
        // with zero mismatches by construction.
        var ttkEstimator = new TtkEstimator();
        var pendingSends = new List<(Slot? Slot, int SpellId, long TMs)>();
        int? recordedCatalog = null;
        var catalogSkew = false;

        // Applies the sends that precede the current tick — AFTER the tick's
        // policy verdicts have been re-evaluated, so a tick can never observe
        // its own send (the live engine evaluated the policy first).
        void ApplyPendingSends()
        {
            if (pendingSends.Count == 0) return;
            replayCatalog ??= AbilityCatalog.Default;
            foreach (var (sendSlot, sendSpell, sendTMs) in pendingSends)
            {
                if (sendSpell > 0 && replayCatalog.TryGet(sendSpell) is { } sentAbility)
                    policyMemory.NoteUse(sentAbility, sendTMs);
                else if (sendSlot == Slot.Trinket)
                    policyMemory.NoteTrinketUse(sendTMs);
            }
            pendingSends.Clear();
        }

        foreach (var evt in events)
        {
            if (!haveSpan || evt.TMs < spanStart) spanStart = evt.TMs;
            if (!haveSpan || evt.TMs > spanEnd) spanEnd = evt.TMs;
            haveSpan = true;

            if (evt.Note is { Length: > 0 } note && evt.Kind is TelemetryKind.Tick or TelemetryKind.Link)
                noteCounts[note] = noteCounts.GetValueOrDefault(note) + 1;

            switch (evt.Kind)
            {
                case TelemetryKind.Session:
                    sessions++;
                    if (evt.CatalogVersion is { } recordedCat)
                    {
                        recordedCatalog = recordedCat;
                        catalogSkew = recordedCat != AbilityCatalog.CatalogVersion;
                    }
                    body.Append(F("t={0}s  session  app={1} proto={2} capacity={3} {4}\n",
                        Sec(evt.TMs), evt.App ?? "?", Int(evt.Proto), Int(evt.Capacity), evt.Note ?? "-"));
                    break;

                case TelemetryKind.Link:
                    links++;
                    if (evt.Error is not null) errors++;
                    if (evt.Fault is { } linkFault) faultCounts[linkFault] = faultCounts.GetValueOrDefault(linkFault) + 1;
                    body.Append(F("t={0}s  link  visible={1}  note=\"{2}\"{3}\n",
                        Sec(evt.TMs), YesNo(evt.Link), evt.Note ?? "-",
                        evt.Fault is { } lf ? $"  fault={lf}" : ""));
                    break;

                case TelemetryKind.Send:
                    sends++;
                    if (evt.Send is { } send)
                    {
                        sendCounts[send.What] = sendCounts.GetValueOrDefault(send.What) + 1;
                        lastSendMs = evt.TMs;
                        lastSendDesc = send.Slot is { } slot
                            ? $"{send.What} {slot} {send.Key.Describe()}"
                            : $"{send.What} {send.Key.Describe()}";
                        body.Append(F("t={0}s  send  {1}  interval={2}ms\n",
                            Sec(evt.TMs), lastSendDesc, send.IntervalMs));
                        // Buffered: the following tick(s) were evaluated BEFORE
                        // this send in the live run (see the ordering note).
                        pendingSends.Add((send.Slot, send.SpellId, evt.TMs));
                    }
                    break;

                case TelemetryKind.Tick:
                    ticks++;
                    if (evt.Fault is { } tickFault) faultCounts[tickFault] = faultCounts.GetValueOrDefault(tickFault) + 1;
                    if (evt.Fault is not null || evt.Error is not null) errors++;

                    DecisionContext? context = null;
                    DecisionResult? recomputed = null;
                    if (evt.Candidates is { Length: > 0 })
                    {
                        context = ToContext(evt);
                        recomputed = DecisionEngine.Evaluate(context);
                    }

                    var interesting = allTicks || evt.Candidates is { Length: > 0 } || evt.Fault is not null || evt.Error is not null;
                    if (evt.Decision is { } recorded)
                    {
                        decisions++;
                        if (recorded.Reason is { } reason) reasonCounts[(int)reason]++;
                        var mismatch = recorded.Source == "intelligence"
                            ? recomputed is null || !SameDecision(recomputed.Value, recorded)
                            : !MatchesFallback(recorded);
                        if (mismatch)
                        {
                            mismatches++;
                            interesting = true;
                        }
                    }

                    // Policy verdict replay runs BEFORE ApplyPendingSends so the
                    // tick never sees its own send (see the ordering note).
                    // A policy record without the v2.3 defensive-urgency field
                    // (du) predates urgency gating: its verdicts cannot be
                    // recomputed by the current evaluator, so they are reported
                    // as legacy and skipped instead of false-mismatching.
                    if (evt.TtkFeedMs is { } ttkFeedMs)
                    {
                        var thp = evt.TargetHpPct ?? -1;
                        ttkEstimator.Update(ttkFeedMs, evt.HasTarget ?? false, thp >= 0,
                            TtkEstimator.BandFromPercent(thp));
                    }
                    if (evt.Policy is { Verdicts: { Length: > 0 } pendingVerdicts, Options: { } pendingOptions })
                    {
                        if (evt.Policy.DefensiveUrgency is null)
                        {
                            legacyPolicyRecords++;
                            interesting = true;
                            body.Append(F("  policy  : legacy pre-v2.3 record ({0} verdict(s) not recomputed; defensive urgency was not recorded)\n",
                                pendingVerdicts.Length));
                        }
                        else
                        {
                            var pendingCombat = ToCombat(evt.Policy!).WithTtk(ttkEstimator.Estimate);
                                var pendingPolicyOptions = new PolicyOptions
                                {
                                    SoloEnabled = pendingOptions.Solo,
                                    EmergencyHpPct = pendingOptions.EmergencyHpPct,
                                    SelfSustainHpPct = pendingOptions.SelfSustainHpPct,
                                    DefensiveEscalateHpPct = pendingOptions.DefensiveEscalateHpPct,
                                    SoloEscalation = pendingOptions.SoloEscalation ?? true,
                                    SoloMinorHpPct = pendingOptions.SoloMinorHpPct ?? 75,
                                    SoloMajorHpPct = pendingOptions.SoloMajorHpPct ?? 50,
                                    SoloImmunityHpPct = pendingOptions.SoloImmunityHpPct ?? 30,
                                    Abilities = AbilityPolicy.FromIds(pendingOptions.AbilitiesOn, pendingOptions.AbilitiesOff),
                                    Preset = ParseEnum(pendingOptions.Preset, RotationPreset.Full),
                                    TargetPreset = ParseEnum(pendingOptions.TargetPreset, TargetPreset.SingleTarget),
                                };
                            var pendingCatalog = replayCatalog ??= AbilityCatalog.Default;
                            foreach (var recordedVerdict in pendingVerdicts)
                            {
                                // Ext2 alternate verdicts were evaluated with the
                                // SelfHeal2 candidate's OWN range probe; apply it
                                // so the recomputation sees the same context.
                                var verdictCombat = recordedVerdict.Alternate
                                    ? pendingCombat.WithSlotRange((int)recordedVerdict.Slot, ParseTriEncoded(recordedVerdict.AlternateRange))
                                    : pendingCombat;
                                var recomputedVerdict = PolicyEvaluator.Evaluate(new PolicyInput
                                {
                                    Slot = recordedVerdict.Slot,
                                    SpellId = recordedVerdict.SpellId,
                                    Context = verdictCombat,
                                    Options = pendingPolicyOptions,
                                    Memory = policyMemory,
                                    NowMs = evt.TMs,
                                    InCombat = evt.InCombat ?? false,
                                    HasTarget = evt.HasTarget ?? false,
                                }, pendingCatalog);
                                policyVerdicts++;
                                var verdictMismatch = recomputedVerdict.Verdict.ToString() != recordedVerdict.Verdict
                                    || recomputedVerdict.Reason != recordedVerdict.Reason;
                                // v2.7: when the recording carries provider/evidence,
                                // the recomputed decision must reproduce them too. A
                                // legacy record (both null) is tolerated unchanged.
                                var providerMismatch = recordedVerdict.Provider is { } recordedProvider
                                    && (recomputedVerdict.Provider != recordedProvider
                                        || !SameEvidence(recordedVerdict.Evidence, recomputedVerdict.Evidence));
                                if (verdictMismatch || providerMismatch)
                                {
                                    policyVerdictMismatches++;
                                    body.Append(F("  VERDICT MISMATCH {0} spell={1}: recorded {2} \"{3}\" -> recomputed {4} \"{5}\"{6}\n",
                                        recordedVerdict.Slot, recordedVerdict.SpellId,
                                        recordedVerdict.Verdict, recordedVerdict.Reason,
                                        recomputedVerdict.Verdict, recomputedVerdict.Reason,
                                        providerMismatch
                                            ? $" (provider {recordedVerdict.Provider} -> {recomputedVerdict.Provider})"
                                            : ""));
                                }
                            }
                        }
                    }

                    ApplyPendingSends();

                    if (!interesting) break;

                    body.Append(F("t={0}s  tick #{1}  {2}  hb={3}  target={4}  gcd={5}  link={6}  note=\"{7}\"\n",
                        Sec(evt.TMs), evt.Seq, evt.State ?? "-", Int(evt.Heartbeat),
                        YesNo(evt.HasTarget), YesNo(evt.OnGcd), YesNo(evt.Link), evt.Note ?? "-"));
                    if (evt.Fault is not null || evt.Error is not null)
                        body.Append(F("  error   : {0}{1}\n",
                            evt.Fault is { } f ? $"fault={f} " : "", evt.Error ?? ""));
                    if (evt.Slots is { } slots)
                        body.Append(F("  slots   : {0}\n", DescribeSlots(slots)));

                    if (evt.Candidates is { } candidates)
                    {
                        foreach (var c in candidates)
                            body.Append(F("  cand    : {0,-10} {1,-6} {2}  age={3}s unchanged={4}s pressed={5}\n",
                                c.Slot, c.Stroke?.Describe() ?? "-",
                                c.Enabled ? (c.Actionable ? "enabled actionable" : "enabled MOVEMENT-bound") : "DISABLED",
                                Sec(evt.TMs - c.FirstSeenMs), Sec(evt.TMs - c.LastChangedMs), c.EverPressed ? "yes" : "no"));
                    }

                    if (recomputed is { } rec)
                    {
                        if (evt.Decision is { Source: "legacy" } legacyRecorded)
                        {
                            body.Append("  decision: legacy priority (evaluator off)\n");
                            body.Append(F("  wouldsel: {0}\n", DescribeDecision(rec)));
                            body.Append(F("  recorded: legacy order {0}\n", string.Join(", ", legacyRecorded.Order)));
                        }
                        else
                        {
                            body.Append(F("  decision: {0}\n", DescribeDecision(rec)));
                            if (evt.Decision is { } recordedDecision)
                            {
                                var ok = SameDecision(rec, recordedDecision);
                                body.Append(F("  recorded: {0}{1}\n", DescribeDecision(recordedDecision),
                                    ok ? "  OK" : "  MISMATCH"));
                            }
                        }
                    }
                    else if (evt.Decision is { } onlyRecorded)
                    {
                        body.Append(F("  recorded: {0} (no candidate context)\n", DescribeDecision(onlyRecorded)));
                    }

                    if (evt.Policy is { } policy)
                    {
                        body.Append(F("  policy  : {0}{1}  held={2} skipped={3} suppressed={4}{5}\n",
                            policy.Reason, policy.Selected is { } policySlot ? $" -> {policySlot}" : "",
                            policy.Held, policy.Skipped, policy.Suppressed,
                            policy.Detail is { Length: > 0 } detail ? $"  \"{detail}\"" : ""));
                        if (policy.Fresh)
                            policyCounts[policy.Reason] = policyCounts.GetValueOrDefault(policy.Reason) + 1;
                        if (policy.Verdicts is { Length: > 0 } verdicts)
                        {
                            foreach (var verdict in verdicts)
                                body.Append(F("  verdict : {0,-10} spell={1,-8} {2,-4} {3}{4}{5}\n",
                                    verdict.Slot, verdict.SpellId, verdict.Verdict, verdict.Reason,
                                    verdict.Provider is { Length: > 0 } provider ? $"  [{provider}]" : "",
                                    verdict.Evidence is { Length: > 0 } why ? $"  why: {string.Join("; ", why)}" : ""));
                        }
                    }

                    if (context is not null && recomputed is { } rejectionSource)
                    {
                        var rejected = RejectedList(context, rejectionSource);
                        if (rejected.Count > 0)
                            body.Append(F("  rejected: {0}\n", string.Join(", ", rejected)));
                    }

                    if (recomputed is { Reason: DecisionReason.GcdHold, Selected: { } held } && held != Slot.Interrupt)
                        body.Append("  held    : GCD active - send path skips non-interrupt\n");

                    if (context is not null && recomputed is { DemotedStale: true })
                        body.Append("  stale   : a pressed candidate was demoted behind a fresh one\n");

                    if (lastSendMs != long.MinValue && evt.TMs >= lastSendMs)
                        body.Append(F("  timing  : last send {0} {1}ms ago\n", lastSendDesc, evt.TMs - lastSendMs));
                    break;
            }
        }

        ApplyPendingSends();   // trailing sends with no following tick

        var report = new StringBuilder();
        report.Append("MaxDps Companion rotation replay\n");
        report.Append(source.Length > 0 ? $"file       : {source}\n" : "file       : (in-memory events)\n");
        report.Append($"format     : {TelemetryFormat.Version}\n");
        report.Append($"events     : {events.Count} (tick {ticks}, send {sends}, link {links}, session {sessions}, bad {badLines})\n");
        report.Append(haveSpan
            ? $"span       : {Sec(spanStart)}s .. {Sec(spanEnd)}s ({Sec(spanEnd - spanStart)}s)\n"
            : "span       : -\n");
        report.Append($"decisions  : {decisions} recorded\n");
        report.Append($"verdicts   : {policyVerdicts} policy verdicts recomputed, {policyVerdictMismatches} mismatch(es)\n");
        if (legacyPolicyRecords > 0)
            report.Append($"             ({legacyPolicyRecords} legacy pre-v2.3 policy record(s) skipped: defensive urgency was not recorded)\n");
        if (recordedCatalog is { } catalogRev)
            report.Append($"catalog    : recorded v{catalogRev}, current v{AbilityCatalog.CatalogVersion}{(catalogSkew ? "  (SKEW)" : "")}\n");
        if (catalogSkew)
            report.Append("             (catalog revision differs: any verdict mismatch may be data skew, not non-determinism)\n");
        report.Append($"decode errs: {errors}\n");
        report.Append($"mismatches : {mismatches}\n");
        if (mismatches > 0)
            report.Append("             (recomputed decision differs from the recording - build/version skew or non-determinism; investigate)\n");
        if (policyVerdictMismatches > 0)
            report.Append("             (recomputed policy verdict differs from the recording - build/version skew or non-determinism; investigate)\n");
        report.Append('\n');
        report.Append(body);
        report.Append('\n');
        report.Append("summary\n");
        AppendCounts(report, "reasons", reasonCounts.Select((count, index) => (Enum.GetValues<DecisionReason>()[index].ToString(), count)));
        AppendCounts(report, "policy ", policyCounts.Select(kv => (kv.Key, kv.Value)));
        AppendCounts(report, "sends  ", sendCounts.Select(kv => (kv.Key, kv.Value)));
        AppendCounts(report, "faults ", faultCounts.Select(kv => (kv.Key, kv.Value)));
        AppendCounts(report, "notes  ", noteCounts.Select(kv => (kv.Key, kv.Value)));

        return new ReplayResult
        {
            Report = report.ToString(),
            Events = events.Count,
            Ticks = ticks,
            Decisions = decisions,
            Sends = sends,
            Errors = errors,
            Mismatches = mismatches,
            BadLines = badLines,
            Verdicts = policyVerdicts,
            VerdictMismatches = policyVerdictMismatches,
        };
    }

    private static DecisionContext ToContext(TelemetryEvent evt) => new()
    {
        InCombat = evt.InCombat ?? false,
        OnGcd = evt.OnGcd ?? false,
        HasTarget = evt.HasTarget ?? false,
        State = Enum.TryParse<BridgeState>(evt.State, out var state) ? state : BridgeState.Idle,
        NowMs = evt.TMs,
        StaleAfterMs = evt.StaleAfterMs ?? 1500,
        Candidates = (evt.Candidates ?? []).Select(ToCandidate).ToArray(),
    };

    /// <summary>
    /// Rebuilds the exact combat context the policy saw for one recorded tick
    /// (the pol record carries every field the policy may read).
    /// </summary>
    private static CombatContext ToCombat(TelemetryPolicy policy)
    {
        // hpSrc is absent on records written before v3.0.0; a known HP then was
        // necessarily a plain cell-27 reading, so default it to Plain.
        var hpSource = ParseEnum(policy.HpSource, HpSource.Unknown);
        if (policy.HpKnown && hpSource == HpSource.Unknown) hpSource = HpSource.Plain;
        return new CombatContext
        {
        HpValid = policy.HpKnown,
        HpSource = hpSource,
        HpPct = policy.HpPct ?? 0,
        HpPctUpper = policy.HpPctUpper ?? policy.HpPct ?? 0,
        Cast = ParseEnum(policy.Cast, PlayerCastState.Unknown),
        TargetCasting = ParseTriState(policy.TargetCast),
        TargetCastInterruptible = ParseTriState(policy.TargetInterruptible),
        TargetInMelee = ParseTriState(policy.Melee),
        SlotRange = DecodeRange(policy.Range),
        SlotBuffActive = DecodeBuffs(policy.Buffs),
        DefensiveUrgency = ParseEnum(policy.DefensiveUrgency, DefensiveUrgency.Unknown),
        StaggerUrgency = ParseEnum(policy.StaggerUrgency, DefensiveUrgency.Unknown),
        DefensiveCatalogSource = policy.DefensiveCatalogSource ?? false,
        OffensiveDerivedGapFill = policy.OffensiveDerivedGapFill,
        ContextValid = policy.ContextValid,
        Class = policy.Class,
        Spec = policy.Spec,
        };
    }

    private static TriState ParseTriState(string? value) => value switch
    {
        nameof(TriState.Yes) => TriState.Yes,
        nameof(TriState.No) => TriState.No,
        _ => TriState.Unknown,
    };

    /// <summary>Decodes the compact 'U'/'I'/'O' alternate-range field.</summary>
    private static TriState ParseTriEncoded(string? value) => value switch
    {
        "I" => TriState.Yes,
        "O" => TriState.No,
        _ => TriState.Unknown,
    };

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) ? parsed : fallback;

    private static TriState[] DecodeRange(string? encoded)
    {
        var range = new TriState[PixelProtocol.SlotCount];
        if (encoded is null) return range;
        for (var i = 0; i < range.Length && i < encoded.Length; i++)
            range[i] = encoded[i] switch { 'I' => TriState.Yes, 'O' => TriState.No, _ => TriState.Unknown };
        return range;
    }

    private static TriState[] DecodeBuffs(string? encoded)
    {
        // The v1 recording stores Yes as '1' and everything else as '0'; replay
        // reconstructs Yes/No (Unknown is not stored). The policy treats No and
        // Unknown identically for the buff gate, so verdicts replay exactly.
        var buffs = new TriState[PixelProtocol.SlotCount];
        if (encoded is null) return buffs;
        for (var i = 0; i < buffs.Length && i < encoded.Length; i++)
            buffs[i] = encoded[i] == '1' ? TriState.Yes : TriState.No;
        return buffs;
    }

    private static ActionCandidate ToCandidate(TelemetryCandidate candidate) => new(
        candidate.Slot,
        candidate.Stroke?.ToKeyStroke() ?? default,
        candidate.Enabled,
        candidate.Actionable,
        candidate.FirstSeenMs,
        candidate.LastChangedMs,
        candidate.LastPressedMs,
        candidate.EverPressed);

    /// <summary>True when a recorded evidence list matches the recomputed one (null = not recorded = tolerant).</summary>
    private static bool SameEvidence(string[]? recorded, IReadOnlyList<string> recomputed)
    {
        if (recorded is null) return true;
        if (recorded.Length != recomputed.Count) return false;
        for (var i = 0; i < recorded.Length; i++)
            if (!string.Equals(recorded[i], recomputed[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool SameDecision(DecisionResult result, TelemetryDecision recorded) =>
        recorded.Source == "intelligence"
        && recorded.Selected == result.Selected
        && recorded.Reason == result.Reason
        && recorded.Confidence == result.Confidence
        && recorded.DemotedStale == result.DemotedStale
        && recorded.Order.AsSpan().SequenceEqual(result.Order);

    private static bool MatchesFallback(TelemetryDecision recorded)
    {
        var fallback = DecisionResult.Fallback();
        return recorded.Selected is null
            && recorded.Reason == fallback.Reason
            && recorded.Confidence == fallback.Confidence
            && recorded.Order.AsSpan().SequenceEqual(fallback.Order);
    }

    /// <summary>
    /// Candidates the evaluator/send path did not act on, with the reason.
    /// Derived from the recomputed decision + the recorded context, so it
    /// cannot drift from the evaluator's own rules.
    /// </summary>
    private static List<string> RejectedList(DecisionContext context, DecisionResult result)
    {
        var rejected = new List<string>();
        foreach (var candidate in context.Candidates)
        {
            if (!candidate.Enabled) { rejected.Add($"{candidate.Slot} (slot disabled)"); continue; }
            if (!candidate.Actionable) { rejected.Add($"{candidate.Slot} (movement-bound)"); continue; }
            if (result.Selected == candidate.Slot) continue;

            var index = Array.IndexOf(result.Order, candidate.Slot);
            if (index > 0)
            {
                var stale = candidate.IsStale(context.NowMs, context.StaleAfterMs) ? ", stale" : "";
                rejected.Add($"{candidate.Slot} (lower rank #{index + 1}{stale})");
                continue;
            }

            if (index < 0)
            {
                var duplicate = context.Candidates.FirstOrDefault(other =>
                    other.Slot != candidate.Slot
                    && other.Enabled
                    && other.Stroke.VirtualKey == candidate.Stroke.VirtualKey
                    && other.Stroke.Shift == candidate.Stroke.Shift
                    && other.Stroke.Ctrl == candidate.Stroke.Ctrl
                    && other.Stroke.Alt == candidate.Stroke.Alt
                    && Array.IndexOf(result.Order, other.Slot) >= 0);
                rejected.Add(duplicate.Stroke.VirtualKey != 0
                    ? $"{candidate.Slot} (duplicate stroke of {duplicate.Slot})"
                    : $"{candidate.Slot} (not in order)");
            }
        }
        return rejected;
    }

    private static void AppendCounts(StringBuilder report, string label, IEnumerable<(string Key, int Count)> counts)
    {
        var entries = counts
            .Where(pair => pair.Count > 0)
            .OrderByDescending(pair => pair.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0)
        {
            report.Append($"  {label} : -\n");
            return;
        }
        report.Append($"  {label} : {string.Join(", ", entries.Select(pair => $"{pair.Key} {pair.Count}"))}\n");
    }

    private static string DescribeDecision(DecisionResult decision) =>
        decision.Selected is { } slot
            ? $"{slot} -> {decision.Reason} ({decision.Confidence}%){(decision.DemotedStale ? " [stale demoted]" : "")}"
            : $"{decision.Reason} ({decision.Confidence}%)";

    private static string DescribeDecision(TelemetryDecision decision)
    {
        var reason = decision.Reason?.ToString() ?? "-";
        var confidence = decision.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "-";
        return decision.Selected is { } slot
            ? $"{slot} -> {reason} ({confidence}%){(decision.DemotedStale ? " [stale demoted]" : "")}"
            : $"{reason} ({confidence}%)";
    }

    private static string DescribeSlots(TelemetryStroke?[] slots)
    {
        var parts = new string[PixelProtocol.SlotCount];
        for (var i = 0; i < PixelProtocol.SlotCount && i < slots.Length; i++)
            parts[i] = $"{SlotName((Slot)i)}={slots[i]?.Describe() ?? "-"}";
        return string.Join("  ", parts);
    }

    private static string SlotName(Slot slot) => slot switch
    {
        Slot.Main => "Main",
        Slot.Offensive => "Off",
        Slot.Defensive => "Def",
        Slot.Consumable => "Cons",
        Slot.Trinket => "Trin",
        Slot.Interrupt => "Int",
        _ => slot.ToString(),
    };

    private static string YesNo(bool? value) => value switch { true => "yes", false => "no", _ => "-" };

    private static string Int(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Sec(long ms) => (ms / 1000.0).ToString("F3", CultureInfo.InvariantCulture);

    private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
