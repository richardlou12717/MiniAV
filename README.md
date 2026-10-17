# MiniAV — Windows 单机病毒扫描项目

**状态**：v0.1 项目初始化 / 只读 CLI 原型（尚非安全产品）  
**架构**：混合检测路线：本地 SHA-256 扫描 + 后续可选 ClamAV 适配器  
**目标平台**：Windows 11 x64；.NET 10 SDK

MiniAV 是个人电脑本地按需病毒扫描的学习与工程项目。当前演示库**不具备真实恶意软件识别能力**，软件不替代 Microsoft Defender。

## 目录
- `docs/` — 项目章程、需求、架构、安全模型、路线图、决策和开发规范
- `src/MiniAV.Cli/` — 只读 SHA-256 文件扫描程序原型
- `signatures/` — 仅用于验证的无害演示特征库
- `tests/fixtures/` — 无害测试文件
- `scripts/` — Windows PowerShell 冒烟测试

## 快速开始（PowerShell）
需要安装 .NET 10 SDK。

```powershell
dotnet run --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json
```

预期 `demo-sample.txt` 命中 `DEMO-0001`，其他文件显示 `NO_MATCH`。这不表示任何真实病毒检测结果。

运行冒烟测试：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

## 安全限制
- 当前只读，不执行、删除、修改、移动或隔离被扫描文件。
- 未匹配不等于安全；无法读取会标注为 Error 或 Skipped。
- 不关闭 Defender，不要求管理员权限、不安装驱动。
- 尚未在目标 Windows/.NET 环境完成编译与测试。

从 `docs/00_PROJECT_CHARTER.md` 开始阅读；路线图见 `docs/04_ROADMAP.md`。
