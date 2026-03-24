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
