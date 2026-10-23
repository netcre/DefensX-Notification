# 二次开发手册

[项目说明](../README.md) · [API](API.md) · [交接](HANDOVER.md) · [开发](DEVELOPMENT.md)

[1. 开发者快速开始](#1-开发者快速开始) · [2. 技术栈全景](#2-技术栈全景) · [3. 架构与设计](#3-架构与设计) · [4. 源码导读](#4-源码导读) · [5. 本地配置与调试](#5-本地配置与调试) · [6. 编码规范](#6-编码规范) · [7. 扩展点总览](#7-扩展点总览) · [8. 新增业务模块教程](#8-新增业务模块教程) · [9. 新增 API 接口教程](#9-新增-api-接口教程) · [10. 数据库二次开发](#10-数据库二次开发) · [11. 后台任务与异步开发](#11-后台任务与异步开发) · [12. 测试体系](#12-测试体系) · [13. 构建与打包](#13-构建与打包) · [14. 兼容性与版本演进](#14-兼容性与版本演进) · [15. 贡献与发布流程](#15-贡献与发布流程) · [16. 二次开发完整示例](#16-二次开发完整示例)

# 1. 开发者快速开始

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

开发运行 `& $dotnet run --project .\src\DwhMonitor.App\DwhMonitor.App.csproj`。调试与正式实例共享 Local Mutex，先退出已有实例，避免影响运行账号。测试用例使用 Fake client、临时 SQLite 与合成账号，不要求生产凭据；手工启动 App 仍需机器人配置/网页登录。项目未提供 IDE 配置或第三方插件清单。

# 2. 技术栈全景

C# / .NET 10 / WinForms；Core 用 System.Net.Http/System.Text.Json/Microsoft.Data.Sqlite；App 用 WebView2/DPAPI/System.Drawing/注册表。语言版本未显式指定，随 SDK 默认；SDK 当前验证10.0.302，不存在 global.json。

直接和传递 NuGet 版本见 [DEPENDENCIES](DEPENDENCIES.md)，外部服务/系统要求见 README。训练脚本 Python + numpy/torch/Pillow，无 requirements 锁；C# 生产推理不依赖这些库。项目没有前端 npm 构建、Web 后端、Redis/MQ/ORM。

# 3. 架构与设计

```text
Program.Main (WinForms / Local\DwhMonitor Mutex)
  └─ TrayApplicationContext (组装依赖、设置、生命周期)
      ├─ SessionWebViewForm → WebView2 → 盾网页面登录 / Cookie
      ├─ CaptchaRecognizer → CaptchaPrimaryModel → captcha_primary_cnn.bin
      ├─ MonitorEngine → IDwhApiClient → 盾网 JSON API
      │                 ├─ IMonitorStore → SQLite WAL
      │                 └─ INotificationSink → 钉钉 HTTPS Webhook
      └─ StatusForm / SettingsForm / FileMonitorLogger / AutostartManager
```
Core 不依赖 App。Python 训练器只在开发阶段运行，生产推理无 Python/Tesseract/ONNX Runtime 依赖。没有 Redis、MQ、远端 SQL、容器或监听端口。
启动、数据流与故障传播见交接。MonitorEngine 的 poll lock 同时串行轮询和自动登录失败状态更新。状态仅当前值与待发信息；采样/状态/发送不在整体事务，不可假设 exactly-once。不变量：固定 long GUID、严格 o>mc、mc 动态读取、五次真实提交、单活动账号与不同验证码要求。

# 4. 源码导读

建议顺序：Program → TrayApplicationContext → Models → DwhApiClient → MonitorEngine → AlertStateMachine → MonitorStore → DingTalkNotifier → AppSettingsStore/AppPaths → SessionWebViewForm/CaptchaRetryCoordinator → CaptchaPrimaryModel → UI/Tests。

[FILES](FILES.md) 为逐文件归属；[SOURCE-API](SOURCE-API.md) 给出准确声明。Properties/AssemblyInfo.cs 的测试友元是实现边界，不是外部插件授权。bin/obj 是生成代码/产物，不修改；artifacts 生产模型是生成资源，修改须对应训练与元数据。

# 5. 本地配置与调试

配置位于 EXE 旁的 `config.json`，AppSettingsStore 读取；不从当前工作目录寻找。缺失文件用默认配置；JSON 解析失败回退默认值，单个损坏账号密文被忽略。没有项目专用环境变量覆盖层、命令行配置覆盖或热加载；UI 保存更新内存并写文件，外部编辑需重启。

| JSON 键 | 类型/默认 | 必填与敏感性 | 生效点 |
| --- | --- | --- | --- |
| ProtectedWebhook | string/null | 发通知必填；DPAPI 密文 | GetCredentials → DingTalkNotifier |
| ProtectedSecret | string/null | 发通知必填；DPAPI 密文 | GetCredentials → DingTalkSigner |
| AutoStartEnabled | bool/true | 可选；普通 | ApplyAutostartSetting |
| AutoLoginEnabled | bool/false | 可选；有账号才可启用 | 自动登录控制器 |
| ActiveAccountId | string/null | 可选；无效时取首个可读账号 | GetActiveLoginAccount |
| LoginAccounts | array/null | 可选；敏感容器 | GetLoginAccounts |
| LoginAccounts[].Id | string/无默认 | 非空；本地标识 | 活动账号匹配 |
| LoginAccounts[].ProtectedUsername | string/无默认 | 账号必填；DPAPI | 解密后页面填写 |
| LoginAccounts[].ProtectedPassword | string/无默认 | 账号必填；DPAPI | 解密后页面填写 |
| LoginAccounts[].LastVerifiedUtc | ISO-8601/null | 可选；验证时间 | MarkLoginAccountVerified |

环境来源仅见系统 API 的 LocalApplicationData（旧根）、AppContext.BaseDirectory（新根）、Environment.ProcessPath（自启路径）。没有 `.env` 读取器。DPAPI 使用 CurrentUser、entropy `DwhMonitor.Settings.v2`；旧版钉钉配置兼容 `DwhMonitor.Settings.v1`。entropy 不是独立解密密钥，跨机/跨用户复制密文不保证可用。

业务默认常量不是 JSON 配置：BaseUrl=`https://www.dwhips.com`，TargetGuid=`2547092390`，PollIntervalSeconds=30，ApiFailureThreshold=3，HistoryRetentionDays=90，源为 `src/DwhMonitor.Core/Models.cs`。需修改源码并测试才能改变。HttpClient 超时 10 秒、Cookie 读取 10 秒、登录响应 12 秒、页面契约等待 15 秒、验证码自动换图等待 800ms/主动刷新最多 5 秒，均为代码默认而非环境变量。
断点：InitializeAsync/PollOnceAsync/HandleApiFailureAsync、GetSessionInfoAsync、LoginPageRecovery.Decide、CaptchaRetryCoordinator.RunAsync、SqliteMonitorStore.SaveAlertStateAsync。只在隔离实例测试，会话切换有清理副作用。没有热重载配置服务或 Mock 服务器；TestSupport.cs 的 FakeDwhApiClient/FakeCookieProvider/FakeNotificationSink/FixedClock/InMemoryMonitorStore 是现成测试替身。UI 问题参考 SettingsFormLayoutTests。

# 6. 编码规范

以下为现状归纳，不是未发现的团队规范：C# file-scoped namespace、PascalCase 公有成员、_camelCase 字段、nullable enable、implicit usings、异步方法 Async 后缀并传 CancellationToken、record 传递状态。Core 面向接口，App 手工构造依赖，无 DI 容器。

保持异常类型与取消边界：仅退出 token 取消才终止循环，不把 HttpClient Timeout 一律当退出。外部服务错误转换为会话异常/数据异常或 NotificationResult；日志不保存凭据正文。using/await using 释放请求/连接。格式以 dotnet format --verify-no-changes 为准，没有独立 .editorconfig/eslint。

# 7. 扩展点总览

| 扩展点 | 入口/接口 | 注册/生命周期 | 示例与兼容 |
| --- | --- | --- | --- |
| 通知渠道 | INotificationSink | Tray 初始化构造，随应用使用 | DingTalkNotifier；保持 Result/取消语义 |
| 采集协议 | IDwhApiClient | Tray 构造、传入 Engine | DwhApiClient；保留会话/采样异常 |
| 会话来源 | ICookieProvider | SessionWebViewForm | UI 线程与超时边界 |
| 存储 | IMonitorStore | Engine 初始化 | SqliteMonitorStore；去重/历史兼容 |
| 时钟 | IClock | 构造可选参数 | FixedClock 稳定测试 |
| 日志 | IMonitorLogger | Engine 构造 | FileMonitorLogger/NullMonitorLogger |

这些是可替换依赖接缝，不是运行时插件加载器。App 内部类、DOM 选择器和模型张量名是实现点，不承诺稳定 ABI。

# 8. 新增业务模块教程

最小侵入路径：在 src/DwhMonitor.Core 新文件定义接口/record/业务类，禁止引用 WinForms；在 App/TrayApplicationContext.InitializeAsync 组装，若需 UI 添加到 App。参数检查/取消/错误/日志与现有模式一致；Tests 新增相同模块 Tests.cs，使用临时时钟/存储和 Fake 服务。

涉及数据库先见第 10 节，涉及权限使用上游会话而非虚构本地角色。新模块测试先红后绿，随后全量测试、格式、发布到新目录，保留采样与告警回归。完整通知装饰器示例见第 16 节。

# 9. 新增 API 接口教程

当前不是 Web 后端，没有可注册的 HTTP 控制器。如果新增盾网上游调用，应在 IDwhApiClient 添加签名、DwhApiClient 使用 HttpRequestMessage、DTO/JsonDocument 严格解析，复用 Cookie/取消/HTTP错误分类；在测试使用 HttpMessageHandler 替身，不依赖真实账号。

逐项更新 API.md 的索引/独立详解：方法、URI、认证、参数类型/默认、响应、业务错误、副作用、重试与测试。不得为了新增一个调用悄悄引入监听端口；确需服务化属于新架构，另设计鉴权、限流、审计与生命周期。

# 10. 数据库二次开发

现有初始化见 HANDOVER DDL：CREATE IF NOT EXISTS，不会修改旧表。新增表可在 InitializeAsync 同步 DDL；新增字段需要先在临时旧版数据库副本设计迁移，检查 PRAGMA table_info，事务 ALTER TABLE，再更新 reader ordinal/INSERT/UPSERT 与 SampleRecord/AlertState。

现状无迁移 CLI、Down 脚本或 ORM；因此不能提供一个不存在的 migrate 命令。可复现开发检查是在临时 DB 的 xUnit 中创建旧 Schema → 初始化/迁移 → PRAGMA 校验 → 读写断言 → 再次初始化幂等 → 旧版读取兼容。大表变更先测副本耗时与锁；回滚不保证可删字段，优先向前兼容或完整备份回退。

# 11. 后台任务与异步开发

现有任务是 MonitorEngine.RunAsync 的 PeriodicTimer、自动登录协调和随成功轮询触发的清理，无 MQ worker/死信/分布式锁。新增任务由 Tray 保存 Task 与退出 token，在 ExitAsync 等待终止，避免 async void 除 UI 事件以外传播异常。

共享告警/采样必须沿用串行协调，不另起并发写者破坏状态。用 IClock 或可控周期缩短测试，不做 Thread.Sleep 长等待；模拟 Timeout、应用取消和一次异常后继续。人工补偿目前仅重新登录/重启，不存在补采历史接口。

# 12. 测试体系

xUnit：协议解析、GUID/int64、严格超额与动态 mc、持续/恢复/重启去重、连续故障、HTTP Timeout、循环异常隔离、SQLite/90天清理、DPAPI/损坏配置、五次验证码、Token 弹窗和 UI 布局。固定图片参与过训练，只能用于一致性，不能当独立泛化结果。

Windows PowerShell，仓库根目录，前置 SDK：
```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet test .\DwhMonitor.sln -c Release
& $dotnet test .\DwhMonitor.sln -c Release --filter FullyQualifiedName~MonitorEngineTests
& $dotnet test .\DwhMonitor.sln -c Release --collect:'XPlat Code Coverage'
```
本次全量 43/43 通过；覆盖率命令提供但未执行。真实机器人/网页端到端与性能测试是人工验收，不能与 Fake handler 测试等同。最低新增测试覆盖成功、参数边界、取消、外部失败和持久化恢复。

# 13. 构建与打包

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

Debug/Release 均使用相同生产模型，Version=1.0.0；没有 Git SHA 自动注入、签名、清理脚本或可复现构建承诺。模型复制 Always，不能改成 PreserveNewest 并忽略哈希。只验证 win-x64，非 Windows App 不支持，其他架构未测。

离线建议方案（未断网演练）：联网机 `dotnet restore .\DwhMonitor.sln -r win-x64 --packages .\offline-packages`，归档包目录并计算哈希；将 SDK、WebView2 离线介质与源码/模型一同传输。离线机使用 `dotnet restore .\DwhMonitor.sln -r win-x64 --source .\offline-packages --packages .\restored-packages`，然后 --no-restore 构建/发布。SDK runtime packs 是否齐全必须实际断网验证，包目录建议放仓库外，不能上传缓存。

可选训练：先独立 venv，再 `python -m pip install numpy torch Pillow`；这些版本未锁，需要训练人员记录真实环境。脚本执行会从固定上游提交下载字体并覆盖 artifacts 模型/元数据，先备份，不能把重训练当无副作用验证。训练参数见 SOURCE-API 与脚本 --help；生产使用现成 .bin 无需训练。

# 14. 兼容性与版本演进

无既有正式版本策略，建议先记录破坏性变更再决定版本。禁止随意改 JSON 键、DPAPI entropy、AlertKind 文本、SQLite字段、模型 header/张量名、DOM 契约和异常类型。旧 v1 钉钉读取兼容不代表任意旧账号密文兼容。

升级依赖先看清单/传递树/NU1903，再测试/格式/新目录发布/原生 DLL/真实 UI；保留锁与离线环境快照。数据库需旧副本升级测试和可用回退包，不能把反编译输出或模型时间戳当版本权威。

# 15. 贡献与发布流程

当前未发现 CONTRIBUTING、分支保护、CI、签名或许可证；不虚构团队规则。建议独立分支小提交，PR 描述证据/测试/不兼容项，维护者审查后合并。没有许可证时先确认贡献/分发安排。

变更触发四文档同步：功能/配置改 README+HANDOVER；协议/DTO 改 API；扩展/构建/测试改 DEVELOPMENT；发布更新版本、变更日志、制品哈希和验证记录。手工验证成功才宣称上线，不能拿构建成功代替运行。

当前公开上传是初始源码与文档，不等同发行二进制，也未创建 Release/Tag。

# 16. 二次开发完整示例

下面是**建议扩展示例，未写入业务源码**：对现有 INotificationSink 加“不记录消息正文”的发送结果日志装饰器，不改 DB/协议，不增加网络请求。

拟新增 `src/DwhMonitor.Core/LoggingNotificationSink.cs`：
```csharp
namespace DwhMonitor.Core;

public sealed class LoggingNotificationSink(
    INotificationSink inner, IMonitorLogger logger) : INotificationSink
{
    public async Task<NotificationResult> SendAsync(
        NotificationMessage message, CancellationToken cancellationToken)
    {
        var result = await inner.SendAsync(message, cancellationToken);
        logger.Info(result.Success ? "通知发送结果：成功" : "通知发送结果：失败");
        return result;
    }
}
```
在 TrayApplicationContext 构造 MonitorEngine 前用 `new LoggingNotificationSink(notifier, _logger)` 替换传入通知依赖；不改变原 notifier 本身。此为源码组合示例，不是已存在插件名。

拟新增 Tests/LoggingNotificationSinkTests：用 FakeNotificationSink/TestSupport 的日志替身验证成功/失败结果保持一致、只调用一次、不记录 Title/MarkdownBody、取消异常仍传播。先按模块过滤运行，再全量 Release/format/publish；隔离配置测试钉钉且检查日志不含正文。DB无需迁移，配置无新键，通知内容/签名不变。

发布核对真实消息仅一条且采样继续；README 更新“当前是否启用装饰器”，API 更新内部合约，HANDOVER 更新日志行，DEVELOPMENT 保留装配和测试说明。若只有构建成功而未做真实收件验收，不宣称上线成功。
