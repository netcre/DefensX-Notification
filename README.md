# DefensX-Notification · 盾网实时监控

基于当前源码整理的中文部署、运维与开发文档。验证日期：2026-10-24。

[API 手册](docs/API.md) · [运维交接](docs/HANDOVER.md) · [二次开发](docs/DEVELOPMENT.md) · [验证记录](docs/VERIFICATION.md)

[1. 项目简介](#1-项目简介) · [2. 功能特性](#2-功能特性) · [3. 运行环境](#3-运行环境) · [4. 完整依赖库清单](#4-完整依赖库清单) · [5. 项目结构说明](#5-项目结构说明) · [6. 安装部署教程](#6-安装部署教程) · [7. 使用教程](#7-使用教程) · [8. 报错与问题排查](#8-报错与问题排查) · [9. 性能优化方案](#9-性能优化方案) · [10. 安全加固方案](#10-安全加固方案) · [11. 项目对应全套反编译工具](#11-项目对应全套反编译工具) · [12. 完整逆向分析教程](#12-完整逆向分析教程) · [13. 防逆向与防反编译保护方案](#13-防逆向与防反编译保护方案) · [14. 更新日志](#14-更新日志) · [15. 开源协议与使用声明](#15-开源协议与使用声明) · [16. API 接口总览](#16-api-接口总览) · [17. 团队运维交接摘要](#17-团队运维交接摘要) · [18. 二次开发摘要](#18-二次开发摘要)

# 1. 项目简介

Windows 交互式托盘程序，监控盾网“云机房”；每 30 秒采集，终端数严格超过实时 VIP 额度时通知钉钉。不是通用多租户平台，也不是 Web 服务。仓库名 DefensX-Notification，解决方案/程序集保留 DwhMonitor。App csproj 声明版本 **1.0.0**，不是已发布 Tag 的证明。

本次 Windows x64 / .NET SDK 10.0.302：Release 测试 **43/43、0 跳过**，构建、格式检查和自包含发布通过；有 NU1903 告警。未执行真实登录、真实机器人发送或正式客户端启动。见 [验证记录](docs/VERIFICATION.md)。

规划资料已完整读取，原文件不改且只留本地：

| 来源 | 主要结论 | 冲突与证据等级 | 写入章节 |
| --- | --- | --- | --- |
| task_plan.md | 固定 GUID、严格超额、单活动账号、阶段交付 | 头部阶段 13 与后续阶段 14 不一致，按源码/当前验证描述 | 1/2/6/14 |
| findings.md | 字段映射、超时、OCR、迁移 | 早期 Tesseract 和关闭闸门已被 CNN+五次提交取代 | 4/7/8/9 |
| progress.md | 历史测试、发布、掉线恢复 | 历史 PID/采集时间不是今日状态；单张与五次流程不可混同 | 6/8/9/14 |

差异化能力是浏览器会话复用、本地模型、状态落盘去重和 DPAPI。定位源码见下文，最低硬件资源未确认。历史使用范围 Windows 10/11；本次只验证当前 Windows x64 构建/测试，不声称覆盖各 OS 版本。

# 2. 功能特性

| 能力 | 入口/输入 | 输出与边界 | 源码 |
| --- | --- | --- | --- |
| 终端超额 | 每轮 o/mc | 仅 o>mc，动态额度，等于不报 | MonitorEngine |
| 网吧离线 | status=false | 首次异常与首次恢复 | MonitorEngine |
| 后台会话失效 | Cookie、401/403/业务错误 | 通知并尝试恢复 | DwhApiClient/SessionWebViewForm |
| 接口不可达 | 连续 3 次失败 | 告警，一轮完整成功恢复 | MonitorEngine |
| 自动登录失败 | 验证码/短信/凭据/页面 | 独立状态及人工接管 | AutoLoginModels/TrayApplicationContext |
| 钉钉 | Webhook/secret | Markdown 加签、重试 | DingTalkNotifier |
| 多账号设置 | 新增/更新/删除/活动选择 | 只有一个活动账号，不遍历 | AppSettingsStore/SettingsForm |
| 自动登录 | 默认关闭，显式勾选 | 最多 5 次真实提交、15 张图 | CaptchaRetryCoordinator |
| 采样/去重 | 30 秒串行轮询 | SQLite WAL、90 天样本保留 | MonitorStore/MonitorEngine |
| 托盘/状态 | 双击/右键 | 关窗不退出 | StatusForm/TrayApplicationContext |
| 自启 | 托盘开关 | 当前用户 HKCU Run | AutostartManager |
| 数据迁移 | 旧 LocalAppData | 复制缺失，不覆盖不删源 | AppPaths |

通知按告警种类持久化，只记录当前状态和一条待发消息，不是完整事件队列；状态变化可能覆盖未发旧消息。发送与保存不是分布式事务，不能保证 exactly-once。轮询用 SemaphoreSlim，异常按周期隔离；Mutex 名称为 Local\DwhMonitor，不承诺跨所有 Windows 会话全局单实例。

# 3. 运行环境

App/Tests 目标 `net10.0-windows10.0.17763.0`，Core `net10.0`；本次 SDK 10.0.302，路径 `C:\Users\Administrator\.dotnet\dotnet.exe`。没有 global.json 或 packages.lock.json。

| 平台 | 状态 |
| --- | --- |
| Windows x64 | 本次构建/测试/发布通过；需要 WebView2 Runtime |
| Windows 10/11 各具体版本 | 历史使用说明，未逐版本重验 |
| x86/ARM64 | 未验证原生库、DPAPI 与 UI 行为 |
| Linux/macOS | 当前 WinForms App 不支持 |

普通用户 asInvoker，EXE 目录需可写，需交互式登录桌面；自包含版无需另装 .NET，WebView2 仍需单独具备。只出站 HTTPS 443，无监听端口/反向代理。系统 DNS/证书/代理影响联网，无专用代理配置。数据库 UTC，UI/日志本地时区，生产建议核对 UTC+08:00。UTF-8；Program Files 等不可写位置不推荐。开发机需要 SDK，运行机只需完整自包含包及系统依赖。

# 4. 完整依赖库清单

直接依赖版本来自 csproj，不把它们称作锁定的完整依赖树：

| 包 | 声明版本 | 调用位置/用途 |
| --- | --- | --- |
| Microsoft.Web.WebView2 | 1.0.4129.50 | App 浏览器与 Cookie |
| Microsoft.Data.Sqlite | 10.0.10 | Core/MonitorStore |
| Microsoft.NET.Test.Sdk | 17.14.1 | 测试宿主 |
| xunit | 2.9.3 | 测试断言/用例 |
| xunit.runner.visualstudio | 3.1.4 | 测试适配器 |
| coverlet.collector | 6.0.4 | 覆盖率采集 |

完整传递包解析版本见 [DEPENDENCIES](docs/DEPENDENCIES.md)。系统依赖为 Windows、WinForms、DPAPI、注册表、WebView2 和 SQLite 原生库。外部服务为盾网和钉钉，构建依赖 NuGet；没有缓存/队列服务。训练可选 numpy/torch/Pillow，项目未锁版本；C# 运行不需 Python。安装还原与校验见第 6 节，`dotnet clean` 只清编译输出，不随意删除包引用或全局缓存。

离线方案见 [开发文档](docs/DEVELOPMENT.md#13-构建与打包)。未建立隔离断网验收，依赖许可证需另审，不将缓存存在当离线恢复成功。

# 5. 项目结构说明

```bash
DefensX-Notification/
├── DwhMonitor.sln
├── .gitignore
├── README.md
├── docs/                    # API/交接/开发/验证/文件与依赖清单
├── src/DwhMonitor.App/      # 入口、UI、DPAPI、WebView2、OCR
├── src/DwhMonitor.Core/     # 采集、告警、钉钉、SQLite
├── tests/DwhMonitor.Tests/  # xUnit 与 sample-01..50.png
├── tools/ocr/               # 三个研究/训练脚本
└── artifacts/              # 生产 .bin 与元数据 .json
```

[FILES.md](docs/FILES.md) 为逐文件说明与发布边界。手工源码与生成目录分离：bin/obj/publish 不上传；训练 .pt/非生产模型保留本地。根目录规划与研究笔记只留本地并已总结。

运行根为 AppContext.BaseDirectory，config.json、data/monitor.db、logs、WebView2 均在 EXE 旁。验收额外写 data/ocr-acceptance.json，采集模式写 data/ocr-training-temp。没有 PID、上传或业务导出目录。全部配置键、类型、默认、用途和优先级见 [HANDOVER](docs/HANDOVER.md#4-配置与环境变量)。

# 6. 安装部署教程

Windows PowerShell，新建机器进入可写父目录，前置 Git、SDK 和 WebView2：
```powershell
git clone https://github.com/netcre/DefensX-Notification.git
Set-Location .\DefensX-Notification
```

Windows PowerShell，仓库根目录，前置 .NET 10 SDK 与 WebView2 Runtime：
```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet --list-sdks
& $dotnet restore .\DwhMonitor.sln
& $dotnet test .\DwhMonitor.sln -c Release
& $dotnet build .\DwhMonitor.sln -c Release --no-restore
& $dotnet format .\DwhMonitor.sln --verify-no-changes --no-restore
& $dotnet publish .\src\DwhMonitor.App\DwhMonitor.App.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
Get-FileHash .\publish\win-x64\captcha_primary_cnn.bin -Algorithm SHA256
```

启动：`Start-Process .\publish\win-x64\DwhMonitor.App.exe`；复制整个发布目录，不是单个 EXE。数据库自动 CREATE IF NOT EXISTS，无外部初始化账号。开发模式为 `dotnet run --project .\src\DwhMonitor.App\DwhMonitor.App.csproj`。

正式包应发布到新目录，不从运行目录复制打包。首次启动若发现旧 LocalAppData 数据，会复制 config/data/logs/WebView2 的缺失文件，已有目标不覆盖，旧源不删除；“新包”不保证启动后不迁移旧数据。迁移前退出旧进程。

停止从托盘“退出”，确认进程结束才重启。开机自启写 HKCU Run，不是 Windows Service；注销/关机停止，RDP 断开不等于注销。健康/备份/回滚详见交接；未实现容器、systemd、反代或多节点灰度。升级前备份完整运行目录、用新包验证后切换，失败保留现场恢复旧版本和兼容数据。卸载先取消自启、退出，再移走便携目录，保留备份。

# 7. 使用教程

1. 首次启动输入钉钉加签机器人的 HTTPS Webhook 和 secret，“测试钉钉”成功后保存；无机器人配置时等待配置，不正常开始轮询。
2. 用内置 WebView2 手工登录盾网，确认固定目标可见。添加账号是可选功能，默认 AutoLoginEnabled=false。
3. 账号编辑区第一行账号、第二行密码；新增两项必填，已有账号更新时密码留空保留原密码。选择活动账号后验证；切换会清 Cookie/localStorage/sessionStorage/缓存。
4. 双击托盘查看状态。右键菜单：查看状态、重新登录、设置、测试钉钉、打开数据目录、登录 Windows 后自动启动、退出。
5. 可显式启用自动登录。页面原生 value setter 填写账号密码验证码并点击提交，站点自身负责密码变换；每批最多真实提交五次。图片未加载、重复或识别非六位不占提交数，但最多读取十五张。短信、凭据拒绝、页面契约变化需要人工处理。

Windows PowerShell，仓库根目录，已发布/已配置活动账号且先退出托盘：
```powershell
.\publish\win-x64\DwhMonitor.App.exe --ocr-acceptance 20
# 研究模式会写入图片与 DPAPI 标签，不能上传运行目录：
.\publish\win-x64\DwhMonitor.App.exe --ocr-acceptance-capture 20
```
仅接受计数 1–100；其它参数不构成完整 CLI，`--help` 未实现。结果看 `data/ocr-acceptance.json`，后续验收覆盖同名报告，不能只凭退出码判断成功。没有内置批量账号查询、导入导出 API 或定时任务控制台。

# 8. 报错与问题排查

| 现象/错误关键字 | 定位 | 处置及复核 |
| --- | --- | --- |
| SDK 未找到 | dotnet --list-sdks，PATH 可能仅 Runtime | 改用用户目录 SDK，restore/test |
| NU1903 | 本次已出现；见安全章节 | 单独评估升级，不能称零告警 |
| 未检测到 Microsoft Edge WebView2 Runtime | App 初始化日志 | 安装 Evergreen Runtime 后重启 |
| 缺少盾网验证码专用模型 | 检查 EXE 旁 .bin | 重新 publish，核对模型 SHA-256 |
| 验证码模型文件头不正确/张量不完整 | 比对 DWCMODEL1、大小和哈希 | 恢复生产模型，不把 .pt 改名 .bin |
| 会话失效/无可用 Cookie | 上游 401/403、getUserInfo 或本地状态 | 内置页重新登录，再验证完整采样 |
| PageContractChanged | DOM 控件不唯一或过期弹窗 | 人工登录，开发副本检查页面契约 |
| SmsRequired/CredentialRejected | 需人工验证或凭据错误 | 不盲目重试，人工处理后重新验证 |
| 未找到 GUID | 当前第一页 30 条无目标/权限变化 | 检查权限和列表，当前不自动翻页 |
| database is locked | 文件写者、ACL、磁盘 | 退出全部实例，备份后检查 SQLite |
| 钉钉 HTTP/errcode 失败 | secret、URL、时钟、网络 | 托盘测试，核对业务 errcode=0 |
| 日志不增长/进程仍在 | 最新成功记录、磁盘、ACL | 不以托盘存活认定健康，见 Runbook |

已被历史记录和当前测试覆盖的问题：HTTP Timeout 被当退出取消、Token 弹窗、跨盘迁移判断、PreserveNewest 保留旧模型、密码行遮挡。本次未复现磁盘满、证书失效或 SQLite 损坏，这些是建议排查项。没有监听端口，因此端口占用不适用；Linux/macOS 不是当前 App 支持平台。

Windows PowerShell，仓库根目录：
```powershell
Get-PSDrive -PSProvider FileSystem
Get-Acl .\publish\win-x64 | Format-List
Get-TimeZone
Test-NetConnection www.dwhips.com -Port 443
Get-ChildItem .\publish\win-x64\logs -File | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content -Tail 80
```
不要先删数据库或浏览器目录来“排错”，这会丢失历史、去重状态或会话。路径含空格用 -LiteralPath/引号；数据根不受当前工作目录影响。

# 9. 性能优化方案

尚未建立本次性能基线；不能给出 CPU/RAM 最低阈值、延迟或吞吐承诺。历史独立单张 47/50 与最多五次流程 20/20 来自不同口径，不代表今天准确率或耗时。

当前热点：CaptchaPrimaryModel.Recognize 的 C# 卷积、原图/去线双通道与粗到细起点扫描；HTTP 与 Cookie 读取可能阻塞串行轮询。SQLite 各操作开连接，样本与告警状态不在整体事务内。30 秒 PeriodicTimer 不保证慢请求下每轮恰好 30 秒完成。

建议基准：固定机器/模型哈希/WebView2 版本，运行独立新样本验收，保存报告，对 elapsedMilliseconds 算 P50/P95，同时记录提交次数、成功率、CPU/内存、冷启动和热会话。模型批处理/内存复用、数据库事务与连接复用属于建议，须对比相同数据回归，不能给未经测量的提升百分比。

# 10. 安全加固方案

**当前实现**：配置使用 DPAPI CurrentUser，临时文件写入后移动；HTTPS 出站；Cookie 由 WebView2 持久化；FileMonitorLogger 只对钉钉 URL 做正则替换。自动登录告警正文包含账号字段，不能声称所有日志/消息都自动去除个人信息。普通用户拥有目录即拥有数据库、日志和会话的文件访问权，无独立应用角色/数据库密码。

**发布边界**：公开仓库不包含真实运行凭据、Cookie、数据库、WebView2 资料、迁移备份、训练标签或私钥全文/Base64。只记录配置键、来源与用途，不解密并公开生产配置。附件中要求公开这些原文的条目未执行；不把未提取写成已完成。

**本次发现**：NuGet 对 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 发出 `NU1903` 高危告警，构建输出指向 [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)。本次不升级依赖或改业务代码。建议维护者单独升级并回归 SQLite、离线依赖和自包含发布，不能把测试通过等同无漏洞。

**建议方案**：限制数据目录 ACL、加密备份、轮换钉钉 secret 与账号密码、建立依赖锁和漏洞检查、签名制品、日志字段分级。DPAPI 不能防同用户恶意进程；换机重新配置。未发现代码签名、SBOM、独立审计、速率限制或启动防篡改校验。

# 11. 项目对应全套反编译工具

本项目为 C# 托管程序集 + 原生 .NET apphost + DWCMODEL1 模型；优先源码与 PDB。

| 类别 | 适用工具/来源 | 入口与边界 |
| --- | --- | --- |
| 源码/静态 | .NET SDK build/format | 当前已验证，见部署命令 |
| IL 反编译 | ILSpy/ilspycmd .NET 工具包 | 分析 App.dll/Core.dll，不把 EXE apphost 当业务主体 |
| 动态调试 | Visual Studio 托管调试器 | Program/Main、PollOnceAsync、WebView2 事件；未在本次执行 |
| 内存分析 | dotnet-dump .NET 工具包 | 隔离实例转储；可能含访问材料，不上传 |
| 协议观察 | WebView2 开发者工具/测试 HttpMessageHandler | 优先对照请求/DTO 测试，不保存生产正文 |
| 模型检查 | BinaryReader/Get-FileHash | 首 9 字节 DWCMODEL1 和张量长度 |

未发现壳/混淆/安装包/镜像/固件，因此脱壳、去混淆和容器解包不适用。可选工具未安装验证，版本兼容需实际检查，不堆砌无关原生逆向工具。

# 12. 完整逆向分析教程

以下为隔离副本的源码辅助分析方法，不操作生产会话；可选工具流程本次未执行。

Windows PowerShell，仓库根目录，前置 .NET SDK 和已发布 DLL：
```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
New-Item -ItemType Directory -Path .\analysis\original -Force | Out-Null
Copy-Item .\publish\win-x64\DwhMonitor.App.dll,.\publish\win-x64\DwhMonitor.Core.dll .\analysis\original
Get-ChildItem .\analysis\original -File | Get-FileHash -Algorithm SHA256
& $dotnet tool install ilspycmd --tool-path .\analysis\tools
& .\analysis\tools\ilspycmd.exe -p -o .\analysis\decompiled .\analysis\original\DwhMonitor.Core.dll
```

从 Program/Main → TrayApplicationContext/InitializeAsync → MonitorEngine/PollOnceAsync 阅读；协议看 DwhApiClient，状态看 AlertStateMachine，存储看 MonitorStore，配置看 AppSettingsStore。反编译项目不保证直接重构为原项目，以原始源码重新构建。

模型格式由 CaptchaPrimaryModel 明确：9 字节头、int32 张量数、UTF-8 名称长度/名称、rank/维度、float32 数据。模型 .pt 不是 .bin。DingTalkSigner 为 HMAC-SHA256，详见 API；没有独立客户端授权算法。DPAPI entropy 不能独立解密用户配置，不公开生产密文/明文。

在隔离开发环境断点检查取消状态、轮询异常和 UI 调度；改源码后模块测试、全量测试、发布新目录、核对模型哈希，以真实采样/通知判断成功。没有现成反调试或签名加载器，不能虚构其绕过步骤。

# 13. 防逆向与防反编译保护方案

当前未启用混淆、NativeAOT、反调试/反注入、制品签名、SBOM 或启动哈希检查。App PublishTrimmed=false，直接开启裁剪不等于安全加固且可能破坏 UI/依赖。

建议先做 ACL、加密备份、密钥轮换、制品独立签名与 PDB 分离，再评估 IL 混淆。保留序列化字段名、UI/JavaScript 契约与测试访问；对 WebView2、DPAPI、SQLite、模型一致性做回归。核心原生化/商业保护会增加架构和更新成本，无法绝对阻止逆向。程序内可修改的哈希不能独立构成信任根；长期秘密和授权应以外部服务为边界。

# 14. 更新日志

本地此前没有 .git、Tag 或 CHANGELOG，**无法从现有材料恢复历史版本**，不能虚构 v1.0.0 的发布日期。1.0.0 仅为当前 csproj 声明。

| 日期 | 变化 | 证据 |
| --- | --- | --- |
| 2026-08-12 | 初始托盘/采集/通知 | progress 历史记录 |
| 2026-08-13 | Timeout 与轮询守护 | 记录 + 当前测试 |
| 2026-08-15 | 账号、CNN、五次流程、Token 弹窗 | 记录 + 当前实现 |
| 2026-08-29 | 运行目录迁移与干净包 | 记录 + 当前 AppPaths |
| 2026-10-24 | 文档整理与源码上传验收 | VERIFICATION.md |

后续变更记录建议固定版本、日期、功能/修复、破坏性变更、迁移/回滚条件、测试和制品哈希。本次未创建 GitHub Release 或版本标签。

# 15. 开源协议与使用声明

**当前仓库未提供许可证文件**；不擅自指定 MIT、Apache 或 GPL。公开源码不自动授权任何商用/分发方式。NuGet、字体来源、模型和固定样本各自的许可需维护者审定；本次未做完整法律核验。

本工具不是盾网/钉钉官方发布。外部服务账号、消息接收者、数据留存由使用者维护，禁止将私密运行资料附到公开问题。问题通过本仓库 Issues 提交环境、版本、错误和复现；没有独立安全联系邮箱或贡献协议，不虚构联系人。贡献前对齐维护者许可与发布安排。

# 16. API 接口总览

[完整 API 手册](docs/API.md)。不存在本地 HTTP/GraphQL/gRPC/WebSocket 服务。实际协议：

| 类型 | 路径/合约 | 用途 |
| --- | --- | --- |
| 上游 GET | /api/user/getUserInfo | Cookie 会话 |
| 上游 POST | /api/netbar/netbarlist | 第一页 30 条采样 |
| 页面上游 POST | /api/base/captcha、/api/base/login | 页面原生请求，不是公开客户端 REST 服务 |
| 出站 Webhook | 配置的钉钉 URL | 毫秒时间戳/HMAC、Markdown |
| C# 合约 | IDwhApiClient/IMonitorStore/INotificationSink/ICookieProvider | 内部依赖替换 |
| 文件/诊断 | config.json、monitor.db、--ocr-acceptance | 本地持久化和验收 |

认证从内置 WebView2 登录取得，无自建角色/Token 刷新端点、API 版本路由。参数/错误/副作用见独立手册；最小可执行无网络示例在 API 快速开始。

# 17. 团队运维交接摘要

[完整运维交接](docs/HANDOVER.md)。单交互式用户 + 托盘 + WebView2 子进程 + SQLite，外连盾网和钉钉。运行目录、配置默认与日志策略均有证据；本次未启动正式应用或执行恢复演练。

每天检查路径/进程、最近成功采样、连续错误和钉钉待发送状态；升级前停机并完整备份。固定 GUID、严格 o>mc、动态额度、串行状态机和单活动账号是关键不变量。没有独立 Prometheus/仪表盘/值班制度；负责人、SLA、容量阈值未确认。历史 PID 和健康时间只能用于复盘。

# 18. 二次开发摘要

[完整二次开发手册](docs/DEVELOPMENT.md)。App 组装 Core，Python 仅训练；生产运行使用 C# 模型加载。现状是 .NET 标准命名、异步 CancellationToken、xUnit 测试，没有插件目录或稳定 SDK 兼容承诺。

新增通知实现 INotificationSink；配置改 StoredAppSettings + UI/DPAPI 测试；数据库需显式迁移设计，当前 CREATE IF NOT EXISTS 不会自动加列。新增后台任务必须接入退出令牌和串行锁。开发闭环：明确需求 → 源码/测试 → 模块测试 → 全量 Release test → format verify → 新目录 publish → 人工采样/通知验证 → 同步 README/API/HANDOVER/DEVELOPMENT。
