## Context

当前 RadarScan 已经具备稳定的 LiDAR 可视化链路:

- gsplat 命中来源由 GPU compute 重建 range image
- 每个 cell 只保留最近的 gsplat 命中
- 最终 draw 阶段从 range image 重建规则点云
- external target 也已经能参与 first return,但当前实现仍然依赖:
  - 360 度方向 LUT
  - CPU `RaycastCommand`
  - skinned mesh 的 `BakeMesh()`
  - 每 tick 把 external hit 全量上传回 GPU

这条 external target 路线在“功能上成立”,但用户已经实测到明显性能问题. 同时用户明确给了新的约束:

- 不希望通过降低当前视觉密度、扫描频率或 RGB 语义来换性能
- 扫描口径不再需要 360 度,只需要 camera frustum
- external target 需要拆成 static / dynamic 两组
- external mesh 路线可以接受 GPU depth/color 实现

因此这次 change 的目标不是在现有 CPU raycast 路线上继续打补丁,而是把 external target 与 LiDAR aperture 一起切到更适合实时表现的架构.

## Goals / Non-Goals

**Goals:**

- 为 RadarScan 增加基于 camera frustum 的扫描口径模式.
- 保留现有 `LidarAzimuthBins` / `LidarBeamCount` 作为“密度基线”,在 frustum 模式下尽量保持屏幕内的点密度观感不突变.
- 将 external target 输入拆成 static / dynamic 两组,并允许 dynamic 组独立更新频率.
- 把 external mesh 的主扫描路径改成 GPU depth/color 采集,替代当前 CPU `RaycastCommand` 为主的实现.
- 保持 external hit 与 gsplat hit 的最近距离竞争语义.
- 保持 `Depth` / `SplatColorSH0`、show/hide、scan-only 可见性等既有视觉语义.
- 为静态组提供“相机/目标未变化时可复用”的缓存策略,避免不必要的重复重建.

**Non-Goals:**

- 不移除现有 360 度 LiDAR 扫描路径.
- 不改变当前 RadarScan 的颜色语义或 show/hide 动画语言.
- 不引入贴图级材质采样、法线反射强度或多回波等高阶传感器仿真.
- 不在本 change 内处理 VFX Graph 专用后端.
- 不承诺“static target 永远只扫描一次”.
  - static 结果只有在 frustum camera 位姿/投影与 static target transform 都未变化时才可复用.

## Decisions

### 1) 新增 frustum 模式,而不是直接替换 360 度模式

**Decision:**

- 新增一个 LiDAR aperture / projection 模式,让用户可以在:
  - 现有 360 度模式
  - camera frustum 模式
 之间切换.
- frustum 模式下需要显式指定一个 camera 作为扫描口径来源.
- frustum 模式下,指定的 frustum camera 直接作为 authoritative LiDAR sensor pose.
- 指定的 frustum camera 提供:
  - sensor origin / translation
  - aperture 的朝向基准( forward / up / right )
  - projection / FOV / aspect / pixelRect 语义
- external GPU capture 在 frustum 模式下直接使用 frustum camera 的完整 pose/projection:
  - translation = `frustumCamera.transform.position`
  - rotation = `frustumCamera.transform.rotation`
  - projection / aspect / pixelRect semantics = `frustumCamera`
- `LidarOrigin` 在 frustum 模式下不再作为必填原点.
  - 它继续保留给旧 360 模式或非 frustum 路线使用.

**Alternatives considered:**

- A) 直接把现有 360 度模式整体改成 frustum
  - 问题: 会破坏既有场景的行为,也无法平滑迁移.
- B) 继续保持 360 度,只优化 external target
  - 问题: 大量不可见方向仍然被白算,收益有限.
- C) 继续保留 `LidarOrigin` 为 frustum 模式的 beam origin
  - 问题: 用户已经明确决定“直接用相机位置”,继续保留双原点只会增加配置复杂度和理解成本.
- D) 只取 frustum camera 的 FOV,完全忽略它的朝向
  - 问题: 用户已经明确希望“用一个 cam 作为扫描口径”,camera 的朝向不应被忽略.

**Why:**

- 新增模式可以保留兼容性.
- frustum 模式能直接切掉“视口之外”的无效扫描,是这轮最大的收益来源之一.
- frustum camera 直接承担 sensor origin + aperture frame,参数心智模型更简单.
- `LidarOrigin` 退回旧 360 路线使用,可以避免 frustum 模式里同时维护两个 origin 的歧义.

### 2) `LidarAzimuthBins` / `LidarBeamCount` 继续保留为“密度基线”,active cells 按 frustum 角度推导

**Decision:**

- 不直接把 `LidarAzimuthBins` / `LidarBeamCount` 解释为“frustum 模式下的最终 active cell 数”.
- 改为:
  - 水平方向以 360 度为基线密度
  - 垂直方向以当前 LiDAR 垂直角范围为基线密度
- frustum 模式下的 active horizontal / vertical cell 数,由 camera FOV 相对基线角度推导并取整.

**Alternatives considered:**

- A) 在 frustum 模式下仍然强行用完整 `2048 * 128`
  - 问题: 可见区域点密度会骤增,视觉会变.
- B) 让用户再配置一套 frustum 专用 bin 数
  - 问题: 参数体系会明显变复杂,且用户已经明确不想手工改视觉密度.

**Why:**

- 这样可以最大程度保住当前屏幕内的“点有多少、看起来多密”的感觉.
- 也能避免用户为了性能优化被迫重新调视觉参数.

### 3) external target 从单数组拆成 static / dynamic 两组,并用不同更新策略

**Decision:**

- public API 拆成:
  - `LidarExternalStaticTargets`
  - `LidarExternalDynamicTargets`
  - `LidarExternalDynamicUpdateHz`
- static 组:
  - 只有在 static capture signature 失效时才重新捕获
- dynamic 组:
  - 允许按独立频率更新
  - 未到更新时间时,允许继续复用上一帧/上一轮结果
- static capture signature 至少包含:
  - frustum camera 的 position / rotation / projection / FOV / aspect / pixelRect
  - capture RT layout(宽高,cell-to-RT mapping 规则)
  - static renderer 的 enabled / active 状态
  - static renderer 的 transform / mesh
  - external surface main color 语义所依赖的材质状态( `_BaseColor` / `_Color` / material slot 映射 )

**Alternatives considered:**

- A) 保持一个数组,内部猜哪些是 static / dynamic
  - 问题: 对用户不透明,而且对 `SkinnedMeshRenderer`/动画 prefab 的判断不稳.
- B) 让 static 组永远只扫描一次
  - 问题: 只要 camera 变了,screen-space depth/color 结果就变了,这个语义不成立.
- C) 只把 transform / mesh 当作 invalidation 条件
  - 问题: 材质主色、renderer active 状态、capture layout 变化同样会导致复用结果过期.

**Why:**

- 两组输入最直观.
- 同时把“camera 变化也会导致 static capture 失效”这件事明确进设计,避免后续实现犯方向性错误.
- 把材质主色与 capture layout 一并纳入 signature,可以避免“几何没变但颜色/采样布局已经过期”的隐性 bug.

### 4) external mesh 主路径改为 GPU depth/color capture,不再把 CPU raycast 当作默认实现

**Decision:**

- 在 frustum 模式下,external targets 优先走 GPU depth/color capture:
  - 使用 frustum camera 的完整 sensor pose/projection
  - 输出 external depth RT 与 external surfaceColor RT
  - 再通过 GPU 侧 resolve pass 把它们整理成 external hit buffer
- 当前 CPU raycast helper 保留为:
  - 旧 360 模式 fallback
  - 或调试/兼容路径
- external depth RT 只是中间结果,不能直接等同于当前 LiDAR external hit buffer 里的距离语义.
  - GPU resolve pass 必须执行:
    - depth RT -> linear hit position
    - hit position -> LiDAR local / sensor space
    - 按 active cell center ray 投影为 LiDAR `depth` / `depthSq`
  - 这样写入的 external range 才能与当前 `Gsplat.compute` 的 `depthSq` 语义一致.
- `surfaceColor RT` 不是普通“场景最终颜色”.
  - v1 只保留当前 external color 语义:
    - 材质 `_BaseColor`
    - 或 `_Color`
  - 不引入:
    - 贴图采样
    - 实时光照
    - 后处理
    - tonemap 后的最终 scene color
- external GPU capture 应优先采用显式 render list + override material / command buffer draw.
  - 不应依赖“scene 里普通 renderer 当前是否可见”来决定 capture 结果.
  - 这样才能与 `KeepVisible / ForceRenderingOff / ForceRenderingOffInPlayMode` 三态可见性保持兼容.

**Alternatives considered:**

- A) 继续优化 `RaycastCommand`
  - 问题: 即使做缓存,在高 cellCount 下仍然是 CPU 重路径.
- B) external hit 直接在最终 LiDAR shader 里采样 depth RT,不经过 buffer
  - 问题: 会让最终点云 shader 同时承担更多投影/采样分支,并增加多路径耦合.
- C) 直接把硬件 depth 当作 external range
  - 问题: 当前 LiDAR 存的是“沿离散射线的 depthSq”,不是普通相机 `view-space z`.
- D) 直接复用 scene color / lit color RT
  - 问题: 会破坏现有 external 的“材质主色”语义,并引入光照/后处理干扰.
- E) 用 hidden camera 直接渲染 source renderers 的当前可见状态
  - 问题: 容易与 `forceRenderingOff` 的 scan-only 语义冲突.

**Why:**

- GPU depth/color 更适合 camera frustum 语义.
- 先把 external capture resolve 成 buffer,能最大程度复用当前“gsplat hit + external hit 逐 cell 比较”的稳定绘制路径.
- depth 与颜色都先做“语义对齐”再写 buffer,可以避免后续出现“性能提升了,但点位置和颜色悄悄不对”的回归.

### 5) external GPU capture 采用 static / dynamic 分层渲染,最终在 GPU 上合并

**Decision:**

- static 与 dynamic 使用分离的 capture 路径或分离的 render lists.
- 最终 external 深度/颜色结果在 GPU 上合并:
  - 取最近深度
  - 颜色跟随最近命中的 external surface
- dynamic 更新未触发时,static 结果仍可直接复用; dynamic 结果是否复用由独立更新门禁控制.
- static / dynamic 的 capture 与 resolve 都必须基于同一套 active-cell mapping 与 LiDAR depth 语义.
  - 不允许 static 用一种投影规则,dynamic 再用另一种规则,否则最终 nearest-hit 合并会失去一致性.

**Alternatives considered:**

- A) static 和 dynamic 强行渲染到同一条每帧都完整重建的路径
  - 问题: 无法体现分组带来的性能收益.
- B) CPU 侧合并两组 external hit
  - 问题: 又把数据拉回 CPU,与 GPU 路线目标相悖.

**Why:**

- 让“分组”真正产生收益,而不是只是换了两个字段名.
- 保持 external nearest-hit 合并逻辑仍在 GPU.
- 保持 static / dynamic 在同一几何契约下工作,才能让复用和合并都保持可预测.

### 6) 保留现有 LiDAR 最终 draw 语义,只替换上游 aperture 与 external-hit 生产方式

**Decision:**

- 最终 `GsplatLidarScan.RenderPointCloud(...)` 继续保留:
  - LiDAR 粒子形态
  - color mode
  - show/hide
  - visibility mode
- 本次主要替换:
  - active cell 定义方式
  - external hit 的生产方式

**Alternatives considered:**

- A) 连最终点云 shader 一起彻底重写
  - 问题: 风险大,也更容易引入视觉回归.

**Why:**

- 用户最在意的是“不要改坏现在的视觉形态”.
- 保持最后一层 draw 尽量不动,是风险最低的路线.

## Risks / Trade-offs

- [风险] frustum 模式下“密度保持不变”的定义如果不够清楚,容易出现点数突然变多或变少
  - 缓解: 在 design/spec 中明确“保的是角密度与屏幕内观感”,而不是硬保全量 cell 数
- [风险] static capture 只有在 camera 不变时才能真正复用,如果 camera 每帧都动,收益会低于预期
  - 缓解: 把该条件写成显式 invalidation 规则,避免错误承诺“static 只扫一次”
- [风险] GPU depth/color 方案需要跨 BiRP/URP/HDRP 保持稳定
  - 缓解: 优先采用 package 内可控的离屏 render / command buffer 路线,并保留 CPU fallback
- [风险] `SplatColorSH0` 下 external target 的材质主色在 GPU capture 路线上可能与当前 CPU 路线不完全一致
  - 缓解: v1 继续把 external 颜色语义限制为材质主色 override pass,不扩展到贴图采样或 lit color
- [风险] dynamic 组降频后会出现短暂的“上一轮 external capture 残留”
  - 缓解: 在 spec 中把这定义为显式预算型行为,并允许用户把 dynamic 频率调高
- [风险] 需要同时维护旧 360 CPU 路线和新 frustum GPU 路线,实现复杂度上升
  - 缓解: 明确把 CPU raycast helper 下沉为 fallback/debug 路线,不要让两条路径长期并行演化出两套语义
- [风险] 如果 frustum 模式下“camera 就是 LiDAR sensor pose”的契约没有写死,实现很容易在不同模块里偷偷回退到旧 `LidarOrigin` 语义
  - 缓解: 直接明确“frustum mode = camera 位置和朝向就是 LiDAR sensor pose”,不再保留双 origin 歧义
- [风险] 如果 static capture 没把 renderer/material/capture-layout 变化纳入 invalidation,会出现“几何没变但颜色或布局已过期”的复用错误
  - 缓解: 把 invalidation signature 扩展成明确字段集合,并做回归测试锁定
- [风险] frustum 模式直接使用 camera 位置后,无法表达“相机和 LiDAR 真实有安装偏移”的 rig
  - 缓解: 把这件事明确为当前 frustum v1 的简化约束; 若后续确实需要 rig offset,再单独扩展 sensor rig 配置

## Migration Plan

- 默认保持当前 360 度 LiDAR 路线不变.
- 只有当用户显式启用 frustum 模式并指定 camera 时,才进入新路径.
- 对已有 external target 用户:
  - 可以先继续沿用旧数组并通过迁移脚本或 `OnValidate` 自愈分流到 static / dynamic 两组
  - 未完成迁移时,系统可先将旧数组整体视作 dynamic 组,保证功能不丢
- 对 frustum mode 用户:
  - frustum camera 现在直接负责 sensor origin、aperture 朝向与 projection
  - `LidarOrigin` 在 frustum 模式下不再是必填项
  - 旧 360 模式继续保留 `LidarOrigin` 语义
- 若新 GPU 路线在某个平台或管线出现回归:
  - 回退到旧 360 / CPU external path
  - 或仅对 external targets 使用 CPU fallback

## Open Questions

- static / dynamic 的旧字段迁移策略最终落在哪一层:
  - 直接 `OnValidate` 自动迁移
  - 还是保留旧字段一段时间后再移除
- external GPU capture 的实现形式:
  - 统一走显式 render list + `CommandBuffer.DrawRenderer`
  - 还是在某些平台上仍保留 hidden camera 作为次优 fallback
- 360 模式是否需要后续也复用 frustum GPU capture 的部分设施,作为长期统一架构的一部分
