using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>程序入口：单实例控制、提权子命令分发。</summary>
internal static class App
{
    public const string Title = "系统设置小工具";
    public const string Version = "1.6.0";

    public const string ResultFileName = "last-elevated.txt";

    [STAThread]
    private static int Main(string[] args)
    {
        // 提权实例：只执行一条命令、写结果文件、退出，不创建窗口。
        // 注意必须在单实例互斥之前判断，否则提权实例会被主实例挡住。
        if (args != null && args.Length >= 2 && args[0] == "--apply")
        {
            return RunCommand(args);
        }

        bool created;
        using (Mutex mutex = new Mutex(true, @"Local\NavPaneLockMgr", out created))
        {
            if (!created)
            {
                MessageBox.Show("工具已经在运行了。", Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            finally
            {
                try { mutex.ReleaseMutex(); }
                catch { }
            }
        }
    }

    private static int RunCommand(string[] args)
    {
        // 用法：--apply <动作> <值>
        string action = args[1];
        bool on = args.Length >= 3 && args[2] == "1";

        int code = 3;
        string message = "未知动作：" + action;

        try
        {
            switch (action)
            {
                case "sort":
                    {
                        OpResult r = NavPaneCore.SetNumericSort(on, out message);
                        code = ToCode(r);
                        break;
                    }

                case "brightness-current":
                    {
                        OpResult r = PowerCore.SetBrightness(on, false, out message);
                        code = ToCode(r);
                        break;
                    }

                case "brightness-all":
                    {
                        OpResult r = PowerCore.SetBrightness(on, true, out message);
                        code = ToCode(r);
                        break;
                    }

                case "hibernate":
                    {
                        OpResult r = PowerCore.SetHibernate(on, out message);
                        code = ToCode(r);
                        break;
                    }

                case "clip-history":
                    {
                        OpResult r = ClipboardCore.SetHistory(on, out message);
                        code = ToCode(r);
                        break;
                    }

                case "clip-service":
                    {
                        OpResult r = ClipboardCore.RepairService(out message);
                        code = ToCode(r);
                        break;
                    }

                default:
                    message = "未知动作：" + action;
                    code = 3;
                    break;
            }
        }
        catch (Exception ex)
        {
            message = "执行失败：" + ex.Message;
            code = 3;
        }

        WriteResult(code, message);
        return code;
    }

    private static int ToCode(OpResult r)
    {
        if (r == OpResult.Ok) return 0;
        if (r == OpResult.NoPermission) return 4;
        if (r == OpResult.Precondition) return 2;
        return 3;
    }

    private static void WriteResult(int code, string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NavPaneLock");

            Directory.CreateDirectory(dir);

            string text = code + "\r\n" + (message ?? "");
            File.WriteAllText(Path.Combine(dir, ResultFileName), text, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    /// <summary>读取并删除提权实例留下的结果文件。</summary>
    public static void ReadResult(out int code, out string message)
    {
        code = -1;
        message = "";

        try
        {
            string path = Path.Combine(NavPaneCore.DataDir, ResultFileName);
            if (!File.Exists(path)) return;

            string[] lines = File.ReadAllText(path, Encoding.UTF8).Split(new string[] { "\r\n" }, StringSplitOptions.None);

            if (lines.Length > 0) int.TryParse(lines[0].Trim(), out code);
            if (lines.Length > 1) message = lines[1];

            File.Delete(path);
        }
        catch
        {
        }
    }
}
