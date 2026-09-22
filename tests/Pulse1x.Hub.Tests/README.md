# GameHub regression checks

Run on Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/Pulse1x.Hub.Tests/Pulse1x.Hub.Tests.csproj -c Release
```

This STA console harness references the real app, reports PASS/FAIL and returns a
nonzero exit code on failure. `dotnet test` does not execute this console harness.
Tests cover two-press activation, timing boundaries, focus routing and modal
restoration, Xbox/PlayStation/generic action equivalence, reconnect/resume with
held buttons, virtual input echoes, stick/trigger hysteresis, SDL native loading,
and the Input Lab polling/interval/jitter calculations.

For the optional WPF integration checks using locally cached game artwork:

```powershell
dotnet run --project tests/Pulse1x.Hub.Tests/Pulse1x.Hub.Tests.csproj -c Release -- --ui
```

This requires a populated local library and an interactive desktop. It hosts the
actual page without running the app startup, redirects fixture persistence to
`artifacts/hub-verification`, disables scanning/online artwork/launcher startup,
and replaces the launch command with a counter. Real games are never launched.
It checks selection borders, both card launch paths, PlayStation hints and the
profile modal, and writes nonblank GameHub render checks at 1600x900, 1180x780
and 900x600. It also renders the Input Lab at 1180x780 and 900x600.

Physical USB/Bluetooth controller testing remains separate from these simulated
input checks. Hidden controllers exposed only as virtual Xbox devices cannot be
identified automatically; use Settings > Controller buttons > PlayStation.

SDL3 supplies native device support and standardized mappings. An optional
`gamecontrollerdb.txt` beside the app can extend SDL's built-in mappings.
Reference: https://wiki.libsdl.org/SDL3/CategoryGamepad
