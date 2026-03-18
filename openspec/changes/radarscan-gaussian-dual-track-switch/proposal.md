## Why

当前 `show-hide-switch-高斯` 的真实目标已经被用户反复澄清:
它不是普通的“渐变切换”,也不是“hide 到一半就切 show”.

用户要的是一个明确的双轨切换契约:
雷达粒子的 `visibility hide` 必须完整跑完,Gaussian 的 show 在 hide 过半时启动,中段两者同时可见,并且只有在雷达 hide 轨真正完成后才能关闭 `EnableLidarScan`.

现有实现还在把“雷达 hide 轨”和“高斯 show 轨”塞进同一套共享显隐状态里.
这会导致 hide 轨在结构上被 `Showing` 覆盖,也会让 overlap 阶段的 splat 提交门禁不稳定.
如果不先把这个行为契约写成 OpenSpec,后续继续补丁只会反复偏离用户要的时间线.

## What Changes

- 为 RadarScan -> Gaussian 的专用切换新增一份明确的双轨行为契约:
  - 起点立即启动雷达粒子的 `visibility hide`
  - hide 过半时启动 Gaussian show
  - overlap 阶段允许 `高斯 + 雷达粒子` 同时呈现
  - 雷达 hide 轨完整结束前不得提前关闭 `EnableLidarScan`
- 在 runtime 侧把这条切换从“共享单轨状态机”改成“共享 show 轨 + 独立 LiDAR hide overlay 轨”的最小双轨方案.
- 明确 overlap 阶段的渲染门禁:
  - LiDAR 继续沿 hide 轨推进
  - Gaussian splat 允许提交
  - `HideSplatsWhenLidarEnabled` 不能把 overlap 阶段的高斯 show 整体挡掉
- 保持 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的按钮语义一致.
- 增加定向回归测试,锁定“半程启动 show,完整跑完 hide,最后再关 LiDAR”的时序.

## Capabilities

### New Capabilities
- `gsplat-radarscan-gaussian-switch`: 定义 RadarScan -> Gaussian 专用切换的双轨时间线、overlap 可见性、LiDAR 关闭时机与 splat 提交门禁.

### Modified Capabilities
- (无)

## Impact

- 影响的代码区域(预计):
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
- 影响的系统行为:
  - `show-hide-switch-高斯` 从“半程切过去”改为“真正的双轨 overlap 切换”
  - overlap 阶段会短时间同时保留 LiDAR draw 与 Gaussian splat draw
- 兼容性:
  - 只影响该专用切换路径
  - 不改变普通 `PlayShow()` / `PlayHide()` / `SetRenderStyleAndRadarScan(...)` 的既有默认语义
