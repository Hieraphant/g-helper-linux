# Tangor fork — Findings & Decisions (authoritative record)

Single source of truth for everything verified on-metal. **Read this before re-investigating.**
Machine: ASUS TUF A16 **FA617NT**, Ryzen 7 7735HS (8c/16t), 62 GB RAM, Radeon RX 7700S (Navi33,
dGPU) + 680M (Rembrandt, iGPU), Ubuntu 26.04 / KDE Wayland, kernel 7.0.0-22, BIOS FA617NT.422.

## 1. THE core finding — asus-wmi PPT is a no-op here
Writing `ppt_pl1_spl / ppt_pl2_sppt / ppt_fppt / ppt_apu_sppt` (legacy `asus-nb-wmi` OR
`asus-armoury` firmware-attributes) **stores the value but never reaches the SMU**. Verified
against `ryzenadj -i` before/after, including the full sequence (manual fans `pwm_enable=1` →
write PPT → `throttle_thermal_policy` toggle to trigger EC). SMU PM table never moved.
→ **All CPU/APU power goes through RyzenAdj** (writes the SMU mailbox directly).

Mapping (g-helper attr → ryzenadj flag, W→mW): SPL`ppt_pl1_spl`→`--stapm-limit`,
FPPT`ppt_fppt`→`--fast-limit`, SPPT`ppt_pl2_sppt`→`--slow-limit`, APU`ppt_apu_sppt`→`--apu-slow-limit`.
RyzenAdj also: `--tctl-temp`, `--apu-skin-temp`, `--dgpu-skin-temp` (RyzenAdj-only, not in asus sysfs).

## 2. Clobber behaviour (verified)
- EC clobber is **event-driven, NOT periodic** (applied RyzenAdj held for 30s idle, no drift).
- Clobber **latency ≈ 5s** after a mode/policy/AC event (single shot). g-helper's built-in
  "Reapply power" (config `reapply_time`, default 10s) re-runs SetPptLimit → RyzenAdj → covers it.
  **So the standalone tangor-power watcher is redundant once the GUI fork is used.**
- Two mode-switch paths: **Fn+F5/Armoury** moves `platform_profile`+`throttle_thermal_policy` only
  (NOT PPD); **KDE applet/PPD** moves all three. (PPD-only watcher would miss Fn+F5.)

## 3. GPU facts (verified)
- **dGPU RX 7700S (card1, 03:00.0, 0x7480, 8GB):** clock OC **locked** — `pp_od_clk_voltage`
  ABSENT even with `amdgpu.ppfeaturemask=0xffffffff`; DPM-state masking ignored; no undervolt.
  Only `power1_cap` (100–120W) works. It is the **primary/display GPU** (DRI_PRIME=1 → iGPU).
- **iGPU 680M (card2, 78:00.0, 0x1681, 4GB injected):** `pp_od_clk_voltage` PRESENT → min/max
  clock control works (200–2200 MHz). RyzenAdj `--max-gfxclk` is **unsupported on this family**
  ("set_max_gfxclk_freq is not supported") → only sysfs/LACT can set iGPU clocks.
- g-helper's `LinuxAmdGpuControl.cs` already writes power1_cap / pp_od_clk_voltage / perf-level —
  it targets the dGPU (higher cap). So dGPU power-cap slider works (after udev); dGPU clock
  sliders are dead (no pp_od); iGPU clocks not currently wired (it only drives one AMD GPU).
- 7B LLM inference is **memory-bandwidth-bound**: ~52 tok/s at BOTH 100W and 120W dGPU cap.

## 3b. dGPU monitoring sources (verified on-metal) + SmartShift confirmation
All under `/sys/class/drm/card1/device/` (dGPU). Read these for the monitor; do NOT trust
hwmon `power1_average` under load (~2× glitch on this RDNA3 mobile card — at idle it reads a
plausible ~3W, but it inflates under load).
- `hwmon/hwmon*/power1_average` (draw µW, suspect under load), `power1_cap` / `power1_cap_max` (µW; 120W).
- `hwmon/hwmon*/temp1_input` edge, `temp2_input` hotspot/junction, `temp3_input` mem (m°C).
- `pp_dpm_sclk` core states (idle e.g. `1: 805Mhz *`, top `2: 2208Mhz`), `pp_dpm_mclk` mem states.
- `gpu_busy_percent` utilization; `mem_info_vram_used` (~1.4 GiB idle) + `mem_info_gtt_used`
  (system RAM the dGPU borrows) — watch BOTH during inference (spill goes to CPU, GTT stays low).
- `pp_features` = SMU feature table. **SMARTSHIFT (bit 25) = ENABLED** — confirms the
  apu-slow-limit → dGPU SmartShift lever is real. Also enabled: GFXOFF, THROTTLERS, FAN_CONTROL,
  DPM_GFXCLK/UCLK/FCLK/SOCCLK, ACDC, OUT_OF_BAND_MONITOR. Disabled: GFX_EDC, GFX_PCC_DFLL, LED_DISPLAY.

## 4. LACT (evaluated, then DROPPED)
Whole useful surface here = dGPU watts + iGPU clocks + monitoring, all ~70 lines of direct
sysfs. Decided to drop the daemon, do it in-fork via direct sysfs (fits SysfsHelper). LACT facts
if ever needed: socket `/run/lactd.sock`, newline-JSON `{"command":"<snake>","args":{...}}`,
pending→confirm protocol, `set_enabled_power_states` kind=`core_clock` (not "core"). dGPU
**COMPUTE** power-profile = a 1000 MHz min-active-clock floor (≡ VR mode) → latency-only, **zero
LLM throughput benefit** (benchmarked). dGPU own fan = PMFW-managed, static pwm EINVALs.

## 5. CPU EPP / amd-pstate
`amd-pstate-epp`. Governor `performance` LOCKS EPP to performance; governor **`powersave`
unlocks** the full EPP range (performance / balance_performance / balance_power / power /
default=balance_performance). → set governor=powersave + EPP hint per mode.

## 6. Decisions
- **RyzenAdj for all CPU/APU power** (never the dead asus PPT — user: "never default ASUS").
- **Direct sysfs for GPU** (drop LACT). GPU sysfs survives mode switches (not EC-clobbered).
- Merge intent UNDECIDED — "functional for me first." If we upstream later: gate the RyzenAdj
  reroute behind a default-off config flag (it's currently unconditional). ~10 min, deferred.
- Input/Fn-remap: use in-tree `/dev/uinput` (EvdevInterop/FnLockRemapper), NO external tools.

## 7. Fork changes (branch tangor-fork)
- `src/Platform/Linux/RyzenAdj.cs` (new) + `LinuxAsusWmi.SetPptLimit` reroute.
- `src/UI/Controls/FanCurveChart.cs`: single-point drag clamped monotonic (no impossible curves).
- `install/install.sh`: deploy ryzenadj root-owned `/usr/local/lib/ghelper/ryzenadj` + sudoers NOPASSWD.
- `install/90-ghelper.rules`: `/dev/uinput` write rule; amdgpu rules (power1_cap/pp_od/perf-level);
  robust backlight rule.
- Per-user config (NOT committed — ~/.config/ghelper/config.json): `sysfiles_skip_startup=1`,
  `skip_update_prompt=1` (suppress nag popups; = the "Don't show again" boxes).

## 8. Deployed system state (this machine)
- `/usr/local/lib/ghelper/ryzenadj` (root) + `/etc/sudoers.d/ghelper-ryzenadj` (NOPASSWD).
- `/etc/udev/rules.d/90-ghelper.rules` (our version) → fan-curve pwm, kbd_rgb, brightness,
  /dev/uinput, amdgpu GPU nodes all writable. `modprobe uinput` done.
- Reversibility log: `/home/hieraphant/tangor-power/CHANGELOG.md` + `undo-all.sh`.

## 9. QA status (on-metal, 2026-06-08)
WORKS: CPU power sliders (silent), mode switch (KDE PPD follows), fan curves, fan-curve monotonic
drag, keyboard RGB+strobe, LCD brightness, undervolt (Curve Optimizer), GPU/MUX modes, dGPU
power-cap slider, monitoring, battery limit, camera toggle, popups suppressed.
DEAD/HW: dGPU clock+undervolt (vbios), dGPU own fan (PMFW).
OPEN: EPP per mode (TODO); self-contained C# installer (ryzenadj+gpu-helper); virtual mic
(ghelper-audio not built — low pri); minor (panel-OD notif spam, mic main-screen toggle).

## 9b. QA round 2 (2026-06-08 late) + new work
- **Restored the missing RyzenAdj knobs**: added **APU Power / CPU Temp (Tctl) / dGPU Skin** sliders
  to the CPU tab (FansWindow), wired to RyzenAdj setters. APU is now user-controlled (SmartShift
  lever) instead of pinned to max(PL1,PL2). Config keys: limit_apu / limit_tctl / limit_dgpu_skin.
- **Microphone DSP works** (ghelper-audio built + embedded; needs libpipewire-0.3-dev). Denoise/
  Vocoder/EQ/Delay run; measurably improves the user's voice-to-text (speech impediment + weak mic).
- **CPU Boost** toggle = CPU turbo/precision boost (cpufreq `boost` sysfs). On = clock above base
  (~4.8GHz, more heat); Off = pinned to base. Independent of the wattage sliders.
- **Power targets to remember**: dGPU power cap = **120W** (power1_cap, GPU tab slider); CPU **150W**
  = PL2/SPPT (slider Maximum=150, already there — was just sitting low).
- **MONITORING SOURCE note (user):** read power/temps from the **RyzenAdj PM table**, not only hwmon —
  the dGPU hwmon `power1_average` is glitched (~2× on this RDNA3 mobile card); RyzenAdj is ground truth
  for APU/CPU power. (Hardware Monitor currently uses hwmon → may misreport; revisit.)

### OPEN bugs (QA round 2)
- **Mic mute not synced**: ghelper-audio owns the mic as a virtual source; G-Helper's mute and the
  system (wpctl @DEFAULT_AUDIO_SOURCE@) mute don't sync → one overrides the other. Need to mirror
  G-Helper master-mute ↔ system source-mute.
- **Brightness Fn-hotkeys** (F7/F8) recognized but don't change brightness (the slider does). The Fn
  remapper shows "No keyboard devices found" → can't grab the integrated keyboard (i8042, not 0b05;
  udev event* rule only covers vendor 0b05). The brightness *action* also needs wiring to amdgpu_bl1.
- **Panel Overdrive checkbox** doesn't apply (panel_od/panel_overdrive write). User has it on in KDE.
- **gpu-helper auth prompt**: app prompts `pkexec --install-gpu-helper /opt/ghelper` for some GPU ops
  (helper not installed). Either run the full install or route those ops via the now-writable sysfs.

## 10. Build / run / test
- Build: `dotnet build src/GHelper.Linux.csproj -c Release -r linux-x64` (the .sln is a stub).
- Run test instance: from `src/bin/Release/net10.0/linux-x64/` →
  `LD_LIBRARY_PATH=$PWD dotnet ./ghelper.dll` (else Skia/HarfBuzz native libs don't load).
- Manage app safely: kill by `ps -eo pid,comm | awk '$2=="dotnet"{print $1}'` — NEVER `pkill -f`
  (self-matches the command line; hit this 3×).
- Screenshot: `spectacle -b -n -f -o x.png`; raise windows via `qdbus6 org.kde.KWin /Scripting`.
