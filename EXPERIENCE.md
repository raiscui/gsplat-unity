# EXPERIENCE

## [2026-03-24 16:18:00] [Session ID: 20260324_27] Unity MCP 有效验证纪律

### 适用场景

- 需要用 Unity MCP 做编译、跑测、读 Console 或刷新工程。
- 特别是刚经历 `refresh_unity`、domain reload、脚本重编译之后。

### 经验结论

- 不能只因为 `mcpforunity://editor/state` 可读,就认定动作通道已经恢复。
- `run_tests` 或相关 job 如果返回 `summary.total = 0`,不能把它记成“测试通过”。
- 更稳的验证组合是:
  - 读取 `mcpforunity://editor/state`
  - 回看 `~/.unity-mcp/unity-mcp-status-*.json`
  - 用 `lsof -nP -iTCP:<port> -sTCP:LISTEN` 确认实际 listener
  - 最终只接受非 0 的测试总数或明确的 XML / 日志完成标志

### 什么时候优先阅读

- 当你看到:
  - `tests_running`
  - `No Unity Editor instances found`
  - `summary.total = 0`
  - refresh 后端口变化

## [2026-03-24 16:18:00] [Session ID: 20260324_27] 大规模线性 compute kernel 要用 chunked dispatch

### 适用场景

- shader 或 command buffer 里有线性一维 compute 工作负载。
- 你正打算用一次 `DispatchCompute(groupsX, 1, 1)` 覆盖全部 item。
- 现场已经出现:
  - `Thread group count is above the maximum allowed limit`

### 经验结论

- 真正受限的是“单次 dispatch 的单个维度 group count”,不是“总数据量永远不能更大”。
- 对线性 kernel,更稳的模式是:
  - CPU 侧按维度上限切 chunk
  - shader 侧引入 `dispatchBaseIndex`
  - 每次 dispatch 只处理自己的线性区间
- 这比继续压缩输入规模、额外塞历史 clamp,更接近问题本质。

### 什么时候优先阅读

- 当你在 Unity compute 路径里处理千万级 item。
- 当旧代码里存在“历史防呆上限”,但你怀疑它只是为了绕开单次 dispatch 限制。

## [2026-03-24 16:18:00] [Session ID: 20260324_27] `LidarBeamCount=512` 只属于历史结论

### 适用场景

- 回读 2026-03-24 之前的 LiDAR 支线笔记。
- 排查“为什么 beam count 调大没效果”。

### 经验结论

- 截至 2026-03-24:
  - `GsplatRenderer` / `GsplatSequenceRenderer` 的 runtime `512` clamp 已移除。
  - 当前真实约束已经转成 `beamCount * azimuthBins` 带来的性能、显存和 buffer 成本。
- 因此旧支线里凡是把 `512` 当作“当前硬上限”的段落,都只能按历史背景理解,不能直接当成现状。

### 什么时候优先阅读

- 当旧笔记、旧截图、旧讨论和当前代码表现出现矛盾时。
- 当你准备继续做 LiDAR 分辨率、精度或性能分析时。
