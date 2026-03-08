## 1. API 与参数面板

- [x] 1.1 为 `GsplatRenderer` 增加 `LidarExternalTargets : GameObject[]` 序列化字段,并补齐默认值与 `OnValidate` 自愈逻辑.
- [x] 1.2 为 `GsplatSequenceRenderer` 增加同款 `LidarExternalTargets : GameObject[]` 字段,保持静态/序列后端 API 一致.
- [x] 1.3 更新 `Editor/GsplatRendererEditor.cs` 与 `Editor/GsplatSequenceRendererEditor.cs`,在 LiDAR 面板中增加 `External Targets` 配置区与必要提示文案.

## 2. External Target Runtime Helper

- [x] 2.1 在 `Runtime/Lidar/` 新增 external target helper,负责递归收集 `LidarExternalTargets` 下的 `MeshRenderer` / `SkinnedMeshRenderer`.
- [x] 2.2 为静态 mesh 建立真实 `MeshCollider(sharedMesh)` 代理,并把代理对象放入隔离 `PhysicsScene`.
- [x] 2.3 为 `SkinnedMeshRenderer` 建立 baked mesh 复用路径,在 LiDAR 更新 tick 时刷新 `BakeMesh()` 结果并同步到代理 `MeshCollider`.
- [x] 2.4 为 unsupported renderer、缺失 mesh、失效对象和 helper 生命周期清理补齐诊断与释放逻辑.

## 3. External Hit 采样与 first return 合并

- [x] 3.1 生成与 LiDAR `beamCount * azimuthBins` 对齐的批量射线方向,并用 `RaycastCommand` 在隔离 `PhysicsScene` 中执行批量查询.
- [x] 3.2 为 external hit 建立每-cell 结果缓冲,至少包含 `rangeSqBits` 与 `baseColor`.
- [x] 3.3 在 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的 LiDAR 更新链路里接入 external helper,让 external scan 与现有 `LidarUpdateHz` 同步节流.
- [x] 3.4 扩展 `GsplatLidarScan.RenderPointCloud(...)` 与相关资源绑定,让 shader 能同时读取 gsplat hit 与 external hit 并选择最近结果.

## 4. 颜色、显示与联合 bounds

- [x] 4.1 为 external hit 实现材质主色提取,包含 `_BaseColor` / `_Color` 回退顺序.
- [x] 4.2 为多材质 mesh 实现 `triangleIndex -> submesh -> material` 映射缓存,确保 `SplatColorSH0` 下 external hit 使用正确材质主色.
- [x] 4.3 更新 `Runtime/Shaders/GsplatLidar.shader`,让 `Depth` 与 `SplatColorSH0` 都能正确处理 external hit.
- [x] 4.4 让 `BuildLidarShowHideOverlayForThisFrame(...)` 使用 gsplat 与 external targets 的联合 bounds,确保 show/hide 覆盖外部目标.
- [x] 4.5 验证 `HideSplatsWhenLidarEnabled=true` 时 splat sort/draw 关闭但 gsplat/external 扫描与最终 LiDAR draw 保持正常.

## 5. 测试、文档与收尾

- [x] 5.1 增加 EditMode tests,覆盖字段默认行为、external hit 最近距离竞争、材质主色解析与联合 bounds 逻辑.
- [x] 5.2 增加最小 physics 集成测试,验证没有现成 Collider 的 mesh 目标也能通过代理真实 mesh 被 LiDAR 命中.
- [x] 5.3 更新 `README.md` 的 LiDAR 章节,补充 `LidarExternalTargets` 用法、颜色语义与静态/蒙皮目标说明.
- [x] 5.4 更新 `CHANGELOG.md`,记录 RadarScan 新增外部 `GameObject[]` 扫描能力.
- [x] 5.5 运行 OpenSpec 状态检查与相关 Unity EditMode 回归,确认 change 已可进入 apply 阶段.

## 6. External target 普通 mesh 可见性

- [x] 6.1 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 增加 external target 可见性模式字段,默认 `ForceRenderingOff`,并补齐 validate 防御.
- [x] 6.2 更新 `GsplatLidarExternalTargetHelper`,让 `ForceRenderingOff` 模式下 source renderer 继续参与扫描但不显示普通 mesh,并在目标移除/Dispose 时恢复原状态.
- [x] 6.3 更新两个 Inspector 与 README/CHANGELOG,明确 external target 的 scan-only 语义与 `KeepVisible/ForceRenderingOff` 差异.
- [x] 6.4 增加 EditMode tests,覆盖默认值/非法值回退,以及 helper 的隐藏/恢复行为.
- [x] 6.5 重新运行相关 Unity EditMode 回归,确认 change 仍可进入 archive 阶段.

## 7. Play 模式专用隐藏

- [x] 7.1 扩展 `GsplatLidarExternalTargetVisibilityMode`,新增 `ForceRenderingOffInPlayMode`.
- [x] 7.2 更新 `GsplatLidarExternalTargetHelper`,让可见性决策同时考虑 mode 与 `Application.isPlaying`.
- [x] 7.3 更新 Inspector / README / CHANGELOG / OpenSpec 文案,明确三态模式的差异.
- [x] 7.4 增加 EditMode tests,锁定 `ForceRenderingOffInPlayMode` 的决策语义(编辑器显示,Play 隐藏).
- [x] 7.5 重新运行相关 Unity EditMode 回归,确认 change 仍保持可归档状态.
