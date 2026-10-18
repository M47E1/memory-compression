# Memory Compression

关闭最后一个 localhost 预览页后，停止对应的本地开发服务。支持 Chrome、Edge 和 Windows。

**名称说明：Memory Compression 是项目名称，不是 Windows 系统内存压缩功能的开关，也不包含自行实现的内存压缩算法。** 主要功能是预览服务收尾；内存释放通过用户自行选择的外部引擎执行。

[下载 Windows 便携版](https://github.com/M47E1/memory-compression/releases/latest) · [使用源码](https://github.com/M47E1/memory-compression)

**不需要内存清理引擎。** 默认只做预览收尾，不清空系统缓存，不删除分页文件，也不下载或捆绑鲁大师等第三方程序。需要定时调用自己的可信引擎时，可以单独配置。

## 直接使用

需要 Windows 10/11 和 .NET Framework 4.8 或更新的 4.x 运行环境。普通使用不需要 Node.js、Python、Visual Studio 或管理员权限。目标服务必须属于当前用户可停止的进程；权限不足时不会自动提权。

1. 解压 `windows-portable.zip`，双击 `MemoryCompression.exe`。可选引擎留空即可。
2. Chrome 打开 `chrome://extensions`，Edge 打开 `edge://extensions`，开启开发者模式，选择“加载已解压的扩展”，加载压缩包内的 `browser-extension` 文件夹。
3. 扩展名称为 **Memory Compression**，固定 ID 为 `dgpfiadablpfojbalengndogkiidplcb`。打开扩展弹窗，显示“已连接”后再使用。
4. 打开自己的 localhost 预览；关闭最后一个对应端口的预览页后，工具等待约 3 秒，再确认并停止对应进程。进程核对和退出还需要少量时间，不保证在整整 3 秒时退出。

Chrome、Edge、不同浏览器配置文件需要分别加载。同一端口在任一已连接浏览器中仍打开时，不停止。无痕窗口需要在这个扩展的详细设置里允许无痕/InPrivate 使用；未允许时，不要在普通窗口与未受监看的无痕窗口同时查看同一个服务。

发布程序未做商业代码签名。下载来源、Windows SmartScreen、企业软件策略可能影响运行；本项目不会关闭或绕过这些保护。组织策略拒绝时请联系管理员。

## 会关闭什么

仅识别直接由 Node 启动的 Next.js（`dev` / `start`）、Vite（`dev` / `serve` / `preview`）、Astro（`dev` / `preview`）特定入口。Next.js、Vite 无参数的默认开发模式也支持。工具根据真实监听端口登记 PID、创建时间、程序路径和父进程，停止前重新检查，不按 `node.exe` 名字批量结束进程。只处理在这个扩展中被观察、识别过的服务，不猜测哪些旧服务是“残留”。

识别只看实际入口脚本，不看参数里是否“提到”框架名；`build`、内联脚本、通用 `cmd.exe` 启动器都不管理。常见 Node 调试、预加载和内存参数可以识别；遇到不认识的 Node 参数，或只传框架选项却省略子命令时，会保留服务而不是猜测。后一种情况可改为显式 `next dev`、`vite dev` 等启动方式。某些通用启动器可能继续存在，本工具不会为清掉它们而扩大进程终止范围。

**停止使用的是进程终止，不是应用层优雅退出。** 不要用它管理生产服务器、后台任务或必须常驻的 localhost 应用。希望页面关闭后继续运行时，先取消“关闭最后一个 localhost 预览页时停止对应服务”的勾选；“停止”按钮会同时暂停预览收尾与可选引擎。

刷新、切换标签、最小化、冻结/休眠标签不算关闭。普通 Node API、MCP 和不认识的服务不处理。浏览器强退或扩展失联也不推定为关页；恢复连接并收到新报告后再确认。没有安装扩展的其他浏览器、其他设备和用户无法被观察，因此不要把这些未连接的客户端用于同一服务的生命周期保护。

首次停止失败时，默认每 200 秒再尝试；这项重试与外部内存引擎无关。权限拒绝、进程身份变化或操作记录不完整时，不放宽检查强行停止。

## 可选：自己的内存释放引擎

默认留空。只选择自己信任、有权使用的外部 EXE。窗口中的“浏览”可以选择引擎；程序不会扫描电脑寻找引擎，不会下载、捆绑、提权或自动安装第三方程序。

需要参数时，在 EXE 所在目录运行：

```powershell
.\MemoryCompression.exe --optimizer "D:\Tools\TrustedOptimizer.exe" --optimizer-arguments "/optimize" --interval 200
```

`/optimize` 只是参数格式示例，具体参数由所选引擎决定，默认不传参数。设置仅对本次启动生效。引擎缺失、无法启动、返回失败或执行时间较长，都不暂停预览收尾。相同引擎尚未退出时，不重复启动。

引擎在单独的隐藏 Windows 桌面上执行，避免动效打扰；不兼容这种方式的引擎会失败，但不会影响预览收尾。界面中的“可用内存增加”是执行前后系统可用内存的差值，受其他程序影响，不能证明引擎实际释放了同等内存，也不是分页文件清理量。

## 隐私、记录和并存

扩展只获得 localhost、127.0.0.1、::1 的 HTTP(S) 主机权限及 `storage` / `alarms` 权限，没有浏览全部网站的 `tabs` 权限，也没有内容脚本。发送内容只有随机浏览器标识、递增编号、时间和本地端口，不包含网页标题、路径或非本地地址。请求只发往本机 `127.0.0.1:47832`。

运行记录保存在当前用户的 `%LOCALAPPDATA%\MemoryCompression\preview-events`。记录包含本地进程路径等诊断信息，**不要把自己的记录目录或浏览器配置文件上传到公开仓库**。记录为版本 1 的追加式事件，包含编号、UTC 时间、运行与浏览器身份；观察、判断、请求停止、结果分开记录。重启时只读旧记录，不修改原始内容，未收到浏览器新报告前不重放停止操作。

开源版有自己的 EXE 名称、扩展 ID、端口、单实例标识和数据目录，不替换旧版工具或旧扩展。验证时可以并存；实际使用建议只给同一个项目启用一套生命周期管理工具，避免两套工具各自作出停止判断。

## 从源码编译

源码包不包含编译结果、第三方引擎、浏览器、个人路径、旧备份或本机运行记录。

```powershell
.\Build.ps1
.\bin\MemoryCompression.exe
```

使用 Windows .NET Framework 自带的 C# 编译器，不需要 .NET SDK 或 NuGet 下载。脚本不会更改系统执行策略。若脚本被组织策略禁止，请使用获准的开发环境，不要为了运行它关闭保护。

## 开发验证

对抗式审计结果与已知限制见 [AUDIT.md](AUDIT.md)。

需要 Node.js 18+。基础验证不启动浏览器，也不需要安装 npm 依赖：

```powershell
.\Test.ps1
```

包含扩展端口规则、控制器生命周期、追加事件重放、50 个命令行误识别反例与正常用例、真实 WinForms 定时运行、无引擎/无效引擎/失败引擎/运行中引擎的独立性，以及离屏界面渲染。可选引擎测试使用测试代码编译出的微型助手，不执行用户安装的清理引擎。

浏览器验证另外安装固定版本的开发依赖和 Chromium：

```powershell
npm ci --ignore-scripts
npx playwright install chromium
.\Test.ps1 -Browser
```

浏览器测试使用一个全新隔离配置和真正的无引擎 GUI 程序，不触碰日常浏览器配置。Windows 窗口放在屏幕外，并检查位置；结束后关闭测试自身启动的进程。资源不足、端口占用或缺少浏览器时会停止验证，不结束其他进程来腾位置。默认通过微型 Node 服务验证识别与退出流程，不把它们冒充成实际框架兼容性验证。

可用已有 Playwright/Chromium，避免重新下载：

```powershell
.\Test.ps1 -Browser -PlaywrightPath "D:\Dev\node_modules\playwright" -ChromiumPath "D:\Browsers\chromium\chrome.exe"
```

已有一个安装依赖并构建完成的 Next.js 项目时，可额外验证真实 Next.js 进程。会在另一个临时端口启动一份实例，关闭测试页后核对对应 PID 退出、端口释放，不关闭原来的实例：

```powershell
.\Test.ps1 -Browser -RealNextProject "D:\Dev\built-next-app"
```

本地结果保留在 `test-output`，不进入源码包或便携包。程序设置和界面为中文；未声称在所有 Windows 版本、所有浏览器配置和所有框架版本上验证过。

## 打包与许可证

```powershell
.\Package.ps1
```

`artifacts` 下生成源码 ZIP 和 Windows 便携 ZIP。打包按明确的文件名单复制，不会把开发目录、测试结果、依赖缓存或个人备份整个压进去。已存在的包不会覆盖，重新打包可传 `-OutputDirectory` 指定新目录。

本项目采用 [MIT License](LICENSE)（[OSI 原文](https://opensource.org/license/mit)）。MIT 许可仅覆盖本项目代码，不授予任何第三方内存清理引擎的使用或分发权利。参见 [NOTICE](NOTICE)。
