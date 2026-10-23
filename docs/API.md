# API 与协议手册

[项目说明](../README.md) · [API](API.md) · [交接](HANDOVER.md) · [开发](DEVELOPMENT.md)

[1. 文档范围与接口盘点](#1-文档范围与接口盘点) · [2. 快速开始](#2-快速开始) · [3. 通用约定](#3-通用约定) · [4. 接口索引](#4-接口索引) · [5. 接口逐项详解](#5-接口逐项详解) · [6. 实时与异步接口](#6-实时与异步接口) · [7. SDK、CLI 与内部调用接口](#7-sdkcli-与内部调用接口) · [8. 数据模型](#8-数据模型) · [9. 调用场景](#9-调用场景) · [10. 版本与兼容](#10-版本与兼容)

# 1. 文档范围与接口盘点

无自建服务路由。本手册盘点 **5 条出站协议流程**：会话 GET、列表 POST、页面验证码 POST、页面登录 POST、钉钉 Webhook；前两条和钉钉由 C# 明确构造，另两条由网页负责，证据边界分别标注。另有内部 C# 合约、两种诊断模式和本地文件格式。没有 GraphQL/gRPC/IPC/入站回调或 WebSocket/SSE。

入口 Program.cs，组装 TrayApplicationContext.cs，协议在 DwhApiClient.cs、SessionWebViewForm.cs、DingTalkNotifier.cs。

# 2. 快速开始

本程序不是 HTTP 服务，无本地监听地址、/health、Swagger 或 Token 颁发端点。外部上游基础地址为 `https://www.dwhips.com`。通过 EXE 设置钉钉、内置页登录，再观察采样。

Windows PowerShell，已构建仓库根目录，前置用户目录 .NET SDK；下面是可执行、无网络、无凭据的解析调用示例：
```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet test .\tests\DwhMonitor.Tests\DwhMonitor.Tests.csproj -c Release --filter FullyQualifiedName~DwhApiClientTests.ParseSnapshotJson_MapsVerifiedFieldsAndMatchesLongGuid
```
成功标准是该用例通过，字段映射与 GUID 精确匹配；这不证明线上访问成功。真实首次接入在内置页完成 Cookie 会话，程序再请求 getUserInfo 和 netbarlist。Bash/cURL 不作为 Windows DPAPI/WebView2 会话获取方案；本手册不虚构 Linux 客户端。

# 3. 通用约定

UTF-8 JSON，Cookie 为站点会话认证，当前客户端无 RBAC、OAuth 刷新服务或自建 API 版本。HTTP 客户端由 Tray 初始化为 10 秒超时；Cookie 读取 10 秒，取消传递到调用链。无 traceId、通用错误 envelope、请求体上传上限或幂等键。列表固定 page=1/pageSize=30，无动态搜索/排序/字段选择功能。

上游 code 可为数字/整数字符串；getUserInfo 业务失败按会话失效，列表按 msg 关键词区分会话/其他错误。钉钉只接受数字 errcode=0。完整服务端错误码/限流和 token 有效期未确认，需根据实际上游响应核对，不编造。

并发由 MonitorEngine 串行控制，异步取消不等于业务失败；应用退出取消和请求 Timeout 区分。无全局 HTTP 响应码转换，调用者得到类型化异常或 NotificationResult。

# 4. 接口索引

| 接口 | 方法/签名 | 权限/用途 | 源码 |
| --- | --- | --- | --- |
| 会话 | GET /api/user/getUserInfo | Cookie/会话有效性 | DwhApiClient |
| 列表 | POST /api/netbar/netbarlist | Cookie/固定目标采样 | DwhApiClient |
| 验证码 | 页面 POST /api/base/captcha | 站点页面/验证码 | SessionWebViewForm + 历史发现 |
| 登录 | 页面 POST /api/base/login | 站点页面/登录 | SessionWebViewForm |
| 消息 | POST 配置的 HTTPS Webhook | HMAC-SHA256/钉钉 | DingTalkNotifier |
| 诊断 | --ocr-acceptance[-capture] N | 本地活动账号/验收 | Program/OcrAcceptanceApplicationContext |

内部方法索引与 DTO 字段详见第 7/8 节及 SOURCE-API。

# 5. 接口逐项详解

## 会话检查

**调用**：`Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken)`；`CheckSessionAsync` 包装该方法并丢弃返回值。源码 `src/DwhMonitor.Core/DwhApiClient.cs`。

**协议/认证**：GET `https://www.dwhips.com/api/user/getUserInfo`，Cookie Header（非空，来自 WebView2），Accept `application/json, text/plain, */*`，Referer 为站点根；Tray 中 User-Agent=DwhMonitor/1.0。无 Path/Query/Body，未实现本地角色鉴权，上游权限由会话决定。

| 参数 | 类型/必填/默认 | 校验 |
| --- | --- | --- |
| cookieHeader | string/是/无 | 空白抛 SessionExpiredException |
| cancellationToken | CancellationToken/是/无 | 控制请求取消 |

**成功**：HTTP 成功且 `code=0` 且存在 `data`。递归寻找 username/userName/account/phone/mobile 字符串作为 Account，找不到可为 null；IsValid=true。示意结构（合成示例，不是线上快照）：
```json
{"code":0,"data":{"username":"example-user"},"msg":"成功"}
```
**错误**：HTTP 401/403 → SessionExpiredException；code!=0 或缺 data → SessionExpiredException；无效 JSON/code 缺失/格式无效 → InvalidDataException；其他 HTTP 失败 → HttpRequestException；Timeout/取消向调用层传播。code 接受 int 或可解析整数字符串。

**副作用/并发/日志**：该方法自身不写数据库/文件，不自动刷新会话、不重试；调用者 MonitorEngine 串行并将失败转为采样/告警。测试 `GetSessionInfoAsync_ExtractsNestedAccount`。凭据不写入示例。

## 网吧列表

**调用**：`Task<DwhSnapshot> GetSnapshotAsync(string cookieHeader, DateTimeOffset sampledAtUtc, CancellationToken cancellationToken)`；纯解析 `static DwhSnapshot ParseSnapshotJson(string json, long targetGuid, DateTimeOffset sampledAtUtc)`。源码 `src/DwhMonitor.Core/DwhApiClient.cs`。

**协议/认证**：POST `/api/netbar/netbarlist`，同会话 Cookie/Accept/Referer，Content-Type application/json；没有路径参数。完整请求体为实现中的固定值：
```json
{"page":1,"pageSize":30,"id":0,"preload":["NetbarGroup","SoftPacket"],"osearch":{},"asearch":{},"admins":{}}
```

| 字段 | 类型/默认 | 当前范围 |
| --- | --- | --- |
| page | int/1 | 固定首个页面，不遍历 |
| pageSize | int/30 | 固定，未验证服务端最大值 |
| id | int/0 | 固定 |
| preload | string[]/两项 | NetbarGroup、SoftPacket |
| osearch/asearch/admins | object/{} | 空过滤结构 |
| cookieHeader | string/无 | 必须非空 |
| sampledAtUtc | DateTimeOffset/无 | 调用层提供采样时间，不从服务端读取 |

返回匹配 long GUID 的首条记录。合成示例与测试形状一致：
```json
{"code":0,"data":{"list":[{"netbar_guid":2547092390,"netbar_name":"云机房","status":true,"o":998,"t":4293,"mc":1650,"d":10528}]},"msg":"查询成功"}
```

| 响应键 | 类型 | DwhSnapshot 字段/用途 |
| --- | --- | --- |
| netbar_guid | int64 | TargetGuid/精确匹配 |
| netbar_name | string | NetbarName |
| status | bool | Online |
| o | int32 | TerminalCurrent |
| t | int32 | TerminalTotal |
| mc | int32 | VipLimit/每轮动态比较 |
| d | int32 | DefenseCount |

数值字段要求 JSON 数字，不接收字符串数字；status 要求布尔，不接收 0/1。未实现额外正数/长度范围校验。部分非目标行缺 GUID 也会抛错，不保证跳过损坏行。

**失败**：401/403 会话异常；code!=0 且 msg 含 登录/会话/token → SessionExpiredException，否则 InvalidDataException；缺 data.list/字段/类型异常为 InvalidDataException 或 JSON 类型读取异常；目标缺失 TargetNotFoundException；HTTP、无效 JSON、网络与取消同前。不存在完整服务端业务码清单，不编造其含义。

**副作用**：本方法无本地写入；MonitorEngine 完整成功后写 samples、更新告警状态、清理过期样本并发送待发通知。接口本身不重试、不自动翻页；调用层下一周期继续。测试 ParseSnapshotJson_MapsVerifiedFieldsAndMatchesLongGuid/ThrowsWhenTargetIsMissing/RejectsInvalidJson。

## 页面验证码

**类型**：盾网页面上游 `POST /api/base/captcha`。本程序通过页面图片 DOM 读取 Data URI，不构造此请求；历史 findings 记录上游返回 picPath/captchaId/captchaLength/openCaptcha，当前源码没有完整请求体 DTO，不能视作已验证公共 API。

输入由站点页面生成；Body/服务端限流/身份绑定未确认，检查范围 SessionWebViewForm 与历史记录。输出为验证码图（历史 240×80 六位）；C# Recognize 仅接收 240×80 PNG 并要求六位数字结果。前端控件 img[alt="点击刷新验证码"] 必须唯一，重复图刷新不计提交；自动换图等 800ms，点击后最多等 5 秒。

失败：图片非 Data URI/拒识可刷新；控件缺失或重复 PageContractChanged，5 秒无新图 NetworkUnavailable。读取图片本身无数据库写入；capture 验收可把图片和 DPAPI 标签写本地。未在本次在线调用。验证见 CaptchaRetryCoordinatorTests、CaptchaRecognizerTests。

## 页面登录

**类型**：盾网页面上游 `POST /api/base/login`。当前实现是原生 DOM 填写并点击页面按钮，不存在 C# 直接协议登录 SDK；不要把明文密码直接当已知请求算法。

历史资料记录 username/password/captcha/captchaId，短信分支 code；站点页面负责密码变换和请求生成，其完整约束未在 C# 项目定义。登录响应监听仅匹配含 `/api/base/login` 的 URI，读取 code（数字或整数字符串）、msg、data.needcode。code=303 或 needcode=true 需短信；成功还会检查 getUserInfo，会话验证成功才算流程成功。

失败按 AutoLoginFailureKind：CredentialRejected、CaptchaExhausted、NetworkUnavailable、SmsRequired、PageContractChanged、UnexpectedResponse。业务错误通过 msg 文本分类，不虚构其他数值错误码。登录响应等待 12 秒，页面契约等待 15 秒；验证码实际提交最大五次且图片需变化，凭据/短信/页面问题人工接管。

副作用包括站点会话建立、Cookie/localStorage/sessionStorage 更新，切换账号和启动恢复会清站点数据；因此此流程不是无副作用探针。日志只记录计数与摘要，但自动登录告警包含账号字段。验证 CaptchaRetryCoordinatorTests、LoginPageRecoveryTests；线上登录未在本次执行。

## 钉钉消息

**调用**：`Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken)`，`static Uri DingTalkSigner.BuildSignedUri(string webhook, string secret, long timestampMilliseconds)`。源码 `src/DwhMonitor.Core/DingTalkNotifier.cs`。

webhook 为有效绝对 HTTPS URI，secret 非空，timestamp 为 UTC Unix 毫秒；输入 `timestamp + "\n" + secret`，以 secret UTF-8 为 key 执行 HMAC-SHA256，再 Base64、URL 编码；将 timestamp/sign 追加到已有 Query 后。URL 来自用户设置，不在仓库固定真实机器人。

Body（合成消息示例）：
```json
{"msgtype":"markdown","markdown":{"title":"[盾网监控] 配置测试","text":"测试消息"}}
```
Title/MarkdownBody 为 string，无项目自定义长度检查。Content-Type 为 application/json UTF-8。HTTP 成功且 errcode 为数字 0 才成功；缺 errcode 当 -1，其他码返回带 errmsg 的 Failed；不是 HTTP 200 就算消息投递完成。无凭据返回 Failed。非取消异常被捕获为 Failed；OperationCanceledException（包括 HttpClient 超时）向上传播，这是未单独做发送超时回归的边界。

外部发送有真实副作用；状态机在 30 秒、1 分、2 分、5 分上限退避，成功后保存状态，不保证严格一次投递。无出站消息 ID 与审计 traceId。测试 BuildSignedUri_UsesDingTalkHmacSha256Formula、SendAsync_PostsMarkdownAndAcceptsErrCodeZero；真实发送未执行。

# 6. 实时与异步接口

没有 WebSocket/SSE、消息订阅、入站 Webhook、队列/RPC topic 或 ack。钉钉是客户端出站调用，不需要提供公网回调服务。

MonitorEngine 使用 PeriodicTimer 和 SemaphoreSlim，默认 30 秒串行轮询，采样后更新状态和待发送；同条件持续不重复告警，恢复一次。状态不变时不会更新排队消息为最新采样，不能等价为事件日志。后台任务无独立补跑 CLI；退出应用即取消。

# 7. SDK、CLI 与内部调用接口

App 内部类不是稳定 SDK；以下合约来自当前源码，没有二进制兼容保证。完整导出签名与 DTO 见 [SOURCE-API](SOURCE-API.md)。

| 合约 | 参数/返回/副作用 | 并发与错误 |
| --- | --- | --- |
| ICookieProvider.GetCookieHeaderAsync | token → string?；WebView2 Cookie | UI 调度，读取上限 10 秒 |
| INotificationSink.SendAsync | NotificationMessage/token → NotificationResult；外部消息 | 调用层重试，不保证幂等 |
| IMonitorStore.InitializeAsync | token → Task；CREATE IF NOT EXISTS | 无版本化迁移 |
| InsertSampleAsync | SampleRecord/token → Task；插入采样 | 参数化 SQL；异常向上传 |
| GetLastSuccessfulSnapshotAsync | token → DwhSnapshot? | error_kind IS NULL，按 id 倒序 |
| LoadAlertStatesAsync | token → 字典 | 跳过未知 AlertKind |
| SaveAlertStateAsync | AlertState/token → Task；UPSERT | 单键更新，无整体轮询事务 |
| CleanupSamplesAsync | cutoffUtc/token → 删除条数 | 只删 samples 的早期记录 |
| MonitorEngine.RunAsync | interval/token → 循环 Task | interval>0，退出取消才结束，隔离周期异常 |
| PollOnceAsync | token → Task | SemaphoreSlim 串行；采样/状态/消息 |
| SetAutoLoginFailureAsync | active/account/error/token → Task | 同 poll lock；独立自动登录状态 |
| AlertStateMachine.ApplyCondition | current/kind/active/now/message → AlertState | 纯状态转换，不直接 IO |
| MarkSent/MarkFailed | state/now → AlertState | 纯函数；重试上限 5 分钟 |

CLI：`--ocr-acceptance N`、`--ocr-acceptance-capture N`，N=1..100 且参数总数为两项，WinExe 不提供标准 JSON stdout 或稳定业务退出码；不符合参数形状会按托盘模式启动。注册表 `--autostart` 也走正常托盘流程。验收报告需单独读取，不以退出码代替结果。

# 8. 数据模型

DTO 与持久化实体见 [SOURCE-API](SOURCE-API.md)；数据库完整 DDL 见 [交接](HANDOVER.md#5-数据库与持久化)。

DwhSnapshot 是成功采样（DateTimeOffset、long GUID、string 名称、bool Online、四个 int 计数）；SampleRecord 允许失败时字段 null，并附 AuthOk/ErrorKind/ErrorMessage。NotificationMessage 含 Title/MarkdownBody；NotificationResult 含 Success/Error。AlertKind 为 TerminalExceeded、SessionExpired、NetbarOffline、ApiUnavailable、AutoLoginFailed。AlertState 存 Active/首次/变化/通知时间与 pending/retry；MonitorRuntimeStatus 表示 UI 运行状态，不是 HTTP 响应。

日期为 UTC ISO-8601 存储、UI 本地展示；数据库 bool 为 0/1、可空字段 DBNull。error_message 存储前最多截到 1000 字符。无金额、货币精度或文件上传字段。

config.json 的 DPAPI 字段与默认值见交接第 4 节。生产模型 .bin 格式见 README 分析教程；OcrAcceptanceReport 为 camelCase JSON：startedUtc/updatedUtc/modelSha256/requestedTrials/completedTrials/successfulTrials/successRate/abortReason/trials。每轮包含 number/success/outcome/submittedCaptchaAttempts/captchaRefreshes/uniqueCaptchaImages/elapsedMilliseconds/completedUtc。

# 9. 调用场景

首次设置机器人 → 测试保存 → WebView2 登录 → Cookie → getUserInfo → netbarlist → GUID 精确匹配 → samples → 比较 o>mc/status → 状态落盘 → 钉钉业务成功后标记已发送。

失效时清理旧状态、导航登录页、唯一 Token 弹窗“重新登录”恢复；活动账号自动登录要显式启用；短信人工。真实登录本次未执行。

接口连续三次失败才报 ApiUnavailable；会话掉线立即独立告警。完整采样成功恢复接口不可达；o<=mc 恢复超额。不存在文件上传下载、分页全量遍历、批量账号或实时订阅能力，不提供虚构调用。

# 10. 版本与兼容

App 当前声明 1.0.0，上游 URI 无 /v1 前缀。没有正式语义版本/废弃周期/稳定 SDK 承诺；改字段名、异常类型、固定目标、DPAPI entropy、模型结构或 DB Schema 均可能破坏兼容。

有旧 v1 钉钉 DPAPI 解密回退，但账号使用 v2；不要擅改 entropy。SQLite CREATE IF NOT EXISTS 不会添加字段，必须单独设计前向迁移与旧版回滚。上游服务完整 Schema/限流/生命周期未确认。固定第一页限制和页面 DOM 依赖见 README 已知限制。
