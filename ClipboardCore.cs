using System;
using System.Collections.Generic;
using Microsoft.Win32;

internal sealed class ClipboardInfo
{
    /// <summary>注册表里是否显式存在该值。</summary>
    public bool ValuePresent;

    /// <summary>实际生效的开关状态（值不存在时按系统默认 = 开启）。</summary>
    public bool HistoryEnabled;

    public bool ServiceFound;
    public string ServiceName = "";

    /// <summary>当前用户的服务实例 Start 值；2=自动 3=手动 4=禁用。</summary>
    public int? ServiceStart;

    /// <summary>服务模板 cbdhsvc 的 Start 值。</summary>
    public int? TemplateStart;

    /// <summary>模板或实例被禁用 —— 此时开关拨了也不会生效。</summary>
    public bool Blocked;
}

/// <summary>
/// 剪贴板历史（Win+V）。设置值在 HKCU，写入不需要管理员；
/// 但服务被禁用时需要管理员才能修复。
/// </summary>
internal static class ClipboardCore
{
    public const string RegPath = @"Software\Microsoft\Clipboard";
    public const string ValueName = "EnableClipboardHistory";

    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
    private const string TemplateName = "cbdhsvc";
    private const int StartDisabled = 4;
    private const int StartManual = 3;

    public static ClipboardInfo GetInfo()
    {
        ClipboardInfo info = new ClipboardInfo();
        info.HistoryEnabled = true;

        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
            {
                if (k != null)
                {
                    object v = k.GetValue(ValueName, null);

                    if (v != null)
                    {
                        info.ValuePresent = true;
                        info.HistoryEnabled = Convert.ToInt32(v) != 0;
                    }
                }
            }
        }
        catch
        {
        }

        info.TemplateStart = ReadStart(TemplateName);

        // 找一个当前用户的服务实例（cbdhsvc_xxxxx）
        try
        {
            using (RegistryKey services = Registry.LocalMachine.OpenSubKey(ServicesKey))
            {
                if (services != null)
                {
                    foreach (string name in services.GetSubKeyNames())
                    {
                        if (!name.StartsWith("cbdhsvc_", StringComparison.OrdinalIgnoreCase)) continue;

                        info.ServiceFound = true;
                        info.ServiceName = name;
                        info.ServiceStart = ReadStart(name);
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        info.Blocked =
            (info.TemplateStart.HasValue && info.TemplateStart.Value == StartDisabled) ||
            (info.ServiceStart.HasValue && info.ServiceStart.Value == StartDisabled);

        return info;
    }

    public static OpResult SetHistory(bool enabled, out string message)
    {
        message = "";

        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
            {
                if (k == null)
                {
                    message = "无法打开剪贴板设置项。";
                    return OpResult.Precondition;
                }

                k.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            message = "权限不足：" + ex.Message;
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "写入失败：" + ex.Message;
            return OpResult.Failed;
        }

        ClipboardInfo after = GetInfo();

        if (after.HistoryEnabled != enabled)
        {
            message = "写入后回读校验失败。";
            return OpResult.Failed;
        }

        message = enabled
            ? "已开启剪贴板历史，Win+V 可以查看复制过的内容。"
            : "已关闭剪贴板历史，Win+V 不再显示历史内容。";

        if (enabled && after.Blocked)
        {
            message += "\n\n注意：剪贴板服务当前被禁用，开关虽然已经写进去了，"
                     + "但在修复服务并注销重新登录之前，Win+V 仍然不会生效。";
        }

        return OpResult.Ok;
    }

    /// <summary>把模板和当前用户实例都改回「手动」启动。需要管理员权限。</summary>
    public static OpResult RepairService(out string message)
    {
        message = "";

        ClipboardInfo info = GetInfo();
        List<string> targets = new List<string>();

        targets.Add(TemplateName);

        if (info.ServiceFound && !string.IsNullOrEmpty(info.ServiceName))
        {
            targets.Add(info.ServiceName);
        }

        int changed = 0;
        int failed = 0;

        foreach (string name in targets)
        {
            if (SetStart(name, StartManual)) changed++;
            else failed++;
        }

        if (changed == 0)
        {
            message = "无法修改服务启动类型，可能需要管理员权限。";
            return OpResult.NoPermission;
        }

        ClipboardInfo after = GetInfo();

        if (after.Blocked)
        {
            message = "服务启动类型已修改，但仍有项目处于禁用状态。\n"
                    + "模板 cbdhsvc = " + DescribeStart(after.TemplateStart) + "\n"
                    + "实例 " + (after.ServiceName.Length > 0 ? after.ServiceName : "(未找到)")
                    + " = " + DescribeStart(after.ServiceStart);

            if (failed > 0) message += "\n有 " + failed + " 项修改失败。";

            return OpResult.Failed;
        }

        message = "已把剪贴板服务改为「手动」启动。\n\n"
                + "Windows 的剪贴板服务是按用户会话启动的，无法用命令立即拉起，\n"
                + "需要注销并重新登录（或重启）后才会生效。";

        return OpResult.Ok;
    }

    private static int? ReadStart(string serviceName)
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ServicesKey + "\\" + serviceName))
            {
                if (k == null) return null;

                object v = k.GetValue("Start", null);
                if (v == null) return null;

                return Convert.ToInt32(v);
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool SetStart(string serviceName, int value)
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ServicesKey + "\\" + serviceName, true))
            {
                if (k == null) return false;

                k.SetValue("Start", value, RegistryValueKind.DWord);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeStart(int? start)
    {
        if (!start.HasValue) return "未找到";

        switch (start.Value)
        {
            case 2: return "自动";
            case 3: return "手动";
            case 4: return "禁用";
            default: return "未知(" + start.Value + ")";
        }
    }
}
