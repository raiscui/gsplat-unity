# Gsplat VFX Graph Sample(SplatVFX style)

这个 sample 的目标是给你一条"像 SplatVFX 一样顺滑"的 VFX Graph 工作流:

- 导入 `.splat4d` 后,尽量自动生成一个可用的 prefab.
- 通过 `GsplatVfxBinder` 把 Gsplat 的 GPU `GraphicsBuffer` 绑定给 `VisualEffect`.
- 附带一个最小的可视化/回归验证脚本,用来验证 4DGS 的 bounds 扩展是否能避免相机剔除错误.

## 前置条件

- 安装 Unity 的 `Visual Effect Graph` 包(`com.unity.visualeffectgraph`).
- 项目能正常运行本包的 Gsplat 主后端(Compute 排序 + `Gsplat.shader`).

## 一键导入(推荐)

1. 把 `.splat4d` 文件放进项目(拖拽或拷贝都行).
2. 导入器会在该资源下生成一个 `prefab` 子资源(主对象).
3. 当项目已安装 VFX Graph 且能找到本 sample 的 `VFX/Splat.vfx` 时,该 prefab 会自动包含:
   - `GsplatRenderer`(并默认 `EnableGsplatBackend=false`,避免双重渲染)
   - `VisualEffect`(已指向 `VFX/Splat.vfx`)
   - `VFXPropertyBinder`
   - `GsplatVfxBinder`(已自动指定 `VfxComputeShader=Runtime/Shaders/GsplatVfx.compute`)

如果 Console 里提示找不到默认 VFX Graph asset,按下面的"手工搭建"做一次就行.

## 手工搭建(当自动绑定不生效时)

1. 新建一个空物体,添加 `GsplatRenderer`,并设置 `GsplatAsset`.
2. 添加 `VisualEffect`,并把 `visualEffectAsset` 指向本 sample 的 `VFX/Splat.vfx`.
3. 添加 `VFXPropertyBinder`.
4. 在 `VFXPropertyBinder` 上添加 binder: `GsplatVfxBinder`.
5. 在 `GsplatVfxBinder` 上设置:
   - `GsplatRenderer`: 指向第 1 步的 renderer
   - `VfxComputeShader`: 默认会自动填充为 `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatVfx.compute`,若为空再手动指定
6. 如果你只想看 VFX 后端,把 `GsplatRenderer.EnableGsplatBackend=false`(避免双重渲染).

## VFX 后端硬上限

`GsplatSettings.MaxSplatsForVfx` 是 VFX 后端的硬上限(默认 500000).
当 splat buffer capacity 超过该值时:

- `GsplatVfxBinder` 会自动禁用 `VisualEffect`,并输出 warning.
- 建议回退使用 Gsplat 主后端渲染,或先降低 splat 数量.

## 与主后端的差异(重要)

这个 sample 的 VFX Graph 后端并不是对主后端(Compute 排序 + `Gsplat.shader`)的"像素级替代".
它的定位更接近:

- 用 VFX Graph 复刻一套可编辑的工作流.
- 尽量复用 Gsplat 的 GPU buffers,避免重复 upload.

因此你看到与主后端明显不同的现象是正常的,最常见的差异包括:

- 透明排序:
  - `VFX/Splat.vfx` 的 `sort` 默认是 Auto(序列化里是 `sort: 0`).
  - 由于该 sample 的 `blendMode` 是 Alpha,所以 Auto 模式下仍会启用 sorting.
  - 但它默认的排序准则是 `DistanceToCamera`(序列化里是 `sortMode: 0`).
  - 主后端的排序 key 更接近 `CameraDepth`(view-space `z`),因此遮挡关系仍可能不同.
- SH(view-dependent) 颜色:
  - 主后端会按 `GsplatRenderer.SHDegree` 评估 SH 系数.
  - 该 sample 默认只使用 base color(DC),不包含更高阶的 SH 项.
- 颜色语义:
  - `GsplatAsset.Colors` 存的是 f_dc(DC SH 系数),主后端会在 shader 内解码为 baseRgb.
  - 本 sample 会在 `Runtime/Shaders/GsplatVfx.compute` 里提前做 f_dc->baseRgb 解码,让 ShaderGraph 直接使用 baseRgb.

如果你的目标是"最终画面尽可能正确",建议用主后端渲染,把 VFX 当成叠加特效层.
如果你的目标是"VFX Graph 可编辑/可组合",请接受上述差异,必要时切换到 `SplatSorted.vfx` 并评估性能.

### `SplatSorted.vfx`(质量优先,已启用排序)

- 本包提供了一个开启排序的变体: `VFX/SplatSorted.vfx`.
  - 它强制 `sort=On`,并把 `sortMode` 设置为 `CameraDepth`,用于降低透明叠加顺序差异.
  - 目标是让遮挡关系更接近主后端(主后端的 key 是 view-space `z`).
- `.splat4d` 一键导入生成 prefab 时,会优先选择 `SplatSorted.vfx`(若该资产存在),否则回退到 `Splat.vfx`.
- 性能提醒:
  - Sorting 通常会显著增加 VFX Graph 的成本.
  - 当 splat 数接近 `MaxSplatsForVfx` 上限时,你应当优先评估帧率与卡顿,再决定是否启用该变体.

## 验证清单(对应 tasks 6.2/10.x)

### 1) Bounds/剔除验证(任务 6.2)

- 把 `Scripts/Gsplat4DCullingRepro.cs` 挂到场景里任意物体(空物体也行).
- 进入 Play 或在编辑器预览(脚本是 `ExecuteAlways`).
- 观察两个 splat 会沿 +X 方向移动到静态 bounds 外:
  - **预期**: 仍可见,不会因为相机剔除突然消失.

### 2) 时间窗裁剪(任务 10.1)

- 使用含 4D 字段的资产(PLY 或 `.splat4d`).
- 在 `GsplatRenderer` 上拖动 `TimeNormalized`:
  - **预期**: `t` 不在 `[time0, time0+duration]` 的 splat 完全不可见.
- 编辑器预览(无需手点 `VisualEffect.Play()`):
  - `VisualEffect` 在编辑器里默认不播放(通常处于 pause 状态),所以很多人会误以为"拖动参数没生效".
  - 本 sample 的 `GsplatVfxBinder` 默认开启了 `PreviewInEditor`:
    - 当 `TimeNormalized` 或 `SplatCount` 变化时,会自动 Step 一帧刷新预览.
    - 因此你可以只拖动 `TimeNormalized`,不需要再点 `VisualEffect.Play()`.
  - 如果你不希望它自动 step,把 `GsplatVfxBinder.PreviewInEditor=false`.
- 如果你发现拖动 `TimeNormalized` 不更新,而是"只有按 `VisualEffect.Play()` 才刷新一帧":
  - 这通常意味着你使用的 VFX Graph 资产缺失 Update Context.
  - 请确认使用的是本 sample 的 `VFX/Splat.vfx`(最新版会在 Update 阶段每帧从 buffer 刷新粒子属性).

### 3) 线性运动(任务 10.1)

- 在 `GsplatRenderer` 上启用 `AutoPlay`,调整 `Speed`.
- **预期**: splat 按 `pos(t)=pos0+vel*(t-time0)` 匀速运动.

### 4) 遮挡排序变化(任务 10.1)

- 准备两个 splat(或两团明显可区分的区域),让它们在运动中发生深度前后交换.
- **预期**: Gsplat 主后端遮挡关系随时间一致变化,不出现明显闪烁.

### 5) 3D-only 回归(任务 10.1)

- 导入旧的 3DGS PLY(不含任何 4D 字段).
- 改变 `TimeNormalized`(0 与 1).
- **预期**: 画面不变(忽略浮点误差).

### 6) HDRP 注入验证(任务 10.2)

- 在 HDRP 场景中添加一个 `Custom Pass Volume`.
- 添加自定义 pass: `GsplatHDRPPass`.
- 播放并改变 `TimeNormalized`.
- **预期**: HDRP 相机每帧仍会触发排序(`GsplatSorter.DispatchSort`),且随时间播放无明显闪烁.
