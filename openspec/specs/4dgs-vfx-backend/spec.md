# Capability: 4dgs-vfx-backend

## Purpose
定义 Unity VFX Graph 的可选 4DGS 后端.
覆盖编译隔离、GPU buffer 绑定、接近 SplatVFX 的导入工作流、4D 语义一致性、视觉目标与规模上限策略.

## Requirements

### Requirement: VFX backend is optional and does not break compilation
系统 MUST 在项目未安装 Visual Effect Graph 包时仍可编译通过.
系统 MUST 确保 4DGS 的 Gsplat 后端不受影响.

VFX 后端相关代码 MUST 被隔离为可选编译单元.
系统 MUST 提供明确的启用条件(例如 version define 或 scripting define),以避免用户在缺依赖时遭遇编译错误.

#### Scenario: 项目未安装 VFX Graph
- **WHEN** 用户在一个未安装 Visual Effect Graph 的 Unity 项目中导入本包
- **THEN** 项目 MUST 无编译错误,且 Gsplat 渲染仍可用

### Requirement: Bind GS buffers to VisualEffect consistently
当 VFX 后端被启用且场景中存在 `VisualEffect` 时,系统 MUST 能将 4DGS 的 GPU buffers 绑定到 VFX Graph.
绑定内容 MUST 至少包含:
- Position/Scale/Rotation/Color
- 当 `SHBands > 0` 时,还 MUST 绑定 SH
- Velocity/Time/Duration
- SplatCount(用于 VFX Graph 侧控制 spawn 数量与索引上界)
- TimeNormalized(用于驱动 motion/visibility)

#### Scenario: 运行期绑定成功
- **WHEN** 用户在同一 GameObject 上配置了 GsplatRenderer 与 VisualEffect,并启用 VFX 后端
- **THEN** VFX Graph 能读取到已绑定的 GraphicsBuffer,且不会出现空引用或越界采样

### Requirement: Provide SplatVFX-style import workflow for `.splat4d`
当项目启用 VFX 后端且安装了 Visual Effect Graph 时,系统 MUST 提供接近 `SplatVFX` 的导入工作流.

至少应包含:
- `.splat4d` 被导入后,用户能快速得到一个可直接播放的对象(prefab 或等价形式).
- 该对象包含 `VisualEffect` 与对应的 binder,并能自动完成 buffers 的绑定.

#### Scenario: 安装 VFX Graph 时的一键导入体验
- **WHEN** 用户在已安装 Visual Effect Graph 的项目中导入 `.splat4d`
- **THEN** 用户无需手工连线 buffer,即可看到 VFX Graph 正常渲染(在规模未超过上限时)

### Requirement: VFX backend matches 4D motion and visibility semantics
VFX 后端 MUST 与 Gsplat 后端使用相同的:
- 运动模型: `pos(t) = pos0 + vel * (t - time0)`.
- 可见性规则: `t` 在 `[time0, time0 + duration]` 内可见,窗外不可见.

#### Scenario: 同一 t 下两后端可见性一致
- **WHEN** 同一份 4D 资产在同一 `t` 下分别用 Gsplat 后端与 VFX 后端渲染
- **THEN** 不在时间窗内的 splat 在两后端中都 MUST 不可见

### Requirement: Visual parity target for VFX backend
VFX 后端 MUST 尽量复刻 Gsplat 的视觉输出,用于对照与工作流需求.
至少应包含:
- 椭圆协方差投影的 gaussian footprint.
- 与现有 fragment alpha 衰减一致的透明混合行为.
- 可选的 SH 视角相关颜色,并尊重 SHDegree 设置.

#### Scenario: 基准场景下视觉接近
- **WHEN** 使用一个中等规模(例如 <= 1M)的 4D 资产进行对照渲染
- **THEN** VFX 后端输出 MUST 在宏观形状与明暗上与 Gsplat 后端接近

### Requirement: Enforce a hard scale limit for VFX backend
由于 VFX Graph 对超大粒子数不友好,系统 MUST 对 VFX 后端设置明确的规模上限 `MaxSplatsForVfx`.
当 `SplatCount > MaxSplatsForVfx` 时:
- 系统 MUST 禁用 VFX 后端渲染.
- 系统 MUST 输出明确 warning,并建议用户切换到 Gsplat 后端.

#### Scenario: 超过上限时自动禁用 VFX 后端
- **WHEN** 资产 `SplatCount` 大于 `MaxSplatsForVfx`
- **THEN** VFX 后端 MUST 不启动,并输出清晰告警
