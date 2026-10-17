using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the classic toggle switch (old UiControls.cs
/// ToggleSwitch :683-832). Custom ToggleButton template ("ToggleSwitchStyle" in
/// Ui/Theme.xaml): capsule track that cross-fades Border -> brass, white thumb
/// with a soft under-shadow, accent focus ring. The thumb travels on a ~150 ms
/// Composition translation (the plan's parity rule: same toggle semantics, the
/// standard WinUI tween duration instead of the old 15 ms tick).
///
/// Semantics kept from the old control: setting the value raises
/// <see cref="Toggled"/> on every change; the first paint snaps to the loaded
/// value (a settings load must not look like an animation); a click toggles and
/// raises the inherited Click. <c>IsOn</c>/<c>Toggled</c> are the primary API,
/// <c>Checked</c>/<c>CheckedChanged</c> stay as parity aliases.
/// </summary>
public sealed class ToggleSwitch : ToggleButton
{
    private Border? _thumb;
    private Border? _trackOn;
    private Border? _focusRing;
    private bool _hasFocus;
    private bool _loaded;
    private double _travel;

    public event EventHandler? Toggled;

    /// <summary>Old WinForms event name; forwards to <see cref="Toggled"/>.</summary>
    public event EventHandler? CheckedChanged
    {
        add => Toggled += value;
        remove => Toggled -= value;
    }

    public ToggleSwitch()
    {
        ControlTheme.ApplyAppStyle(this, "ToggleSwitchStyle");
        Width = 52;
        Height = 30;
        AutomationProperties.SetName(this, "Toggle");
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => OnIsCheckedChanged());
        Loaded += (_, _) => { _loaded = true; Snap(); };
        Unloaded += (_, _) => _loaded = false;
        GotFocus += (_, _) => { _hasFocus = true; RefreshFocus(); };
        LostFocus += (_, _) => { _hasFocus = false; RefreshFocus(); };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
        SizeChanged += (_, _) => { UpdateTravel(); Snap(); };
    }

    /// <summary>True when the switch is on (old ToggleSwitch.Checked).</summary>
    public bool IsOn
    {
        get => IsChecked == true;
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Old WinForms property name; forwards to <see cref="IsOn"/>.</summary>
    public new bool Checked
    {
        get => IsOn;
        set => IsOn = value;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _thumb = GetTemplateChild("Thumb") as Border;
        _trackOn = GetTemplateChild("TrackOn") as Border;
        _focusRing = GetTemplateChild("FocusRing") as Border;
        if (_thumb is not null)
            ElementCompositionPreview.SetIsTranslationEnabled(_thumb, true);
        UpdateTravel();
        Snap();
        RefreshFocus();
    }

    private void OnIsCheckedChanged()
    {
        Toggled?.Invoke(this, EventArgs.Empty);
        if (_loaded) Animate(); else Snap();
    }

    private void UpdateTravel()
    {
        var height = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 30);
        var width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 52);
        var diameter = Math.Max(8, height - 10);
        _travel = Math.Max(0, width - diameter - 10);
        if (_thumb is not null)
        {
            _thumb.Width = diameter;
            _thumb.Height = diameter;
        }
    }

    /// <summary>Snap the thumb/track to the value without animating (load-time parity).</summary>
    private void Snap()
    {
        if (_trackOn is not null) _trackOn.Opacity = IsOn ? 1 : 0;
        if (_thumb is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_thumb);
            visual.Properties.InsertVector3("Translation", new Vector3((float)(IsOn ? _travel : 0), 0, 0));
        }
        catch (Exception)
        {
            // Composition unavailable: the value still changes, the thumb just stays put.
        }
    }

    private void Animate()
    {
        if (_thumb is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_thumb);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.32f, 0.72f), new Vector2(0f, 1f));

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, new Vector3((float)(IsOn ? _travel : 0), 0, 0), ease);
            move.Duration = TimeSpan.FromMilliseconds(150);
            visual.Properties.StartAnimation("Translation", move);

            if (_trackOn is not null)
            {
                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1f, IsOn ? 1f : 0f, ease);
                fade.Duration = TimeSpan.FromMilliseconds(150);
                ElementCompositionPreview.GetElementVisual(_trackOn).StartAnimation("Opacity", fade);
            }
        }
        catch (Exception)
        {
            Snap();
        }
    }

    private void RefreshFocus()
    {
        if (_focusRing is not null)
            _focusRing.Visibility = _hasFocus && IsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
