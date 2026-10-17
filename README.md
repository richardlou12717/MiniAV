# MiniAV — Windows 单机病毒扫描项目

**状态**：v0.1 初始化 / 只读 CLI 原型（尚非安全产品）  
**架构**：本地 SHA-256 扫描 + 后续可选 ClamAV 适配器  
**目标**：Windows 11 x64 / .NET 10 SDK

MiniAV 是供个人电脑使用的本地按需扫描研究项目，不替代 Microsoft Defender。

## 项目目录
- `docs/`: 章程、需求、架构、安全模型、路线图、决策、工程规范、变更记录
- `src/MiniAV.Cli/`: 只读 SHA-256 命令行原型
- `signatures/`: 仅用于测试的哈希库
- `tests/fixtures/`: 无害测试样本
- `scripts/`: Windows 冒烟测试

## 快速开始
安装 .NET 10 SDK，在仓库根目录使用 PowerShell：

```powershell
dotnet run --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json
```

预期 `demo-sample.txt` 命中无害演示特征。未匹配结果称为 `NO_MATCH`，**不代表文件安全**。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

## 安全边界
- 当前只读取文件，不自动执行、删除、移动或隔离文件。
- 不禁用 Defender、不需要管理员权限、不安装驱动、不上传样本。
- 当前演示特征不具备真实恶意软件检测覆盖能力。
- 尚未在目标 Windows/.NET 运行环境完成构建和冒烟测试。
