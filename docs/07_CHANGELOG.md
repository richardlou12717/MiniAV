## 2026-10-17 — M2 foundation (pending verification)
- Created feature/m2-scan-foundation from merged main.
- Added optional ScanRequest.MaxFileBytes preflight size limit and regression tests; CLI defaults unchanged.
- File length is checked before hashing, not a strict byte-stream cap; size changes during scanning require future safeguards.
- M2 retains WPF UI, progress, logs and cancellation as its primary deliverables.
- Pending Windows validation and GitHub CI; no merge yet.

# 变更记录

## 2026-10-17 — M1 错误与文件数量限制（待本地验证）
- Core 回归测试增加模拟 IOException 和 UnauthorizedAccessException，以验证报告 Error 和退出码 3。
- ScanRequest 新增可选 MaxFiles，超过上限将产生错误而不是静默报告扫描完成；CLI 默认行为不变。
- 尚未完成 Windows 本地验证；字节和时间限制仍在设计范围内。

## 2026-10-17 — M1 取消控制（Windows 自动化测试已通过）
- CLI 捕获 Ctrl+C 并请求取消，返回退出码 130。
- Core 在报告扫描结果和结束任务前确认取消状态。
- 新增无需人工按键的 Core 取消回归测试，并接入 PowerShell 回归脚本。
- 用户已确认 Release 编译、Core cancellation regression tests 和完整 Regression tests 均通过；CLI Ctrl+C 交互尚未单独验证。PR #1 继续保持 draft。

## 2026-10-17 — M1 回归测试扩展\n- 已增加 LF/CRLF 双格式签名测试及 Windows Junction 防越界遍历测试。\n- 修复回归脚本误将汇总行 Matched=0 识别为 MATCHED 的正则断言缺陷。\n- 用户在 Windows 验证 Release 构建及扩展回归测试均通过（1.7s，Regression tests passed）。\n- GitHub Actions 状态尚待验证；PR #1 尚未合并。\n\n## 2026-10-17 — M1 模块化（PR #1，尚未合并）
- 建立 `MiniAV.Core` 扫描契约与协调器。
- 将 SHA-256 检测、演示签名数据库读取拆分到 `MiniAV.Engines.Hash`。
- 保持 CLI 参数、输出及退出码行为。
- 加入 Windows GitHub Actions 与 PowerShell 回归测试。
- 修复 PowerShell 对预期 stderr 的处理。
- 修复无效 SHA-256 特征库抛出未处理异常的问题。
- 用户在 Windows 验证 Debug/Release 编译与回归测试通过（`Regression tests passed`）。
- 后续：完善重解析点/取消/错误场景测试；验证 CI 后再合并。

## 2026-10-17 — M0
- 选定混合检测架构和 .NET 10/C# 技术路线。
- 建立需求、架构、安全模型、路线图、工程规范及 ADR。
- 编写只读 SHA-256 CLI 原型、演示签名及无害测试样本。
- GitHub 私有仓库：richardlou12717/MiniAV。
- 原型已在用户 Windows 机器上构建并运行；不具备生产级恶意软件检测能力。
