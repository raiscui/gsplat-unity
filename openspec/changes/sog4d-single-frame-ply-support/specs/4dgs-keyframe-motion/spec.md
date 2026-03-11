## MODIFIED Requirements

### Requirement: Evaluate keyframe indices and interpolation factor from `TimeNormalized`
系统 MUST 使用 `TimeNormalized`(归一化到 `[0,1]`)评估当前采样的相邻两帧 `(i0, i1)` 与插值因子 `a`.

当 `timeMapping.type="uniform"` 时:
- 当 `frameCount == 1` 时,系统 MUST 定义:
  - `i0 = 0`
  - `i1 = 0`
  - `a = 0.0`
- 当 `frameCount > 1` 时:
  - `u = clamp01(TimeNormalized) * (frameCount - 1)`
  - `i0 = floor(u)`
  - `i1 = min(i0 + 1, frameCount - 1)`
  - `a = u - i0`

当 `timeMapping.type="explicit"` 时:
- 当有效帧数小于等于 `1` 时,系统 MUST 定义:
  - `i0 = 0`
  - `i1 = 0`
  - `a = 0.0`
- 当有效帧数大于 `1` 时:
  - 系统 MUST 找到满足 `t[i0] <= TimeNormalized <= t[i1]` 的相邻帧
  - 当 `t[i1] > t[i0]` 时,`a = (TimeNormalized - t[i0]) / (t[i1] - t[i0])`
  - 当 `t[i1] == t[i0]` 时,系统 MUST 定义 `a = 0.0`(避免除以 0)
  - 当 `TimeNormalized` 落在范围外时,系统 MUST clamp 到首帧或末帧

#### Scenario: uniform mapping 的索引与 a
- **WHEN** `frameCount=5`,`TimeNormalized=0.6`,且 `type="uniform"`
- **THEN** 系统 MUST 得到 `u=2.4`,`i0=2`,`i1=3`,`a=0.4`

#### Scenario: explicit mapping 的索引与 a
- **WHEN** `frameTimesNormalized=[0.0, 0.1, 0.9, 1.0]`,且 `TimeNormalized=0.5`
- **THEN** 系统 MUST 选择 `i0=1`,`i1=2`,并计算 `a=(0.5-0.1)/(0.9-0.1)=0.5`

#### Scenario: 单帧 uniform 映射退化为固定帧
- **WHEN** `frameCount=1`,`TimeNormalized=0.8`,且 `type="uniform"`
- **THEN** 系统 MUST 得到 `i0=0`,`i1=0`,`a=0.0`

#### Scenario: 单帧 explicit 映射退化为固定帧
- **WHEN** `frameCount=1`,`frameTimesNormalized=[0.0]`,且 `type="explicit"`
- **THEN** 系统 MUST 得到 `i0=0`,`i1=0`,`a=0.0`

## ADDED Requirements

### Requirement: Single-frame keyframe assets MUST behave as a fixed-frame sample under all playback controls
当 keyframe 资产的有效帧数为 `1` 时,系统 MUST 将其视为固定帧采样.

在这种情况下:
- `Nearest` 与 `Linear` 两种插值模式 MUST 产生等价结果
- `TimeNormalized`、`AutoPlay`、`Loop` 的变化 MUST NOT 改变最终显示结果
- decode / interpolate / render 路径 MUST NOT 依赖一个与 `i0` 不同的第二帧资源

系统 MAY 在实现上复用同一帧作为 `i0` 与 `i1`,但对外行为 MUST 等价于“只有一个固定帧”.

#### Scenario: 单帧资产在不同 TimeNormalized 下显示一致
- **WHEN** 对同一个单帧 keyframe 资产分别设置 `TimeNormalized=0.0` 与 `TimeNormalized=1.0`
- **THEN** 两次评估与渲染结果 MUST 一致(忽略浮点误差)

#### Scenario: 单帧 Linear 模式不要求真实第二帧
- **WHEN** 单帧 keyframe 资产启用 `Linear` 插值模式
- **THEN** 系统 MUST 仍能完成评估与渲染
- **AND** 不得因为不存在独立的 `i1` 帧而失败
