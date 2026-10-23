# 依赖声明与本次解析结果

[README](../README.md) · [开发](DEVELOPMENT.md)

本项目没有 packages.lock.json；以下来自本次 restore 生成的 obj/project.assets.json，不承诺不同日期还原完全一致。SDK 10.0.302；包版本为当前解析版本，不是语言或系统依赖版本。NuGet 包安装由 dotnet restore 完成，校验命令 dotnet list DwhMonitor.sln package --include-transitive。许可证未逐项审定。

## src/DwhMonitor.App/obj/project.assets.json

| 包/版本 | 属性 |
| --- | --- |
| `Microsoft.Data.Sqlite.Core/10.0.10` | 传递 |
| `Microsoft.Data.Sqlite/10.0.10` | 传递 |
| `Microsoft.Web.WebView2/1.0.4129.50` | 直接 |
| `SQLitePCLRaw.bundle_e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.core/2.1.11` | 传递 |
| `SQLitePCLRaw.lib.e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.provider.e_sqlite3/2.1.11` | 传递 |

## src/DwhMonitor.Core/obj/project.assets.json

| 包/版本 | 属性 |
| --- | --- |
| `Microsoft.Data.Sqlite.Core/10.0.10` | 传递 |
| `Microsoft.Data.Sqlite/10.0.10` | 直接 |
| `SQLitePCLRaw.bundle_e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.core/2.1.11` | 传递 |
| `SQLitePCLRaw.lib.e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.provider.e_sqlite3/2.1.11` | 传递 |

## tests/DwhMonitor.Tests/obj/project.assets.json

| 包/版本 | 属性 |
| --- | --- |
| `Microsoft.CodeCoverage/17.14.1` | 传递 |
| `Microsoft.Data.Sqlite.Core/10.0.10` | 传递 |
| `Microsoft.Data.Sqlite/10.0.10` | 传递 |
| `Microsoft.NET.Test.Sdk/17.14.1` | 直接 |
| `Microsoft.TestPlatform.ObjectModel/17.14.1` | 传递 |
| `Microsoft.TestPlatform.TestHost/17.14.1` | 传递 |
| `Microsoft.Web.WebView2/1.0.4129.50` | 传递 |
| `Newtonsoft.Json/13.0.3` | 传递 |
| `SQLitePCLRaw.bundle_e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.core/2.1.11` | 传递 |
| `SQLitePCLRaw.lib.e_sqlite3/2.1.11` | 传递 |
| `SQLitePCLRaw.provider.e_sqlite3/2.1.11` | 传递 |
| `coverlet.collector/6.0.4` | 直接 |
| `xunit.abstractions/2.0.3` | 传递 |
| `xunit.analyzers/1.18.0` | 传递 |
| `xunit.assert/2.9.3` | 传递 |
| `xunit.core/2.9.3` | 传递 |
| `xunit.extensibility.core/2.9.3` | 传递 |
| `xunit.extensibility.execution/2.9.3` | 传递 |
| `xunit.runner.visualstudio/3.1.4` | 直接 |
| `xunit/2.9.3` | 直接 |

## 系统、训练与外部依赖

Windows WinForms/DPAPI/注册表/WebView2；Python 可选 numpy/torch/Pillow，没有锁定版本。系统组件不通过 NuGet 安装。生产 .bin 由 C# 自行推理，无 Tesseract/ONNX 依赖。外部盾网/钉钉不是本仓库服务端，协议兼容需独立验证。

## 已知告警

SQLitePCLRaw.lib.e_sqlite3 2.1.11 → NU1903，构建输出的公告 https://github.com/advisories/GHSA-2m69-gcr7-jv3q 。不在本次升级。离线缓存方法与未验证范围见 DEVELOPMENT 第 13 节。
