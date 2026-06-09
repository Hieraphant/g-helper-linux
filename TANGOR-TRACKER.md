# Tangor fork — Feature Tracker

The single to-do/status board so ASUS quirks stop derailing us. Pairs with TANGOR-FINDINGS.md
(the *why*) and TANGOR-NOTES.md (onboarding). Status: ✅ done · 🔶 partial · ⬜ todo · 🚫 HW-blocked.
Type: **R**=redirect (point existing code at a working backend) · **N**=new code · **F**=fix · **S**=system/install.
Most work is **R** — small, upstream-friendly diffs.

## CPU / APU power
| Item | Status | Type | Notes / upstream |
|---|---|---|---|
| Route PPT → RyzenAdj (asus PPT is cosmetic) | ✅ | R | `SetPptLimit` reroute. **Upstream: gate behind default-off config flag.** |
| Mapping: stapm is firmware-locked → PL1→slow, PL2/fPPT→fast, APU→apu-slow | ✅ | F | findings §9d |
| Read limits from `ryzenadj -i`/config, not cosmetic sysfs | ✅ | F | findings §9c/§ cosmetic |
| Sliders: PL1/PL2/fPPT | ✅ | (exists) | works as root, silent |
| Sliders: APU / Tctl / dGPU-skin (RyzenAdj-only) | ✅ | N | added to CPU tab |
| **Reapply loop reinforces APU/Tctl/skin too (align AutoCpuPower w/ slider path)** | ⬜ | F | THE gap — timer only re-asserts slow/fast; APU set to ceiling, Tctl/skin ignored |
| Manual **Apply** button for power | ⬜ | N | user-requested ×3; sliders auto-apply on drag today |
| Reapply timer (clobber reinforcement) | ✅ | (exists) | set ~10s; the robust catch-all for all clobbers |
| ryzenadj deploy root-owned + sudoers NOPASSWD | ✅ | S | silent writes |
| EPP / amd-pstate per mode | ⬜ | N | only in headless tangor-power. **Make a separate window → easy to lift for upstream** |

## GPU
| Item | Status | Type | Notes |
|---|---|---|---|
| dGPU power-cap slider (power1_cap, direct sysfs) | ✅ | R | via existing LinuxAmdGpuControl + udev |
| amdgpu udev rules (power1_cap/pp_od/perf-level) | ✅ | S | |
| iGPU min/max clock control (pp_od) | ⬜ | N | g-helper only drives one AMD GPU; would need 2nd-GPU wiring |
| dGPU clock OC / undervolt | 🚫 | — | vbios-locked (no pp_od on dGPU) |
| gpu-helper deploy + sudoers (silence GPU pkexec prompt) | ⬜ | S | mode/MUX ops prompt for root; deploy like ryzenadj |
| Monitoring reads RyzenAdj/trusted nodes (not glitched power1_average) | 🔶 | F | Hardware Monitor still uses hwmon; revisit |

## Fans
| Item | Status | Type | Notes |
|---|---|---|---|
| Custom fan curves apply | ✅ | (exists) | udev made pwm writable |
| Fan-curve drag clamped monotonic | ✅ | F | no impossible curves |
| **Fan Reset/Apply re-asserts power after (reset-to-base clobbers)** | ⬜ | F | verified: fan RESET clobbers SLOW 31→145; timer recovers within interval |

## Modes / display / misc (mostly persist, no reinforcement needed)
| Item | Status | Notes |
|---|---|---|
| Mode switch Silent/Balanced/Turbo | ✅ | KDE PPD follows; re-applies power |
| Fn+F5 detection (external mode change) | ⬜/optional | g-helper can't see it; reapply timer covers it. Detector = polish, skip |
| MUX / GPU eco | ✅ | works, reboot-gated (persists) |
| Panel overdrive | ✅ | works (subtle anti-ghosting; not a bug) |
| LCD brightness | ✅ | udev backlight rule |
| Keyboard RGB + strobe | ✅ | |
| Battery charge limit | ✅ | persists |
| Camera toggle | ✅ | |

## Input / Audio
| Item | Status | Type | Notes |
|---|---|---|---|
| Fn-key remapper (brightness etc.) | ✅ | S | needs `input` group (added to install.sh); FN-Lock ON = media keys |
| Virtual mic / DSP (ghelper-audio) | ✅ | (build) | built w/ libpipewire; Denoise/Vocoder/EQ — helps user's voice-typing |
| Mic-mute sync (G-Helper toggle ↔ system source mute) | ✅ | F | `App.SyncSystemMicMute` after ToggleMaster |

## Install / system integration
| Item | Status | Notes |
|---|---|---|
| udev rules (asus + amdgpu + uinput + backlight) | ✅ | deployed |
| ryzenadj root-owned + sudoers | ✅ | |
| Port ryzenadj deploy into C# Installer (self-contained GUI install) | ⬜ | currently install.sh only |
| gpu-helper deploy + sudoers | ⬜ | silences GPU prompt |
| Suppress nag popups (sysfiles/update) | ✅ | per-user config flags |

## Upstreaming concerns (keep diffs liftable)
- Gate the RyzenAdj reroute behind a default-off config flag (currently unconditional).
- EPP as a separate window/module (easy removal).
- Everything else is additive R/F — small reviewable diffs.

## NEXT UP (priority)
1. Align `AutoCpuPower` to reinforce APU/Tctl/skin (closes the clobber loop) ← highest value
2. Manual Apply button (user-requested)
3. gpu-helper deploy (silence GPU root prompt)
4. EPP-per-mode as its own window
5. (optional) iGPU clock control; Fn+F5 detector
