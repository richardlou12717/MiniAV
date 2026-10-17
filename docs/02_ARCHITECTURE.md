# MiniAV 技术架构

```text
CLI (M1) / WPF (M2)
       |
 ScanCoordinator
       |
   IScanEngine
   /        \
HashEngine   ClamAV Adapter (M3, optional)
   |
SignatureStore
       |
Results / Logs
       |
QuarantineService (M2, explicit action only)
```

计划契约：
- ScanRequest: targetPath, engines, limits, cancellation
- EngineResult: findings, error, unavailable, metrics
- ScanOutcome: totals, findings, errors, skipped, duration, signature version
- IScanEngine.ScanFileAsync(file, cancellationToken)
- IQuarantineService: 与引擎隔离，仅明确授权调用

当前 CLI 为原型，尚未完成模块拆分。外部引擎优先用独立本地进程调用，不隐式关闭 Defender。ClamAV 的许可及引擎更新方式需审查。
