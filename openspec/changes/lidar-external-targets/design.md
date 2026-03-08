## Context

当前包里的 RadarScan 已经具备一套稳定的 LiDAR 可视化链路:

- gsplat 命中来源由 GPU compute 重建 range image
- 每个 `(beam, azBin)` 只保留最近的 gsplat 命中
- draw 阶段从 range image 重建规则点云
- `Depth / SplatColorSH0`、show/hide、HideSplatsWhenLidarEnabled 都已经有既定语义

这次需求不是替换这条链路,而是在它旁边增加“外部 Unity 模型”命中源,并让它与 gsplat 共享同一套 first return 竞争逻辑.

约束与现状:

- 包是 UPM package,默认行为必须保持不变.
- 现有 LiDAR 主命中源来自 GPU buffer,而外部 `GameObject[]` 的几何来源是 Unity 场景层级中的 Renderer/Mesh.
- 用户已经明确要求:
  - 外部模型必须使用真实 mesh 参与扫描
  - 外部模型与 gsplat 需要互相遮挡
  - `SplatColorSH0` 下外部模型命中显示材质主色
  - 参与扫描的外部模型默认不应继续显示普通 mesh shader 效果,应更接近“scan-only 目标”
- 当前 show/hide 的覆盖半径只按 gsplat 自身 bounds 计算,如果外部模型范围更大,必须扩成联合范围.

## Goals / Non-Goals

**Goals:**

- 为两个 Renderer 新增 `LidarExternalTargets : GameObject[]`.
- 让外部模型与 gsplat 在每个 `(beam, azBin)` 上竞争 first return 最近距离.
- 静态 mesh 与 `SkinnedMeshRenderer` 都使用真实 mesh 参与命中.
- 让 draw 阶段能够区分“gsplat 命中”和“external 命中”,并按现有颜色模式给出正确颜色.
- 保持现有 gsplat compute / draw 链路尽量不动,把外部模型扫描作为旁路能力接入.
- 扩展 show/hide bounds,让外部目标也受 RadarScan 显隐覆盖.
- 让 external target 可以只显示雷达粒子,不再强制显示普通 mesh.

**Non-Goals:**

- 不把外部模型转换成新的 gsplat 数据或写回 gsplat buffer.
- 不实现多回波、多材质贴图采样、法线强度模型、噪声掉点等高阶传感器仿真.
- 不引入 VFX Graph 专用外部目标扫描路径.
- 不使用近似碰撞体替代真实 mesh.

## Decisions

### 1) 保留现有 gsplat range image,外部模型作为第二命中源并在 draw 阶段合并

**Decision:**

- 保留 `GsplatLidarScan.TryRebuildRangeImage(...)` 现有职责,继续只负责 gsplat 命中.
- 外部模型命中结果单独写入一套 external hit buffers.
- `GsplatLidar.shader` 在每个 cell 同时读取:
  - gsplat `minRangeSqBits/minSplatId`
  - external `rangeSqBits/baseColor`
  然后选择最近命中作为最终显示来源.

**Alternatives considered:**

- A) 把外部模型命中也回写进现有 gsplat `minRangeSqBits/minSplatId`
  - 问题: external hit 没有 splatId 语义,颜色来源也不同,会把现有 shader 逻辑搅乱.
- B) 在 CPU 侧直接生成最终点云位置并跳过现有 LiDAR shader
  - 问题: 会复制现有 show/hide、颜色混合、余辉逻辑,维护成本高.

**Why:**

- 现有 gsplat LiDAR 路径已经稳定,本次最稳的策略是“新增一条平行命中源,在最终显示处汇合”.
- 这样可以最大程度复用现有 show/hide、余辉和颜色过渡语义.

### 2) 外部模型扫描使用隔离 PhysicsScene + 真实 mesh 代理 + RaycastCommand

**Decision:**

- 为每个启用 LiDAR 的 Renderer 维护一个内部 external-target helper.
- helper 创建隔离 `PhysicsScene`,并为 `LidarExternalTargets` 收集到的 mesh 建立隐藏代理对象:
  - 静态模型: `MeshCollider(sharedMesh)`
  - 蒙皮模型: `SkinnedMeshRenderer.BakeMesh()` 到复用的临时 mesh,再赋给 `MeshCollider`
- 用 `RaycastCommand(physicsScene, ...)` 批量发射 `beamCount * azimuthBins` 条射线,得到每个 cell 的外部最近 hit.

**Alternatives considered:**

- A) 直接使用主场景 Physics 查询
  - 问题: 会受到用户场景现有 Collider 的污染,也不方便只扫描目标数组内对象.
- B) GPU 深度/离屏渲染方式采集外部模型
  - 问题: 需要按渲染管线管理深度采集,跨 BiRP/URP/HDRP 复杂度显著更高.
- C) 用球体/胶囊/盒体近似 Collider
  - 问题: 用户已明确拒绝,且 first return 轮廓会严重偏离真实 mesh.

**Why:**

- 隔离 `PhysicsScene` 可以只包含目标数组中的代理碰撞体,不会污染主场景.
- `RaycastCommand` 能把 262k 条射线分批并行调度,比逐条 `PhysicsScene.Raycast` 更可控.
- 真实 mesh 代理满足用户对命中精度的要求.

### 3) 外部目标以“根对象递归收集 Renderer”的方式建模

**Decision:**

- `LidarExternalTargets` 的每个元素都视为根对象.
- helper 递归收集其子层级中的:
  - `MeshRenderer + MeshFilter`
  - `SkinnedMeshRenderer`
- 不支持的 Renderer 类型忽略,并仅打印一次可行动日志.

**Alternatives considered:**

- A) 只扫描数组对象自身,不递归子层级
  - 问题: 对实际场景 prefab 很不友好,用户还要手动拆每个子节点.
- B) 扫描所有场景对象,数组只当过滤层
  - 问题: 范围过大,容易产生非预期命中和性能问题.

**Why:**

- “根对象递归展开”最符合 Unity 用户对 `GameObject[]` 的直觉.
- 只扫描显式列入数组的对象,边界清晰.

### 4) 外部命中颜色固定为“材质主色”,并在 draw 阶段与 gsplat 颜色并行选择

**Decision:**

- external hit 缓冲直接存最终 `baseColor`.
- 材质主色解析顺序固定为:
  - `_BaseColor`
  - `_Color`
  - 默认白色
- 多材质 mesh 通过 `RaycastHit.triangleIndex -> submesh -> sharedMaterials[subMeshIndex]` 解析命中材质.

**Alternatives considered:**

- A) external hit 在 `SplatColorSH0` 下回退成深度色
  - 问题: 用户已经明确希望外部模型使用材质主色.
- B) 读取贴图采样色
  - 问题: 需要 UV/纹理读回,复杂度远超本次目标.

**Why:**

- 主色是最稳定、成本最低、可解释性最强的颜色来源.
- 直接把 external 命中颜色缓冲化,可以避免 shader 再去理解 Unity 材质系统.

### 5) show/hide、visibility center 与最大半径使用“gsplat + external”的联合 bounds

**Decision:**

- 新增内部 bounds 计算:
  - gsplat 本地 bounds
  - external target world bounds
  - 将 external world bounds 转回 renderer local 后求联合 bounds
- `BuildLidarShowHideOverlayForThisFrame(...)` 改用联合 bounds.

**Alternatives considered:**

- A) 保持只使用 gsplat bounds
  - 问题: 外部目标超出 gsplat 范围时会被 show/hide 半径提前裁掉.
- B) 对 external 单独做第二套 show/hide
  - 问题: 会破坏当前“一个 RadarScan 只有一套显隐中心与时间轴”的语义.

**Why:**

- 联合 bounds 是最自然的扩展,也能保持现有可见性动画逻辑不分叉.

### 6) external helper 作为独立运行时模块,由两个 Renderer 共享调用模式

**Decision:**

- 在 `Runtime/Lidar/` 下新增独立 helper 类,负责:
  - external target 发现与缓存
  - proxy object 生命周期
  - skinned bake 与 collider 更新
  - batch raycast
  - external hit 缓冲更新
- `GsplatRenderer` 与 `GsplatSequenceRenderer` 只负责:
  - 提供 LiDAR 参数
  - 在 `UpdateHz` tick 时调用 helper
  - 在 draw 时把 helper 缓冲传给 `GsplatLidarScan.RenderPointCloud(...)`

**Alternatives considered:**

- A) 直接把所有 external 逻辑塞进两个 Renderer
  - 问题: 两份实现很容易漂移,也会让 renderer 文件继续膨胀.

**Why:**

- external target 扫描属于单独的子系统,抽到 `Runtime/Lidar/` 下更符合现有目录结构.
- 两个 Renderer 可以共享完全一致的行为语义.

### 7) external target 的普通 mesh 可见性优先采用 `forceRenderingOff`

**Decision:**

- 系统为两个 Renderer 增加 external target 可见性模式字段.
- 第一版只提供两态:
  - `KeepVisible`
  - `ForceRenderingOff`
  - `ForceRenderingOffInPlayMode`
- 默认值为 `ForceRenderingOff`.
- helper 在追踪 source renderer 时负责:
  - 保存原始 `Renderer.forceRenderingOff`
  - 按当前模式应用/撤销覆盖
  - 在目标移除或 helper Dispose 时恢复原值

**Alternatives considered:**

- A) 直接把 `renderer.enabled=false`
  - 问题: 当前 helper 会把 disabled renderer 视为“不参与扫描”.
- B) 直接把目标 `GameObject.SetActive(false)`
  - 问题: 同样会丢失扫描资格,且还会影响用户层级逻辑.
- C) 用 layer + camera culling mask 隐藏
  - 问题: 会牵连用户现有 camera/physics 分层语义,侵入更强.

**Why:**

- `forceRenderingOff` 更接近“保留对象语义,只关闭普通可见渲染”.
- 它不会破坏 helper 对 source renderer、mesh、bounds、material 的读取路径.
- 相比 layer 方案,它不要求用户重配相机 culling mask,也不用改物理层矩阵.
- `ForceRenderingOffInPlayMode` 让用户在编辑器摆场景时仍能看见原始模型,但进入 Play 后自动切成 scan-only,适合调试与最终表现并存的工作流.

## Risks / Trade-offs

- [风险] `SkinnedMeshRenderer.BakeMesh()` 在高 `UpdateHz` 下可能带来明显 CPU 开销
  - 缓解: 只对 `LidarExternalTargets` 中实际包含的 skinned renderer 执行 bake,并与 `LidarUpdateHz` 绑定节流
- [风险] `RaycastCommand` 的 262k 级别查询在目标过多时可能成为瓶颈
  - 缓解: 继续沿用现有 `LidarAzimuthBins`/`LidarBeamCount` 调参降级路径,并在 design 落地时优先复用预分配 `NativeArray`
- [风险] 多材质 mesh 的 triangleIndex -> submesh 解析如果每次 hit 线性扫描,会有额外热点
  - 缓解: 预缓存每个 mesh 的 submesh triangle ordinal range,hit 时只做范围判断
- [风险] 隔离 PhysicsScene 里的代理对象如果生命周期管理不好,会在 EditMode 下残留隐藏对象
  - 缓解: helper 必须在 `OnDisable/OnDestroy/Dispose` 里统一清理 proxy scene 与 baked mesh
- [风险] external target 被强制 `forceRenderingOff` 后,若恢复逻辑遗漏,会污染用户原始场景状态
  - 缓解: helper 对每个 tracked renderer 保存原始值,并在 remove/dispose 路径统一恢复
- [风险] LiDAR shader 新增 external buffers 后,Metal 平台再次出现“声明了 StructuredBuffer 但未绑定”跳绘制
  - 缓解: 外部命中缓冲一律视为必绑资源,即使没有 external target 也绑定空缓冲
- [取舍] 这次 external hit 颜色只支持材质主色,不读取贴图
  - 缓解: 在文档里明确语义,未来若真有需求再单独扩展 capability

## Migration Plan

- 默认 `LidarExternalTargets` 为空数组或 `null`,升级旧场景时行为保持不变.
- 只有用户显式配置外部目标数组时,external helper 才创建 proxy scene 与 raycast 资源.
- 若实现出现回归,可以通过将 `LidarExternalTargets` 清空回退到当前纯 gsplat RadarScan 行为,不影响既有 LiDAR 参数与场景序列化.

## Open Questions

- 无. 本 change 的关键取舍已经在需求收敛阶段确定:
  - 外部目标与 gsplat 互相遮挡
  - 使用真实 mesh
  - 支持 `SkinnedMeshRenderer`
  - `SplatColorSH0` 下外部命中取材质主色
