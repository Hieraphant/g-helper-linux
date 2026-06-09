using System;
using System.IO;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// CPU/APU power limits on this ASUS AMD platform do NOT reach the SMU through the
/// asus-wmi PPT sysfs attributes (verified no-op on FA617NT / kernel 7.0 — the nodes
/// store the value but it never propagates to the SMU PM table). RyzenAdj writes the
/// SMU mailbox directly, so the real power lever goes through here.
///
/// Power values are in WATTS at this API boundary (converted to mW for ryzenadj);
/// temperatures are in degrees C. Runs privileged via the same sudo-NOPASSWD/pkexec
/// path g-helper already uses for its GPU helper.
/// </summary>
public static class RyzenAdj
{
    public static string BinaryPath { get; set; } = ResolveBinary();

    private static string ResolveBinary()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            Environment.GetEnvironmentVariable("RYZENADJ_PATH") ?? string.Empty,
            "/usr/local/lib/ghelper/ryzenadj", // root-owned, deployed by install.sh (sudoers NOPASSWD target)
            "/usr/local/bin/ryzenadj",
            "/usr/bin/ryzenadj",
            Path.Combine(home, "RyzenAdj", "build", "ryzenadj"), // dev fallback
        };
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
        return "ryzenadj"; // hope it's on PATH
    }

    public static bool Available => BinaryPath == "ryzenadj" || File.Exists(BinaryPath);

    /// <summary>
    /// Map a g-helper PPT attribute name + watts to the matching ryzenadj flag (mW), or null
    /// if the attribute isn't a RyzenAdj-backed power limit.
    ///   ppt_pl1_spl (SPL)  -> --stapm-limit
    ///   ppt_fppt   (FPPT)  -> --fast-limit
    ///   ppt_pl2_sppt(SPPT) -> --slow-limit
    ///   ppt_apu_sppt(APU)  -> --apu-slow-limit
    /// </summary>
    public static string? FlagForPpt(string attribute, int watts) => attribute switch
    {
        // FA617NT: `--stapm-limit` is FIRMWARE-LOCKED — ryzenadj reports success but the SMU
        // ignores it (verified on-metal, even as root). slow/fast/apu-slow DO stick. So map the
        // sustained slider (PL1/SPL) to the working sustained lever (slow-limit), and the boost
        // sliders (PL2/SPPT, fPPT) to fast-limit. (See TANGOR-FINDINGS §9d.)
        "ppt_pl1_spl" => $"--slow-limit={watts * 1000}",
        "ppt_pl2_sppt" => $"--fast-limit={watts * 1000}",
        "ppt_fppt" => $"--fast-limit={watts * 1000}",
        "ppt_apu_sppt" => $"--apu-slow-limit={watts * 1000}",
        _ => null,
    };

    /// <summary>Apply one or more ryzenadj flags in a single privileged call. True on success.</summary>
    public static bool Apply(params string[] flags)
    {
        if (flags == null || flags.Length == 0) return false;
        var result = SysfsHelper.RunSudoOrPkexec(BinaryPath, flags);
        if (result == null)
        {
            Helpers.Logger.WriteLine($"RyzenAdj apply failed: {string.Join(' ', flags)}");
            return false;
        }
        return true;
    }

    // Convenience setters — watts for power, degrees C for temps.
    public static bool SetStapm(int w) => Apply($"--stapm-limit={w * 1000}");
    public static bool SetFast(int w) => Apply($"--fast-limit={w * 1000}");
    public static bool SetSlow(int w) => Apply($"--slow-limit={w * 1000}");
    public static bool SetApuSlow(int w) => Apply($"--apu-slow-limit={w * 1000}");
    public static bool SetTctlTemp(int c) => Apply($"--tctl-temp={c}");
    public static bool SetApuSkinTemp(int c) => Apply($"--apu-skin-temp={c}");
    public static bool SetDgpuSkinTemp(int c) => Apply($"--dgpu-skin-temp={c}");
}
