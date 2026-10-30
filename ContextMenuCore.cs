using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>右键菜单要打开的两样东西：命令提示符 / Windows PowerShell。</summary>
internal enum ShellKind
{
    Cmd = 0,
    PowerShell = 1
}

/// <summary>
/// 右键菜单的注册状态。两种入口各自三个位置，共六个开关位。
/// </summary>
internal sealed class ContextMenuInfo
{
    public bool CmdBackground;
    public bool CmdDirectory;
    public bool CmdDrive;

    public bool PsBackground;
    public bool PsDirectory;
    public bool PsDrive;

    public bool CmdAny { get { return CmdBackground || CmdDirectory || CmdDrive; } }

    public bool PsAny { get { return PsBackground || PsDirectory || PsDrive; } }

    public bool Any(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdAny : PsAny;
    }

    public bool IsBackground(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdBackground : PsBackground;
    }

    public bool IsDirectory(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdDirectory : PsDirectory;
    }

    public bool IsDrive(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdDrive : PsDrive;
    }
}

/// <summary>
/// 右键菜单外壳动词（shell verb）的注册核心。
///
/// 全部写在 HKCU\Software\Classes 下，只对当前用户生效，不需要管理员权限。
/// 注册的是普通的注册表动词，Explorer 自己会把它画进右键菜单，
/// 不需要 COM 扩展，也不需要额外的 DLL。
///
/// 兼容性要点（Win10 与 Win11 通用）：
///   · 不设 Extended        —— 设了 Win10 要按 Shift、Win11 新菜单直接不显示
///   · 不设 velocity id     —— 保持 Git for Windows 的形态，两边都显示
///   · 写可执行文件绝对路径 —— 不依赖 PATH，两版行为一致
/// </summary>
internal static class ContextMenuCore
{
    /// <summary>动词名（注册表里的子键名），不要和其他工具重名。</summary>
    public const string CmdVerbName = "NavPaneLockCmd";

    public const string PsVerbName = "NavPaneLockPowershell";

    public const string CmdLabel = "在此处打开CMD窗口";

    public const string PsLabel = "在此处打开PowerShell窗口";

    private const string ClassesRoot = @"Software\Classes";

    private const string CmdBackgroundRoot = ClassesRoot + @"\Directory\Background\shell\" + CmdVerbName;
    private const string CmdDirectoryRoot = ClassesRoot + @"\Directory\shell\" + CmdVerbName;
    private const string CmdDriveRoot = ClassesRoot + @"\Drive\shell\" + CmdVerbName;

    private const string PsBackgroundRoot = ClassesRoot + @"\Directory\Background\shell\" + PsVerbName;
    private const string PsDirectoryRoot = ClassesRoot + @"\Directory\shell\" + PsVerbName;
    private const string PsDriveRoot = ClassesRoot + @"\Drive\shell\" + PsVerbName;

    // 作用范围偏好的存放位置：每个 kind 各一个值，1 = 三处都注册，0 = 只空白处
    private const string ScopeKeyPath = @"Software\NavPaneLock";
    private const string ScopeCmdValue = "ScopeCmd";
    private const string ScopePsValue = "ScopePs";

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // ------------------------------------------------------------ 名称与命令

    public static string VerbName(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdVerbName : PsVerbName;
    }

    public static string Label(ShellKind kind)
    {
        return kind == ShellKind.Cmd ? CmdLabel : PsLabel;
    }

    private static string RootFor(ShellKind kind, int location)
    {
        // location: 0 = 空白处，1 = 文件夹本身，2 = 驱动器
        if (kind == ShellKind.Cmd)
        {
            if (location == 0) return CmdBackgroundRoot;
            if (location == 1) return CmdDirectoryRoot;
            return CmdDriveRoot;
        }

        if (location == 0) return PsBackgroundRoot;
        if (location == 1) return PsDirectoryRoot;
        return PsDriveRoot;
    }

    /// <summary>
    /// 两种 shell 的命令行语法完全不同，这里分开拼：
    ///   CMD       —— /k 保持窗口，pushd 进目录（UNC 也能进）
    ///   PowerShell —— -NoExit 保持窗口，Set-Location 进目录
    ///
    /// 注意：PowerShell 不用 -WorkingDirectory —— 实测它在 powershell.exe 上
    /// 被接受却不生效（窗口还是停在原目录），所以改用 -Command + Set-Location。
    /// </summary>
    private static string CommandFor(ShellKind kind, int location)
    {
        string slot = location == 0 ? "%V" : "%1";

        if (kind == ShellKind.Cmd)
        {
            return @"cmd.exe /d /s /k pushd """ + slot + @"""";
        }

        // 路径用单引号包住：PowerShell 里单引号是字面量，不会做变量展开
        return "\"" + PowerShellPath() + "\" -NoExit -Command \"Set-Location -LiteralPath '"
            + slot + "'\"";
    }

    /// <summary>
    /// Windows PowerShell 5.1 的可执行文件。Win10 / Win11 都自带，
    /// 路径运行时取，不硬编码（系统装在别的盘也照样找得到）。
    /// </summary>
    public static string PowerShellPath()
    {
        try
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (!string.IsNullOrEmpty(sys))
            {
                string path = Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe");

                if (File.Exists(path)) return path;
            }
        }
        catch
        {
        }

        return "powershell.exe";
    }

    // ------------------------------------------------------------ 状态读取

    public static ContextMenuInfo GetInfo()
    {
        ContextMenuInfo info = new ContextMenuInfo();

        info.CmdBackground = IsRegistered(CmdBackgroundRoot, CommandFor(ShellKind.Cmd, 0));
        info.CmdDirectory = IsRegistered(CmdDirectoryRoot, CommandFor(ShellKind.Cmd, 1));
        info.CmdDrive = IsRegistered(CmdDriveRoot, CommandFor(ShellKind.Cmd, 2));

        info.PsBackground = IsRegistered(PsBackgroundRoot, CommandFor(ShellKind.PowerShell, 0));
        info.PsDirectory = IsRegistered(PsDirectoryRoot, CommandFor(ShellKind.PowerShell, 1));
        info.PsDrive = IsRegistered(PsDriveRoot, CommandFor(ShellKind.PowerShell, 2));

        return info;
    }

    /// <summary>动词是否已注册，且 command 的内容仍是我们写的那条。</summary>
    private static bool IsRegistered(string root, string command)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(root + @"\command"))
            {
                if (k == null) return false;

                object v = k.GetValue(null, null);
                if (v == null) return false;

                return string.Equals(v.ToString().Trim(), command, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 作用范围

    /// <summary>读取某个 kind 的作用范围偏好：true = 三处都注册。</summary>
    public static bool GetScope(ShellKind kind)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ScopeKeyPath))
            {
                if (k == null) return true;

                object v = k.GetValue(kind == ShellKind.Cmd ? ScopeCmdValue : ScopePsValue, null);
                if (v == null) return true;

                return Convert.ToInt32(v) != 0;
            }
        }
        catch
        {
            return true;
        }
    }

    /// <summary>保存某个 kind 的作用范围偏好。</summary>
    public static void SetScope(ShellKind kind, bool allLocations)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ScopeKeyPath))
            {
                if (k == null) return;

                k.SetValue(kind == ShellKind.Cmd ? ScopeCmdValue : ScopePsValue,
                    allLocations ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------ 开关

    /// <summary>
    /// 开启或关闭某个入口的菜单项。
    /// </summary>
    /// <param name="kind">CMD 或 PowerShell。</param>
    /// <param name="enabled">是否注册菜单项。</param>
    /// <param name="allLocations">
    /// true = 空白处、文件夹上、驱动器三处都注册；
    /// false = 只注册文件夹空白处。
    /// </param>
    public static OpResult SetEnabled(ShellKind kind, bool enabled, bool allLocations, out string message)
    {
        message = "";

        List<string> wanted = new List<string>();

        if (enabled)
        {
            wanted.Add(RootFor(kind, 0));

            if (allLocations)
            {
                wanted.Add(RootFor(kind, 1));
                wanted.Add(RootFor(kind, 2));
            }
        }

        // 名字被别的程序占用时不要硬覆盖
        foreach (string root in wanted)
        {
            string owner = DescribeOwner(root, kind);
            if (owner == null) continue;

            message = "该菜单项名已被其他程序占用（" + owner + "），请先卸载它再试。";
            NavPaneCore.Log("context-menu", root + " 被占用: " + owner, OpResult.Precondition);
            return OpResult.Precondition;
        }

        try
        {
            // 先把不需要的清掉，作用范围缩小时才不会留下残项
            for (int i = 0; i < 3; i++)
            {
                string root = RootFor(kind, i);
                if (wanted.Contains(root)) continue;

                DeleteTree(root);
            }

            for (int location = 0; location < 3; location++)
            {
                string root = RootFor(kind, location);
                if (!wanted.Contains(root)) continue;

                WriteVerb(root, Label(kind), CommandFor(kind, location), IconPath(kind));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            message = "权限不足：" + ex.Message;
            NavPaneCore.Log("context-menu", "权限不足: " + ex.Message, OpResult.NoPermission);
            return OpResult.NoPermission;
        }
        catch (Exception ex)
        {
            message = "写入失败：" + ex.Message;
            NavPaneCore.Log("context-menu", "异常: " + ex.Message, OpResult.Failed);
            return OpResult.Failed;
        }

        NotifyShell();

        ContextMenuInfo after = GetInfo();

        bool ok;

        if (!enabled)
        {
            ok = !after.Any(kind);
        }
        else if (allLocations)
        {
            ok = after.IsBackground(kind) && after.IsDirectory(kind) && after.IsDrive(kind);
        }
        else
        {
            ok = after.IsBackground(kind) && !after.IsDirectory(kind) && !after.IsDrive(kind);
        }

        if (!ok)
        {
            message = "写入后回读校验失败。";
            NavPaneCore.Log("context-menu", "回读校验失败", OpResult.Failed);
            return OpResult.Failed;
        }

        if (enabled)
        {
            message = allLocations
                ? "已开启：" + Label(kind) + "，空白处、文件夹上、驱动器右键都能看到。"
                : "已开启：" + Label(kind) + "，仅在文件夹空白处右键时出现。";
        }
        else
        {
            message = "已关闭：右键菜单里的「" + Label(kind) + "」已移除。";
        }

        NavPaneCore.Log("context-menu", message, OpResult.Ok);
        return OpResult.Ok;
    }

    // ------------------------------------------------------------ 内部工具

    private static void WriteVerb(string root, string label, string command, string icon)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(root))
        {
            if (k == null) throw new InvalidOperationException("无法创建注册表项：" + root);

            k.SetValue(null, label, RegistryValueKind.String);
            k.SetValue("MUIVerb", label, RegistryValueKind.String);
            k.SetValue("Icon", icon, RegistryValueKind.String);

            // 与系统内置那条保持一致：目录切换交给命令自己，Explorer 不要再去设工作目录
            k.SetValue("NoWorkingDirectory", "", RegistryValueKind.String);
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(root + @"\command"))
        {
            if (k == null) throw new InvalidOperationException("无法创建注册表项：" + root + @"\command");

            k.SetValue(null, command, RegistryValueKind.String);
        }
    }

    private static void DeleteTree(string root)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, false);
        }
        catch (ArgumentException)
        {
            // 本来就不存在
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 检查同名动词是不是别的程序留下的。是我们自己写的就返回 null。
    /// </summary>
    private static string DescribeOwner(string root, ShellKind kind)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(root + @"\command"))
            {
                if (k == null) return null;

                object v = k.GetValue(null, null);
                if (v == null) return null;

                string command = v.ToString().Trim();

                if (command.IndexOf(CommandFor(kind, 0), StringComparison.OrdinalIgnoreCase) >= 0
                    || command.IndexOf(CommandFor(kind, 1), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return null;    // 是我们自己写的
                }

                return command.Length > 60 ? command.Substring(0, 60) + "…" : command;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>菜单图标跟着各自的程序走：CMD 用 cmd.exe，PowerShell 用 powershell.exe。</summary>
    private static string IconPath(ShellKind kind)
    {
        if (kind == ShellKind.PowerShell)
        {
            try
            {
                string path = PowerShellPath();

                if (path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) > 0
                    && File.Exists(path))
                {
                    return path;
                }
            }
            catch
            {
            }

            return "powershell.exe";
        }

        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (string.IsNullOrEmpty(dir)) return "cmd.exe";

            return Path.Combine(dir, "cmd.exe");
        }
        catch
        {
            return "cmd.exe";
        }
    }

    /// <summary>通知 Explorer 关联项已变化，右键菜单随即重建，不用重启资源管理器。</summary>
    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
        }
    }
}
