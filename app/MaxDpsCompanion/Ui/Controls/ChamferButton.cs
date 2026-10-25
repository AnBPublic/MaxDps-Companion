using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System.Numerics;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace MaxDpsCompanion;

public enum ButtonRole { Primary, Ghost, Danger }

/// <summary>
/// P2-B WinUI port of the flat chamfered action button (old UiControls.cs
/// ChamferButton :51-285); v4.0.1 glass polish: full pill (radius = height/2),
/// optional leading glyph in its own nested circular chip (button-in-button)
/// and role-aware chip fills. The template lives in Ui/Theme.xaml
/// ("ChamferButtonStyle"). Press physics are the old magnetic model:
/// pointer-down snaps to 0.98, release springs back on the shared
/// cubic-bezier(0.32, 0.72, 0, 1) curve; on hover the glyph chip translates
/// 1px and scales 1.05. Disabled buttons keep a crisp hairline ring and fade
/// only their content to 38% opacity.
/// </summary>
public sealed class ChamferButton : Button
{
    private ButtonRole _role = ButtonRole.Ghost;
    private WColor? _accent;
    private WColor? _ghostSurface;
    private WColor? _ring;
    private string? _leadingGlyph;
    private string? _leadingGlyphFont;
    private string? _trailingGlyph = "\u2197"; // ↗
    private bool _hover;
    private bool _pressed;
    private bool _focused;
    private FrameworkElement? _root;
    private FrameworkElement? _leadChip;
    private FrameworkElement? _trailChip;

    public ChamferButton()
    {
        ControlTheme.ApplyAppStyle(this, "ChamferButtonStyle");
        Height = 42;
        MinHeight = 42;
        Padding = new Thickness(14, 0, 14, 0);
        FontSize = ControlTheme.Dip(DesignTokens.LabelSize);
        FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        PointerEntered += (_, _) => { _hover = true; RefreshVisual(); };
        PointerExited += (_, _) =>
        {
            _hover = false;
            if (_pressed)
            {
                _pressed = false;
                AnimatePressRelease();
            }
            RefreshVisual();
        };
        PointerPressed += (_, _) =>
        {
            if (!IsEnabled) return;
            _pressed = true;
            SetPressScale(0.98);
            RefreshVisual();
        };
        PointerReleased += (_, _) =>
        {
            if (!_pressed) return;
            _pressed = false;
            AnimatePressRelease();
            RefreshVisual();
        };
        PointerCaptureLost += (_, _) =>
        {
            if (!_pressed) return;
            _pressed = false;
            AnimatePressRelease();
            RefreshVisual();
        };
        GotFocus += (_, _) => { _focused = true; RefreshVisual(); };
        LostFocus += (_, _) => { _focused = false; RefreshVisual(); };
        IsEnabledChanged += (_, _) => RefreshVisual();
        SizeChanged += (_, _) => UpdateCornerRadii();
    }

    // ----- shape DPs consumed by the template -----

    public static readonly DependencyProperty ShapeCornerRadiusProperty = DependencyProperty.Register(
        nameof(ShapeCornerRadius), typeof(CornerRadius), typeof(ChamferButton),
        new PropertyMetadata(new CornerRadius(DesignTokens.RadiusControl)));

    public CornerRadius ShapeCornerRadius
    {
        get => (CornerRadius)GetValue(ShapeCornerRadiusProperty);
        set => SetValue(ShapeCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty InnerCornerRadiusProperty = DependencyProperty.Register(
        nameof(InnerCornerRadius), typeof(CornerRadius), typeof(ChamferButton),
        new PropertyMetadata(new CornerRadius(8)));

    public CornerRadius InnerCornerRadius
    {
        get => (CornerRadius)GetValue(InnerCornerRadiusProperty);
        set => SetValue(InnerCornerRadiusProperty, value);
    }

    // ----- role / accent (old API parity) -----

    public ButtonRole Role
    {
        get => _role;
        set
        {
            if (_role == value) return;
            _role = value;
            RefreshVisual();
        }
    }

    /// <summary>Optional explicit fill (PRP chrome). Null = role colour.</summary>
    public WColor? AccentColor
    {
        get => _accent;
        set { _accent = value; RefreshVisual(); }
    }

    /// <summary>Optional ghost fill override (Exp theme). Null keeps DesignTokens.Surface.</summary>
    public WColor? GhostSurface
    {
        get => _ghostSurface;
        set { _ghostSurface = value; RefreshVisual(); }
    }

    /// <summary>
    /// Optional hairline override for the ghost ring (Sunwell-gold accent on
    /// Restart Game). Null keeps the shared white/6 ring; focus always wins.
    /// </summary>
    public WColor? RingColor
    {
        get => _ring;
        set { _ring = value; RefreshVisual(); }
    }

    /// <summary>Optional leading glyph rendered inside its own chip, left of the label.</summary>
    public string? LeadingGlyph
    {
        get => _leadingGlyph;
        set { _leadingGlyph = value; RefreshVisual(); }
    }

    /// <summary>Optional icon font for the leading glyph (e.g. "Segoe Fluent Icons"); null = inherited UI font.</summary>
    public string? LeadingGlyphFont
    {
        get => _leadingGlyphFont;
        set { _leadingGlyphFont = value; RefreshVisual(); }
    }

    /// <summary>Trailing glyph inside a nested circle (button-in-button). Primary only.</summary>
    public string? TrailingGlyph
    {
        get => _trailingGlyph;
        set { _trailingGlyph = value; RefreshVisual(); }
    }

    /// <summary>
    /// Sets the Exp transport label as glyph chip + text while keeping the old
    /// combined accessible name ("▶ Start"): <paramref name="accessibleLeading"/>
    /// is the legacy character announced to UIA when the rendered glyph is an
    /// icon-font codepoint (e.g. Fluent E769 for ⏸ Pause).
    /// </summary>
    public void SetLabel(string? leadingGlyph, string text, string? accessibleLeading = null, string? glyphFont = null)
    {
        LeadingGlyph = leadingGlyph;
        LeadingGlyphFont = glyphFont;
        Content = text;
        var announced = accessibleLeading ?? leadingGlyph;
        AutomationProperties.SetName(this, announced is null ? text : $"{announced} {text}");
    }

    /// <summary>
    /// Width-tier type step (D5). 4.5.1: the pill height follows the tier too
    /// (42 -> 52 DIP) so the transport band grows with a wide window instead of
    /// staying a fixed strip; the corner radii re-derive from the new height.
    /// Buttons with a deliberate compact size (Exp Calibrate) are not scaled.
    /// </summary>
    public void ApplyScale(UiScale scale)
    {
        FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 0.5f));
        var height = Math.Max(42, scale.ButtonHeight);
        if (Math.Abs(Height - height) > 0.5)
        {
            Height = height;
            MinHeight = height;
            var pad = 14 + Math.Max(0, (height - 42) * 0.45);
            Padding = new Thickness(pad, 0, pad, 0);
            UpdateCornerRadii();
        }
        RefreshVisual();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("Root") as FrameworkElement;
        _leadChip = GetTemplateChild("LeadGlyphHost") as FrameworkElement;
        _trailChip = GetTemplateChild("GlyphHost") as FrameworkElement;
        UpdateCornerRadii();
        RefreshVisual();
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsEnabled && e.Key == Windows.System.VirtualKey.Space)
        {
            _pressed = true;
            SetPressScale(0.98);
            RefreshVisual();
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Space && _pressed)
        {
            _pressed = false;
            AnimatePressRelease();
            RefreshVisual();
        }
        base.OnKeyUp(e);
    }

    private WColor BaseColor() => _accent ?? _role switch
    {
        ButtonRole.Primary => ControlTheme.Accent,
        ButtonRole.Danger => ControlTheme.Danger,
        _ => ControlTheme.Surface,
    };

    private void UpdateCornerRadii()
    {
        var height = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 42);
        // Full pill: radius = half the height (brief §2). Concentric inner
        // radius = outer minus the 2 DIP inset used by the top highlight.
        var radius = Math.Max(8, height / 2);
        ShapeCornerRadius = new CornerRadius(radius);
        InnerCornerRadius = new CornerRadius(Math.Max(6, radius - 2));
    }

    private void RefreshVisual()
    {
        // 4.5.0: the base colour is never pre-lightened - the gradient ramp
        // owns hover/press so a pill cannot end up washed out at both stops.
        var color = BaseColor();

        if (!IsEnabled)
        {
            // Crisp disabled: hairline ring stays, fill is a neutral surface
            // tint (never the role colour, which would make a stopped button
            // look more alive than an enabled one), content fades to 38%.
            var surface = _ghostSurface ?? ControlTheme.Surface;
            Background = ControlTheme.Brush(ControlTheme.Blend(Microsoft.UI.Colors.White, surface, 0.035f));
            BorderBrush = ControlTheme.Brush(ControlTheme.Ring);
            BorderThickness = new Thickness(1);
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary);
        }
        else if (_role == ButtonRole.Ghost)
        {
            var surface = _ghostSurface ?? ControlTheme.Surface;
            Background = ControlTheme.GhostPill(surface, _hover);
            BorderBrush = ControlTheme.Brush(_focused
                ? ControlTheme.Accent
                : _ring ?? ControlTheme.Ring);
            BorderThickness = new Thickness(_focused ? 2 : 1);
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary);
        }
        else
        {
            // 4.5.0: lit-from-above pill ramp instead of a flat fill; hover and
            // press shift the whole ramp so the gradient never inverts.
            var lift = _pressed ? -0.10f : _hover ? 0.06f : 0f;
            Background = ControlTheme.Pill(color, lift);
            BorderBrush = null;
            BorderThickness = new Thickness(0);
            // Both primary and danger pills carry light labels: the fills are
            // deep enough (see ExpTheme.*Fill) that white keeps AA contrast
            // across the whole ramp.
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary);
        }

        var contentOpacity = IsEnabled ? 1.0 : ControlTheme.DisabledContentOpacity;

        if (GetTemplateChild("InnerHighlight") is FrameworkElement highlight)
            highlight.Visibility = _role == ButtonRole.Ghost || !IsEnabled ? Visibility.Collapsed : Visibility.Visible;

        // Glyph chips (button-in-button): white/10 normally, black/12 on the
        // primary (accent) fill; hover translates 1px / scales 1.05 from code.
        var chipFill = _role == ButtonRole.Primary && IsEnabled
            ? ControlTheme.Brush(ControlTheme.ChipDark)
            : ControlTheme.Brush(ControlTheme.ChipLight);

        if (_leadChip is not null)
        {
            _leadChip.Visibility = !string.IsNullOrEmpty(_leadingGlyph)
                ? Visibility.Visible : Visibility.Collapsed;
            _leadChip.Opacity = contentOpacity;
            if (GetTemplateChild("LeadChip") is Microsoft.UI.Xaml.Shapes.Ellipse leadChipFill)
                leadChipFill.Fill = chipFill;
            if (GetTemplateChild("LeadGlyphText") is TextBlock leadGlyphText)
            {
                if (_leadingGlyph is not null) leadGlyphText.Text = _leadingGlyph;
                leadGlyphText.FontFamily = _leadingGlyphFont is null
                    ? ControlTheme.UiFamily
                    : new MediaFontFamily(_leadingGlyphFont);
            }
        }
        if (_trailChip is not null)
        {
            _trailChip.Visibility = _role == ButtonRole.Primary && !string.IsNullOrEmpty(_trailingGlyph)
                ? Visibility.Visible : Visibility.Collapsed;
            _trailChip.Opacity = contentOpacity;
            if (GetTemplateChild("TrailChip") is Microsoft.UI.Xaml.Shapes.Ellipse trailChipFill)
                trailChipFill.Fill = chipFill;
            if (GetTemplateChild("GlyphText") is TextBlock glyphText && _trailingGlyph is not null)
                glyphText.Text = _trailingGlyph;
        }
        if (GetTemplateChild("TextHost") is FrameworkElement textHost)
            textHost.Opacity = contentOpacity;
        if (GetTemplateChild("FocusRing") is FrameworkElement ring)
            ring.Visibility = _focused && IsEnabled && _role == ButtonRole.Ghost
                ? Visibility.Visible : Visibility.Collapsed;

        // Chip hover physics ride the same pointer state as the pill colours.
        if (_leadChip is { Visibility: Visibility.Visible }) Motion.ChipHover(_leadChip, _hover);
        if (_trailChip is { Visibility: Visibility.Visible }) Motion.ChipHover(_trailChip, _hover);
    }

    private void SetPressScale(double scale)
    {
        if (_root is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_root);
            visual.StopAnimation("Scale");
            visual.Scale = new Vector3((float)scale, (float)scale, 1f);
        }
        catch (Exception)
        {
            // Composition unavailable: the button still clicks, just without the tween.
        }
    }

    private void AnimatePressRelease()
    {
        if (_root is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_root);
            var compositor = visual.Compositor;
            var animation = compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f),
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.32f, 0.72f), new Vector2(0f, 1f)));
            animation.Duration = TimeSpan.FromMilliseconds(220);
            visual.StartAnimation("Scale", animation);
        }
        catch (Exception)
        {
            // Composition unavailable: leave the last painted scale.
        }
    }
}
