using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

internal sealed class BrightnessInfo
{
    public bool Supported = true;
    public bool ActiveOn;
    public int SchemeCount;
    public int SchemeOnCount;

    public bool AllOn { get { return SchemeCount > 0 && SchemeOnCount == SchemeCount; } }
    public bool PartiallyOn { get { return SchemeOnCount > 0 && SchemeOnCount < SchemeCount; } }
}

internal sealed class HibernateInfo
{
    public bool? Enabled;

    /// <summary>hiberfil.sys 是否存在；null 表示无权限判断（该文件是系统文件，普通用户常常看不到）。</summary>
    public bool? FileExists;

    public bool Supported = true;
}

/// <summary>
/// 电源相关功能：自适应亮度、休眠。全部走 PowrProf / powercfg，不直接改注册表值。
/// </summary>
internal static class PowerCore
{
    private static readonly Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid AdaptiveBrightness = new Guid("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");

    private const uint AccessScheme = 16;
    private const int ErrorSuccess = 0;
    private const int ErrorNoMoreItems = 259;

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";

    /// <summary>休眠状态所在的注册表键（HibernateEnabled 值）。</summary>
    public const string HibernateRegistryPath = @"SYSTEM\CurrentControlSet\Control\Power";

    // ------------------------------------------------------------ 自适应亮度

    public static BrightnessInfo GetBrightness()
    {
        BrightnessInfo info = new BrightnessInfo();

        List<Guid> schemes = EnumSchemes();
        info.SchemeCount = schemes.Count;

        IntPtr active = GetActiveSchemePtr();

        try
        {
            Guid sub = SubVideo;
            Guid setting = AdaptiveBrightness;

            uint value;
            uint rc = PowerReadACValueIndex(IntPtr.Zero, active, ref sub, ref setting, out value);

            if (rc != ErrorSuccess)
            {
                info.Supported = false;
                return info;
            }

            info.ActiveOn = value != 0;

            foreach (Guid scheme in schemes)
            {
                IntPtr p = GuidToPtr(scheme);

                try
                {
                    uint v;
                    Guid s2 = SubVideo;
                    Guid g2 = AdaptiveBrightness;

                    if (PowerReadACValueIndex(IntPtr.Zero, p, ref s2, ref g2, out v) == ErrorSuccess && v != 0)
                    {
                        info.SchemeOnCount++;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }
        }
        catch
        {
            info.Supported = false;
        }
        finally
        {
            if (active != IntPtr.Zero) LocalFree(active);
        }

        return info;
    }

    public static OpResult SetBrightness(bool on, bool allSchemes, out string message)
    {
        message = "";

        List<Guid> schemes = EnumSchemes();
        if (schemes.Count == 0)
        {
            message = "无法枚举电源方案。";
            return OpResult.Failed;
        }

        if (!allSchemes)
        {
            IntPtr active = GetActiveSchemePtr();

            try
            {
                if (active == IntPtr.Zero)
                {
                    message = "无法读取当前电源方案。";
                    return OpResult.Failed;
                }

                if (!WriteScheme(active, on))
                {
                    message = "写入当前电源方案失败，可能需要管理员权限。";
                    return OpResult.NoPermission;
                }
            }
            finally
            {
                if (active != IntPtr.Zero) LocalFree(active);
            }

            ApplyActive();
            message = on ? "已在当前电源方案中开启自适应亮度。" : "已在当前电源方案中关闭自适应亮度。";
            return OpResult.Ok;
        }

        int okCount = 0;
        int failCount = 0;

        foreach (Guid scheme in schemes)
        {
            IntPtr p = GuidToPtr(scheme);

            try
            {
                if (WriteScheme(p, on)) okCount++;
                else failCount++;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        ApplyActive();

        if (okCount == 0)
        {
            message = "所有电源方案都写入失败，可能需要管理员权限。";
            return OpResult.NoPermission;
        }

        message = on
            ? "已在 " + okCount + " 个电源方案中开启自适应亮度。"
            : "已在 " + okCount + " 个电源方案中关闭自适应亮度。";

        if (failCount > 0)
        {
            message += "\n有 " + failCount + " 个方案写入失败（可能是只读方案）。";
        }

        return OpResult.Ok;
    }

    private static bool WriteScheme(IntPtr schemePtr, bool on)
    {
        Guid sub = SubVideo;
        Guid setting = AdaptiveBrightness;
        uint value = on ? 1u : 0u;

        uint rc1 = PowerWriteACValueIndex(IntPtr.Zero, schemePtr, ref sub, ref setting, value);

        Guid sub2 = SubVideo;
        Guid setting2 = AdaptiveBrightness;
        uint rc2 = PowerWriteDCValueIndex(IntPtr.Zero, schemePtr, ref sub2, ref setting2, value);

        return rc1 == ErrorSuccess || rc2 == ErrorSuccess;
    }

    private static void ApplyActive()
    {
        IntPtr active = GetActiveSchemePtr();

        try
        {
            if (active != IntPtr.Zero) PowerSetActiveScheme(IntPtr.Zero, active);
        }
        finally
        {
            if (active != IntPtr.Zero) LocalFree(active);
        }
    }

    // ------------------------------------------------------------ 休眠

    public static HibernateInfo GetHibernate()
    {
        HibernateInfo info = new HibernateInfo();

        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(PowerKey))
            {
                if (k != null)
                {
                    object v = k.GetValue("HibernateEnabled", null);
                    if (v != null) info.Enabled = Convert.ToInt32(v) != 0;
                }
            }
        }
        catch
        {
        }

        info.FileExists = QueryHiberFileExists();

        if (!info.Enabled.HasValue)
        {
            info.Supported = false;
            info.Enabled = info.FileExists;
        }

        return info;
    }

    /// <summary>
    /// hiberfil.sys 是隐藏的系统文件，普通用户用 File.Exists 会因访问被拒而误判为不存在，
    /// 因此改用 GetFileAttributes 并区分「确实没有」和「看不到」。
    /// </summary>
    private static bool? QueryHiberFileExists()
    {
        try
        {
            uint attr = GetFileAttributes(@"C:\hiberfil.sys");

            if (attr != 0xFFFFFFFF) return true;

            int err = Marshal.GetLastWin32Error();

            if (err == 2 || err == 3) return false;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>enable = true 打开休眠功能，false 关闭。需要管理员权限。</summary>
    public static OpResult SetHibernate(bool enable, out string message)
    {
        message = "";

        string args = enable ? "/hibernate on" : "/hibernate off";

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "powercfg.exe";
        psi.Arguments = args;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        try
        {
            using (Process p = Process.Start(psi))
            {
                if (p == null)
                {
                    message = "无法启动 powercfg.exe。";
                    return OpResult.Failed;
                }

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(120000))
                {
                    try { p.Kill(); }
                    catch { }

                    message = "powercfg 执行超时（120 秒）。\n"
                            + "如果你刚刚开启休眠，系统可能正在创建 hiberfil.sys，请稍后在「查看详细信息」里确认状态。";
                    return OpResult.Failed;
                }

                int code = p.ExitCode;

                // powercfg 在状态已经是目标值时同样返回 0，因此结果以回读状态为准
                HibernateInfo after = GetHibernate();

                if (after.Enabled.HasValue && after.Enabled.Value == enable)
                {
                    message = enable
                        ? "已开启休眠功能，paging 文件会占用约内存 40% 的磁盘空间。"
                        : "已关闭休眠功能，同时关闭了「快速启动」。";

                    if (code != 0) message += "\n（powercfg 返回码 " + code + "）";
                    return OpResult.Ok;
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("powercfg 执行后状态未变化（返回码 " + code + "）。");

                string detail = (stdout + "\n" + stderr).Trim();
                if (detail.Length > 0) sb.Append("\n\n").Append(detail);

                if (!enable && !after.Enabled.HasValue)
                {
                    sb.Append("\n\n无法确认休眠状态，请用 powercfg /a 检查。");
                }

                message = sb.ToString();
                return OpResult.Failed;
            }
        }
        catch (Exception ex)
        {
            message = "执行 powercfg 失败：" + ex.Message;
            return OpResult.Failed;
        }
    }

    // ------------------------------------------------------------ 底层

    private static List<Guid> EnumSchemes()
    {
        List<Guid> list = new List<Guid>();
        uint index = 0;

        while (true)
        {
            byte[] buffer = new byte[16];
            uint size = 16;

            uint rc = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);

            if (rc == ErrorNoMoreItems) break;
            if (rc != ErrorSuccess) break;

            list.Add(new Guid(buffer));
            index++;

            if (index > 256) break;
        }

        return list;
    }

    private static IntPtr GetActiveSchemePtr()
    {
        IntPtr p;

        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != ErrorSuccess) return IntPtr.Zero;
            return p;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr GuidToPtr(Guid g)
    {
        IntPtr p = Marshal.AllocHGlobal(16);
        Marshal.Copy(g.ToByteArray(), 0, p, 16);
        return p;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid, uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid, uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, IntPtr schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributes(string lpFileName);
}
