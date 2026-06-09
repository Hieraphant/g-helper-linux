# Tangor fork — context for collaborators

Fork of utajum/g-helper-linux, adapting it for an **ASUS TUF A16 FA617NT** (AMD Ryzen 7
7735HS + Radeon RX 7700S dGPU + 680M iGPU, Ubuntu 26.04 / KDE Wayland, kernel 7.0).

## The core finding (why this fork exists)
On this board the **asus-wmi PPT sysfs is a verified no-op**: writing `ppt_pl1_spl`,
`ppt_pl2_sppt`, `ppt_fppt`, `ppt_apu_sppt` stores the value but it **never reaches the SMU
PM table** (confirmed against `ryzenadj -i` before/after, with the full manual-fans +
throttle-policy-toggle sequence). So upstream's power path does nothing here.

**Fix:** route the four CPU/APU power limits through **RyzenAdj** (writes the SMU mailbox
directly). See `src/Platform/Linux/RyzenAdj.cs` + the reroute in `LinuxAsusWmi.SetPptLimit`.
Mapping: SPL→`--stapm-limit`, FPPT→`--fast-limit`, SPPT→`--slow-limit`, APU→`--apu-slow-limit`.

## Verified hardware facts
- ASUS PPT sysfs: no-op (above). EC clobbers RyzenAdj ~5s after a mode/policy event
  (event-driven, not periodic) → g-helper's built-in "reapply power" (10s) covers it.
- dGPU RX 7700S: clock OC **locked** (no `pp_od_clk_voltage`, DPM-mask ignored, no undervolt) —
  only `power1_cap` (100–120W) works. iGPU 680M: full clock control via `pp_od_clk_voltage`.
- LACT was evaluated as a GPU backend then dropped — its whole useful surface here (dGPU watts,
  iGPU clocks, monitoring) is ~70 lines of direct sysfs. GPU sliders TODO via direct sysfs.
- Power via RyzenAdj needs root: deployed root-owned to `/usr/local/lib/ghelper/ryzenadj` +
  sudoers NOPASSWD (see `install.sh`). udev rules + `/dev/uinput` write rule in `90-ghelper.rules`.

## What works / what's open (QA on real hardware, 2026-06-08)
Working: CPU power sliders (silent), mode switch (KDE PPD follows), fan curves, keyboard RGB +
strobe, LCD brightness, undervolt (Curve Optimizer), GPU/MUX modes, monitoring, battery limit,
camera toggle. Most features functional once udev rules + ryzenadj sudoers are deployed.

Open queue:
1. Fan-curve editor: drag should always behave as Shift-held (keep curve monotonic).
2. dGPU fan reading may be misreported (own amdgpu fan vs chassis GPU fan).
3. dGPU power-cap + iGPU clock sliders — not built yet (direct sysfs).
4. EPP/amd-pstate per mode (governor=powersave unlocks EPP hints).
5. Port ryzenadj + gpu-helper deploy into the C# installer (self-contained GUI install).
6. Virtual mic: `ghelper-audio` not built into the binary (needs build.sh audio step).
7. Minor: panel-OD notification spam; mic toggle differs main-screen vs settings.

## Build / run
`dotnet build src/GHelper.Linux.csproj -c Release -r linux-x64` (the .sln is a stub).
Run test instance from the output dir with `LD_LIBRARY_PATH=$PWD dotnet ./ghelper.dll`.
