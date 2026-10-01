using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>Mutable filter state for the ability explorer. Rebuilt only on change (debounced).</summary>
internal sealed class AbilityFilterState
{
    public string Query { get; set; } = "";
    public string Status { get; set; } = "All";
    public readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDefault => Query.Length == 0 && Status == "All" && Categories.Count == 0;
}

/// <summary>
/// Owner-drawn virtual list. Only visible rows paint; nothing is created per
/// row, so the 3279-entry catalog stays responsive. One row can expand inline;
/// the rest of the detail lives in the inspector.
/// </summary>
internal sealed class VirtualAbilityList : Control
{
    private readonly List<AbilityDefinition> _items = [];
    private readonly List<int> _expanded = [];
    private readonly VScrollBar _scroll = new() { Dock = DockStyle.Right, Visible = true };
    private readonly Func<AbilityDefinition, bool> _isEnabled;
    private int _hoverRow = -1;

    public int RowHeight => Math.Max(44, (int)(52 * (DeviceDpi / 96f)));
    public int ExpandedExtra => Math.Max(120, (int)(180 * (DeviceDpi / 96f)));

    public event Action<AbilityDefinition>? Selected;
    public event Action<AbilityDefinition, bool>? Toggled;

    public VirtualAbilityList(Func<AbilityDefinition, bool> isEnabled)
    {
        _isEnabled = isEnabled;
        DoubleBuffered = true;
        BackColor = DesignTokens.Background;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Ability list";
        TabStop = true;
        Controls.Add(_scroll);
        _scroll.ValueChanged += (_, _) => Invalidate();
    }

    public AbilityDefinition? Selection { get; private set; }

    public int Count => _items.Count;

    /// <summary>v3.4.0 §6: empty-state copy supplied by the explorer (names the preset + class/spec).</summary>
    public string EmptyText { get; set; } = "No abilities match the current filters.";

    /// <summary>v3.4.0 §5: per-spell condition one-liner computed once per rebuild.</summary>
    public IReadOnlyDictionary<int, string> Conditions { get; set; } = new Dictionary<int, string>();

    /// <summary>
    /// S6 Class Browser: per-spell live "why held / why ready" verdict supplied
    /// by the host. The list only paints it — the scheduler owns evaluation, so
    /// nothing here re-derives a gate. Missing entries render no verdict pill.
    /// </summary>
    public IReadOnlyDictionary<int, string> Verdicts { get; set; } = new Dictionary<int, string>();

    // S5: one owned, autosizing themed bubble app-wide (was a bare ToolTip that
    // clipped long "why held" reasons).
    private readonly OwnedToolTip _tip = new();

    public void SetItems(List<AbilityDefinition> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _expanded.Clear();
        _scroll.Value = 0;
        UpdateScroll();
        Invalidate();
    }

    private int ContentHeight()
    {
        var total = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            total += RowHeight;
            if (_expanded.Contains(_items[i].SpellId)) total += ExpandedExtra;
        }
        return total;
    }

    private void UpdateScroll()
    {
        var content = ContentHeight();
        var view = Math.Max(1, ClientSize.Height);
        _scroll.Maximum = Math.Max(0, content - 1);
        _scroll.LargeChange = Math.Max(1, view);
        _scroll.SmallChange = RowHeight;
        _scroll.Visible = content > view;
        if (_scroll.Value > _scroll.Maximum) _scroll.Value = _scroll.Maximum;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _scroll.Width = Math.Max(12, (int)(14 * (DeviceDpi / 96f)));
        UpdateScroll();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var delta = -e.Delta / 120 * RowHeight * 3;
        _scroll.Value = Math.Clamp(_scroll.Value + delta, _scroll.Minimum, Math.Max(_scroll.Minimum, _scroll.Maximum - _scroll.LargeChange + 1));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = RowAt(e.Y);
        if (index != _hoverRow)
        {
            _hoverRow = index;
            // v3.4.0 §5: the hover hint carries the full reason.
            if (index >= 0 && index < _items.Count) _tip.SetToolTip(this, FullReason(_items[index]));
            else _tip.SetToolTip(this, string.Empty);
            Invalidate();
        }
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    private static string FullReason(AbilityDefinition ability)
    {
        var reason = $"{ability.Name}: {Why(ability)}; holds {Hold(ability)}.";
        if (ability.MinimumUrgency != DefensiveUrgency.White && ability.IsSurvival)
            reason += $" Urgency {ability.MinimumUrgency}+.";
        return reason;
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var index = RowAt(e.Y);
        if (index >= 0 && index < _items.Count)
        {
            var ability = _items[index];
            if (ToggleRect(index).Contains(e.X, e.Y) && ability.Automatable)
            {
                Toggled?.Invoke(ability, !_isEnabled(ability));
            }
            else
            {
                Selection = ability;
                if (_expanded.Contains(ability.SpellId)) _expanded.Remove(ability.SpellId);
                else _expanded.Add(ability.SpellId);
                UpdateScroll();
                Selected?.Invoke(ability);
                Invalidate();
            }
        }
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_items.Count == 0) { base.OnKeyDown(e); return; }
        var current = Selection is null ? -1 : _items.FindIndex(a => a.SpellId == Selection.SpellId);
        switch (e.KeyCode)
        {
            case Keys.Down:
                Selection = _items[Math.Clamp(current + 1, 0, _items.Count - 1)];
                EnsureVisible(Selection);
                Selected?.Invoke(Selection);
                e.Handled = true;
                break;
            case Keys.Up:
                Selection = _items[Math.Clamp(current - 1, 0, _items.Count - 1)];
                EnsureVisible(Selection);
                Selected?.Invoke(Selection);
                e.Handled = true;
                break;
            case Keys.Space:
            case Keys.Enter:
                if (Selection is { } sel && sel.Automatable) Toggled?.Invoke(sel, !_isEnabled(sel));
                e.Handled = true;
                break;
        }
        Invalidate();
        base.OnKeyDown(e);
    }

    private void EnsureVisible(AbilityDefinition ability)
    {
        var index = _items.FindIndex(a => a.SpellId == ability.SpellId);
        if (index < 0) return;
        var y = YForIndex(index);
        if (y < _scroll.Value) _scroll.Value = y;
        else if (y + RowHeight > _scroll.Value + ClientSize.Height) _scroll.Value = y + RowHeight - ClientSize.Height + 1;
        Invalidate();
    }

    private int YForIndex(int index)
    {
        var y = -_scroll.Value;
        for (var i = 0; i < index && i < _items.Count; i++)
        {
            y += RowHeight;
            if (_expanded.Contains(_items[i].SpellId)) y += ExpandedExtra;
        }
        return y;
    }

    private int RowAt(int mouseY)
    {
        var y = -_scroll.Value;
        for (var i = 0; i < _items.Count; i++)
        {
            var h = RowHeight + (_expanded.Contains(_items[i].SpellId) ? ExpandedExtra : 0);
            if (mouseY >= y && mouseY < y + h) return i;
            y += h;
            if (y > ClientSize.Height && mouseY < y) return -1;
        }
        return -1;
    }

    private Rectangle ToggleRect(int index)
    {
        var y = YForIndex(index);
        var size = (int)(22 * (DeviceDpi / 96f));
        return new Rectangle(Width - _scroll.Width - 52, y + (RowHeight - size) / 2, size, size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(DesignTokens.Background);
        e.Graphics.FillRectangle(bg, ClientRectangle);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, EmptyText,
                DesignTokens.Type(DesignTokens.BodySize), new Rectangle(16, 20, Width - 32, 24),
                DesignTokens.TextMuted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return;
        }

        var viewBottom = ClientSize.Height;
        var y = -_scroll.Value;
        for (var i = 0; i < _items.Count; i++)
        {
            var ability = _items[i];
            var expanded = _expanded.Contains(ability.SpellId);
            var h = RowHeight + (expanded ? ExpandedExtra : 0);
            if (y + h >= 0 && y <= viewBottom)
                DrawRow(e.Graphics, ability, i, y, h, expanded);
            y += h;
            if (y > viewBottom) break;
        }
    }

    private void DrawRow(Graphics g, AbilityDefinition ability, int index, int y, int height, bool expanded)
    {
        var selected = Selection?.SpellId == ability.SpellId;
        var rowWidth = Width - _scroll.Width;
        var rowBounds = new Rectangle(0, y, rowWidth, height);
        using (var fill = new SolidBrush(selected ? DesignTokens.SurfaceElevated : index % 2 == 0 ? DesignTokens.Surface : DesignTokens.Background))
            g.FillRectangle(fill, rowBounds);
        if (selected)
        {
            using var marker = new SolidBrush(DesignTokens.Accent);
            g.FillRectangle(marker, 0, y, 3, height);
        }
        else if (_hoverRow == index)
        {
            using var hover = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
            g.FillRectangle(hover, rowBounds);
        }

        var iconSize = (int)(32 * (DeviceDpi / 96f));
        var icon = new Rectangle(10, y + (RowHeight - iconSize) / 2, iconSize, iconSize);
        DrawIcon(g, ability, icon);

        var textLeft = icon.Right + 10;
        // Reserved right cluster: tier badge + owner badge + verdict pill +
        // ON/OFF label + toggle, with explicit spacing so none overlap (S6).
        var textWidth = Math.Max(40, rowWidth - textLeft - 272);
        TextRenderer.DrawText(g, ability.Name, DesignTokens.Type(DesignTokens.LabelSize, FontStyle.Bold),
            new Rectangle(textLeft, y + 7, textWidth, 20), DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        // v3.4.0 §5: append the catalog-derived condition one-liner when known.
        var condition = Conditions.TryGetValue(ability.SpellId, out var known)
            ? known
            : AbilityViewPresets.ConditionLine(ability);
        // S6: surface the cooldown in the row body (icon/name/cooldown/tier).
        var cooldown = ability.CooldownMs > 0
            ? $"{ability.CooldownMs / 1000.0:0.#}s"
            : "";
        var sub = condition.Length == 0
            ? $"{ability.Category} \u00B7 {SubRole(ability)}"
            : $"{ability.Category} \u00B7 {SubRole(ability)} \u00B7 {condition}";
        if (cooldown.Length > 0) sub += $" \u00B7 {cooldown}";
        TextRenderer.DrawText(g, sub, DesignTokens.Type(DesignTokens.MetaSize),
            new Rectangle(textLeft, y + 26, textWidth, 18), DesignTokens.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        DrawTierBadge(g, ability, rowWidth, y);
        DrawOwnerBadge(g, ability, rowWidth, y);
        DrawVerdict(g, ability, rowWidth, y);
        DrawToggle(g, ability, ToggleRect(index));

        if (expanded) DrawInlineDetails(g, ability, y + RowHeight, rowWidth);
        using var sep = new Pen(Color.FromArgb(40, DesignTokens.Border), 1F);
        g.DrawLine(sep, 0, y + height - 1, rowWidth, y + height - 1);
    }

    private void DrawIcon(Graphics g, AbilityDefinition ability, Rectangle bounds)
    {
        var hue = (int)(Math.Abs((long)ability.SpellId * 2654435761u) % 360);
        var color = ColorFromHue(hue);
        using var path = Ui.Rounded(bounds, 7);
        using var fill = new SolidBrush(DesignTokens.Tint(color, DesignTokens.Surface));
        using var border = new Pen(color, 1F);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        var letter = string.IsNullOrEmpty(ability.Name) ? "?" : ability.Name[..1].ToUpperInvariant();
        TextRenderer.DrawText(g, letter, DesignTokens.Type(13F, FontStyle.Bold), bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private static Color ColorFromHue(int hue)
    {
        var (r, g, b) = HsvToRgb(hue, 0.5, 0.9);
        return Color.FromArgb(r, g, b);
    }

    private static (int, int, int) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    private void DrawOwnerBadge(Graphics g, AbilityDefinition ability, int rowWidth, int y)
    {
        var owner = ability.Ownership switch
        {
            IntelligenceOwnership.Companion => "Companion",
            IntelligenceOwnership.MaxDps => "MaxDps",
            IntelligenceOwnership.Shared => "Shared",
            IntelligenceOwnership.Manual => "Manual",
            _ => "Unavailable",
        };
        var tone = ability.Ownership switch
        {
            IntelligenceOwnership.Companion => DesignTokens.Success,
            IntelligenceOwnership.MaxDps => DesignTokens.Info,
            IntelligenceOwnership.Shared => DesignTokens.Accent,
            IntelligenceOwnership.Manual => DesignTokens.TextMuted,
            _ => DesignTokens.Warning,
        };
        var auto = ability.Automatable;
        var left = rowWidth - 180;
        var rect = new Rectangle(left, y + 14, 84, 20);
        using (var path = Ui.Rounded(rect, 6))
        using (var fill = new SolidBrush(DesignTokens.Tint(tone, DesignTokens.Surface)))
        using (var border = new Pen(Color.FromArgb(130, tone), 1F))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        TextRenderer.DrawText(g, owner, DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold), rect, tone,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        _ = auto; // stale toggle state (N/A) and the sub-role line carry the structural meaning
    }

    /// <summary>
    /// S6: the defensive tier badge (Minor/Major/Immunity), drawn only for
    /// survival rows whose tier is known. Sits left of the ownership badge.
    /// </summary>
    private void DrawTierBadge(Graphics g, AbilityDefinition ability, int rowWidth, int y)
    {
        if (rowWidth < 380) return;
        if (!ability.IsSurvival || ability.Tier == DefensiveTier.None) return;
        var tier = ability.Tier switch
        {
            DefensiveTier.Minor => "Minor",
            DefensiveTier.Major => "Major",
            DefensiveTier.Immunity => "Immunity",
            _ => "",
        };
        if (tier.Length == 0) return;
        var tone = ability.Tier switch
        {
            DefensiveTier.Immunity => DesignTokens.Danger,
            DefensiveTier.Major => DesignTokens.Warning,
            _ => DesignTokens.Info,
        };
        var rect = new Rectangle(rowWidth - 258, y + 14, 72, 20);
        using (var path = Ui.Rounded(rect, 6))
        using (var fill = new SolidBrush(DesignTokens.Tint(tone, DesignTokens.Surface)))
        using (var border = new Pen(Color.FromArgb(130, tone), 1F))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        TextRenderer.DrawText(g, tier, DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold), rect, tone,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// S6: the live why-held / why-ready verdict the host read from the
    /// scheduler. Painted as a pill under the badges; nothing here evaluates a
    /// gate (the scheduler owns evaluation).
    /// </summary>
    private void DrawVerdict(Graphics g, AbilityDefinition ability, int rowWidth, int y)
    {
        if (rowWidth < 380) return;
        if (!Verdicts.TryGetValue(ability.SpellId, out var verdict) || string.IsNullOrWhiteSpace(verdict)) return;
        var tone = verdict.StartsWith("held", StringComparison.OrdinalIgnoreCase)
            ? DesignTokens.Warning
            : DesignTokens.Success;
        var rect = new Rectangle(rowWidth - 258, y + 36, 162, 16);
        using var fill = new SolidBrush(DesignTokens.Tint(tone, DesignTokens.Surface));
        g.FillRectangle(fill, rect);
        TextRenderer.DrawText(g, verdict, DesignTokens.Type(DesignTokens.MicroSize, FontStyle.Bold), 
            new Rectangle(rect.X + 4, rect.Y, rect.Width - 8, rect.Height), tone,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private void DrawToggle(Graphics g, AbilityDefinition ability, Rectangle r)
    {
        var on = _isEnabled(ability);
        var enabled = ability.Automatable;
        var track = new Rectangle(r.X - 12, r.Y, 34, 22);
        using var path = Ui.Rounded(track, 11);
        using var fill = new SolidBrush(!enabled ? DesignTokens.Disabled : on ? DesignTokens.Success : DesignTokens.TextMuted);
        g.FillPath(fill, path);
        var thumb = new Rectangle(on ? track.Right - 18 : track.X + 2, track.Y + 2, 18, 18);
        using var thumbFill = new SolidBrush(Color.White);
        g.FillEllipse(thumbFill, thumb);
        TextRenderer.DrawText(g, enabled ? (on ? "ON" : "OFF") : "N/A", DesignTokens.Type(7.5F, FontStyle.Bold),
            new Rectangle(track.X - 26, track.Y, 24, track.Height), DesignTokens.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private void DrawInlineDetails(Graphics g, AbilityDefinition ability, int top, int rowWidth)
    {
        var rect = new Rectangle(10, top, rowWidth - 20, ExpandedExtra - 12);
        using var fill = new SolidBrush(Color.FromArgb(22, 0, 0, 0));
        g.FillRectangle(fill, rect);
        var lines = new[]
        {
            $"Why it can fire: {Why(ability)}",
            $"When it holds: {Hold(ability)}",
            $"Requirements: {ability.Requires}",
            $"Patch: {ability.SourcePatch ?? "?"}  \u00B7  source {ability.SourceType} ({ability.SourceConfidence})",
            $"Mode: {ability.Automation}  \u00B7  {(ability.LiveVerified ? "live-verified" : "live-unverified")}",
        };
        var y = top + 8;
        foreach (var line in lines)
        {
            TextRenderer.DrawText(g, line, DesignTokens.Type(DesignTokens.MetaSize),
                new Rectangle(18, y, rowWidth - 36, 18), DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += 18;
        }
        TextRenderer.DrawText(g, "Open the inspector for relationships, evidence and audit findings.",
            DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Italic),
            new Rectangle(18, y + 4, rowWidth - 36, 18), DesignTokens.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    internal static string SubRole(AbilityDefinition a)
    {
        if (a.HasInterruptCapability && a.Purpose == AbilityPurpose.Interrupt) return "kick";
        if (a.IsDefensive) return a.Tier.ToString().ToLowerInvariant() + " defensive";
        if (a.Purpose == AbilityPurpose.SelfHeal) return "self-heal";
        if (a.HasMobilityCapability) return a.MobilityKind == MobilityKind.Unknown ? "movement" : a.MobilityKind.ToString().ToLowerInvariant();
        if (a.Purpose == AbilityPurpose.MajorOffensive) return "major offensive";
        if (a.Purpose == AbilityPurpose.MinorOffensive) return "minor offensive";
        if (a.Automation == AutomationContext.Manual) return "manual utility";
        return a.Status.ToString();
    }

    private static string Why(AbilityDefinition a)
    {
        if (a.Automation == AutomationContext.MaxDpsOnly) return $"MaxDps surfaces it; delegated ({a.Delegation})";
        if (a.NeverAutomatic) return $"manual by design ({a.ManualReason})";
        if (a.HasInterruptCapability) return "companion kicks observed casts";
        if (a.IsSurvival) return "survival rules + MaxDps urgency";
        return $"companion policy ({a.Status})";
    }

    private static string Hold(AbilityDefinition a)
    {
        if (a.UseBelowHpPct is not null) return $"below {a.UseBelowHpPct}% HP";
        if (a.HoldAboveHpPct is not null) return $"above {a.HoldAboveHpPct}% HP";
        if (a.RequiresEnemyCast) return "target is casting";
        if (a.RidesGcd) return "GCD / key-interval gates";
        return "no condition";
    }
}
