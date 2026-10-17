# MaxDpsCompanion.Core

Portable class library: no WinForms, no P/Invoke, no `System.Drawing.Common`,
no `System.Windows.Forms`. Everything here must build on any `net8.0`
platform (Windows, macOS, Linux CI).

What lives here in P0a (scaffolding only):

- `Platform/` interfaces (owned by another stream)
- `AppVersion` (single version truth, reads `Version.props` / assembly metadata)
- `PixelSampler` (pure pixel-decode helper, no capture calls)
- `Keys` (portable key-name table, no `PostMessage` / send calls)

P0b note: verbatim moves of `PixelProtocol` / `Decision` / `Scheduler` /
`Knowledge` happen on Windows with dotnet 8.
