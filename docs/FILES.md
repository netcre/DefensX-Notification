# 文件清单与发布边界

[README](../README.md) · [开发](DEVELOPMENT.md)

源码、测试与训练脚本逐文件扫描；bin/obj 是工具生成，publish 与 migration-backups 是运行/历史材料，不提交。规划记录完整读取但不公开上传，哈希见 VERIFICATION。

| 项目相对路径 | 大小（字节） | 职责 |
| --- | --- | --- |
| `src/DwhMonitor.App/app.manifest` | 608 | asInvoker 和 Windows 兼容性 manifest |
| `src/DwhMonitor.App/AppPaths.cs` | 11845 | EXE 根目录、旧数据递归复制、不覆盖与跨盘保护 |
| `src/DwhMonitor.App/AppSettingsStore.cs` | 7174 | DPAPI v1/v2、账号保存/选择/验证、配置写入 |
| `src/DwhMonitor.App/AutoLoginModels.cs` | 576 | 自动登录结果与失败枚举 |
| `src/DwhMonitor.App/AutostartManager.cs` | 1062 | HKCU Run 注册/删除 |
| `src/DwhMonitor.App/CaptchaPrimaryModel.cs` | 14320 | DWCMODEL1 解析与 C# CNN 推理 |
| `src/DwhMonitor.App/CaptchaRecognizer.cs` | 825 | 模型加载、六位识别入口与放行常量 |
| `src/DwhMonitor.App/CaptchaRetryCoordinator.cs` | 6614 | 五次实际提交/十五张读取/换图协调 |
| `src/DwhMonitor.App/DwhMonitor.App.csproj` | 1202 | 目标框架、依赖、引用与构建/模型复制规则 |
| `src/DwhMonitor.App/FileMonitorLogger.cs` | 2029 | 按日 INFO/ERROR、URL 替换和启动清理 |
| `src/DwhMonitor.App/LoginPageRecovery.cs` | 1043 | Token 弹窗控件唯一性决策 |
| `src/DwhMonitor.App/OcrAcceptanceApplicationContext.cs` | 7449 | 独立轮次验收、报告和可选 DPAPI 样本 |
| `src/DwhMonitor.App/Program.cs` | 964 | STA 入口、Mutex、验收参数分流 |
| `src/DwhMonitor.App/Properties/AssemblyInfo.cs` | 91 | 测试友元声明 |
| `src/DwhMonitor.App/SessionWebViewForm.cs` | 30397 | WebView2、会话、页面填写/换图/响应监听 |
| `src/DwhMonitor.App/SettingsForm.cs` | 15056 | 钉钉与账号配置 UI |
| `src/DwhMonitor.App/StatusForm.cs` | 4007 | 运行状态 UI |
| `src/DwhMonitor.App/TrayApplicationContext.cs` | 20983 | 依赖组装、托盘、启动/退出与恢复 |
| `src/DwhMonitor.App/TrayIconFactory.cs` | 1060 | 托盘图标绘制 |
| `src/DwhMonitor.Core/AlertStateMachine.cs` | 2155 | 纯状态转换、去重、恢复、退避 |
| `src/DwhMonitor.Core/DingTalkNotifier.cs` | 3630 | 钉钉 URI 签名与消息发送 |
| `src/DwhMonitor.Core/DwhApiClient.cs` | 9462 | Cookie 会话/列表协议和字段解析 |
| `src/DwhMonitor.Core/DwhMonitor.Core.csproj` | 328 | 目标框架、依赖、引用与构建/模型复制规则 |
| `src/DwhMonitor.Core/Models.cs` | 3272 | 默认常量、领域记录、依赖接口 |
| `src/DwhMonitor.Core/MonitorEngine.cs` | 14470 | 串行采样、独立告警、通知/清理 |
| `src/DwhMonitor.Core/MonitorStore.cs` | 11453 | SQLite Schema、参数化读写与清理 |
| `tests/DwhMonitor.Tests/AlertStateMachineTests.cs` | 1908 | 纯状态转换、去重、恢复、退避 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/AppPathsTests.cs` | 6746 | EXE 根目录、旧数据递归复制、不覆盖与跨盘保护 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/AppSettingsStoreTests.cs` | 5457 | DPAPI v1/v2、账号保存/选择/验证、配置写入 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/CaptchaRecognizerTests.cs` | 3448 | 模型加载、六位识别入口与放行常量 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/CaptchaRetryCoordinatorTests.cs` | 5844 | 五次实际提交/十五张读取/换图协调 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/DingTalkNotifierTests.cs` | 2730 | 钉钉 URI 签名与消息发送 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/DwhApiClientTests.cs` | 2856 | Cookie 会话/列表协议和字段解析 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/DwhMonitor.Tests.csproj` | 1465 | DwhMonitor. 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/contact-1.png` | 75247 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/contact-2.png` | 73918 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-01.png` | 2040 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-02.png` | 1927 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-03.png` | 1992 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-04.png` | 1986 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-05.png` | 2146 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-06.png` | 2109 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-07.png` | 2036 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-08.png` | 2018 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-09.png` | 1958 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-10.png` | 2046 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-11.png` | 1992 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-12.png` | 1976 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-13.png` | 1908 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-14.png` | 2063 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-15.png` | 1997 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-16.png` | 1965 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-17.png` | 1952 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-18.png` | 1928 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-19.png` | 1984 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-20.png` | 1957 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-21.png` | 1948 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-22.png` | 1947 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-23.png` | 1987 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-24.png` | 2000 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-25.png` | 2077 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-26.png` | 1927 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-27.png` | 1978 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-28.png` | 1879 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-29.png` | 1911 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-30.png` | 2007 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-31.png` | 1932 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-32.png` | 2010 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-33.png` | 1903 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-34.png` | 2011 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-35.png` | 1936 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-36.png` | 1983 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-37.png` | 2008 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-38.png` | 1947 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-39.png` | 1903 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-40.png` | 1933 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-41.png` | 1942 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-42.png` | 1991 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-43.png` | 2067 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-44.png` | 1928 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-45.png` | 1937 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-46.png` | 1963 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-47.png` | 2058 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-48.png` | 2092 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-49.png` | 1978 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/Fixtures/Captcha/sample-50.png` | 1928 | 验证码固定样本/拼图；仅模型一致性，不证明独立准确率 |
| `tests/DwhMonitor.Tests/LoginPageRecoveryTests.cs` | 1316 | Token 弹窗控件唯一性决策 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/MonitorEngineTests.cs` | 9725 | 串行采样、独立告警、通知/清理 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/MonitorStoreTests.cs` | 2117 | SQLite Schema、参数化读写与清理 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/SettingsFormLayoutTests.cs` | 1892 | SettingsFormLayout 的 xUnit 回归测试 |
| `tests/DwhMonitor.Tests/TestSupport.cs` | 4154 | 可控时钟、Fake API/Cookie/通知与内存存储 |
| `tools/ocr/train_alignment_cnn.py` | 9018 | 研究用对齐模型训练/读取 |
| `tools/ocr/train_primary_crnn.py` | 31361 | 生产共享字符模型训练、字体下载与 .bin 导出 |
| `tools/ocr/train_sequence_crnn.py` | 9680 | 研究用序列 CRNN 训练 |
| `DwhMonitor.sln` | 4595 | App/Core/Tests 解决方案 |
| `.gitignore` | 427 | 构建/运行资料与本地研究产物排除规则 |
| `artifacts/captcha_primary_cnn.bin` | 625097 | 构建/发布必须的生产 DWCMODEL1 权重 |
| `artifacts/captcha_primary_cnn.json` | 936 | 生产权重元数据、训练参数与历史准确率 |

## 文档职责

README：18 项目模块；API：10 协议/内部合约模块；HANDOVER：13 运维模块；DEVELOPMENT：16 开发模块。DEPENDENCIES 记录本次解析的 NuGet 全树，SOURCE-API 逐文件摘录当前声明与训练参数，VERIFICATION 记录测试/构建/哈希与验证边界；本文件为发布清单。

## 本地未上传材料

`publish/`（生成包和运行数据）、`migration-backups/`、`src/**/bin,obj`、`tests/**/bin,obj`、`tools/**/__pycache__`、研究 .pt/非生产模型，以及根目录 task_plan.md/findings.md/progress.md/ocr_research.md 均只留本地。运行配置、日志、浏览器资料、SQLite、DPAPI 标签、私钥、.env 被排除。未修改业务源代码，也未删除这些本地目录。
