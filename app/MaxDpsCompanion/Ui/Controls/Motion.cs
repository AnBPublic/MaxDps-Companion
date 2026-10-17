using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace MaxDpsCompanion;

/// <summary>
/// v4.0.1 shared motion vocabulary (design brief §4): ONE easing curve
/// — KeySpline(0.32, 0.72, 0, 1) — for every hover/press/reveal animation.
/// Only Opacity and transforms are animated (Translation / Scale / Rotation),
/// never Width/Height/Margin, so layout never thrashes. RenderTransformOrigin
/// 0.5,0.5 is set by the owners; compositor CenterPoint matches.
/// </summary>
internal static class Motion
{
    /// <summary>The single shared easing curve: cubic-bezier(0.32, 0.72, 0, 1).</summary>
    public static readonly Vector2 EaseA = new(0.32f, 0.72f);
    public static readonly Vector2 EaseB = new(0f, 1f);

    public const int FadeUpDurationMs = 600;
    public const double FadeUpOffsetY = 12;

    public static Microsoft.UI.Composition.CubicBezierEasingFunction Ease(Microsoft.UI.Composition.Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(EaseA, EaseB);

    /// <summary>Card/row entrance: fade in while translating up 12px (delay per row).</summary>
    public static void FadeUp(FrameworkElement element, int delayMs)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            // Offset animation (the compositor transform that can't be
            // expressed in layout): start 12 DIP below the base slot, settle
            // to the base value. Opacity fades 0 -> 1 on the same curve.
            var baseOffset = visual.Offset;
            visual.Opacity = 0f;

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0f);
            opacity.InsertKeyFrame(1f, 1f, Ease(compositor));
            opacity.Duration = TimeSpan.FromMilliseconds(FadeUpDurationMs);
            opacity.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            visual.StartAnimation("Opacity", opacity);

            var translate = compositor.CreateVector3KeyFrameAnimation();
            translate.InsertKeyFrame(0f, baseOffset + new Vector3(0f, (float)FadeUpOffsetY, 0f));
            translate.InsertKeyFrame(1f, baseOffset, Ease(compositor));
            translate.Duration = opacity.Duration;
            translate.DelayTime = opacity.DelayTime;
            visual.StartAnimation("Offset", translate);
        }
        catch (Exception)
        {
            // Composition unavailable: the element simply stays fully visible.
        }
    }

    /// <summary>Newest log line: fade in over ~150 ms (opacity only).</summary>
    public static void Reveal(FrameworkElement element, int durationMs = 150)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            visual.Opacity = 0f;
            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0f);
            opacity.InsertKeyFrame(1f, 1f, Ease(compositor));
            opacity.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Opacity", opacity);
        }
        catch (Exception)
        {
            // Composition unavailable: the line is already visible.
        }
    }

    /// <summary>Chip hover feedback: translate 1px + scale 1.05, or back to rest.</summary>
    public static void ChipHover(FrameworkElement element, bool hover)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var ease = Ease(compositor);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1f, hover ? new Vector3(1.05f, 1.05f, 1f) : Vector3.One, ease);
            scale.Duration = TimeSpan.FromMilliseconds(140);
            visual.StartAnimation("Scale", scale);

            var translate = compositor.CreateVector3KeyFrameAnimation();
            translate.InsertKeyFrame(1f, hover ? new Vector3(1f, 0f, 0f) : Vector3.Zero, ease);
            translate.Duration = scale.Duration;
            visual.StartAnimation("Translation", translate);
        }
        catch (Exception)
        {
            // Composition unavailable: the chip keeps its resting look.
        }
    }

    /// <summary>Chevron chip: rotate to <paramref name="degrees"/> with the shared curve.</summary>
    public static void Rotate(FrameworkElement element, double degrees, double width, double height, int durationMs = 220)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3((float)(width / 2), (float)(height / 2), 0f);
            var compositor = visual.Compositor;
            var rotation = compositor.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(1f, (float)degrees, Ease(compositor));
            rotation.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("RotationAngleInDegrees", rotation);
        }
        catch (Exception)
        {
            // Composition unavailable: the chevron keeps its last painted angle.
        }
    }
}
