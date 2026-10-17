namespace MaxDpsCompanion;

/// <summary>
/// 4.1 A2.11: caches the parsed TargetKey / InteractKey stroke so the engine
/// does not re-parse the same one-token string on every tick. A parse failure
/// is cached together with its error message (the status line still explains
/// an unparseable InteractKey exactly as before). The cache re-parses only
/// when the configured text changes.
/// </summary>
internal sealed class ParsedStrokeCache
{
    private readonly Func<string, (bool Ok, KeyStroke Stroke, string? Error)> _parse;
    private string? _text;
    private bool _ok;
    private KeyStroke _stroke;
    private string? _error;

    public ParsedStrokeCache(Func<string, (bool Ok, KeyStroke Stroke, string? Error)>? parse = null) =>
        _parse = parse ?? DefaultParse;

    private static (bool Ok, KeyStroke Stroke, string? Error) DefaultParse(string text)
    {
        var ok = MovementGuard.TryParseKey(text, out var stroke);
        return (ok, stroke, ok ? null : MovementGuard.LastParseError);
    }

    public bool TryGet(string text, out KeyStroke stroke, out string? error)
    {
        if (!string.Equals(_text, text, StringComparison.Ordinal))
        {
            _text = text;
            (_ok, _stroke, _error) = _parse(text);
        }
        stroke = _stroke;
        error = _error;
        return _ok;
    }
}
