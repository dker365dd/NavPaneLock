using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal enum OpResult
{
    Ok,
    Precondition,
    NoPermission,
    Failed
}

internal sealed class AceInfo
{
    public AccessControlType Type;
    public string Identity;
    public string Rights;
    public bool IsInherited;
}

internal sealed class StatusSnapshot
{
    public bool KeyExists;
    public bool Locked;
    public bool HasMatchingDeny;
    public bool DenyHasDelete;
    public readonly List<AceInfo> Aces = new List<AceInfo>();
    public string Owner = "<unknown>";
    public bool InheritanceEnabled;
    public long ValueBytes = -1;
    public int? ExpandToCurrentFolder;
    public int? ShowAllFolders;
    public int? NumericSort;
    public string BackupPath = "";
    public string BackupTime = "";
    public string LogPath = "";
    public string LogTime = "";
    public string UserName = "";
    public string UserSid = "";
    public bool IsElevated;
    public bool HiveWritable;
    public string OsVersion = "";
}

/// <summary>
/// 注册表状态管理核心。不含任何界面代码，便于单独测试。
/// </summary>
internal static class NavPaneCore
{
    public const string NavPanePath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Modules\NavPane";

    public const string AdvancedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>HKLM 下的策略位置，普通用户可以读，写入需要管理员。</summary>
    public const string SortPolicyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private const string ValueNoStrCmpLogical = "NoStrCmpLogical";

    private const string ValueExpandToCurrentFolder = "NavPaneExpandToCurrentFolder";
    private const string ValueShowAllFolders = "NavPaneShowAllFolders";

    // 只拒绝「写值」和「删值/删键」，不碰 WriteDac，保证解锁永远可行。
    private const RegistryRights DeniedRights = RegistryRights.SetValue | RegistryRights.Delete;

    public static string DataDir { get; private set; }

    public static string LogPath { get; private set; }
    public static string BackupLatestPath { get; private set; }

    static NavPaneCore()
    {
        DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NavPaneLock");

        LogPath = Path.Combine(DataDir, "actions.log");
        BackupLatestPath = Path.Combine(DataDir, "sddl_latest.bak");
    }

    // ------------------------------------------------------------ 状态读取

    /// <summary>
    /// 当前进程能否写入 HKCU。用于区分「注册表权限有问题」和「进程本身被外部策略限制」，
    /// 例如 exe 被放在沙箱目录里运行时，一切注册表写入都会被拦掉。
    /// </summary>
    public static bool CanWriteHive()
    {
        const string probePath = @"Software\NavPaneLockSelfTest";

        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(probePath))
            {
                if (k == null) return false;
            }

            try { Registry.CurrentUser.DeleteSubKeyTree(probePath, false); }
            catch { }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string EnvironmentHint()
    {
        return "当前进程无法写入 HKEY_CURRENT_USER，连新建一个测试项都被拒绝。\n"
             + "这说明它运行在受限环境中，而不是注册表权限有问题。\n\n"
             + "最常见的原因：工具被放在沙箱或受控目录里运行。\n"
             + "请把 NavPaneLock.exe 复制到桌面（或任意普通目录）后再运行。\n"
             + "以管理员身份运行不能解决这个问题。";
    }

    public static bool KeyExists()
    {
        IntPtr hKey;
        int rc = NativeMethods.RegOpenKeyEx(
            new IntPtr(NativeMethods.HKEY_CURRENT_USER), NavPanePath, 0,
            NativeMethods.KEY_QUERY_VALUE, out hKey);

        if (rc == 0)
        {
            NativeMethods.RegCloseKey(hKey);
            return true;
        }

        // 2/3 = 找不到；其他错误（如 5 = 拒绝访问）说明键存在但打不开
        return rc != 2 && rc != 3;
    }

    /// <summary>能否写入：申请 KEY_SET_VALUE 打开，被拒绝即视为已锁定。</summary>
    public static bool IsLocked()
    {
        IntPtr hKey;
        int rc = NativeMethods.RegOpenKeyEx(
            new IntPtr(NativeMethods.HKEY_CURRENT_USER), NavPanePath, 0,
            NativeMethods.KEY_SET_VALUE, out hKey);

        if (rc != 0) return true;

        NativeMethods.RegCloseKey(hKey);
        return false;
    }

    public static StatusSnapshot GetStatus()
    {
        return GetStatus(false);
    }

    /// <param name="probeHive">
    /// 是否顺带探测「能否写入 HKCU」。这个探测要新建再删除一个注册表键，
    /// 而 GetStatus 挂在窗口激活事件上，所以默认不做，只在需要判断限制环境时按需调用。
    /// </param>
    public static StatusSnapshot GetStatus(bool probeHive)
    {
        StatusSnapshot s = new StatusSnapshot();

        WindowsIdentity id = WindowsIdentity.GetCurrent();
        s.UserName = id.Name;
        s.UserSid = id.User.Value;
        s.IsElevated = IsElevated();
        s.HiveWritable = probeHive ? CanWriteHive() : true;
        s.OsVersion = DescribeOs();
        s.KeyExists = KeyExists();

        if (!s.KeyExists) return s;

        s.Locked = IsLocked();

        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(NavPanePath))
            {
                object state = k.GetValue("ExpandedState", null);
                byte[] bytes = state as byte[];
                s.ValueBytes = bytes == null ? -1 : bytes.Length;
            }
        }
        catch
        {
        }

        try
        {
            RegistrySecurity sec = ReadAcl();

            if (sec != null)
            {
                s.Owner = IdentityName(sec.GetOwner(typeof(SecurityIdentifier)));
                s.InheritanceEnabled = !sec.AreAccessRulesProtected;

                foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    RegistryAccessRule r = rule as RegistryAccessRule;
                    if (r == null) continue;

                    AceInfo a = new AceInfo();
                    a.Type = r.AccessControlType;
                    a.Identity = IdentityName(r.IdentityReference);
                    a.Rights = r.RegistryRights.ToString();
                    a.IsInherited = r.IsInherited;
                    s.Aces.Add(a);
                }

                foreach (RegistryAccessRule r in CollectDenyRules(sec))
                {
                    s.HasMatchingDeny = true;

                    if ((r.RegistryRights & RegistryRights.SetValue) != 0
                        && (r.RegistryRights & RegistryRights.Delete) != 0)
                    {
                        s.DenyHasDelete = true;
                    }
                }
            }
        }
        catch
        {
        }

        s.ExpandToCurrentFolder = ReadAdvancedInt(ValueExpandToCurrentFolder);
        s.ShowAllFolders = ReadAdvancedInt(ValueShowAllFolders);
        s.NumericSort = GetNumericSort();

        try
        {
            if (File.Exists(BackupLatestPath))
            {
                s.BackupPath = BackupLatestPath;
                s.BackupTime = File.GetLastWriteTime(BackupLatestPath).ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (File.Exists(LogPath))
            {
                s.LogPath = LogPath;
                s.LogTime = File.GetLastWriteTime(LogPath).ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        catch
        {
        }

        return s;
    }

    // ------------------------------------------------------------ 锁定 / 解锁

    public static OpResult Lock(out string message)
    {
        message = "";

        if (!KeyExists())
        {
            message = "未找到导航窗格状态键。请先打开一次资源管理器，关闭后再试。";
            Log("lock", "状态键不存在", OpResult.Precondition);
            return OpResult.Precondition;
        }

        if (IsLocked())
        {
            message = "导航窗格已经是锁定状态。";
            Log("lock", "已处于锁定状态，跳过", OpResult.Ok);
            return OpResult.Ok;
        }

        if (!CanWriteHive())
        {
            message = EnvironmentHint();
            Log("lock", "进程无法写入 HKCU，疑似受限环境", OpResult.NoPermission);
            return OpResult.NoPermission;
        }

        SecurityIdentifier sid = CurrentSid();

        try
        {
            RegistrySecurity sec = ReadAcl();
            if (sec == null)
            {
                message = "无法读取注册表项权限。";
                Log("lock", "ReadAcl 返回 null", OpResult.Precondition);
                return OpResult.Precondition;
            }

            try
            {
                SaveBackup(sec.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            }
            catch
            {
                // 备份失败不阻断主流程
            }

            sec.AddAccessRule(new RegistryAccessRule(
                sid,
                DeniedRights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny));

            ApplyAcl(sec);
        }
        catch (InvalidOperationException ex)
        {
            message = "无法修改注册表权限：" + ex.Message;
            Log("lock", "写入 DACL 失败: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (UnauthorizedAccessException ex)
        {
            message = "权限不足：" + ex.Message;
            Log("lock", "权限不足: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "锁定失败：" + ex.Message;
            Log("lock", "异常: " + ex.Message, OpResult.Failed);
            return OpResult.Failed;
        }

        if (!IsLocked())
        {
            TryRestoreFromBackup();
            message = "锁定未生效，已尝试回滚。请用管理员身份重试。";
            Log("lock", "Deny 未生效，已尝试回滚", OpResult.Failed);
            return OpResult.Failed;
        }

        message = "已锁定。导航窗格展开状态被冻结，重启资源管理器后回到当前形态。";
        Log("lock", "成功", OpResult.Ok);
        return OpResult.Ok;
    }

    public static OpResult Unlock(out string message)
    {
        message = "";

        if (!KeyExists())
        {
            message = "未找到导航窗格状态键，无需解锁。";
            Log("unlock", "状态键不存在", OpResult.Precondition);
            return OpResult.Precondition;
        }

        if (!IsLocked())
        {
            message = "导航窗格未锁定，无需操作。";
            Log("unlock", "本来就是解锁状态", OpResult.Ok);
            return OpResult.Ok;
        }

        bool sawInheritedDeny = false;
        int removed = 0;

        try
        {
            RegistrySecurity sec = ReadAcl();
            if (sec == null)
            {
                message = "无法读取注册表项权限。";
                Log("unlock", "ReadAcl 返回 null", OpResult.Precondition);
                return OpResult.Precondition;
            }

            List<RegistryAccessRule> explicitRules = new List<RegistryAccessRule>();

            foreach (RegistryAccessRule r in CollectDenyRules(sec))
            {
                if (r.IsInherited) { sawInheritedDeny = true; continue; }
                explicitRules.Add(r);
            }

            if (explicitRules.Count == 0)
            {
                if (sawInheritedDeny)
                {
                    message = "拒绝规则来自父键继承，无法在本键删除，请到父键处理。";
                    Log("unlock", "仅存在继承型 Deny", OpResult.Failed);
                    return OpResult.Failed;
                }

                // 先排除「进程本身被限制」：这种情况下补权限也不可能成功
                if (!CanWriteHive())
                {
                    message = EnvironmentHint();
                    Log("unlock", "进程无法写入 HKCU，疑似受限环境", OpResult.Failed);
                    return OpResult.Failed;
                }

                // 没有拒绝规则却写不进去：多半是 DACL 里丢了当前用户的写入权限（而不是加了拒绝）。
                // 这种情况直接补一条显式完全控制，把它修回来。
                if (RepairAcl(sec))
                {
                    message = "已修复：该键原本缺少当前用户的写入权限（不是拒绝规则），已补回完全控制。";
                    Log("unlock", "无 Deny，已通过补授权修复", OpResult.Ok);
                    return OpResult.Ok;
                }

                message = "无法解除，也没有找到可修复的拒绝规则。\n\n" + DescribeAcl(sec);
                Log("unlock", "无 Deny 且修复失败", OpResult.Failed);
                return OpResult.Failed;
            }

            foreach (RegistryAccessRule r in explicitRules)
            {
                if (sec.RemoveAccessRule(r)) { removed++; }
                else { sec.RemoveAccessRuleSpecific(r); removed++; }
            }

            Trace("removed=" + removed + " sddl=" + (TraceEnabled ? sec.GetSecurityDescriptorSddlForm(AccessControlSections.Access) : "-"));

            ApplyAcl(sec);

            if (TraceEnabled) Trace("after apply IsLocked=" + IsLocked() + " disk=" + DiskSddl());
        }
        catch (InvalidOperationException ex)
        {
            message = "无法修改注册表权限：" + ex.Message;
            Log("unlock", "写入 DACL 失败: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (UnauthorizedAccessException ex)
        {
            message = "权限不足：" + ex.Message;
            Log("unlock", "权限不足: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "解锁失败：" + ex.Message;
            Log("unlock", "异常: " + ex.Message, OpResult.Failed);
            return OpResult.Failed;
        }

        if (IsLocked())
        {
            if (TryRestoreFromBackup())
            {
                message = "已通过备份还原权限。";
                Log("unlock", "回退到备份还原", OpResult.Ok);
                return OpResult.Ok;
            }

            message = "解锁未生效，请手动检查该键的权限设置。";
            Log("unlock", "移除 Deny 后仍不可写", OpResult.Failed);
            return OpResult.Failed;
        }

        message = "已解锁。导航窗格可以重新保存展开/折叠状态了。";
        Log("unlock", "成功，移除规则数=" + removed, OpResult.Ok);
        return OpResult.Ok;
    }

    // ------------------------------------------------------------ 全局开关

    public static OpResult SetExpandToCurrentFolder(bool enabled, out string message)
    {
        message = "";

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedPath))
            {
                if (key == null)
                {
                    message = "无法打开 Advanced 注册表项。";
                    Log("expand", "CreateSubKey 返回 null", OpResult.Precondition);
                    return OpResult.Precondition;
                }

                key.SetValue(ValueExpandToCurrentFolder, enabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            if (!CanWriteHive())
            {
                message = EnvironmentHint();
                Log("expand", "进程无法写入 HKCU，疑似受限环境", OpResult.NoPermission);
                return OpResult.NoPermission;
            }

            message = "权限不足：" + ex.Message;
            Log("expand", "权限不足: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "写入失败：" + ex.Message;
            Log("expand", "异常: " + ex.Message, OpResult.Failed);
            return OpResult.Failed;
        }

        int? verify = ReadAdvancedInt(ValueExpandToCurrentFolder);
        if (verify == null || verify.Value != (enabled ? 1 : 0))
        {
            message = "写入后回读校验失败。";
            Log("expand", "回读校验失败", OpResult.Failed);
            return OpResult.Failed;
        }

        message = enabled
            ? "已开启：导航窗格会自动展开到当前打开的文件夹。"
            : "已关闭：导航窗格不再自动展开到当前文件夹。";

        Log("expand", "设置为 " + (enabled ? 1 : 0), OpResult.Ok);
        return OpResult.Ok;
    }

    // ------------------------------------------------------------ 资源管理器

    public static bool RestartExplorer()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(250);
                if (Process.GetProcessesByName("explorer").Length == 0) break;
            }

            Thread.Sleep(500);
            if (Process.GetProcessesByName("explorer").Length > 0) return true;

            // 提权进程直接拉起 explorer 会得到「管理员权限的资源管理器」，先等系统自动重启。
            if (!IsElevated())
            {
                try { Process.Start("explorer.exe"); }
                catch { }
            }

            for (int i = 0; i < 24; i++)
            {
                Thread.Sleep(250);
                if (Process.GetProcessesByName("explorer").Length > 0) return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 内部工具

    /// <summary>
    /// 读取状态键的安全描述符。走 RegGetKeySecurity 并只申请 READ_CONTROL：
    /// .NET 的 OpenSubKey(…, RegistryRights) 实际可能按 KEY_READ 打开，
    /// 一旦键上的读权限不完整就会读不到 ACL，连解锁都没法做。
    /// </summary>
    private static RegistrySecurity ReadAcl()
    {
        IntPtr hKey;
        int rc = NativeMethods.RegOpenKeyEx(
            new IntPtr(NativeMethods.HKEY_CURRENT_USER), NavPanePath, 0,
            NativeMethods.READ_CONTROL, out hKey);

        if (rc != 0) return null;

        try
        {
            int info = NativeMethods.DACL_SECURITY_INFORMATION | NativeMethods.OWNER_SECURITY_INFORMATION;
            int size = 0;

            NativeMethods.RegGetKeySecurity(hKey, info, IntPtr.Zero, ref size);

            if (size <= 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                int rc2 = NativeMethods.RegGetKeySecurity(hKey, info, buffer, ref size);
                if (rc2 != 0) return null;

                byte[] sd = new byte[size];
                Marshal.Copy(buffer, sd, 0, size);

                RegistrySecurity sec = new RegistrySecurity();
                sec.SetSecurityDescriptorBinaryForm(sd);

                return sec;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// 写回 DACL。直接走 RegSetKeySecurity，只申请 READ_CONTROL | WRITE_DAC。
    /// 不用 RegistryKey.SetAccessControl：它在 .NET Framework 下会重新申请写权限，
    /// 而键一旦带上 Deny SetValue 就会失败 —— 也就是锁定后无法解锁。
    /// </summary>
    private static void ApplyAcl(RegistrySecurity sec)
    {
        byte[] sd = sec.GetSecurityDescriptorBinaryForm();

        IntPtr hKey;
        int rc = NativeMethods.RegOpenKeyEx(
            new IntPtr(NativeMethods.HKEY_CURRENT_USER), NavPanePath, 0,
            NativeMethods.READ_CONTROL | NativeMethods.WRITE_DAC, out hKey);

        if (rc != 0)
        {
            throw new InvalidOperationException(
                rc == 5 ? "访问被拒绝（错误码 5）" : "无法打开注册表项，错误码 " + rc);
        }

        try
        {
            int rc2 = NativeMethods.RegSetKeySecurity(hKey, NativeMethods.DACL_SECURITY_INFORMATION, sd);
            if (rc2 != 0)
            {
                throw new InvalidOperationException(
                    rc2 == 5 ? "访问被拒绝（错误码 5）" : "写入安全描述符失败，错误码 " + rc2);
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// 找出会拦住当前进程的拒绝规则：身份既可能是用户 SID，也可能是用户所在的组
    /// （例如对 BUILTIN\Users 或 Everyone 下 Deny，同样会让当前用户写不进去）。
    /// </summary>
    private static List<RegistryAccessRule> CollectDenyRules(RegistrySecurity sec)
    {
        List<SecurityIdentifier> mine = TokenSids();
        List<RegistryAccessRule> list = new List<RegistryAccessRule>();

        foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            RegistryAccessRule r = rule as RegistryAccessRule;
            if (r == null) continue;
            if (r.AccessControlType != AccessControlType.Deny) continue;

            SecurityIdentifier who = r.IdentityReference as SecurityIdentifier;
            if (who == null) continue;
            if (!Contains(mine, who)) continue;

            list.Add(r);
        }

        return list;
    }

    private static bool Contains(List<SecurityIdentifier> list, SecurityIdentifier sid)
    {
        foreach (SecurityIdentifier s in list)
        {
            if (s.Equals(sid)) return true;
        }

        return false;
    }

    /// <summary>当前进程令牌里的所有 SID：用户本身 + 所属组。</summary>
    private static List<SecurityIdentifier> TokenSids()
    {
        List<SecurityIdentifier> sids = new List<SecurityIdentifier>();

        try
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();

            if (id.User != null) sids.Add(id.User);

            foreach (IdentityReference g in id.Groups)
            {
                SecurityIdentifier sid = g as SecurityIdentifier;
                if (sid != null) sids.Add(sid);
            }
        }
        catch
        {
        }

        return sids;
    }

    /// <summary>
    /// 修复「没有拒绝规则但写不进去」的状态：移除所有拦得住的拒绝规则，
    /// 再给当前用户补一条显式完全控制。只在解锁失败时调用。
    /// </summary>
    private static bool RepairAcl(RegistrySecurity sec)
    {
        try
        {
            foreach (RegistryAccessRule r in CollectDenyRules(sec))
            {
                if (r.IsInherited) continue;
                if (!sec.RemoveAccessRule(r)) sec.RemoveAccessRuleSpecific(r);
            }

            sec.AddAccessRule(new RegistryAccessRule(
                CurrentSid(),
                RegistryRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));

            ApplyAcl(sec);

            return !IsLocked();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把当前 ACL 和令牌信息整理成可复制的一段文字，用于反馈问题。</summary>
    private static string DescribeAcl(RegistrySecurity sec)
    {
        StringBuilder sb = new StringBuilder();

        try
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();

            sb.AppendLine("当前用户：" + id.Name);
            sb.AppendLine("用户 SID：" + (id.User == null ? "-" : id.User.Value));
            sb.AppendLine("管理员：" + (IsElevated() ? "是" : "否"));
            sb.AppendLine();

            sb.AppendLine("所有者：" + IdentityName(sec.GetOwner(typeof(SecurityIdentifier))));
            sb.AppendLine("继承：" + (sec.AreAccessRulesProtected ? "已关闭" : "已启用"));
            sb.AppendLine("ACE 明细：");

            foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                RegistryAccessRule r = rule as RegistryAccessRule;
                if (r == null) continue;

                sb.AppendLine(string.Format("  {0,-6}{1,-40}{2,-28}{3}",
                    r.AccessControlType,
                    IdentityName(r.IdentityReference),
                    r.RegistryRights,
                    r.IsInherited ? "继承" : "显式"));
            }

            sb.AppendLine();
            sb.AppendLine("请把这段内容连同操作步骤一起反馈。");
        }
        catch (Exception ex)
        {
            sb.AppendLine("读取权限信息时出错：" + ex.Message);
        }

        return sb.ToString();
    }

    private static readonly bool TraceEnabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVPANE_TRACE"));

    private static void Trace(string text)
    {
        if (!TraceEnabled) return;

        try
        {
            string file = Path.Combine(Path.GetTempPath(), "navpane_trace.txt");
            File.AppendAllText(file, text + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static string DiskSddl()
    {
        try
        {
            RegistrySecurity sec = ReadAcl();
            return sec == null ? "<null>" : sec.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        }
        catch (Exception ex)
        {
            return "<err " + ex.Message + ">";
        }
    }

    private static void SaveBackup(string sddl)
    {
        Directory.CreateDirectory(DataDir);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string file = Path.Combine(DataDir, "sddl_" + stamp + ".bak");

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        File.WriteAllText(file, sddl, utf8NoBom);
        File.WriteAllText(BackupLatestPath, sddl, utf8NoBom);
    }

    private static bool TryRestoreFromBackup()
    {
        try
        {
            if (!File.Exists(BackupLatestPath)) return false;

            string sddl = File.ReadAllText(BackupLatestPath).Trim();
            if (sddl.Length == 0) return false;

            RegistrySecurity sec = new RegistrySecurity();
            sec.SetSecurityDescriptorSddlForm(sddl);
            ApplyAcl(sec);

            return !IsLocked();
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadAdvancedInt(string name)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AdvancedPath))
            {
                if (k == null) return null;

                object v = k.GetValue(name, null);
                if (v == null) return null;

                return Convert.ToInt32(v);
            }
        }
        catch
        {
            return null;
        }
    }

    private static SecurityIdentifier CurrentSid()
    {
        return WindowsIdentity.GetCurrent().User;
    }

    // ------------------------------------------------------------ 数字排序

    /// <summary>返回 NoStrCmpLogical 的原始值；null 表示未设置（系统默认 = 启用数字排序）。</summary>
    public static int? GetNumericSort()
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(SortPolicyPath))
            {
                if (k == null) return null;

                object v = k.GetValue(ValueNoStrCmpLogical, null);
                if (v == null) return null;

                return Convert.ToInt32(v);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>enabled = true 写 0，false 写 1。需要管理员权限。</summary>
    public static OpResult SetNumericSort(bool enabled, out string message)
    {
        message = "";
        int target = enabled ? 0 : 1;

        try
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(SortPolicyPath))
            {
                if (k == null)
                {
                    message = "无法打开策略注册表项。";
                    Log("numeric-sort", "CreateSubKey 返回 null", OpResult.Precondition);
                    return OpResult.Precondition;
                }

                k.SetValue(ValueNoStrCmpLogical, target, RegistryValueKind.DWord);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            message = "需要管理员权限才能修改系统级设置。\n" + ex.Message;
            Log("numeric-sort", "权限不足: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "写入失败：" + ex.Message;
            Log("numeric-sort", "异常: " + ex.Message, OpResult.Failed);
            return OpResult.Failed;
        }

        int? verify = GetNumericSort();
        if (verify == null || verify.Value != target)
        {
            message = "写入后回读校验失败。";
            Log("numeric-sort", "回读校验失败", OpResult.Failed);
            return OpResult.Failed;
        }

        message = enabled
            ? "已启用数字排序：文件名里的数字按数值大小比较（1、2、10）。"
            : "已禁用数字排序：改为逐字符比较（1、10、2）。";

        Log("numeric-sort", "设置为 " + target, OpResult.Ok);
        return OpResult.Ok;
    }

    /// <summary>是否加入了域 —— 域环境里该策略可能被组策略覆盖。</summary>
    public static bool IsDomainJoined()
    {
        try
        {
            return !string.Equals(Environment.UserDomainName, Environment.MachineName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdminUser()
    {
        return IsElevated();
    }

    private static bool IsElevated()
    {
        try
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string IdentityName(IdentityReference ir)
    {
        if (ir == null) return "<unknown>";

        string name = null;

        try
        {
            NTAccount account = ir.Translate(typeof(NTAccount)) as NTAccount;
            if (account != null) name = account.Value;
        }
        catch { }

        if (string.IsNullOrEmpty(name)) name = ir.Value;
        if (string.IsNullOrEmpty(name)) return "<unknown>";

        if (name.EndsWith(@"\ALL APPLICATION PACKAGES", StringComparison.OrdinalIgnoreCase))
        {
            return "ALL APPLICATION PACKAGES";
        }

        if (name.Length > 56)
        {
            return name.Substring(0, 30) + "..." + name.Substring(name.Length - 14);
        }

        return name;
    }

    private static string DescribeOs()
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (k != null)
                {
                    object product = k.GetValue("ProductName", null);
                    object display = k.GetValue("DisplayVersion", null);
                    object build = k.GetValue("CurrentBuildNumber", null);

                    string text = product == null ? "Windows" : product.ToString();

                    // 注册表里 Win11 仍写作 "Windows 10"，按 build 号纠正
                    int buildNumber = 0;
                    if (build != null) int.TryParse(build.ToString(), out buildNumber);

                    if (buildNumber >= 22000 && text.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                    {
                        text = "Windows 11" + text.Substring("Windows 10".Length);
                    }

                    if (display != null) text += " " + display;
                    if (build != null) text += " (build " + build + ")";

                    return text;
                }
            }
        }
        catch
        {
        }

        return Environment.OSVersion.VersionString;
    }

    private static void Log(string action, string detail, OpResult result)
    {
        try
        {
            Directory.CreateDirectory(DataDir);

            RotateLogIfNeeded();

            string line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss}\t{1}\t{2}\t结果={3}\t用户={4}",
                DateTime.Now, action, detail, result, WindowsIdentity.GetCurrent().Name);

            if (!File.Exists(LogPath))
            {
                File.WriteAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            else
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }

    /// <summary>日志超过 512 KB 时轮转一份备份，避免长期使用无限增长。</summary>
    private static void RotateLogIfNeeded()
    {
        try
        {
            FileInfo fi = new FileInfo(LogPath);

            if (!fi.Exists || fi.Length < 512 * 1024) return;

            string old = LogPath + ".1";

            if (File.Exists(old)) File.Delete(old);

            File.Move(LogPath, old);
        }
        catch
        {
        }
    }

    private static class NativeMethods
    {
        public const int HKEY_CURRENT_USER = unchecked((int)0x80000001);
        public const int KEY_QUERY_VALUE = 0x0001;
        public const int KEY_SET_VALUE = 0x0002;
        public const int READ_CONTROL = 0x00020000;
        public const int WRITE_DAC = 0x00040000;
        public const int DACL_SECURITY_INFORMATION = 0x00000004;
        public const int OWNER_SECURITY_INFORMATION = 0x00000001;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RegOpenKeyEx(
            IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegSetKeySecurity(
            IntPtr hKey, int securityInformation, byte[] pSecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegGetKeySecurity(
            IntPtr hKey, int securityInformation, IntPtr pSecurityDescriptor, ref int lpcbSecurityDescriptor);

        [DllImport("advapi32.dll")]
        public static extern int RegCloseKey(IntPtr hKey);
    }
}
