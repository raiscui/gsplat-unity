## Why

当前仓库已经有 `show-hide-switch-高斯`,可以把 RadarScan 退场并切回 Gaussian,但还没有一颗与之对应的反向按钮,让用户从 Gaussian 侧稳定切回 RadarScan。结果就是两种表现之间只能单向切,或者被迫依赖普通 show/hide 与 render style 硬切,时间线不对,观感也不对。

这次需要把反向按钮 `show-hide-switch-雷达` 正式写成一条独立契约: 高斯场景先进入 hide,当 hide 推进到默认 `0.35` 的触发点时立即启动雷达效果的 show,并允许这个触发点后续可调。中段必须允许两种视觉 overlap,并让这颗按钮与 `show-hide-switch-高斯` 形成一组明确的双向切换入口。

## What Changes

- 为 Gaussian -> RadarScan 的反向切换新增一份明确的双轨行为契约:
  - 按钮触发后立即启动 Gaussian 场景的 hide
  - 当 Gaussian hide 推进到默认 `0.35` 的可调触发阈值时启动雷达效果的 show
  - overlap 阶段允许 `高斯 + 雷达效果` 同时可见
  - 完整切换后进入稳定的 RadarScan 呈现,而不是普通硬切
- 为 Inspector / 用户入口补上一颗与 `show-hide-switch-高斯` 对应的 `show-hide-switch-雷达` 按钮,让两种效果可以双向切换。
- 明确 runtime 在反向切换期间的门禁与时序:
  - Gaussian hide 轨不能因为雷达 show 启动而被提前截断
  - Radar show 轨必须从 `0.35` 阈值开始接管,不能等到高斯完全消失后才出现
  - overlap 期间两种效果都要按预期提交与显示
- 保持 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的这组双向切换语义一致。
- 增加定向回归测试,锁定“高斯先 hide,到 `0.35` 启动雷达 show,两者可 overlap,最终稳定切到雷达”的时序。

## Capabilities

### New Capabilities
- `gsplat-gaussian-radarscan-switch`: 定义 Gaussian -> RadarScan 专用切换的双轨时间线、默认 `0.35` 且可调的启动阈值、overlap 可见性、以及与 `show-hide-switch-高斯` 成对的按钮语义。

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
  - 新增 `show-hide-switch-雷达`,作为 `show-hide-switch-高斯` 的反向入口
  - Gaussian -> RadarScan 切换从“普通开关/硬切”升级为“带 overlap 的双轨切换”
  - 两种按钮共同组成 `Gaussian <-> RadarScan` 的成对切换体验
- 兼容性:
  - 只影响这条专用反向切换路径
  - 不改变普通 `PlayShow()` / `PlayHide()` / `SetRenderStyleAndRadarScan(...)` 的默认语义
