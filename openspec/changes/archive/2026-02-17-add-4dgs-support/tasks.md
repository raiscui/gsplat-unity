## 1. 数据模型与导入

- [x] 1.1 扩展 `Runtime/GsplatAsset.cs`,新增 `Velocities/Times/Durations` 与 motion 统计字段(例如 maxSpeed/maxDuration),并保持旧资产兼容
- [x] 1.2 扩展 `Editor/GsplatImporter.cs`,解析 4D PLY 字段(vx/vy/vz,time,duration)与别名映射,并在缺失时写入安全默认值
- [x] 1.3 新增 `.splat4d` importer,按 spec 的 64-byte record layout 读取 position/scale/rotation/color + velocity/time/duration,并生成 `GsplatAsset`
  - 一期 `.splat4d` 资产强制 `SHBands=0`(不导入高阶 SH)
- [x] 1.4 在导入阶段(PLY 与 `.splat4d`)对 time/duration 做 clamp,并输出包含统计信息的 warning(日志包含 assetPath)
- [x] 1.5 在导入阶段(PLY 与 `.splat4d`)统计 maxSpeed/maxDuration,为后续 bounds 扩展提供输入

## 2. Runtime GPU Buffers 与上传

- [x] 2.1 在 `Runtime/GsplatRendererImpl.cs` 增加 `VelocityBuffer/TimeBuffer/DurationBuffer`,并在 `Dispose/RecreateResources` 中正确释放与重建
- [x] 2.2 在 `Runtime/GsplatRenderer.cs` 的同步上传路径中上传 4D 数组到新 buffers(仅当资产包含 4D 字段时创建/上传)
- [x] 2.3 在 `Runtime/GsplatRenderer.cs` 的异步分批上传路径中支持 4D buffers 的分批 `SetData`(与 Position/Color 等保持一致 offset/count 语义)
- [x] 2.4 在 `Runtime/GsplatRendererImpl.cs` 的 `MaterialPropertyBlock` 绑定中加入 4D buffers 与 `_TimeNormalized` 参数

## 3. 排序 Pass(Compute) 的 4D 扩展

- [x] 3.1 修改 `Runtime/Shaders/Gsplat.compute`,新增 `_VelocityBuffer/_TimeBuffer/_DurationBuffer/_TimeNormalized`,在 `CalcDistance` 中用 `pos(t)` 计算深度 key
- [x] 3.2 在 `CalcDistance` 中实现时间窗可见性判断,对不可见 splat 写入"排序友好"的极端 key(渲染仍以 shader 裁剪为准)
- [x] 3.3 修改 `Runtime/GsplatSortPass.cs`(如需要)与 `Runtime/GsplatSorter.cs`,确保 dispatch 时绑定新增 buffers 并设置 `_TimeNormalized`

## 4. 渲染 Shader 的 4D 扩展

- [x] 4.1 修改 `Runtime/Shaders/Gsplat.shader`,新增 4D buffers 与 `_TimeNormalized` 参数声明
- [x] 4.2 在 vertex 阶段按 `pos(t)=pos0+vel*(t-time0)` 计算动态中心,并在时间窗外输出 `discardVec` 实现硬裁剪
- [x] 4.3 保持现有 SH 计算、GammaToLinear 选项与透明混合行为不变,仅叠加 4D 逻辑

## 5. 播放控制(TimeNormalized API)

- [x] 5.1 选择并实现播放接口形态(推荐新增 `Runtime/GsplatPlayback.cs`,或在 `GsplatRenderer` 内增加可动画字段),并提供 `TimeNormalized/Speed/Loop/AutoPlay`
- [x] 5.2 扩展 `IGsplat`/Sorter 与渲染路径,保证同一帧内排序与渲染使用同一个 `TimeNormalized`(per-renderer)
- [x] 5.3 对 `TimeNormalized` 与 `Speed` 做输入 clamp/约束,并保证对 3D-only 资产不改变结果

## 6. Bounds 与剔除安全

- [x] 6.1 修改 `Runtime/GsplatRendererImpl.cs` 的 `RenderParams.worldBounds` 计算,在存在 4D 字段时按 `maxSpeed*maxDuration` 做保守 bounds 扩展
- [x] 6.2 添加一个最小可复现实例(两高斯移动到静态 bounds 外),验证不会被相机剔除掉

## 7. 资源预算、告警与自动降级

- [x] 7.1 在 `Runtime/GsplatSettings.cs` 增加资源预算配置(阈值比例、VFX 上限、降级策略枚举等)
- [x] 7.2 在创建 buffers 前实现显存估算与日志输出(包含 SplatCount,SHBands,估算 MB)
- [x] 7.3 当估算超过阈值时输出明确 warning,并按配置策略执行自动降级(降低 SH 或 cap splatCount)
- [x] 7.4 当 `GraphicsBuffer` 创建失败时以可行动的错误信息失败,并禁用当前 renderer 的渲染

## 8. VFX Graph 后端(可选,按 SplatVFX 风格改进工作流)

- [x] 8.1 在 `Runtime/Gsplat.asmdef` 增加 Version Defines 或编译宏,隔离 `UnityEngine.VFX` 依赖,保证未安装 VFX Graph 时仍可编译
- [x] 8.2 新增 `Runtime/VFX/GsplatVfxBinder.cs`,把 3D+4D buffers 绑定到 `VisualEffect` exposed properties,并同步 `TimeNormalized`
- [x] 8.3 增加 `MaxSplatsForVfx` 硬上限逻辑,超过则禁用 VFX 后端并输出 warning(建议切回 Gsplat 后端)
- [x] 8.4 提供一个最小可用的 VFX Graph sample(建议放在 `Samples~`),并让 `.splat4d` 导入体验尽量接近 `SplatVFX`(自动生成 prefab + binder,若缺 sample 则输出可执行提示)

## 9. 文档同步

- [x] 9.1 更新 `README.md`,新增 4D PLY 字段规范与 `.splat4d` 格式说明、时间语义、播放控制说明、VFX 后端依赖与限制
- [x] 9.2 更新 `Documentation~/Implementation Details.md`,补充 4D buffers、排序 key 计算、shader 裁剪与 bounds 扩展的实现说明

## 10. 验证

- [x] 10.1 添加一个简单的可视化验证场景或步骤,覆盖: 时间窗裁剪、线性运动、遮挡排序变化、3D-only 回归
- [x] 10.2 在 HDRP 下验证 `GsplatHDRPPass` 注入仍能正确触发排序,且随时间播放无明显闪烁
