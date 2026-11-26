# Acer adapter protocol findings

Scope: NitroSense app 3.1.3020.0, inspected independently against existing REA member evidence ev_61faac7dae984655acde9867ba853f7755a5400e45b0ba6bd3119a3cfbf93dfc. Installed service is Acer-signed PSSvc. No hardware write was sent during this investigation.

## Wire format (static evidence, read requests also runtime verified)

Pipe: `PredatorSense_service_namedpipe`, duplex. One connection per request.
Request: ushort LE command, byte argument count, then each argument as uint32 LE length followed by raw bytes. TsDotNetLib.IPCMethods.SendCommandByNamedPipe establishes this layout (static reflection IL, no method invocation).
Reply: byte count=1, uint32 LE payload length, payload. Telemetry payload is UInt64; fan setter reply payload UInt32.

Telemetry command 13, argument uint32 `1 | (selector << 8)`.
Selectors: CPU temperature=1, GPU temperature=10, CPU RPM=2, GPU RPM=6.
Status is low byte of returned UInt64; zero succeeds. Reading is `(response >> 8) & 65535`.
Anchors: CommonFunction.get_wmi_system_health_info 0x060002f6; Nitro_MainWindow.get_temperature_frequency_usage_info_data 0x060000b6; get_fan_speed_info_data 0x060000b7; async request 0x06000447, pipe construction 0x06000448.

Fan setter: command15, one UInt64 argument. Auto=0x410009; Max=0x820009. Response UInt32 low byte zero succeeds. Anchor CommonFunction.set_all_fan_mode 0x060002ff, async setter 0x0600044c.
Only these two mode values are exposed in new adapter. Setter implemented but was NOT executed during this investigation.

## Runtime read proof

Local capture `telemetry-verification.json` (gitignored) recorded exact request/response bytes. At probe time: CPU62, GPU53, fans3750/3947. Later independently compiled Hardware.cs probe showed CPU73/GPU55 and same fan speeds. It authenticated the pipe server PID against Win32_Service PSSvc and validated the executable Authenticode chain/signature via WinVerifyTrust and certificate name Acer Incorporated.
Sandbox pipe and CIM access fail with access denied; approved ordinary host execution succeeds. This does not imply end users need UAC/admin for every poll.

## Honest capability limits

No decoded firmware capability query establishing support of both fan modes was found in inspected UI call paths. WMI method presence alone is insufficient. The original application's mode display is selected UI state plus HKLM SOFTWARE\OEM\NitroSense\FanControl CurrentFanMode (0 Auto,1 Max,2 Custom); it is not a hardware readback. Registry has no live telemetry alternative in inspected subtree.

New adapter requires local per-device verification enrollment in addition to signed running service, matching pipe server PID, AcerGamingFunction SetGamingFanBehavior metadata, and valid recent CPU/GPU fan telemetry. Fingerprint binds local system UUID, manufacturer, model, BIOS version and Acer service version. The enrollment must not ship in public distribution. It does not block any model name. Without enrollment app offers monitoring. Existing prior UI Max/Auto test can justify enrollment on this user's current machine only.

SetFanMode's success means service/firmware reply accepted the request, not that RPM has already reached a target. RPM response must be observed separately. UI should label mode as last selected/requested and should not claim hardware readback. The adapter does not write Acer registry selection; Acer's own UI may show its old saved selection. Concurrent NitroSense controls can override the new app, so mode is inherently a last-request indication.

CPU utilization uses Windows GetSystemTimes. GPU utilization remains unavailable rather than guessing GPU adapter mapping. Power plan uses PowerGetActiveScheme and PowerReadFriendlyName. GPU device identification from Win32_VideoController can enumerate multiple adapters; sensor10 is the NitroSense GPU channel, not established for every model.

## Custom fan speed (command16) and fan-state composition

Recovered 2026-10-18 by reading the vendor app's own IL from the locally installed binary (method tokens below, inspected via a local gitignored `Read-IL.ps1` helper against NitroSense.exe 3.1.3020.0). No Acer code was copied; only the wire values were noted. No hardware write was sent while decoding.

Fan behavior command15, one UInt64 argument, composition from set_all_fan_mode 0x060002ff:
`9 | (cpuState << 16) | (gpuState << 22)`, state 1 = fan follows its automatic curve, state 3 = fan follows custom percentage.
Auto = 0x410009 (states 1,1). Max = 0x820009. Custom = 0xC30009 (states 3,3). Examples: CPU custom (GPU auto) = 0x430009; GPU custom (CPU auto) = 0xC10009.

Per-fan custom speed command16, one UInt64 argument, from set_single_custom_fan_speed 0x06000303 and the per-fan loop in set_all_custom_fan_state 0x06000301:
`(fanSelector | (percentage << 8))`; selector CPU = 1, GPU1 = 4; percentage is a byte 0..100.

Channel: command15 and command16 both travel on PredatorSense_service_namedpipe with the same framing as shown above (closures <>c__DisplayClass5_1::<SetAcerGamingFanGroupBehavior>b__0 0x0600044c and <>c__DisplayClass6_1::<SetAcerGamingFanGroupSpeed>b__0 0x06000451; state machines 0x0600044d / 0x06000452 open the pipe themselves). The AcerGamingFan* WMI methods are wrappers; no WMI call is needed for fan writes.

Setter reply for both: 9-byte reply, status = ToUInt32(reply,5), success when low byte = 0. Same shape already used by command15 in Hardware.cs.

Vendor slider handler Do_fan_custom_ScrollBar_value_change 0x0600006b: writes HKLM SOFTWARE\OEM\NitroSense\FanControl CPUFanPercentage / GPU1FanPercentage first, then sends command16; skipped during app start (init flag) so launch does not overwrite firmware state. SetFanMode 0x06000055 maps 0 Auto, 1 Max, 2 Custom; the CPU/GPU sliders and per-fan auto boxes are visible only in Custom.

Telemetry sweep result (read-only, cmd13, selectors 0..31): nonzero only selector1 CPU temp, selector10 GPU temp, selector2 CPU RPM, selector6 GPU RPM; selector3 returned the same value as selector10 at sweep time (alias not established); every other selector 0 with status 0. No duty/percentage readback exists on this pipe, so custom speed must be verified by observing RPM response and status byte, never by reading it back.

Not yet verified against this device's firmware: acceptance of command16/0xC30009 and firmware behavior when custom is entered while stored percentages are 0 (registry currently CPUFanPercentage=0, GPU1FanPercentage=0). Writes remain gated (enrollment) and will be exercised only in a supervised test on the user's laptop with RPM watched and restore to 0x410009 ready.

## Implementation

`aero-control/Hardware.cs` is pure .NET Framework C#5 with System.Management, no Acer binary dependencies. Build validates managed source. A separate read-only local probe script (gitignored) exercised the normal adapter path. Bound named pipe calls to ~1 second each, validate full reply framing, reject mismatched server identity. Failed/unsupported sensor values become null through ValueRules.

## CoolBoost — 2026-10-18

Recovered via a local IL decoder against the existing managed member inventory and locally installed NitroSense.exe 3.1.3020.0. These are local static IL observations; no vendor method was executed.
Type tokens resolved through CLR metadata: `0x0100016f` = System.UInt64,
`0x0100016a` = System.UInt32.

- `CommonFunction.set_coolboost_state` (`0x06000300`) composes UInt64
  `7 | (enabled ? 1 : 0) << 16`, then calls WMISetFunction. Values: On
  `0x10007`, Off `0x7`. A zero low status byte indicates acceptance.
- `<WMISetFunction>b__0` (`0x06000456`) sends pipe **command17**, with one
  UInt64 input and UInt32 result (9-byte framed response).
- `CommonFunction.get_coolboost_state` (`0x06000304`) requests UInt32 **519**
  (`0x207`) through WMIGetFunction. Vendor code returns true only when status
  byte is zero and `(response >> 8) & 255` equals **1**. It collapses other
  states and errors to false; Aero Control preserves unknown states as null.
- `<WMIGetFunction>b__0` (`0x0600045b`) sends pipe **command20**, one UInt32
  input and UInt64 result (13-byte framed response).
- Async bodies `0x06000457` / `0x0600045c` open the same PredatorSense pipe.
- UI checked/unchecked handlers persist CoolBoostMode=1/0 in HKLM then call
  the setter. Aero Control does not mirror this saved UI record.

Local raw IL decode logs (gitignored): `boost-power-il.txt`, `boost-pipe-il.txt`, `boost-handlers-il.txt`.

Runtime read-only observation on Aspire A715-42G with Acer-signed PSSvc
3.01.3028: query reply `01-08-00-00-00-00-03-06-00-00-00-00-00`, payload
`0x60300`: status **0**, state byte **3**. The saved registry CoolBoostMode=1
does not resolve this discrepancy. No meaning for state 3 is established.
It must not be treated as confirmed On/Off or proof of hardware support.
The adapter rejects a CoolBoost request before sending command17 if a fresh
query returns this state. Live refusal was verified, and state stayed the same.
Actual On/Off operation remains unverified on recognized firmware.

## Windows power plans — 2026-10-18

Vendor `ChangePowerPlan` methods `0x0600012c` / `0x0600016e` call Windows
PowerSetActiveScheme with the saved AC/DC plan GUID. Aero Control implements
manual selection of installed plans only, without writing Acer's registry or
setting a power-mode overlay. It enumerates through PowerEnumerate with
ACCESS_SCHEME=16, reads names through PowerReadFriendlyName, switches via
PowerSetActiveScheme, and confirms PowerGetActiveScheme's GUID. Native buffers
allocated by PowerGetActiveScheme are released with LocalFree.

Primary API reference: [Microsoft Power Scheme Management](https://learn.microsoft.com/en-us/windows/win32/power/managing-power-schemes)
and [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate).

Live verification switched from Acer `4fab1064-5cb4-47ab-ba83-bfd87af0e266`
to Balanced `381b4222-f694-41f0-9685-ff5bb260df2e`, then Power saver
`4f5c26d4-f975-4286-810e-5bcf93e49b58`, confirmed each active GUID, and restored
Acer in a finally block (recorded locally in gitignored `aero-control/test-results/live-features.txt`).
The app re-enumerates before setting, rejects IDs not currently installed,
and reports Windows error codes/readback mismatches rather than assuming success.

## CoolBoost response family 6 — corrected and verified in 0.3.0

This section supersedes the initial interpretation above that state 3 must stay
unavailable. Following the user's working-vendor-app report, an exact legacy
setter test established that query state 3 is real and writable on this Aspire:

| Action | command17 UInt64 input | Status | command20 / 519 readback |
| --- | --- | --- | --- |
| Initial state | none | 0 | 0x60300 |
| Legacy Off | 0x7 | 0 | 0x60000 |
| Legacy On | 0x10007 | 228 (0xE4) | remained Off |
| Restore the captured original state 3 | 0x30007 | 0 | 0x60300 |
| Corrected Off, repeated | 0x7 | 0 | 0x60000 |
| Corrected On, repeated | 0x30007 | 0 | 0x60300 |
| Restore original On | 0x30007 | 0 | 0x60300 |

The first legacy restore attempt failed, then restoring the exact captured state
3 succeeded. No fan speed/mode or Acer registry record was changed. Local raw
records (gitignored): `coolboost-roundtrip.txt`, `coolboost-restored-original.txt`,
`coolboost-profile3-verified.txt`. These are runtime observations from the signed,
PID-authenticated pipe, separate from static IL observations.

For successful replies where `response >> 16 == 6`, Aero Control recognizes
state byte 0 as Off and 3 as On. It selects enable value 3 from that response
family even when starting from Off, and rejects partial state bytes 1/2.
Other response families retain the recovered legacy state 0/1 and enable value
1. State 3 in an unverified response family and nonzero status remain unknown.
No model-name whitelist, trial write sweep, or raw EC access is used.

Production adapter verification also passed, including construction of a fresh
Hardware instance while Off, enabling On, and restoring the initial On state
(recorded locally in gitignored `aero-control/test-results/live-features-0.3.0.txt`). This proves service
acceptance and firmware state transitions; temperature/RPM gains from CoolBoost
were not separately benchmarked.
