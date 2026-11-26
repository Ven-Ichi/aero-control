# Aero Control

A modern, model-agnostic replacement UI for Acer NitroSense. It reads the fan and
temperature telemetry exposed by the Acer NitroSense service, supports Auto/Max/
Custom fan modes and CoolBoost where verified, and switches installed Windows
power plans without a model-name whitelist. All hardware controls are on Overview.

Aero Control talks to the Acer service directly over its local named pipe. It ships
no Acer code or binaries; the Acer NitroSense service must already be installed.

## Requirements

- Windows 10/11 (x64).
- The Acer NitroSense service installed and running (`PSSvc`).
- .NET Framework 4.x runtime (present on Windows by default).

## Build

```powershell
pwsh -NoProfile -File aero-control/build.ps1 -Test   # contract + Windows tests, build, WPF checks
pwsh -NoProfile -File aero-control/build.ps1          # build only
```

Output: `aero-control/dist/AeroControl-0.3.0/AeroControl.exe`. Use `-OutputDirectory`
to build elsewhere if an executable is in use. Test renders are saved in
`aero-control/test-results`.

## Run

```powershell
aero-control/dist/AeroControl-0.3.0/AeroControl.exe             # launch the app
aero-control/dist/AeroControl-0.3.0/AeroControl.exe --diagnose  # write a one-shot report
aero-control/dist/AeroControl-0.3.0/AeroControl.exe --probe     # write a compatibility probe report
```

`--diagnose` and `--probe` take an optional output path
(`--probe report.txt`); the default is next to the executable.

## Other Acer models

Aero Control has no model-name whitelist. It gates capability on what the machine
actually exposes, and every check is measurable rather than assumed. To find out
whether a given laptop is supported, run `--probe` on it and read the verdict:

| Probe result | Meaning |
| --- | --- |
| `Compatible` | Monitoring and fan control are available. |
| `Monitoring only` | Telemetry works; the WMI fan-control method was not detected, so fan writes stay disabled. |
| `Incompatible` | The Acer service pipe did not answer or returned no fan telemetry (older/different protocol generation). |
| `Blocked` | The signed Acer NitroSense service (`PSSvc`) is not installed or not running. |

Expected shape of a supported machine: `Acer service PSSvc present and signed:
True`, `WMI class AcerGamingFunction: True`, `WMI method SetGamingFanBehavior:
True`, and non-empty fan RPM readings.

Confirmed on: **Aspire A715-42G** (monitoring, fan modes, CoolBoost On/Off and
power switching). **Nitro AN515-57** is expected
to be compatible — it runs the same NitroSense 3.x `PSSvc` service and exposes
`AcerGamingFunction.SetGamingFanBehavior` — but it has not been measured yet;
run `--probe` on one and compare to the shape above. Report outcomes so the list
can grow.

## Fan control

Hardware writes are gated until the current laptop is verified:

1. On **Overview**, choose **Enable controls on this laptop**, or use **Verify
   fan control on this laptop** in Device support. This records this machine's
   identity locally (`%LocalAppData%\AeroControl\verified-device.txt`).
2. Auto, Max and Custom become available directly on Overview. Custom expands
   CPU/GPU percentage sliders inline; **Apply fan speeds** sends the requests.

A mode request is reported as *accepted by the Acer service*, not as confirmed;
watch the fan RPM readings to see the response, then restore Auto when done.

Custom mode sets a requested percentage per fan (minimum 20%). Firmware minimums
still apply, there is no duty readback on the Acer pipe, and fans are inherently
last-writer-wins between apps — so verify through the RPM readings and restore
Auto when you are done experimenting.

## CoolBoost

Overview shows CoolBoost On/Off controls. They require device verification,
fresh fan telemetry, and a recognized firmware state. Each change is checked by
reading the state back; a successful service reply alone is not confirmation.

CoolBoost is verified working on this Aspire A715-42G. Its firmware returns
`0x60300` for On and `0x60000` for Off, and requires enable value **3**
(`0x30007`). The legacy boolean enable value **1** (`0x10007`) is rejected with
status **228**. Version 0.2.0 incorrectly blocked this recognized-on-device
format; version 0.3.0 selects the right enable value from the response family.
It also works when the app is restarted while CoolBoost is Off. Legacy boolean
firmware keeps its original encoding. Other unknown states stay unavailable.

## Power profiles

In Overview's **Power profile** card, choose an installed Windows plan and click
**Apply**. Aero Control confirms the active GUID with Windows after applying.
Plans are enumerated by GUID and
displayed with their Windows names, including Acer and custom plans. Only
installed plans are offered; the app does not create performance plans.

Windows power switching works independently of Acer's service or device
verification. Windows uses the chosen plan's existing AC/battery settings.
The selection remains active after closing the app. No separate automatic
AC/DC plan selection or Windows power-mode overlay is implemented.

Live checks switched this laptop to Balanced and Power saver, confirmed both,
and restored its original Acer plan. For diagnostics, `--powerplan <GUID>` and
`--coolboost on|off` write `extracommand.txt` next to the executable and return
an error exit code on failure. CoolBoost uses the same gates as the UI.

## Known limitations

- **Mode record is not mirrored.** Changing the mode drives the hardware
  immediately, but Acer's shared registry record (`HKLM\SOFTWARE\OEM\NitroSense\
  FanControl\CurrentFanMode`, CoolBoostMode, and the stored custom percentages) is only
  writable by an elevated process, so the official NitroSense app may show a
  stale selection after Aero Control changes it.
- **Firmware floors the requested speed.** The embedded controller keeps its
  own temperature-driven minimum: a low custom percentage at high temperatures
  holds the fan above the request (observed: 30% at 84 °C held ~5357 RPM).
  That is the firmware protecting the machine, not a failed write.
- **Capability-based, not model-checked.** Unsupported or unverified features stay
  visible as unavailable rather than being hidden or faked.
- **Implemented fan modes: Auto, Max and Custom (per-fan percentages).** Custom
  fan curves (temperature-driven tables) are not yet wired.
- **CoolBoost needs recognized firmware readback.** Boolean and the measured
  Aspire response family are supported; other unknown formats remain disabled.
- **Compact layout.** The normal dashboard fits at 1040×640. Expanded Custom
  controls fit at 1280×820; at smaller sizes Overview scrolls within the same page.
- **Monitoring is read-only** and never maps an unavailable reading to zero.

## Layout

- `aero-control/Hardware.cs` — Acer service adapter: pipe protocol, capability detection, gating.
- `aero-control/Models.cs` — snapshot/device contracts and validation rules.
- `aero-control/WindowsPower.cs` — Windows plan enumeration, switching and active-GUID readback.
- `aero-control/App.cs` — WPF application: polling, navigation, command handling.
- `aero-control/MainWindow.xaml` — UI definition (loaded as an embedded resource).
- `aero-control/HistoryChart.cs` — temperature history control.
- `aero-control/tests/` — protocol/gate assertions, Windows API checks, WPF page checks, and
  an explicitly run live feature test that restores the original power plan
  and CoolBoost state.
- `docs/PROTOCOL.md` — recovered Acer service pipe protocol and CoolBoost notes.
- `CONTRIBUTING.md` — contributor guidelines and hardware-safety rules.
- `LICENSE` — GNU General Public License v3.0.

## License

Licensed under the [GNU General Public License v3.0](LICENSE) (`GPL-3.0`).
Aero Control ships no Acer binaries or proprietary code; the signed Acer
NitroSense service (`PSSvc`) must already be installed on the target machine.
