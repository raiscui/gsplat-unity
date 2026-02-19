# Capability: 4dgs-playback-api

## Purpose
定义 4DGS(包含线性 motion 与逐帧 keyframe)的播放控制 API.
并保证同一帧内 decode/sort/render 使用一致的时间值,避免排序抖动与视觉闪烁.

## Requirements

### Requirement: Expose TimeNormalized as a first-class control
系统 MUST 提供一个可序列化、可被动画系统驱动的 `TimeNormalized` 参数.
该参数表示当前播放时间,归一化到 `[0, 1]`.

当用户设置 `TimeNormalized` 时:
- 系统 MUST clamp 到 `[0, 1]`.
- 系统 MUST 在同一帧内将该值一致地用于排序与渲染.

#### Scenario: 通过脚本设置 TimeNormalized
- **WHEN** 脚本把 `TimeNormalized` 设置为 `0.25`
- **THEN** 下一次渲染使用的时间 MUST 为 `0.25`

#### Scenario: TimeNormalized 越界时自动 clamp
- **WHEN** 用户设置 `TimeNormalized = 2.0`
- **THEN** 实际生效的时间 MUST 为 `1.0`

### Requirement: Optional autoplay with speed and loop
系统 MUST 提供可选的自动播放能力,用于快速预览动态数据.
当启用自动播放时:
- 系统 MUST 每帧按 `TimeNormalized += deltaTime * Speed` 推进时间.
- `Speed` 的单位 MUST 为 "归一化时间/秒".
- 当 `Loop=true` 时,时间 MUST 在 `[0,1]` 之间循环.
- 当 `Loop=false` 时,时间 MUST clamp 在 `[0,1]` 并在到达边界后停止推进.

#### Scenario: Loop=true 时循环播放
- **WHEN** `Loop=true`,且时间推进超过 `1.0`
- **THEN** `TimeNormalized` MUST 回绕到 `[0,1]` 内继续播放

#### Scenario: Loop=false 时播放到末尾停止
- **WHEN** `Loop=false`,且时间推进超过 `1.0`
- **THEN** `TimeNormalized` MUST 停在 `1.0`,且后续帧不再增长

### Requirement: No behavior change for 3D-only assets
当 `GsplatAsset` 不包含 4D 字段时,`TimeNormalized` MUST 不改变渲染结果.
系统 MAY 仍允许修改该值,但其效果 MUST 等价于静态渲染.

#### Scenario: 静态 3DGS 资产被设置不同 TimeNormalized
- **WHEN** 对仅 3D 字段的资产分别设置 `TimeNormalized=0.0` 与 `TimeNormalized=1.0`
- **THEN** 两次渲染结果 MUST 一致(忽略浮点误差)
