# EDock

Windows 11 左右自动隐藏快捷启动栏。鼠标移到屏幕边缘展开，移开后收回。

## 下载与运行

在 [0.16.19 发布页](https://github.com/yuexialiaoluan/EDock/releases/tag/v0.16.19) 下载，
适用 Windows 11 x64，可选择安装版或免安装便携版。当前发布仅提供最新版本。

| 版本 | 文件 | 大小 | 运行环境 |
| --- | --- | --- | --- |
| 安装版 | `EDock-0.16.19-setup-win-x64.exe` | 53.17 MiB | 已包含运行依赖，支持覆盖升级 |
| 便携版 | `EDock-0.16.19-portable-win-x64.zip` | 79.17 MiB | 已包含运行依赖，解压运行 |

便携版完整解压后双击 **EDock.exe**。升级前先从托盘退出旧版；请保留全部附属文件，不要直接在压缩包里运行。
发布页附各版本对应的 `SHA256.txt` 校验文件。

## 功能

- 左右边栏可统一或独立设置；自动隐藏、固定、展开和多列表。
- 默认在同屏其他应用最大化或全屏时暂停触边呼出；固定边栏默认不置顶，两项均可在基本设置中分别关闭。
- 可同时关闭左右边栏，重启后保留；通过系统托盘打开设置即可重新开启。
- 拖入应用快捷方式、文件、文件夹和网页；排序、内部拖动和桌面快捷方式转移。
- 经典图标、横向图片、纯文本；调整图标大小、间距、列数和按钮位置。
- 贴边直角、直角悬浮、圆角悬浮；主题、透明、亚克力和图片背景。
- 独立的设置窗口外观；系统托盘菜单；可选的开机启动和系统任务栏材质。
- 附属功能提供开始菜单背景透明开关与 0–100% 透明度调节，文字和图标保持清晰；关闭或退出 EDock 后恢复系统背景，默认关闭。
- 附属功能可自定义系统开始按钮图片和大小，支持 PNG、JPG、GIF，分别设置普通、悬停、按下和开始菜单打开时的激活图片；导入图片保存副本，关闭或退出后恢复系统图标，默认关闭。
- 隐藏待机时按需卸载图标、图片和材质资源，再次显示时恢复。

## 快捷键

普通模式下鼠标移入已显示的边栏即可使用。桌面在前台时，固定边栏也接受这些按键，
鼠标可以停在屏幕中央；无需开启快捷模式。双侧固定默认操作左侧，之后沿用最近操作侧。

| 按键 | 操作 |
| --- | --- |
| W / S | 上下选择图标 |
| A / D | 左右选择图标 |
| 长按 WASD | 连续快速移动 |
| Q / E | 切换列表 |
| F | 展开或收回当前边栏 |
| G | 固定或取消固定当前边栏，长按只切换一次 |
| Tab | 切换左右已启用边栏 |
| 空格 | 打开选中的项目 |

普通模式按 Tab 后目标侧临时保持显示；移动鼠标、切换窗口或打开项目后结束。
快捷模式默认关闭，可在设置中开启：桌面或 EDock 设置窗口前台单按 Ctrl
呼出并保持边栏，再按退出。同样支持上述快捷键，也支持滚轮选择。
设置的「快捷模式」页面可修改或关闭每个快捷键，呼出键也在同一列表中。
点击按键开始录入，按 Enter 保存或 Esc 取消，也可点击保存、取消；保存后立即生效，需再次点击才继续修改。
冲突会标明重复的动作并阻止保存；关闭单项保留原按键，可逐项或全部恢复默认。
快捷键支持单键和组合键，录入、文本编辑、菜单、拖动和文件选择期间暂停边栏操作。
首选侧关闭时回退到另一已启用侧；两侧都关闭时暂停呼出，重新启用后恢复。
桌面重命名、菜单和拖动期间暂停桌面快捷键；鼠标移开边栏后，其他应用在前台时不响应。

## 数据与卸载

配置：`%LOCALAPPDATA%\EDock\settings.json`；导入的独立快捷方式：同目录 `Shortcuts`；开始按钮图片副本：同目录 `StartButtonImages`。安装版升级时会一起备份。
导入 `.lnk` / `.url` 会保存副本，删除桌面原快捷方式后仍可启动应用；应用本体及
自定义图片仍需保留。首次使用列表为空，开机启动、快捷模式和任务栏材质默认关闭。

退出后可备份整个配置目录。升级沿用现有配置，旧 EdgeDock 配置会在需要时自动迁移。
安装版可直接重新运行新版安装程序覆盖升级；升级前自动将现有配置完整备份到
`%LOCALAPPDATA%\EDock-backups`，程序文件与个人配置分开存放，卸载时保留配置及备份。
备份失败会停止安装；配置目录外引用的应用和图片不在备份范围内。
恢复备份时先退出 EDock，再将所需备份目录中的内容复制回配置目录。
卸载前关闭开机启动并退出；安装版从 Windows 设置中卸载，便携版删除程序文件夹。
移动程序目录后，如使用开机启动，请在新位置重新开关该选项。

## 构建

在 Windows 上安装 .NET 8 SDK，然后运行：

```powershell
./build.ps1 -Publish
```

输出位于 `publish/win-x64/EDock.exe`。首次构建需要联网还原 NuGet 依赖。
从 0.16.19 起使用 Windows App SDK 1.8 的 WinUI、DWrite 独立组件，
由官方构建流程生成所需依赖和清单，完整版本仍包含 .NET 与界面运行环境，用户无需额外安装。
程序没有使用的 AI、机器学习和 Widgets 组件不再随包分发；保留启动预编译，未启用实验性裁剪。
升级安装时只清理明确列出的旧组件文件，保留个人配置及可能仍被资源管理器加载的原生组件。
更改依赖后请发布到新的空目录，避免把旧目录中遗留的文件打入发布包。
源码附带当前原生任务栏组件和对应源码；如需修改原生组件，用 Zig 0.14.1 构建：

```powershell
./src/EDock/Native/build.ps1 -ZigPath 'C:\Tools\zig\zig.exe'
./src/EDock/Native/build-start-menu.ps1 -ZigPath 'C:\Tools\zig\zig.exe'
./build.ps1 -Publish
```

任务栏组件已经被资源管理器加载时，更新原生实现须同步增加组件和通信协议版本。

## 制作发布包

先按上述方式发布程序，再生成便携压缩包和 SHA-256 校验文件：

```powershell
./packaging/package-portable.ps1 -PublishDirectory ./publish/win-x64 -OutputDirectory ./artifacts
```

安装包使用 [Inno Setup 6.7.3](https://jrsoftware.org/isinfo.php)，以完整版发布目录为输入：

```powershell
./packaging/build-installer.ps1 -PublishDirectory ./publish/win-x64 -OutputDirectory ./artifacts -InnoSetupCompiler 'C:\Tools\Inno Setup 6\ISCC.exe'
```

脚本从程序读取版本，使用 `-setup`、`-portable` 命名，并附带第三方许可。
安装包保持固定应用标识和默认安装位置；后续版本请保留这些值，以继续支持覆盖升级。

## 测试

```powershell
dotnet run --project tests/EDock.CoreTests -c Release
dotnet run --project tests/EDock.SettingsTests -c Release
dotnet run --project tests/EDock.QuickModeTests -c Release
dotnet run --project tests/EDock.StartupTests -c Release
```

`EDock.exe --profile "绝对路径"` 可使用隔离测试配置，不读取日常配置，也不修改
登录启动项和系统任务栏。各测试使用自己的临时数据或随机任务名。

## 兼容性与反馈

当前边栏显示在主显示器两侧，默认在同屏其他应用最大化或全屏时暂停触边呼出。
固定边栏默认使用普通窗口层级，可被其他应用遮挡；两项均可在基本设置中关闭，支持左右独立设置。任务栏透明、亚克力和
毛玻璃已在 Windows 11 25H2 的单显示器环境验证，其他系统版本与多显示器环境
仍需进一步验证。隐藏后的内存占用会随系统和内容变化，没有固定上限；参见
[内存优化说明](docs/内存优化说明.md)。

可通过 [Issues](https://github.com/yuexialiaoluan/EDock/issues) 提交问题，请附 Windows
版本、EDock 版本、复现步骤和必要截图，并隐去个人信息。

第三方组件的许可与声明见 [Licenses](Licenses/README.txt) 和
[NativeThirdPartyNotices.txt](NativeThirdPartyNotices.txt)。
