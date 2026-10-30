# NavPaneLock — 长期知识

> 提炼后的项目知识。详细过程见同目录下的日期日志。

## 项目定位

原生 Win32 单文件 Windows 小工具（.NET Framework 4.x，csc 直接编译，不需要 SDK），
集中管理几项平时要翻好几层系统设置、还容易被系统改回去的选项。
界面是 Win11 风格原生窗口，自动跟随系统深浅色主题。

源码分工：

| 文件 | 职责 |
|---|---|
| `App.cs` | 入口、单实例互斥、按需提权子命令（`--apply <动作> <值>`） |
| `NavPaneCore.cs` | 导航窗格锁定（改注册表 ACL）、数字排序、操作日志 |
| `PowerCore.cs` | 自适应亮度、休眠（走 PowrProf / powercfg） |
| `ClipboardCore.cs` | 剪贴板历史、服务检测与修复 |
| `ContextMenuCore.cs` | 右键菜单 shell verb 的注册与注销 |
| `Gui.cs` | 主题、控件、对话框、主窗口流式布局 |

## 项目约定

- **系统级设置按需提权**：平时普通权限运行，只有写 HKLM 时才拉起一个提权实例，写完即退
- **写完必须回读校验**，不靠"没报错就算成功"
- **不硬编码环境相关内容**：电源方案 GUID 运行时枚举、`powercfg` 只看退出码不看本地化输出、路径运行时取
- **操作日志**写 `%LOCALAPPDATA%\NavPaneLock\actions.log`，超过 512 KB 轮转一份

## 界面约定

- 主窗口用 `FlowLayoutPanel` **纵向流式堆叠**（`AddFlow(控件, 宽, 高, 下方间距)`），
  加一行只要多调一次 `AddFlow`，不再手算 Y 坐标
- 相关设置行可以用 `RowGroup` 合并成一张卡片：组内第一行 `Top`、最后一行 `Bottom`，
  接缝处不画边框，中间加一条内缩 20px 的细分隔线
- 每行支持：标题、副标题（显示状态）、开关、行内次级链接（画在副标题右侧）、箭头

## 环境实测结论（本机 Win11 build 26300）

- 系统自带两条菜单动词，策略正好相反：
  - `HKLM\...\Classes\Directory\Background\shell\cmd` → 带 `Extended` + `HideBasedOnVelocityId`，
    **新版右键菜单里看不见**，只有 Shift+右键「显示更多选项」才有
  - `HKLM\...\Classes\Directory\Background\shell\Powershell` → 带 `ShowBasedOnVelocityId`，新版菜单里能看见
- 自定义动词要两版通用，必须：**不设 `Extended`**、**不设 `Show/HideBasedOnVelocityId`**、
  写可执行文件绝对路径（Git for Windows 的 `git_shell` 就是这个形态）
- `powershell.exe -WorkingDirectory` **被接受却不生效**（窗口仍停在原目录）。
  系统内置那条 `powershell.exe -noexit -WorkingDirectory "%V"` 因此其实没进到目标目录。
  改用 `-NoExit -Command "Set-Location -LiteralPath '...'"` 才有效
- `powershell.exe -Command` 传字符串时，后面跟的内容会被并进命令串，`$args[0]` 形式不可用
- `cmd.exe /d /s /k pushd "%V"` 对含空格、中文、括号的路径都正常（`pushd` 还能处理 UNC）
- 老 csc（`Framework64\v4.0.30319`）按系统 ANSI 码页读源文件，
  **UTF-8 无 BOM 的中文源码会被按 GBK 解**，可能把字节读成 `"` 或 `\` 直接写坏字符串字面量
- 该版本的 `GraphicsPath` 没有 `MoveTo/LineTo`，`AddLine` 只有 `Point/PointF` 版

## 未验证 / 待人工确认

- 右键菜单两条入口在 Explorer 里的**实际观感与落位**（无法做鼠标自动化）
- **Windows 10 实机**：注册写法按两版通用原则设计，但只在 Win11 上验证过
- Win+V 开关的实际行为（本机镜像禁用了 `cbdhsvc` 模板，需注销重登才能验）
- 无环境光传感器的机器上自适应亮度的实际视觉效果
