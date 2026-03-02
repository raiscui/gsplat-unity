# 任务计划: RadarScan show/hide noise 不可见(根因调查)

## 目标

在 Unity 里 RadarScan(LiDAR) 模式下,Show/Hide 期间能稳定看到与 ParticleDots 类似的 noise 颗粒扰动。
如果仍然看不到,也要给出可证据化的根因结论(例如: 实际用的不是这份 shader,或参数未到 shader)。

## 阶段

- [ ] 阶段1: 现场与上下文确认(代码与资产引用)
- [ ] 阶段2: 根因证据采集(参数链路与 shader 路径)
- [ ] 阶段3: 最小修复与验证(只改根因点)
- [ ] 阶段4: 回归测试,更新文档与提交 git

## 关键问题

1. Unity 运行时到底用的是哪一份 `GsplatLidar.shader`(AssetDatabase path)?
2. `showHideNoiseMode/Strength/Scale/Speed` 是否在 show/hide 过渡期真实进入了 draw call?
3. 如果参数链路无断点,但观感仍不可见,是 shader 公式问题还是渲染语义(ZWrite/Blend)导致的“看不出来”?

## 做出的决定

- 先做证据化诊断,再做修复。
  理由: 之前已经多轮“增强噪声”,用户仍反馈完全无变化,必须先排除“根本没跑到我们改的代码”。

## 遇到的错误

- (待补)

## 状态

**目前在阶段2**
- 我刚加了一条 Editor 下的节流诊断日志,会在 LiDAR show/hide 动画期间打印:
  - settings 与 shader 的 AssetDatabase 路径
  - show/hide 与 noise 参数值
  - 材质是否真的有 `_LidarShowHideNoise*` 属性
- 下一步是基于这条日志的结果,锁定断点位置再做最小修复。

## 2026-03-02 18:15:38 +0800 追加: 下一步行动(日志驱动排查)

- [ ] 读取 `Runtime/Shaders/GsplatLidar.shader`,核对 `_LidarShowHideNoise*` 是否存在于 ShaderLab `Properties` 列表,且命名与 C# 侧一致。
- [ ] 读取 `Runtime/Lidar/GsplatLidarScan.cs`,核对 MPB 下发的 property 名称与 shader 完全一致(包含大小写与后缀)。
- [ ] 如果 shader 确实已经声明了 properties,但 `HasProperty` 仍为 0:
  - 追加诊断: 打印 `Shader.GetPropertyCount()` 与前若干 property name,确认 Unity 运行时读到的是新版 shader。
  - 追加诊断: 打印 `Shader.name` 与 `shader.GetInstanceID()` 等,排除 shader 资产被替换/重载。
- [ ] 如果 `HasProperty` 变为 1 但仍“看不到”,做一次强证据化的 shader 可视化:
  - 临时增加一个 debug 开关,把 noise 值直接映射到颜色/亮度,确保能肉眼确认 shader 端收到了 noise 参数。

## 2026-03-02 18:46:40 +0800 进展: 已确认进入 shader,并做幅度调参

- [x] `_LidarShowHideNoise*` 已在 `GsplatLidar.shader` 的 ShaderLab `Properties` 中显式声明(隐藏属性),用于稳态 MPB 绑定。
- [x] 新增 EditMode 单测锁定该“属性契约”,防止未来重构再次丢失。
- [x] 针对用户反馈“只有很小幅度的运动”,已把 LiDAR show/hide 的屏幕空间 warp 改为:
  - 与点半径相关联(更稳定更容易感知).
  - 对 noiseStrength 做 sqrt 提亮(中等强度更明显,仍保持小幅度).
- [x] 已在 `_tmp_gsplat_pkgtests` 跑 EditMode tests(`Gsplat.Tests`):
  - total=44, passed=42, failed=0, skipped=2.

## 2026-03-02 19:00:06 +0800 进展: warp 幅度改为可调(不再与点半径耦合)

- [x] 按用户要求: LiDAR show/hide 的 noise 位移不再跟 `LidarPointRadiusPixels` 绑定。
- [x] 新增可调参数:
  - `GsplatRenderer.LidarShowHideWarpPixels`
  - `GsplatSequenceRenderer.LidarShowHideWarpPixels`
  - shader property: `_LidarShowHideWarpPixels`
- [x] Inspector 已暴露该字段(在 LiDAR Visual 区域),可直接调出你想要的“颗粒扰动幅度”。
- [x] 已在 `_tmp_gsplat_pkgtests` 跑 EditMode tests(`Gsplat.Tests`):
  - total=44, passed=42, failed=0, skipped=2.
