## Why

目前 Gsplat 的显示/隐藏通常是“硬切”:
要么直接禁用组件,要么关闭后端渲染.
这在大规模点云里会显得突兀,也不利于叙事化的出场/退场效果.

我们需要一个可控的 show/hide 动画.
它既要有明显的“燃烧/发光”视觉风格,也要在隐藏后把排序与渲染开销真正停掉.

## What Changes

- 新增一个可选的“显隐燃烧环动画”(默认关闭,不影响旧行为):
  - show: 初始不显示,中心燃烧发光环向外扩散,扩散过的区域逐步稳定显示.
  - hide: 中心起燃更强的发光环向外扩散,被扫过区域慢慢透明消失,噪波逐渐变大像碎屑.
- 新增可控 API:
  - `SetVisible(bool visible, bool animated = true)`
  - `PlayShow()` / `PlayHide()`
  - 目标是让 hide 不依赖 `SetActive(false)`/`enabled=false`,从而保证动画能播放完整.
- Shader 侧新增一组 reveal/burn uniforms,实现:
  - 径向阈值(mask) + 环形 glow + 噪波扰动(边界抖动 + 灰烬颗粒).
- Editor 侧在 Inspector 增加 "Show" / "Hide" 按钮,用于快速验证与调参.
- 增加最小回归测试,锁定“播完后真正隐藏并停开销”的状态机行为.

## Capabilities

### New Capabilities
- `gsplat-visibility-animation`: 定义 show/hide 显隐动画的行为契约,包括中心选择规则、进度语义、噪波随阶段变化规则,以及对排序/渲染开销的门禁策略.

### Modified Capabilities
- (无)

## Impact

- 影响的代码区域(预计):
  - `Runtime/Shaders/Gsplat.shader` / `Runtime/Shaders/Gsplat.hlsl`: 新增 reveal/burn 计算与噪波函数.
  - `Runtime/GsplatRendererImpl.cs`: 新增一组 uniforms 的集中下发方法(写入 MaterialPropertyBlock).
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`: 增加显隐动画状态机与公开 API.
  - `Editor/GsplatRendererEditor.cs`: 增加 Show/Hide 按钮.
  - `Editor/GsplatSequenceRendererEditor.cs`: 新增,用于给序列组件提供同样按钮.
  - `Tests/Editor/*`: 增加状态机最小回归用例.
- 用户可见影响:
  - 默认关闭,用户显式开启后才会看到显隐动画.
  - hide 动画结束后,对象将进入真正隐藏状态,排序与渲染开销会停止.

