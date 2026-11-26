# Contributing

Thank you for contributing to Aero Control!

## Build and offline verification

All changes must compile and pass the hardware-free test suite before submitting a pull request:

```powershell
pwsh -NoProfile -File aero-control/build.ps1 -Test
```

This runs:
- `ContractTests.cs` (protocol wire rules, capability gates, and snapshot contracts)
- `PowerTests.cs` (Windows power-plan enumeration and invalid-GUID rejection)
- `UiSmokeTests.cs` (WPF layout, control gating, and rendering checks)

Ordinary builds and CI do not require an Acer laptop or the `PSSvc` service.

## Hardware safety and evidence standards

- **No model-name whitelists.** Gate every control on measurable runtime capabilities (service signature, WMI method presence, fresh telemetry, and recognized firmware readback).
- **Never fake readings.** Unavailable or unrecognized sensor/firmware values must stay blank/disabled rather than mapping to zero or guessing.
- **Keep hardware writes gated.** Do not bypass device verification, freshness checks, or the 20% minimum custom fan-speed floor.
- **Document evidence honestly.** If you test `aero-control` on another Acer model via `--probe` or live checks, report the exact probe output and observed RPM/firmware behavior rather than assuming compatibility.

## Source boundaries

- **Do not commit vendor binaries, installers, or decompiled IL dumps** (such as Acer NitroSense/PredatorSense packages or `hardware-research/` logs).
- **Do not commit machine-specific captures, personal paths, or `%LocalAppData%\AeroControl\verified-device.txt` fingerprints.**

## License

Licensed under the [GNU General Public License v3.0](LICENSE) (`GPL-3.0`). Contributions are accepted under the same license.
