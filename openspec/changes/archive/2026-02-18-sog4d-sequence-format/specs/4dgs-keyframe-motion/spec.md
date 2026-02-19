# Capability: 4dgs-keyframe-motion

## ADDED Requirements

### Requirement: Evaluate keyframe indices and interpolation factor from `TimeNormalized`
系统 MUST 使用 `TimeNormalized`(归一化到 `[0,1]`)评估当前采样的相邻两帧 `(i0, i1)` 与插值因子 `a`.

当 `timeMapping.type="uniform"` 时:
- `u = clamp01(TimeNormalized) * (frameCount - 1)`
- `i0 = floor(u)`
- `i1 = min(i0 + 1, frameCount - 1)`
- `a = u - i0`

当 `timeMapping.type="explicit"` 时:
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

### Requirement: Provide interpolation modes for all per-frame streams
系统 MUST 支持以下两种插值模式,并对 position, scale, rotation, opacity, SH 全部生效.
其中 opacity MUST 视为 `sh0.webp` 的 alpha(见 `sog4d-sequence-encoding`),并随 `sh0` 一起被插值:
- `Nearest`: 仅使用 `i0` 帧的数据
- `Linear`: 使用 `(i0, i1, a)` 插值

#### Scenario: Nearest 模式不做插值
- **WHEN** 插值模式为 `Nearest`
- **THEN** 在任意 `TimeNormalized` 下,系统 MUST 只使用 `i0` 帧的数据,且不读取 `i1`

### Requirement: Interpolate position linearly in object space
当插值模式为 `Linear` 时,系统 MUST 在对象空间对 position 做线性插值:
- `pos(t) = lerp(pos0, pos1, a)`
其中 `pos0` 与 `pos1` 分别来自 `i0` 与 `i1` 帧的 position stream.

#### Scenario: position 的线性插值
- **WHEN** `pos0=(0,0,0)`,`pos1=(10,0,0)`,且 `a=0.25`
- **THEN** 系统 MUST 得到 `pos(t)=(2.5,0,0)`

### Requirement: Interpolate scale in log-domain to avoid bias
当插值模式为 `Linear` 时,系统 MUST 在对数域插值 scale:
- `s(t) = exp(lerp(log(max(s0, eps)), log(max(s1, eps)), a))`
- `eps` MUST 为一个正的安全常量(例如 `1e-8`)

#### Scenario: scale 的对数域插值
- **WHEN** `s0=(1,1,1)`,`s1=(4,4,4)`,且 `a=0.5`
- **THEN** 系统 MUST 得到 `s(t)=(2,2,2)`

### Requirement: Interpolate rotation using hemisphere-stable nlerp
当插值模式为 `Linear` 时,系统 MUST 使用半球稳定的 nlerp:
- 若 `dot(q0, q1) < 0`,则令 `q1 = -q1`
- `q(t) = normalize(lerp(q0, q1, a))`

#### Scenario: rotation 插值避免长路径翻转
- **WHEN** `q0` 与 `q1` 表示同一旋转但符号相反
- **THEN** 系统 MUST 在插值前把它们归到同一半球,避免插值路径翻转

### Requirement: Interpolate opacity linearly and enforce no-contribution outside alpha threshold
当插值模式为 `Linear` 时,系统 MUST 对 opacity 做线性插值:
- `opacity(t) = lerp(opacity0, opacity1, a)`

其中 `opacity0` 与 `opacity1` MUST 来自相邻两帧 `sh0.webp` 的 alpha 解码结果.

当 `opacity(t)` 过小导致几乎无贡献时,系统 MUST 视为不可见.
该阈值 MUST 与现有渲染管线一致,至少满足:
- 当 `opacity(t) < 1/255` 时,该 splat MUST 不产生任何颜色贡献

#### Scenario: opacity 太小则不可见
- **WHEN** `opacity(t)=0.0`
- **THEN** 该 splat MUST 被裁剪,不应写入任何片元颜色

### Requirement: Interpolate SH coefficients linearly in coefficient space
当插值模式为 `Linear` 时,系统 MUST 对 SH 做逐系数线性插值:
- `sh0(t) = lerp(sh0_0, sh0_1, a)`
- `shRest(t)[k] = lerp(shRest0[k], shRest1[k], a)`

其中 `sh0` 与 `shRest` 为解码到 float 域后的系数表达.

#### Scenario: SH0 的线性插值
- **WHEN** `sh0_0=(0,0,0)`,`sh0_1=(1,0,0)`,且 `a=0.5`
- **THEN** 系统 MUST 得到 `sh0(t)=(0.5,0,0)`

### Requirement: Sorting and rendering MUST use the same evaluated attributes in a frame
在同一帧内,系统 MUST 使用同一组 `(i0,i1,a)` 与相同的插值模式来驱动:
- 深度排序 pass
- 渲染 pass

#### Scenario: 同一帧内排序与渲染一致
- **WHEN** 同一帧内先排序再渲染
- **THEN** 两个 pass MUST 使用同一个 `TimeNormalized` 与同一套插值结果
