# EDock

Windows 11 左右自动隐藏快捷启动栏。鼠标移到屏幕边缘展开，移开后收回。

## 下载与运行

在 [0.16.9 发布页](https://github.com/yuexialiaoluan/EDock/releases/tag/v0.16.9) 下载，
完整解压后双击 **EDock.exe**。适用 Windows 11 x64，免安装。

| 版本 | 压缩包 | 大小 | 运行环境 |
| --- | --- | --- | --- |
| 完整版 | `EDock-0.16.9-win-x64.zip` | 98.67 MiB | 已包含运行依赖，适合首次使用 |
| 精简版 | `EDock-0.16.9-lite-win-x64.zip` | 15.47 MiB | 需已安装 .NET 8 x64 Runtime 和 Windows App Runtime 1.8 x64 |

两版功能相同。精简版所需环境可从微软下载：
[.NET 8 x64 Runtime](https://aka.ms/dotnet/8.0/dotnet-runtime-win-x64.exe)、
[Windows App Runtime 1.8 x64](https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe)。
Windows 不一定自带这些环境；已安装其他主版本也不能直接替代。

升级前先从托盘退出旧版；请保留全部附属文件，不要直接在压缩包里运行。
发布页附两版各自的 `SHA256.txt` 校验文件。

## 功能

- 左右边栏可统一或独立设置；自动隐藏、固定、展开和多列表。
- 可同时关闭左右边栏，重启后保留；通过系统托盘打开设置即可重新开启。
- 拖入应用快捷方式、文件、文件夹和网页；排序、内部拖动和桌面快捷方式转移。
- 经典图标、横向图片、纯文本；调整图标大小、间距、列数和按钮位置。
- 贴边直角、直角悬浮、圆角悬浮；主题、透明、亚克力和图片背景。
- 独立的设置窗口外观；系统托盘菜单；可选的开机启动和系统任务栏材质。
- 隐藏待机时按需卸载图标、图片和材质资源，再次显示时恢复。

## 快捷键

普通模式下鼠标移入已显示的边栏即可使用。

| 按键 | 操作 |
| --- | --- |
| W / S | 上下选择图标 |
| A / D | 左右选择图标 |
| 长按 WASD | 连续快速移动 |
| Q / E | 切换列表 |
| F | 展开或收回当前边栏 |
| Tab | 切换左右已启用边栏 |
| 空格 | 打开选中的项目 |

普通模式按 Tab 后目标侧临时保持显示；移动鼠标、切换窗口或打开项目后结束。
Ctrl 快捷模式默认关闭，可在设置中开启：桌面或 EDock 设置窗口前台单按 Ctrl
呼出并保持边栏，再按退出。同样支持上述快捷键，也支持滚轮选择。
文本编辑、组合键、菜单、拖动和文件选择期间会暂停相关快捷键。
对应边栏关闭时暂停 Ctrl 呼出，重新启用后恢复原来的快捷模式偏好；列表也会保留。

## 数据与卸载

配置：`%LOCALAPPDATA%\EDock\settings.json`；导入的独立快捷方式：同目录 `Shortcuts`。
导入 `.lnk` / `.url` 会保存副本，删除桌面原快捷方式后仍可启动应用；应用本体及
自定义图片仍需保留。首次使用列表为空，开机启动、Ctrl 模式和任务栏材质默认关闭。

退出后可备份整个配置目录。升级沿用现有配置，旧 EdgeDock 配置会在需要时自动迁移。
卸载前关闭开机启动并退出，再删除程序文件夹；配置可按需保留。
移动程序目录后，如使用开机启动，请在新位置重新开关该选项。

## 构建

在 Windows 上安装 .NET 8 SDK，然后运行：

```powershell
./build.ps1 -Publish
```

输出位于 `publish/win-x64/EDock.exe`。首次构建需要联网还原 NuGet 依赖。
精简版可发布到独立目录，避免混入完整版依赖：

```powershell
dotnet publish ./src/EDock/EDock.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -p:WindowsAppSDKSelfContained=false -o ./publish/lite-win-x64
```

源码附带当前原生任务栏组件和对应源码；如需修改原生组件，用 Zig 0.14.1 构建：

```powershell
./src/EDock/Native/build.ps1 -ZigPath 'C:\Tools\zig\zig.exe'
./build.ps1 -Publish
```

任务栏组件已经被资源管理器加载时，更新原生实现须同步增加组件和通信协议版本。

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

当前边栏显示在主显示器两侧，全屏应用会暂停自动呼出。任务栏透明、亚克力和
毛玻璃已在 Windows 11 25H2 的单显示器环境验证，其他系统版本与多显示器环境
仍需进一步验证。隐藏后的内存占用会随系统和内容变化，没有固定上限；参见
[内存优化说明](docs/内存优化说明.md)。

可通过 [Issues](https://github.com/yuexialiaoluan/EDock/issues) 提交问题，请附 Windows
版本、EDock 版本、复现步骤和必要截图，并隐去个人信息。

第三方组件的许可与声明见 [Licenses](Licenses/README.txt) 和
[NativeThirdPartyNotices.txt](NativeThirdPartyNotices.txt)。