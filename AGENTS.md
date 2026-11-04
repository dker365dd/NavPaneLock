# NavPaneLock — Win32 系统设置小工具

单文件 Windows 小工具，管几项「平时要翻好几层设置、还容易被系统改回去」的选项。
原生 Win32 窗口 + Win11 风格界面，跟随系统深浅色。技术栈：.NET Framework 4.x，`csc` 直接编译，不需要 SDK。

## 交接先读

1. `.memo/memory/MEMORY.md` — 长期知识 + 本机实测结论（编码、注册表形态、csc 限制都在这）
2. `README.md` — 每个功能的注册表位置、值名、权限要求
3. `.memo/memory/YYYY-MM-DD.md` — 只在需要某天细节时读，**不要全读**

## 常用命令

```cmd
build.cmd          # 输出 NavPaneLock.exe，失败返回非 0
```

- 产物是单文件 exe，双击即用，无需安装
- 运行日志 `%LOCALAPPDATA%\NavPaneLock\actions.log`，超 512 KB 轮转一份
- 说明：仓库里没有测试脚本，验证靠实跑 + 截图核对，没有自动化可跑

## 三条关键事实

1. **按需提权**：平时普通权限运行、不弹 UAC，只有写 HKLM 时才拉起一个提权实例
   （`--apply <动作> <值>`）执行写入，写完即退。
2. **写完必须回读校验**，不靠「没报错就算成功」。
3. **不硬编码环境**：电源方案 GUID 运行时用 `PowerEnumerate` 枚举，`powercfg` 只看退出码
   不看本地化输出，路径运行时取。

## 红线

- **不要设 `Extended` 或 `Show|HideBasedOnVelocityId`**，否则 Win11 新版右键菜单里直接看不见。
- **不要用 `powershell.exe -WorkingDirectory`**：实测被接受、不报错，但不生效（窗口停在原目录）。
  用 `-NoExit -Command "Set-Location -LiteralPath '...'"`。
- **不要改 `.cs` 的编码而不改 `build.cmd`**：脚本里有 `chcp 936`，两者是一套。改完必须实跑看界面中文。
- **不要覆盖系统自带的 shell 键**，右键菜单只写 `HKCU\Software\Classes`，关开关时删干净子树。
- **休眠开关会连带关闭「快速启动」**，两个方向都要确认后再动手。
- **没有明确授权，不跑 `build.cmd`、不安装、不改注册表。**

## 记在哪

细节先写 `.memo/memory/MEMORY.md`（含实测结论），README 只放面向使用者的功能说明。
留知识前先搜一遍 `MEMORY.md`，别重复记。
