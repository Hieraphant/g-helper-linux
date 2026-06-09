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
- **Mic mute desync — FIX APPLIED:** root cause = the panel/tray "Microphone" toggle is
  `AudioHelper.ToggleMaster` (start/stop the DSP helper), NOT a mute. Starting/stopping switches the
  PipeWire default source and strands the mute ("system shows unmuted but no voice"). `micmute`
  action + the hardware key target @DEFAULT_AUDIO_SOURCE@ and sync fine; only the helper-toggle drifts.
  Fix: `App.SyncSystemMicMute(on)` (600ms settle, then force source mute to match helper state)
  called from `audio_toggle` + `ButtonAudioToggle_Click`. NEEDS on-metal confirm.
- **Panel Overdrive checkbox — NOT a bug:** handler/write verified correct (CheckOverdrive_Changed →
  SetPanelOverdrive → panel_overdrive; sysfs write flips 1↔0 and sticks). Panel OD is anti-ghosting,
  visually subtle → looked like "doesn't work" but the value really changes.
- **Brightness Fn-hotkeys (F7/F8) — FIX APPLIED:** the Fn remapper couldn't grab the integrated
  keyboard ("No keyboard devices found") because event3 (AT Translated Set 2) + event4 (ITE5570) are
  group `input` and the user wasn't in it (the udev 0666 rule only covers vendor 0b05, not the i8042/
  ITE keyboard). Fix: add user to `input` group (`usermod -aG input` — now done + added to install.sh).
  Needs re-login OR launch with `sg input`. RESULT (log-verified): remapper now grabs the keyboard,
  remaps F7→224(BrightnessDown)/F8→225(BrightnessUp) when FN-Lock ON, and **KDE acts on the keysym →
  brightness changes**. WORKS. Caveats: (a) FN-Lock is by design — ON = top-row media/brightness,
  OFF = plain F-keys; (b) on re-enable it logged "no devices grabbed" because the per-device capture
  choice (config `fnlock_capture_VVVV_PPPP_BB`) got set to 0 while cycling → fix in UI: Function Key
  Remap → Devices to capture → Rescan → check the keyboard (sets choice=1); (c) physical Fn+F7 with
  FN-Lock OFF is the hardware EC Fn layer, NOT the remapper — may not emit a brightness keysym on Linux.
- **Panel Overdrive checkbox** doesn't apply (panel_od/panel_overdrive write). User has it on in KDE.
- **gpu-helper auth prompt**: app prompts `pkexec --install-gpu-helper /opt/ghelper` for some GPU ops
  (helper not installed). Either run the full install or route those ops via the now-writable sysfs.

## 9c. Clobber-recovery audit (inspecting-instance Q1–Q3, 2026-06-09)
- **Q2 — `VerifyPptLimits` is COSMETIC (real gap):** it checks via `wmi.GetPptLimit(attr)`, which reads
  the asus-wmi / firmware-attributes **sysfs node** — NOT `ryzenadj -i`. That node is the cosmetic one
  (stores the value, doesn't reflect the SMU). Worse, with the RyzenAdj reroute `SetPptLimit` no longer
  writes that node for pl1/pl2/fppt, so GetPptLimit reads a STALE value. Net: VerifyPptLimits cannot
  detect a real SMU clobber — its WARNING is meaningless for the RyzenAdj path. A correct verify must
  read `ryzenadj -i`. (TODO: add a ryzenadj-based verify.)
- **Q3a — periodic reapply IS wired + ACTIVE:** config `reapply_time=10`; `RefreshReapplyTimer` →
  `Timer(10s)`; live log shows `ReapplyTimer: every 10s`. `ReapplyTimer_Elapsed` re-runs AutoFans +
  **AutoCpuPower** (+AutoGpuPower) for `Modes.GetCurrent()` → re-asserts the current mode's PPT via
  RyzenAdj every 10s. So clobber recovery exists via this timer (NOT via VerifyPptLimits).
- **Q3b — Fn+F5 is INVISIBLE to g-helper (no external-mode listener):** grep found no
  FileSystemWatcher/inotify/udev/D-Bus/poll on `platform_profile`/`throttle_thermal_policy`. g-helper
  only knows the mode IT set (`Modes.GetCurrent()`). So a hardware Armoury switch doesn't trigger a
  mode-change handler; the 10s timer keeps re-applying g-helper's OWN mode notion — which can DIVERGE
  from the hardware mode after Fn+F5 (timer re-asserts e.g. Turbo power while firmware is now Silent).
- **Q1 — decisive empirical test: PENDING on-metal** (set PL1 slider to 60W, clobber via AC unplug/
  replug + Fn+F5, read ryzenadj -i after each: does STAPM revert, and does it self-return to 60 within
  ~10s?). Fill in result below when run.

## 10. Build / run / test
- Build: `dotnet build src/GHelper.Linux.csproj -c Release -r linux-x64` (the .sln is a stub).
- Run test instance: from `src/bin/Release/net10.0/linux-x64/` →
  `LD_LIBRARY_PATH=$PWD dotnet ./ghelper.dll` (else Skia/HarfBuzz native libs don't load).
- Manage app safely: kill by `ps -eo pid,comm | awk '$2=="dotnet"{print $1}'` — NEVER `pkill -f`
  (self-matches the command line; hit this 3×).
- Screenshot: `spectacle -b -n -f -o x.png`; raise windows via `qdbus6 org.kde.KWin /Scripting`.

## 9d. STAPM is firmware-locked on FA617NT (decisive, 2026-06-09)
Verified on-metal as ROOT: `ryzenadj --stapm-limit=60000` prints "Successfully set" but STAPM
stays 174 (read at t+0/+3/+8s — not re-assertion, it just never takes). `--slow-limit`,
`--fast-limit`, `--apu-slow-limit` ALL stick instantly as root. So the firmware locks STAPM and
ryzenadj's success message is a lie. CONSEQUENCE: PL1(SPL) was mapped to `--stapm-limit` = dead
lever → dragging it did nothing while GUI/config showed values (this also explains the GUI-60 /
config-58 / SMU-174 mismatch). FIX: PL1(SPL)→`--slow-limit` (working sustained lever),
PL2(SPPT)+fPPT→`--fast-limit`, APU→`--apu-slow-limit`. Working CPU power levers on this board:
slow-limit, fast-limit, apu-slow-limit (all root). Dead: stapm-limit. NOTE: all power writes go
through RyzenAdj as root (sudoers NOPASSWD), NEVER the asus-wmi PPT sysfs (cosmetic).
