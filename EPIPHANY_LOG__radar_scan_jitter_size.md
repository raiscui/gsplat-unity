## [2026-03-24 12:34:31 +0800] [Session ID: 20260324_8] 主题: Unity MCP 跑测时要同时盯状态文件与实际 listener,不能只看 `editor/state`

### 发现来源
- `lidar-external-hybrid-resolve` 的 `4.4` 收尾验证
- `refresh_unity` 之后再次运行 Unity EditMode 测试的过程中

### 核心问题
- `mcpforunity://editor/state` 可读,不代表动作通道一定稳定可用。
- domain reload / refresh 之后, Unity MCP listener 端口会从 `6400` 换到 `6401` 之类的新端口。
- 同时, `run_tests` 的 `test_names` 过滤即使返回 `succeeded`,若 `summary.total = 0`,也不能当作真正执行过目标测试。

### 为什么重要
- 如果只盯 `editor/state`,很容易把“资源层还能读”误判成“动作层已经恢复”。
- 如果把 `summary.total = 0` 直接当通过,会把验证链路伪装成完成,实际上目标用例可能根本没跑。

### 未来风险
- 后续任何需要 Unity MCP 动作通道的任务,都可能在 refresh / compile / domain reload 后再次踩到同样的问题。
- 如果不显式检查端口与 job 结果,OpenSpec 的验证任务很容易被假阳性收掉。

### 当前结论
- Unity MCP 验证要至少同时看三类证据:
  - `mcpforunity://editor/state`
  - `~/.unity-mcp/unity-mcp-status-*.json`
  - `lsof -nP -iTCP:<port> -sTCP:LISTEN`
- 只有当这些证据一致,且 test job 结果不是 `summary.total = 0`,才能把它当作有效验证。

### 后续讨论入口
- 下次再遇到“`tests_running` / `No Unity Editor instances found` / `summary.total = 0`”混杂时,先回看这条记录。

## [2026-03-24 14:12:25 +0800] [Session ID: 20260324_8] 主题: 线性 compute kernel 不要把大数据量直接塞进单次 `DispatchCompute(x,1,1)`

### 发现来源
- 用户现场报错 `Thread group count is above the maximum allowed limit`
- `GsplatLidarScan.TryRebuildRangeImage(...)` 与 `GsplatLidarExternalGpuCapture` 的 dispatch 路径排查

### 核心问题
- 对线性索引 compute kernel 来说,最常见的直觉写法是:
  - `groupsX = ceil(itemCount / threadsPerGroup)`
  - `DispatchCompute(groupsX, 1, 1)`
- 但这隐含假设“单次 x 维可以无限大”,实际上不成立

### 为什么重要
- 一旦 `itemCount` 进入千万级,即使 kernel 逻辑本身完全正确,也会在提交阶段直接失败
- 这种错误不是 shader 数学错了,而是提交模型错了

### 未来风险
- 后续任何把大规模线性数据交给 compute 的路径,如果继续沿用单次 `DispatchCompute(x,1,1)`,都可能重复踩同一类问题

### 当前结论
- “65535” 这种限制通常不能靠配置去突破
- 可复用的正确模式是:
  - CPU 侧按上限切 chunk
  - shader 侧增加 `dispatchBaseIndex`
  - 每个 dispatch 只处理自己的线性区间

### 后续讨论入口
- 下次只要看到“大规模线性 compute kernel + 单次 x 维 dispatch”,都应该先检查是否需要 `dispatchBaseIndex` 分批模型
