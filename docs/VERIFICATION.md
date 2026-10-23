# 本次整理与验证记录

[README](../README.md) · [交接](HANDOVER.md)

验证日期：2026-10-24（Asia/Shanghai）。本次只整理文档和上传边界，不修改业务源码/依赖，不启动正式客户端，不提交线上登录或发送真实钉钉消息。

## 检查范围

逐文件读取 src/tests/tools 的非生成文本，检查项目清单、manifest、现有 README、全部三个规划记录、生产模型元数据；枚举本地构建、研究、备份与发布目录，生成公开文件清单。没有把运行数据库、浏览器资料、密文配置等私密数据上传，也没有对所有第三方二进制进行反编译/解包，不能宣称穷尽所有运行敏感字符串。

## 本次验证

| 项目 | 命令/依据 | 结果 |
| --- | --- | --- |
| SDK | 用户目录 dotnet --version | 10.0.302 |
| Release 测试 | dotnet test DwhMonitor.sln -c Release | 43/43、0 失败、0 跳过，约40秒测试时间 |
| Release 构建 | dotnet build DwhMonitor.sln -c Release --no-restore | 通过；3 条 NU1903，0 错误 |
| 格式 | dotnet format DwhMonitor.sln --verify-no-changes --no-restore | 退出码0；有工作区加载提示 |
| 自包含发布 | dotnet publish App.csproj -c Release -r win-x64 --self-contained true -o 临时目录 | 通过，未覆盖既有 publish |
| 线上登录/通知 | 未执行 | 未验证 |
| 首次 UI/备份恢复/独立新机离线安装 | 未执行 | 未验证 |

构建告警针对 SQLitePCLRaw.lib.e_sqlite3 2.1.11，NU1903，公告 [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)。保留依赖现状，不能宣称零告警或安全审计通过。

## 规划记录完整性

三文件完整读取；基线记录在文档生成前，完成后再核对；本次没有调用 planning-with-files 技能或修改规划记录。SHA-256 如下：

| 文件/基线属性 | 前 SHA-256 | 后 SHA-256 | 一致 |
| --- | --- | --- | --- |
| `task_plan.md`；13216字节；mtime UTC `2026-08-29T10:10:13.9539808Z` | `F9A3A7229C946F68C9683F84BDBF08D733F24AA820621938940D971607612488` | `F9A3A7229C946F68C9683F84BDBF08D733F24AA820621938940D971607612488` | 是 |
| `findings.md`；22618字节；mtime UTC `2026-08-29T10:14:44.1975591Z` | `3411A4AA6BE6151D730C86519771294BAE543678C48FF97D1D19DF5A7BA0B993` | `3411A4AA6BE6151D730C86519771294BAE543678C48FF97D1D19DF5A7BA0B993` | 是 |
| `progress.md`；27234字节；mtime UTC `2026-08-29T10:14:34.7674253Z` | `CDB03709F7CEC70048C22D69C8CD15164C09B2CA7BFCEAF71D53CAA4C1826B7A` | `CDB03709F7CEC70048C22D69C8CD15164C09B2CA7BFCEAF71D53CAA4C1826B7A` | 是 |

综合摘要已写入 README 第1节；固定目标/比较规则/恢复语义进第2/7节；历史故障与模型口径进第8/9/14节；迁移与数据生命周期进第5/6/17节。早期 Tesseract/单张门槛/PID 与后续实现冲突按当前源码、测试与历史性质分别说明，不拿计划勾选当当前线上成功。

## 模型与构建产物

| 文件 | 字节 | SHA-256 |
| --- | --- | --- |
| `captcha_primary_cnn.bin`（源码） | 625097 | `1D5BF86ED2FA44DF4EA912F2B4840F1A6FE2D232BA47250734F609153CAFC9A6` |
| `captcha_primary_cnn.bin`（本次临时验证） | 625097 | `1D5BF86ED2FA44DF4EA912F2B4840F1A6FE2D232BA47250734F609153CAFC9A6` |
| `DwhMonitor.App.dll`（本次临时验证） | 146944 | `65229A7E9B9F3B3A70D210F743248A33B8402F681A1010289E6DE48A0C5DED90` |
| `DwhMonitor.App.exe`（本次临时验证） | 162304 | `F5BFA526FAD4766E3A3FA594D18EEA0130D847C4D7323069A79DEECF6A2FDB86` |
| `tests.trx`（本次临时验证） | 60229 | `2A461B63E775A8A46115AF1A42EDC0B60993F3B8600091F3315A883DC7D47B39` |

本次干净发布文件数 287，总大小 151609618 字节；未启动该验证包，目录不含 config.json/data/logs/WebView2。发布 .bin 与源码 .bin 哈希已比对。

## 敏感内容处理与验收差异

真实秘密原始值写入：0；敏感文本全文公开：0；敏感二进制 Base64 公开：0。未执行生产密文解密/会话导出；敏感内容与文件总数未做穷尽提取统计，不能填0冒充未发现。公开文档记录敏感配置位置、消费点、DPAPI 与轮换方法；实际运行资料/备份不提交。附件中要求敏感内容完整公开的验收条目未执行，不宣称全部原始条目通过。

## 当前未确认项

最低硬件资源、不同 Windows 版本/架构、实时生产状态、独立泛化准确率、真实收件、离线恢复、备份恢复、签名/许可与历史 Tag 均未确认或未实现；各专项文档提供检查范围与验证方法。此页保留本次事实，不引用旧过程日志为当前证据。
