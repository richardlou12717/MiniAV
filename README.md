# MiniAV — Windows 本地按需扫描工具

**项目状态：M0/M1/M2 已完成；M3（可选 ClamAV 适配器）待启动。**
技术：C# / .NET 10，Windows x64；CLI + WPF，独立 Core 和 SHA-256 引擎。
仓库主分支：`main`；完整跨会话交接以 [PROJECT_STATUS.md](PROJECT_STATUS.md) 为准。

> **演示程序，不是可替代杀毒软件的正式安全产品。** 仅使用无害 SHA-256 演示签名，`NO_MATCH` 不代表文件安全；不关闭或替代 Microsoft Defender。

## 代码结构
- `src/MiniAV.Core/` — ScanCoordinator、扫描契约、资源预算、关闭生命周期控制
- `src/MiniAV.Engines.Hash/` — SHA-256 流式检测与演示签名库
- `src/MiniAV.Cli/` — CLI 扫描入口
- `src/MiniAV.Wpf/` — 图形界面、文件/目录选择、计数、取消、报告保存
- `tests/MiniAV.Core.Regression/` — 可重复的核心回归测试
- `scripts/regression-test.ps1` — Windows CLI/Core 测试
- `.github/workflows/windows-ci.yml` — Windows CLI/WPF Release 构建和回归

## Windows 快速开始
安装 .NET 10 SDK，在仓库根目录运行：

```powershell
dotnet run --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json
dotnet run --project .\src\MiniAV.Wpf -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1
```

CLI 对演示匹配返回 1；无匹配且没有错误返回 0；扫描错误、限制触发或引擎不可用返回 3；无效参数返回 2；用户取消返回 130。只有 SHA-256 演示签名命中，不代表检出真实恶意软件。

WPF 提供可选文件数、单文件 MiB 和扫描秒数限制；留空表示不限制。运行期间可取消，超时及限制均明确标记不完整。界面待处理队列有界，显示日志容量有限；超限后的手动导出会标记为 `PARTIAL DISPLAY LOG`，**不是完整的审计日志**。

## 安全边界及后续
- 扫描只读，不自动删除、移动、隔离或执行被扫描文件；不安装驱动。
- 默认不上传文件或路径；不自动将扫描日志落盘。
- M3 将先设计可选外部 ClamAV 进程接口、安全参数、取消/超时与特征更新策略；不是现有功能。
- 项目架构、安全模型、路线图分别参见 `docs/02_ARCHITECTURE.md`、`docs/03_SECURITY_MODEL.md`、`docs/04_ROADMAP.md`。
