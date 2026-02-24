---
name: self-learning.gsplat-splat4d-segment-subrange-sort
description: |
  优化 Gsplat 的 keyframe `.splat4d(window)` 播放卡顿: 当资产是“多段 segment records 叠加且 segments 不重叠”的形态时,
  只对当前 segment 做 GPU radix sort + draw(子范围 baseIndex),把 O(totalRecords) 降到 O(recordsPerSegment).
  适用场景: (1) Play/AutoPlay 很卡, (2) `.splat4d` 由 keyframe 分段生成(每段 time/duration 常量),
  (3) totalRecords 远大于单段 records,且同一时刻只会有一个 segment 可见.
author: Claude Code
version: 1.0.0
date: 2026-02-24
---

# Gsplat: keyframe `.splat4d(window)` 的 segment 子范围 sort/draw 优化

## 问题
keyframe `.splat4d(window)` 的常见生成方式是:

- 把每个时间段(segment)的一批 splats 依次追加到同一个 asset/buffers 中.
- 每个 segment 内 `time/duration` 基本是常量.
- segment 之间通常不重叠(上一段结束 <= 下一段开始).

但旧的排序策略会导致性能灾难:

- `Gsplat.compute/CalcDistance` 即便能把“时间窗外”的 splat 推到队尾(写入极端 key),
  仍然会对 **全量 records** 做 GPU radix sort.
- segment 越多,totalRecords 越大,PlayMode/AutoPlay 越容易卡成 PPT.

## 上下文 / 触发条件
满足任意一条就值得用这个优化:

- 你导入的 `.splat4d` 是 keyframe 分段导出的(不是单段连续的 gaussian kernel).
- `TimeModel == window`(语义是 `[time0, time0+duration)` 可见).
- 拖动 `TimeNormalized` 或开启 `AutoPlay` 时明显卡顿.

## 解决方案概览
把“每帧要处理的数据规模”从 `totalRecords` 降到 `recordsPerSegment`.
核心是两件事:

1. 检测出 keyframe segments,并在每一帧根据 `TimeNormalized` 选出“当前唯一可见 segment”.
2. 为排序与渲染同时引入 `baseIndex`,只对 `[baseIndex, baseIndex+count)` 子范围做 sort+draw.

## 实现要点(落点与注意事项)

### 1) Segment 检测(保守,宁可不启用也不要误判)
推荐检测条件(本仓库实现也是这个思路):

- 只在 `window model` 下启用(gaussian model 是连续权重,不适合“唯一 segment”假设).
- 把数组按“连续区间内 time/duration 完全相同”分组,得到 segments:
  - 这里用 float bits 比较,避免浮点误差造成错误分段.
- 启用前再做一次强约束校验:
  - `time0` 单调不减.
  - segments 不重叠: `prevEnd <= nextTime0`.

不满足条件就保持旧行为(全量排序 + shader 硬裁剪),保证正确性优先.

### 2) 子范围 sort: `baseIndex + localId`
排序侧的关键点是:

- payload/OrderBuffer 仍然存 local index(0..count-1).
- 但 `CalcDistance` 读 position/time 等数据时,用 `splatId = baseIndex + localId` 做偏移.

落点:

- `Runtime/GsplatSortPass.cs`: 新增并透传 `BaseIndex`.
- `Runtime/Shaders/Gsplat.compute`: 新增 `e_baseIndex`,并在 `CalcDistance` 用它计算 splatId.

边界注意:

- 当 `sortCount` 变化时,要重置 payload 初始化状态,
  否则旧 payload 值可能越界导致排序/渲染读到错误数据.

### 3) 子范围 draw: `_SplatBaseIndex + orderLocalId`
渲染侧关键点:

- shader 原来是: `splatId = _OrderBuffer[localIndex]`.
- 子范围模式下需要变成: `splatId = _SplatBaseIndex + _OrderBuffer[localIndex]`.

落点:

- `Runtime/Shaders/Gsplat.shader`: 新增 `_SplatBaseIndex` 并做上述映射.
- `Runtime/GsplatRendererImpl.cs`: 每次 draw 设置 `_SplatBaseIndex`.

安全注意:

- C# 侧要做越界门禁,避免 GPU 侧 out-of-bounds 读导致随机消失/花屏.

## 验证
推荐用“证据 + 体感”双验证:

1. 证据:
   - 启用优化时打印一次明确日志(例如 “已启用 keyframe segment 子范围 sort/draw 优化”).
   - GPU sort 的处理规模应从 totalRecords 下降为 recordsPerSegment.
2. 体感:
   - PlayMode 下拖动 `TimeNormalized` 不再卡顿.
   - `AutoPlay` 不再卡成 PPT.

## 备注(后续可做但不必一次做完)
- 当前优化只减少了“每帧排序/绘制的 record 数”.
  - sorter 的 scratch buffers 仍可能按 totalRecords 分配,这会浪费 VRAM.
  - 如果 VRAM 压力明显,可以进一步把 sort 资源按“最大 segment count”分配(需要更谨慎的重建策略).
