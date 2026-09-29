using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Reflection;

namespace MaxDpsCompanion;

/// <summary>
/// Bundled Geist faces (SIL OFL, vendored under assets/fonts). Loaded once into
/// a <see cref="PrivateFontCollection"/> from embedded resources so the exe is
/// self-contained and no font is installed on the machine. When the resources
/// are absent the design tokens fall back to the system variable font chain.
/// </summary>
internal static class UiFonts
{
    public const string RegularResource = "MaxDpsCompanion.assets.fonts.Geist-Regular.ttf";
    public const string MediumResource = "MaxDpsCompanion.assets.fonts.Geist-Medium.ttf";
    public const string BoldResource = "MaxDpsCompanion.assets.fonts.Geist-Bold.ttf";

    private static bool _tried;
    private static PrivateFontCollection? _collection;
    private static string? _family;

    /// <summary>True when a bundled Geist family loaded successfully.</summary>
    public static bool FamilyAvailable
    {
        get
        {
            Ensure();
            return _family is not null;
        }
    }

    /// <summary>The bundled family name ("Geist"), or the fallback when absent.</summary>
    public static string FamilyName
    {
        get
        {
            Ensure();
            return _family ?? "Segoe UI";
        }
    }

    /// <summary>Idempotent load of every embedded face (never throws).</summary>
    public static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var collection = new PrivateFontCollection();
            var added = 0;
            foreach (var name in new[] { RegularResource, MediumResource, BoldResource })
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    // AddMemoryFont copies the font data; the pin can be released.
                    collection.AddMemoryFont(handle.AddrOfPinnedObject(), data.Length);
                    added++;
                }
                finally
                {
                    handle.Free();
                }
            }

            if (added > 0 && collection.Families.Length > 0)
            {
                _collection = collection;
                _family = collection.Families[0].Name;
            }
            else
            {
                collection.Dispose();
            }
        }
        catch
        {
            // A malformed or partial font pack must never take the UI down:
            // DesignTokens falls back to the system chain.
            _family = null;
        }
        DesignTokens.InvalidateFamily();
    }
}
