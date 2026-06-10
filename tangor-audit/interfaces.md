# Tangor fork — Control Surface (interfaces we can read/write)

The complete set of interfaces the fork reads/writes (or intends to). This is an **inventory of
what we can touch**, not a working-status report — verification/quirks live in `quirks.md` and the
tracker. Includes the LACT surface for reference even though we use direct sysfs.

## RyzenAdj — SMU mailbox (CPU/APU power + temps)
Binary `/usr/local/lib/ghelper/ryzenadj` (root, sudoers NOPASSWD). **Read** via `ryzenadj -i`,
**write** via flags. Read and write both go through RyzenAdj (not asus sysfs).
- `--stapm-limit` — sustained limit (SPL)
- `--slow-limit` — sustained PPT  (PL1/SPL slider → here)
- `--fast-limit` — boost PPT  (PL2/SPPT + fPPT → here)
- `--apu-slow-limit` — APU envelope (SmartShift)
- `--tctl-temp` — CPU thermal limit
- `--apu-skin-temp` / `--dgpu-skin-temp` — skin temps

## amdgpu sysfs — GPU (dGPU=card1, iGPU=card2)
- `card1/device/hwmon*/power1_cap` (+`_max`) — dGPU power cap
- `card1/device/power_dpm_force_performance_level` — perf level
- `card1/device/pp_dpm_sclk` / `pp_dpm_mclk` — dGPU DPM-state masking
- `card1/device/pp_power_profile_mode` — power profile
- `card2/device/pp_od_clk_voltage` — iGPU min/max clocks (200–2200 MHz)
- `card*/device/pp_dpm_*` — DPM state enable/mask

## ryzen_smu — undervolt + telemetry
- Curve Optimizer (SetCoAll / GetDldoPsmMargin) — per-core undervolt
- SMU PM table — power/temp telemetry (ground truth)

## amd-pstate — CPU energy bias
- `cpu*/cpufreq/scaling_governor`
- `cpu*/cpufreq/energy_performance_preference` (EPP)
- `cpu*/cpufreq/boost`

## Backlight
- `/sys/class/backlight/amdgpu_bl1/brightness`

## Battery (generic Linux node)
- `/sys/class/power_supply/BAT0/charge_control_end_threshold`

## asus-wmi (asus-nb-wmi + asus-armoury firmware-attributes) — ASUS/EC territory
- `throttle_thermal_policy` / `platform_profile` — performance mode
- `gpu_mux_mode` — dGPU MUX
- `dgpu_disable` — GPU Eco
- `panel_overdrive` — anti-ghosting
- `kbd_rgb` (+ AURA HID) — keyboard RGB
- `asus_custom_fan_curve` (hwmon pwm*) — fan curves
- `charge_control_end_threshold` — (also exposed here)
- `ppt_pl1_spl` / `ppt_pl2_sppt` / `ppt_fppt` / `ppt_apu_sppt` / `ppt_platform_sppt` — ASUS PPT
  (we write power via RyzenAdj instead of these)

## Input / Audio
- `/dev/uinput` (EvdevInterop / FnLockRemapper) — Fn-key remap
- `Asus WMI hotkeys` evdev — media/Fn/Armoury keycodes
- PipeWire (ghelper-audio DSP) — virtual mic / Denoise / EQ
- `wpctl @DEFAULT_AUDIO_SOURCE@` — mic/speaker mute

## LACT — reference only (daemon NOT used; direct sysfs instead)
Socket `/run/lactd.sock`, newline-JSON, pending→confirm. Requests map to our direct paths:
- `SetPowerCap` → `power1_cap`
- `SetPerformanceLevel` → `power_dpm_force_performance_level`
- `SetClocksValue` / `BatchSetClocksValue` → `pp_od_clk_voltage`
- `SetEnabledPowerStates` → `pp_dpm_sclk`/`pp_dpm_mclk`
- `SetPowerProfileMode` → `pp_power_profile_mode`
- CLI: `lact power-limit set <W>`, `lact profile set <name>`
