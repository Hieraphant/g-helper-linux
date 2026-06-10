# Tangor fork — Quirks Log

Every quirk/gotcha hit on the FA617NT, with fix status. Companion to `interfaces.md` (the control
surface) and `TANGOR-FINDINGS.md` (the deep why). Status: ✅ fixed · 🔶 mitigated · ⬜ open · ℹ️ not-a-bug.

## Power / SMU
| # | Quirk | Status | Resolution |
|---|---|---|---|
| 1 | asus-wmi PPT sysfs is cosmetic — stores values, never reaches SMU | ✅ | route all power through RyzenAdj |
| 2 | `--stapm-limit` did not take effect in testing (readback stays 174; didn't cap power at 40 under load, app off, past 10s). STAPM is a minutes-scale avg, so not 100% excluded over very long loads | 🔶 | map PL1/SPL → `slow-limit` (reliable, fast-acting sustained lever) |
| 3 | Slider showed cosmetic firmware default (65W) while SMU was 44W | ✅ | read from `ryzenadj -i`/config, not the cosmetic node |
| 4 | Mode-switch clobbers power (resets PPT to firmware) — fires on KDE PPD profile change, Fn+F5, AC plug/unplug | 🔶 | reinforcement timer re-asserts via RyzenAdj; note: this clobber predates the fork (KDE owns `platform_profile`) |
| 5 | Fan **Reset** (reset-to-base) clobbers power (SLOW 31→145) | 🔶 | reinforcement timer recovers; ⬜ re-assert in fan handler still open |
| 6 | Reapply loop sets APU to ceiling + ignores Tctl/skin (slider vs loop disagree) | ⬜ | align `AutoCpuPower` with slider path (tracker #11) |
| 7 | `VerifyPptLimits` reads cosmetic sysfs → false "OK"/warning, can't see real SMU | ⬜ | should read `ryzenadj -i` (findings Q2) |
| 8 | 4 password prompts per power change (pkexec-per-call) | ✅ | deploy ryzenadj root-owned + sudoers NOPASSWD |

## GPU
| # | Quirk | Status | Resolution |
|---|---|---|---|
| 9 | dGPU clock OC / undervolt impossible | ℹ️ | vbios-locked (no `pp_od_clk_voltage` on dGPU) — hardware |
| 10 | dGPU `power1_average` reads ~2× under load | ℹ️ | glitchy on this RDNA3 mobile card; read RyzenAdj PM table instead |
| 11 | GPU mode/MUX ops prompt for root | ⬜ | gpu-helper not deployed; deploy like ryzenadj (tracker #13) |
| 12 | dGPU COMPUTE profile gave zero LLM throughput gain | ℹ️ | inference is memory-bandwidth-bound (~52 tok/s at 100W=120W) |

## Display / Input / Audio
| # | Quirk | Status | Resolution |
|---|---|---|---|
| 13 | Fn+F7/F8 brightness keys did nothing | ✅ | user wasn't in `input` group + needed FN-Lock; fixed via usermod + remapper |
| 14 | Panel overdrive "looked broken" | ℹ️ | works; subtle anti-ghosting; verified no power clobber |
| 15 | G-Helper mic toggle desynced from system mute | ✅ | `SyncSystemMicMute` after ToggleMaster |
| 16 | Exclusive keyboard grab (EVIOCGRAB) broke tools bound to ROG/media keys | 🔶 | user rebound mic key; remapper grab should be opt-in |
| 17 | Fn+F4 (Aura) key | ⬜ | reportedly still inert |

## Battery / System
| # | Quirk | Status | Resolution |
|---|---|---|---|
| 18 | Charge limit "never persisted" | 🔶 | node writes + sticks; ⬜ just needs a boot-time re-apply service (tracker) |
| 19 | App built but didn't render (libSkiaSharp/libHarfBuzz not loaded) | ✅ | launch from output dir with `LD_LIBRARY_PATH=$PWD` |
| 20 | Recurring "system files"/"update" nag popups | ✅ | `sysfiles_skip_startup=1`, `skip_update_prompt=1` |
| 21 | `pkill -f`/`pgrep -f` self-match → self-kill (hit 4×) | ✅ | use pidfile or `ps -eo pid,comm` match, never `-f` on own cmdline |

## Methodology notes
- Test power knobs with **g-helper OFF**, read+write via **RyzenAdj**, read **past the ~10s** window.
- ASUS firmware can't hold custom values: RyzenAdj writes the real SMU + the reinforcement timer
  out-stubborns the clobber. The only true clobber source is mode-switching (KDE/EC), not the fork.
