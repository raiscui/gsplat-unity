## ADDED Requirements

### Requirement: Single-frame static `.splat4d` MUST remain visually stable under all playback controls
当 `.splat4d` 资产实际上表达的是单帧静态数据时,系统 MUST 继续暴露现有播放控制接口:

- `TimeNormalized`
- `AutoPlay`
- `Speed`
- `Loop`

这里的“单帧静态数据”指该资产的 4D 语义等价于:
- `vx/vy/vz = 0`
- `time = 0`
- `duration = 1`

对于这类资产:
- 播放控制 MAY 继续更新内部时间状态
- 但排序、渲染与最终显示结果 MUST 等价于固定画面
- 系统 MUST NOT 依赖独立的第二帧样本、伪造相邻时间帧,或因为时间推进而进入异常的伪动态行为

#### Scenario: Different `TimeNormalized` values keep the same visual result for a static one-frame `.splat4d`
- **WHEN** 对同一个静态单帧 `.splat4d` 资产分别设置 `TimeNormalized = 0.0` 与 `TimeNormalized = 1.0`
- **THEN** 两次排序与渲染后的最终显示结果 MUST 一致(忽略浮点误差)

#### Scenario: `AutoPlay` and `Loop` do not create fake motion for a static one-frame `.splat4d`
- **WHEN** 用户对静态单帧 `.splat4d` 启用 `AutoPlay = true` 且允许时间持续推进或循环
- **THEN** 系统 MUST 仍能持续完成正常渲染
- **AND** 最终显示结果 MUST 保持固定画面
- **AND** 系统 MUST NOT 因为不存在独立的额外帧而失败
