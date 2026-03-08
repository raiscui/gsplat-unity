## 1. Aperture API 与 Inspector

- [x] 1.1 为 `GsplatRenderer` 增加 LiDAR 口径模式与 frustum camera 配置字段,并补齐默认值与 validate 防御.
- [x] 1.2 为 `GsplatSequenceRenderer` 增加同款口径模式、frustum camera、`LidarExternalStaticTargets`、`LidarExternalDynamicTargets` 与 `LidarExternalDynamicUpdateHz` 字段,保持两条 runtime API 一致.
- [x] 1.3 实现旧 `LidarExternalTargets` 到新 static/dynamic 输入的兼容读取或迁移提示,保证老场景在升级后不会丢失 external scan 功能.
- [x] 1.4 在 frustum 模式的 API 与 Inspector 文案里明确写出 sensor-frame 契约:
  - frustum camera 直接提供 sensor origin + aperture 朝向 + projection
  - `LidarOrigin` 在 frustum 模式下不再是必填原点
- [x] 1.5 更新 `Editor/GsplatRendererEditor.cs` 与 `Editor/GsplatSequenceRendererEditor.cs`,在 LiDAR 面板中暴露 aperture mode、frustum camera、static/dynamic arrays 与 dynamic updateHz,并补充必要说明文案.

## 2. Frustum active cells 与 LiDAR 资源

- [x] 2.1 扩展 `GsplatLidarScan` 的 cell 生成逻辑,让 frustum 模式按 camera 水平/垂直 FOV 基于 `LidarAzimuthBins` 与 `LidarBeamCount` 推导 active cells.
- [x] 2.2 让 frustum 模式的 active cell 计算以“保持当前屏幕内点密度观感”为目标,避免把完整 360 基线 cell 数硬塞进更窄口径导致明显变密.
- [x] 2.3 更新 LiDAR LUT、range image 与 external-hit buffers 的尺寸/重建规则,使其能同时支持旧 360 模式与新的 frustum active-cell 布局.
- [x] 2.4 实现 frustum sensor frame 构建:
  - translation = `frustumCamera.transform.position`
  - rotation = `frustumCamera.transform.rotation`
  - projection / aspect / pixelRect semantics = `frustumCamera`
- [x] 2.5 为 frustum camera 缺失、失效或不满足运行条件的情况补齐安全回退,保证系统能明确退回旧路径或禁用新路径而不是进入不确定状态.

## 3. External GPU capture 基础设施

- [x] 3.1 新增或重构 external capture helper/manager,为 frustum 模式创建 depth / surfaceColor capture 所需的 RT、材质与命令提交流程.
- [x] 3.2 为 `LidarExternalStaticTargets` 建立 GPU capture 输入收集路径,支持静态 mesh 渲染列表与资源复用.
- [x] 3.3 为 `LidarExternalDynamicTargets` 建立独立 GPU capture 输入收集路径,并兼容 `SkinnedMeshRenderer` 等动态目标.
- [x] 3.4 实现 external depth capture pass,并保证 capture 所用视图严格对齐 frustum camera 的 sensor frame.
- [x] 3.5 实现 external surface main-color capture pass:
  - 对齐当前 `_BaseColor` / `_Color` 语义
  - 不读取 lit scene color / 后处理结果
- [x] 3.6 实现把 external depth 结果整理成 LiDAR 每-cell external hit buffer 的 GPU resolve pass:
  - depth RT -> linear hit position
  - hit position -> LiDAR local / sensor space
  - hit position -> active-cell ray `depth` / `depthSq`
- [x] 3.7 让 frustum GPU capture 优先走显式 render list + override material / command buffer draw,避免依赖 source renderer 当前的 scene-visible 状态.

## 4. Static / Dynamic 更新策略与 fallback

- [x] 4.1 为 static capture 定义完整 invalidation signature,至少覆盖:
  - frustum camera position / rotation / projection / aspect / pixelRect
  - capture RT layout / cell-to-RT mapping
  - renderer enabled / active 状态
  - transform / mesh
  - `_BaseColor` / `_Color` / material slot 映射
- [x] 4.2 只在上述 static signature 失效时重建 static capture,并提供可调试的脏标记/签名比较路径.
- [x] 4.3 为 dynamic 组实现独立于 `LidarUpdateHz` 的更新门禁,允许在未到下一次更新时间时复用上一轮 dynamic capture.
- [x] 4.4 在 LiDAR 更新链路中接入 static/dynamic 两组 capture 调度,确保它们与现有 RadarScan tick 协同工作而不是互相阻塞.
- [x] 4.5 保留当前 CPU `RaycastCommand` external helper 作为旧 360 模式或不支持 GPU capture 平台的 fallback/debug 路线,并明确切换条件.
- [x] 4.6 补齐 frustum/GPU capture 资源的创建、释放与模式切换清理逻辑,避免在禁用 LiDAR、切换模式或销毁 renderer 时遗留脏资源.

## 5. external / gsplat 命中合并与最终显示语义

- [x] 5.1 把 external GPU hit 接入现有 LiDAR 每-cell 最近命中竞争链路,保持 external 与 gsplat 的 nearest-hit 规则一致.
- [x] 5.2 让 `Depth` 与 `SplatColorSH0` 在 frustum 模式下继续保持现有颜色语义,其中 external 命中仍使用深度着色或 external surface 主色.
- [x] 5.3 更新最终点云重建与 draw 提交,确认 external GPU resolve 写入的是与当前 gsplat 路线一致的 LiDAR `depth` / `depthSq` 语义,而不是普通相机 depth.
- [x] 5.4 保持 external target 的三态可见性模式(`KeepVisible`、`ForceRenderingOff`、`ForceRenderingOffInPlayMode`)在 frustum 模式下继续生效,且 external target 仍参与扫描.
- [x] 5.5 让 show/hide / visibility bounds 继续覆盖 gsplat 与 external targets 的联合范围,避免 frustum 模式下 overlay 或 reveal 区域退化.

## 6. 测试、文档与验证

- [x] 6.1 增加 EditMode tests,覆盖 aperture mode 默认值/校验、frustum active-cell 推导、旧 external 字段兼容与 dynamic updateHz 门禁.
- [x] 6.2 增加针对 frustum sensor frame 的回归测试,锁定:
  - frustum camera 直接决定 sensor origin / 朝向 / projection
  - frustum 模式不再依赖独立 `LidarOrigin`
- [x] 6.3 增加针对 static capture invalidation signature 的回归测试,覆盖材质主色、renderer active 状态与 capture layout 变化.
- [x] 6.4 增加针对 external depth resolve 语义的回归测试,确保 GPU 路线写入的是 LiDAR ray-distance / `depthSq`,而不是原始 hardware depth.
- [x] 6.5 增加针对 frustum 模式下 external visibility 三态与联合 bounds 的回归测试,确保既有 scan-only 语义不回退.
- [x] 6.6 更新 `README.md` 与 `CHANGELOG.md`,说明 frustum 口径、static/dynamic external inputs、sensor-frame 契约、GPU capture 语义与 fallback 行为.
- [x] 6.7 运行 OpenSpec 状态检查与相关 Unity EditMode 回归,确认 change artifacts 完整且实现阶段具备可验证入口.
