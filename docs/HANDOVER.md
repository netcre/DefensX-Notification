# 团队运维交接手册

[项目说明](../README.md) · [API](API.md) · [交接](HANDOVER.md) · [开发](DEVELOPMENT.md)

[1. 交接概览](#1-交接概览) · [2. 系统全景](#2-系统全景) · [3. 环境清单](#3-环境清单) · [4. 配置与环境变量](#4-配置与环境变量) · [5. 数据库与持久化](#5-数据库与持久化) · [6. 部署拓扑与运行方式](#6-部署拓扑与运行方式) · [7. 日常运维 Runbook](#7-日常运维-runbook) · [8. 日志、监控与告警](#8-日志监控与告警) · [9. 发布与回滚](#9-发布与回滚) · [10. 故障处理](#10-故障处理) · [11. 权限与安全操作](#11-权限与安全操作) · [12. 已知问题与技术债](#12-已知问题与技术债) · [13. 交接验收清单](#13-交接验收清单)

# 1. 交接概览

目标固定“云机房”及 GUID 2547092390。App 清单 1.0.0，首次 GitHub 源码发布，未创建 Release/Tag。维护负责人/值班名单未提供，不虚构人名。

2026-10-24：Windows x64、SDK 10.0.302，43/43 测试通过，构建/格式/自包含发布通过但 NU1903 告警；真实账号登录/通知、当前生产新鲜度、恢复演练未验证。无服务/容器/远端 DB，属于不适用。详见 VERIFICATION。

# 2. 系统全景

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

单点是 Windows 用户会话、客户端、上游 Cookie/权限与钉钉网络。Core 状态持久化到本地 SQLite，WebView2 浏览器资料是另一份持久状态，失去任一可能影响采样/告警。没有服务端自动容灾。

# 3. 环境清单

本地源码 D:\项目\盾网，既有运行目录 publish\win-x64、历史干净包 publish\完整版；当前是否运行本次未检查。新构建验证包位于本机临时验证目录，不替换正式发布目录。仓库不上传这些运行包。

.NET SDK 10.0.302 用于开发测试，Windows x64 自包含发布；目标需 WebView2。硬件/OS build/网络代理/证书链/容量阈值未确认，需部署人员通过 Get-ComputerInfo、Get-Process、Get-PSDrive 和联通性测试建立基线。

根目录 AppContext.BaseDirectory；外部域名 www.dwhips.com、用户配置钉钉 HTTPS URL；无入站端口。开发/测试用临时 DB/Fake client，生产由 UI 录入真实凭据，不能复制进测试。

# 4. 配置与环境变量

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

轮换：设置中重新填写机器人并测试保存；账号更新后重新验证，切换活动账号会清站点状态。改配置前停机备份，错误时还原同用户兼容备份；不公开运行配置原文。

# 5. 数据库与持久化

数据库为 EXE 旁 data/monitor.db，SqliteConnectionStringBuilder 使用 ReadWriteCreate + Shared Cache。初始化执行 WAL、synchronous=NORMAL 与以下 DDL（源码原文）：
```sql
PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sampled_at_utc TEXT NOT NULL,
                target_guid INTEGER NOT NULL,
                netbar_name TEXT NULL,
                online INTEGER NULL,
                terminal_current INTEGER NULL,
                terminal_total INTEGER NULL,
                vip_limit INTEGER NULL,
                defense_count INTEGER NULL,
                auth_ok INTEGER NULL,
                error_kind TEXT NULL,
                error_message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_samples_sampled_at
                ON samples(sampled_at_utc);

            CREATE TABLE IF NOT EXISTS alert_state (
                kind TEXT PRIMARY KEY,
                active INTEGER NOT NULL,
                first_seen_utc TEXT NULL,
                last_change_utc TEXT NOT NULL,
                last_notified_utc TEXT NULL,
                notification_pending INTEGER NOT NULL,
                pending_recovery INTEGER NOT NULL,
                retry_count INTEGER NOT NULL,
                next_retry_utc TEXT NULL,
                pending_title TEXT NULL,
                pending_body TEXT NULL
            );
```

无 ORM、迁移版本表、外键、种子数据、分布式事务或数据库账户。每个样本一次 INSERT，各告警按 kind UPSERT，未知种类读取跳过。成功快照按 id 倒序取 error_kind IS NULL 的最近一条。每轮成功路径约每日清理 sampled_at_utc < 当前 UTC-90天；清理只删 samples，不删 alert_state。错误摘要最多 1000 字符。

另有 config.json、WebView2、logs 和诊断报告；日志保留 30 天的删除在 logger 初始化触发，不是独立每天服务。备份/恢复见第 7 节，禁止把 CREATE IF NOT EXISTS 当字段升级工具。

# 6. 部署拓扑与运行方式

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

启动前具备 WebView2、可写运行目录、机器人和登录条件；Start-Process .\publish\win-x64\DwhMonitor.App.exe。程序不是 Windows Service，不需负载均衡/反代。HKCU Run 登录启动且需要桌面会话。Local\DwhMonitor Mutex 不承诺所有 Windows 用户会话唯一，避免多个会话同时使用同一目录。

初始化顺序：AppPaths 迁移/建目录 → logger/settings/tray → HttpClient/WebView2/model → SQLite/状态 → 首次设置 → StartMonitorLoop → 自动/人工登录。无机器人凭据时停在等待配置。

# 7. 日常运维 Runbook

Windows PowerShell，仓库根目录；以实际 EXE 路径确认目标，不只按同名进程判断：
```powershell
$runtime = Join-Path (Get-Location) 'publish\win-x64'
Get-Process DwhMonitor.App -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime,Responding
$latest = Get-ChildItem (Join-Path $runtime 'logs\dwh-monitor-*.log') | Sort-Object LastWriteTime | Select-Object -Last 1
Get-Content $latest.FullName -Tail 80
$line = Select-String -LiteralPath $latest.FullName -Pattern '采集成功' | Select-Object -Last 1
if (-not $line) { throw '当前日志没有成功采集，不能判定健康' }
$last = [DateTimeOffset]::Parse(($line.Line -split ' ')[0])
$age = ([DateTimeOffset]::Now - $last).TotalSeconds
if ($age -gt 90 -or $age -lt 0) { throw "采集时间异常：$age 秒" }
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name DwhMonitor -ErrorAction SilentlyContinue
```
成功条件是预期路径单实例、WebView2 初始化且采样新鲜，不是进程存在或 HTTP 200。关窗不等于退出；托盘“退出”等待进程结束后再重启。没有 HTTP 健康端点。

## 备份与恢复

Windows PowerShell，仓库根目录；前置：从托盘退出所有实例并确认进程结束。备份目录在发布目录之外；下面不覆盖已有备份：
```powershell
$runtime = Join-Path (Get-Location) 'publish\win-x64'
if (Get-Process DwhMonitor.App -ErrorAction SilentlyContinue) { throw '先从托盘退出客户端' }
$backup = Join-Path (Get-Location) ('migration-backups\manual-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $backup) { throw '备份目录已存在' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $runtime -Destination (Join-Path $backup 'runtime') -Recurse -Force
Get-ChildItem $backup -File -Recurse | Get-FileHash -Algorithm SHA256 | Export-Csv (Join-Path $backup 'manifest.csv') -NoTypeInformation -Encoding utf8
```
复制完整停止后的目录，保留配置、数据库与 WAL/SHM、浏览器资料和对应程序版本。不要单独拷贝正在写的 monitor.db。恢复时先停机，保留故障目录，核对备份 manifest，再把 `runtime` 副本恢复到原路径；同 Windows 用户验证 DPAPI，随后启动并做 90 秒健康检查。恢复演练本次未执行，不能宣称备份一定可恢复。若需要数据库独立完整性检查，可在备份副本上用 Python 标准库 sqlite3 执行 `PRAGMA integrity_check`，预期 `ok`。
## 资源、暂停与人工操作

Get-Process 查看 CPU/WorkingSet64；Get-PSDrive 查磁盘；日志与 DB 增长须实测建阈值。没有连接池仪表盘、任务后台或补跑接口。暂停只通过退出客户端；重启重新读告警状态，不应清空 alert_state 来“恢复发送”。清理 OCR 临时资料先备份且确认不再训练；不在线删除浏览器缓存。证书由系统/外部服务维护，依赖升级需完整回归。

# 8. 日志、监控与告警

日志路径 logs/dwh-monitor-yyyy-MM-dd.log，格式 ISO-8601本地时间 [INFO|ERROR] message，可附异常类型和消息，没有全栈/请求 ID。按日写，启动时按修改时间清理 >30 天文件，INFO/ERROR 同文件；无按大小轮转。

告警五类见 README；会话/网吧/VIP/自动登录状态首次与恢复，接口故障窗口为三次；退避30s/1m/2m/5m。日志不是结构化审计，通知 pending 状态在 SQLite；不能用“待发送为空”证明收件人看到了消息。无独立仪表盘/Prometheus/SLA，应建立外部新鲜度监控。

优先级建议而非现有分级：采集停止/登录失败先人工处理，单次网络失败观察下一轮。错误字段可能包含上游消息；URL 脱敏不等于全部数据无敏感内容。

# 9. 发布与回滚

当前没有既有 CI/分支保护/自动版本注入/签名流程；本次主分支 main。建议发布前记录 Git commit、csproj 版本、模型哈希、测试与 NU1903 处理状态；发布到空的新目录，检查没有 config/data/logs/WebView2，再分发整个目录。

先停旧实例，备份，再替换代码文件并保留兼容数据，启动后检查 WebView2/90秒新鲜度/钉钉测试。灰度是独立测试账号/机器的人工验证，不是现成多节点流量切换。当前没有 Schema 升级器，改表后回滚须先验证旧程序读取能力；不要直接把新库覆盖到旧版本。

回滚触发建议：启动异常、模型哈希不符、采样过期、账号不可读或通知异常。保留现场，停机恢复匹配版本/配置/DB，重新做健康检查。期间未采样的数据不能自动补采。

# 10. 故障处理

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

故障现场至少保存相关时间窗日志、版本/模型哈希、进程路径和错误类型；转储/DB/浏览器资料仅受控保留，不上传 Issues。重置 DB 会丢失去重与历史，恢复旧 DB 也可能重发旧通知，应作为明确有副作用操作。

# 11. 权限与安全操作

**当前实现**：配置使用 DPAPI CurrentUser，临时文件写入后移动；HTTPS 出站；Cookie 由 WebView2 持久化；FileMonitorLogger 只对钉钉 URL 做正则替换。自动登录告警正文包含账号字段，不能声称所有日志/消息都自动去除个人信息。普通用户拥有目录即拥有数据库、日志和会话的文件访问权，无独立应用角色/数据库密码。

**发布边界**：公开仓库不包含真实运行凭据、Cookie、数据库、WebView2 资料、迁移备份、训练标签或私钥全文/Base64。只记录配置键、来源与用途，不解密并公开生产配置。附件中要求公开这些原文的条目未执行；不把未提取写成已完成。

**本次发现**：NuGet 对 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 发出 `NU1903` 高危告警，构建输出指向 [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)。本次不升级依赖或改业务代码。建议维护者单独升级并回归 SQLite、离线依赖和自包含发布，不能把测试通过等同无漏洞。

**建议方案**：限制数据目录 ACL、加密备份、轮换钉钉 secret 与账号密码、建立依赖锁和漏洞检查、签名制品、日志字段分级。DPAPI 不能防同用户恶意进程；换机重新配置。未发现代码签名、SBOM、独立审计、速率限制或启动防篡改校验。

# 12. 已知问题与技术债

| 项目 | 影响/触发 | 临时处置/建议 | 证据 |
| --- | --- | --- | --- |
| SQLite 原生库 NU1903 | 依赖告警，具体适用性需评估 | 独立升级回归 | 本次 restore/build |
| 列表固定首 30 条 | 目标不在首页时持续失败 | 检查权限；设计分页 | DwhApiClient |
| 状态仅当前值/单 pending | 连续变化可能覆盖未发事件（源码推断） | 若需审计新增 outbox | AlertStateMachine/Store |
| 发送与状态保存分离 | 崩溃窗口可能重复（源码推断） | 设计幂等/outbox | MonitorEngine |
| Mutex 为 Local | 跨用户会话非全局唯一 | 避免共享运行根 | Program |
| 日志只定向脱敏 | 上游错误/账号字段可能含敏感数据 | 日志分级与清理 | FileMonitorLogger/BuildAutoLoginMessage |
| 无版本化 DB 迁移 | 新字段不会自动创建 | 显式迁移与兼容测试 | MonitorStore |
| Python 无锁/在线字体 | 训练复现依赖网络和环境 | 建隔离环境/锁/离线字体 | tools/ocr |

不将这些源码推断当作已在线复现。本次不改动相应实现。

# 13. 交接验收清单

- [x] 源码、配置字段、数据与日志位置、关键不变量已说明。
- [x] 43/43 测试、构建、格式、自包含发布有本次输出。
- [x] 规划资料只读且 SHA-256 对照记录。
- [ ] 接管人员在新机器安装 WebView2 并首次配置（运维角色）。
- [ ] 接管人员验证真实钉钉收件、登录及连续采样（运维角色）。
- [ ] 完成一次停止后备份/恢复演练（运维角色）。
- [ ] 完成发布/回滚演练并确认 DPAPI 用户边界（开发与运维）。
- [ ] 评估 NU1903、源码/模型/样本许可（维护者）。

上述未勾选是真实未完成验证，不是模板待填；具体命令与成功标准已在本文给出。
