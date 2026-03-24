# Notes: LiDAR 高密度扫描边缘锯齿成因分析

## [2026-03-16 21:54:40] [Session ID: 99992] 笔记: 初始化

## 来源

### 来源1: 用户现象描述

- 现象:
  - 把 `LidarAzimuthBins` 和 `LidarBeamCount` 提高到很高很密集后,mesh 形状边缘依旧呈现巨大锯齿。
  - 用户怀疑是否存在"采样精度优化"导致的边缘不精确。

## 当前约束

- 还没有动态截图、复现参数和运行日志。
- 当前阶段只能先建立静态证据,结论必须明确区分为假设或已验证结论。

## [2026-03-16 22:01:30] [Session ID: 99992] 笔记: 代码链路与量化验证

## 来源

### 来源1: `README.md`

- 关键原文:
  - "Instead of rendering the hit splats directly, it renders a regular point grid (beam x azimuthBins)..."
  - "Rendering: screen-space square points"
- 结论:
  - 该 LiDAR/RadarScan 模式的设计目标,本来就不是连续表面.
  - 它会先落到规则角度网格,再以屏幕空间点精灵绘制。

### 来源2: `Runtime/GsplatRenderer.cs`

- 关键事实:
  - 注释明确写了 "GPU compute 生成规则 range image(beam x azimuthBin),并绘制规则点云"。
  - `LidarAzimuthBins` 没有运行时上限。
  - `LidarBeamCount` 有运行时上限 `512`。
- 结论:
  - 如果用户把 `LidarBeamCount` 调到 512 以上,实际不会继续变密。

### 来源3: `Runtime/Lidar/GsplatLidarScan.cs`

- 关键事实:
  - `GsplatLidarLayout.CellCount = ActiveAzimuthBins * ActiveBeamCount`。
  - frustum 模式会通过 `ScaleCountKeepingDensity(...)` 按角域缩放 active bins / beams。
  - 渲染函数 `RenderPointCloud(...)` 最终调用 `Graphics.RenderMeshPrimitives(...)`。
- 结论:
  - 用户面前看到的是按 cell 渲染出来的规则点云,不是把 hit 点重新三角化成连续 mesh。
  - 在 `CameraFrustum` 模式下,最终 active 分辨率可能比 Inspector 里填的基础值更小。

### 来源4: `Runtime/Shaders/Gsplat.compute`

- 关键事实:
  - 每个 splat 会先根据 `atan2` 和 elevation 落到单个 `(beamIndex, azBin)`。
  - azimuth 使用 `floor(...)` 量化到 bin。
  - 每个 cell 只保留最近 first return(`InterlockedMin`)。
  - 深度用的是投影到 `dirCenter` 的 `depth^2`,这是为了消除旧的"厚壳外推"。
- 结论:
  - 当前实现确实存在有意的角度量化,这是设计本体,不是隐藏优化副作用。
  - "投影到 bin center 射线"这条修正,主要是为了解决厚壳/外推,不是让边缘更锯齿。

### 来源5: `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- 关键事实:
  - 顶点阶段按 `cellId -> beamIndex + azBin` 重建方向。
  - 命中距离会沿当前 cell 的 LUT 中心方向重建位置。
  - 片元阶段明写了 `形状: 正方形点(屏幕空间)`。
- 结论:
  - 即使命中距离是精确的,最后的外观仍受"离散角度 + 方形点精灵"共同控制。
  - AA 只能软化每个点自己的边缘,不能把离散采样补成连续轮廓。

## 动态量化验证

- 使用代码参数公式换算的世界空间间距:
  - 默认 `2048 x 128`,在 100m 处约为:
    - 水平步长 `0.175781°`,对应约 `0.3068m`
    - 竖直步长 `0.312500°`,对应约 `0.5454m`
  - 即使把 beam 提到 runtime 上限 `512`,在 100m 处竖直间距仍约 `0.1364m`
  - 若 azimuth 提到 `8192`,beam 仍为 `512`,在 100m 处约为:
    - 水平 `0.0767m`
    - 竖直 `0.1364m`
- 解释:
  - 距离越远,固定角步长对应的线性间距越大。
  - 远处硬边缘天然更容易出现"台阶感"或"锯齿轮廓"。

## 综合发现

### 现象

- 用户把 `LidarAzimuthBins` / `LidarBeamCount` 提高后,仍能看见明显锯齿边缘。

### 主假设

- 主因是 LiDAR 模式本身采用"规则角度栅格 + first return + 点精灵渲染"。
- 也就是说,轮廓是由 cell 离散采样决定的,不是连续表面重建。

### 最强备选解释

- 若现场看起来比理论台阶更夸张,还可能叠加以下因素:
  - `LidarBeamCount` 实际已被 clamp 到 512,继续调大没有效果。
  - `CameraFrustum` 模式下 active bins / beams 按视场比例缩小,有效分辨率低于期望。
  - 点形状本身是屏幕空间方点,在小半径和无 MSAA/A2C 时更容易显得硬。

### 当前结论

- "这是采样精度优化导致的吗?" 目前静态证据支持的答案是:
  - 不是因为某个专门追求性能的"精度优化"把边缘做坏了。
  - 更准确地说,这是该模式为了实现规则扫描线和 first return 语义,主动采用的离散角度采样结果。
  - 另有一个真正会影响体感的限制是 `LidarBeamCount` 会被 clamp 到 512。

### 仍缺的证据

- 如果要进一步判断"现场锯齿是否超出这套设计应有的程度",还需要:
  - 具体模式(`Surround360` / `CameraFrustum`)
  - 实际设置值
  - 观察距离
  - 一张现场截图

## [2026-03-16 22:14:40] [Session ID: 99992] 笔记: `beam=512` 与 `CameraFrustum` active count 的精确定义

## 来源

### 来源1: `Runtime/Lidar/GsplatLidarScan.cs`

- 关键原文:
  - `activeAzimuthBins = ScaleCountKeepingDensity(baseAzimuthBins, azimuthSpan, 2π)`
  - `activeBeamCount = ScaleCountKeepingDensity(baseBeamCount, beamSpan, baselineBeamSpan)`
  - `ScaleCountKeepingDensity = round(baseCount * activeSpan / baselineSpan)`
- 结论:
  - `CameraFrustum` 的 active count 是按角密度等比缩放出来的。
  - 不是简单沿用 Inspector 基础值。

### 来源2: `Tests/Editor/GsplatLidarScanTests.cs`

- 关键事实:
  - 测试明确用下面公式校验:
    - `activeAz = round(baseAz * horizontalFov / (2π))`
    - `activeBeam = round(baseBeam * verticalFov / baselineVerticalSpan)`
  - 测试还明确断言:
    - `activeAzimuthBins < renderer.LidarAzimuthBins`
    - `activeBeamCount > renderer.LidarBeamCount`
- 结论:
  - 在 frustum 模式里,`activeBeamCount` 不但可能变小,也可能比基础 `LidarBeamCount` 更大。

### 来源3: `Runtime/GsplatRenderer.cs`

- 关键事实:
  - `LidarBeamCount > 512` 时会先被 clamp 到 `512`
  - 随后 `TryGetEffectiveLidarLayout(...)` 会把这个基础值送进 `TryCreateCameraFrustum(...)`
- 结论:
  - `512` 是基础输入值的上限,不是 frustum 最终 active beam 的绝对上限。

## 综合发现

### Surround360 模式的判定规则

- 有效分辨率就是:
  - `effectiveAzimuthBins = LidarAzimuthBins`
  - `effectiveBeamCount = min(max(LidarBeamCount, 1), 512)`
- 所以:
  - 如果你不是 `CameraFrustum`,那 `beam > 512` 的设置确实不会再继续生效。

### CameraFrustum 模式的判定规则

- 先做基础值清洗:
  - `baseAzimuthBins = LidarAzimuthBins`
  - `baseBeamCount = clamp(LidarBeamCount, 1..512)`
- 再算 active:
  - `activeAzimuthBins ≈ round(baseAzimuthBins * horizontalFov / 360°)`
  - `activeBeamCount ≈ round(baseBeamCount * cameraVerticalFov / (LidarUpFovDeg - LidarDownFovDeg))`

### 直接可用的判断口径

- 如果你当前是 `Surround360`:
  - 看 `LidarBeamCount` 是否大于 `512`
  - 大于的话,超过部分完全无效
- 如果你当前是 `CameraFrustum`:
  - 水平方向一定会缩:
    - 因为相机水平 FOV 不会到 360°,所以 `activeAzimuthBins` 一定小于基础 `LidarAzimuthBins`
  - 竖直方向不一定缩:
    - 若 `camera.fieldOfView < (LidarUpFovDeg - LidarDownFovDeg)`,则 `activeBeamCount` 变小
    - 若两者差不多,则接近不变
    - 若 `camera.fieldOfView > (LidarUpFovDeg - LidarDownFovDeg)`,则 `activeBeamCount` 会变大

### 量化例子

- 例子:
  - `CameraFrustum`
  - `LidarAzimuthBins = 8192`
  - `LidarBeamCount = 512`
  - `LidarUpFovDeg = 10`
  - `LidarDownFovDeg = -30`
  - 相机 `16:9`, `verticalFov = 60°`
- 计算结果:
  - `horizontalFov ≈ 91.49°`
  - `activeAzimuthBins ≈ 2082`
  - `activeBeamCount ≈ 768`
- 含义:
  - 这时真正限制你轮廓横向精度的,更可能是 frustum 把 azimuth 从 `8192` 缩到了约 `2082`
  - 竖直方向反而可能没有你想象中那么低

## [2026-03-16 22:16:37] [Session ID: 99992] 笔记: 用户截图现场现象

## 来源

### 来源1: 用户补充截图

- 观察到的现象:
  - 角色轮廓表现为一层层非常明显的水平扫描线。
  - 头部和肩部的外轮廓不是细密均匀的小方格,而是大段“横向条带 + 轮廓台阶”。
  - 地面也呈现出规则扫描线,说明当前画面语义非常接近“LiDAR 规则扫描线”而不是表面重建。

## 初步判断

- 这张图更像:
  - 规则 beam line 主导的条带观感
  - 外轮廓再叠加有限横向角分辨率形成的台阶
- 这张图不太像:
  - 单纯 shader 精度不足
  - 某个随机优化导致的局部破坏

## [2026-03-16 22:18:10] [Session ID: 99992] 笔记: 基于截图的新证据回滚口径

## 现象

- 条带不仅出现在人物外轮廓,也大面积出现在:
  - 人物身体内部
  - 地面
  - 场景其它表面
- 每一层都明显接近“水平扫描线”。

## 上一假设

- 上一条跟进里,我强调了 `CameraFrustum` 下横向 `activeAzimuthBins` 可能被缩得很厉害,因此常常是第一嫌疑项。

## 推翻它的证据

- 这张截图里最显眼的不是“每条线上横向采样不够密”,而是“竖向一层层 beam 条带非常明显”。
- 如果主要问题是横向 active azimuth 不够:
  - 更常见的观感会是每条扫描线内部点列稀疏、左右边缘更明显地左右台阶
  - 而不是全画面都被粗 horizontal bands 主导

## 修正后的判断

- 对这张截图而言,主导观感的第一因素更像是:
  - `effectiveBeamCount` 对应的竖向层数仍然不够细
  - 再叠加较明显的 `LidarPointRadiusPixels`,把同一 beam 上的点连成粗线
- 横向 `activeAzimuthBins` 仍可能参与,但从这张图看更像次要因素。

## [2026-03-16 22:18:45] [Session ID: 99992] 笔记: 用户澄清“像乐高一样的方片边缘”

## 现象

- 用户强调的问题不是“扫描线数量少”。
- 用户真正关心的是:
  - 线加得再多,最终仍会拼成方片
  - 外轮廓像乐高块一样阶梯化

## 对应静态证据

- `README.md` 明写:
  - `Rendering: screen-space square points`
- `Runtime/Shaders/GsplatLidarPassCore.hlsl` 明写:
  - `形状: 正方形点(屏幕空间)`
  - 并使用 `d = max(abs(uv.x), abs(uv.y))` 的 Chebyshev 距离定义点形状

## 当前结论

- 这说明当前 LiDAR 点的基本图元就是“方形点”,不是圆点,更不是连续曲面。
- 所以即使把采样继续加密,也只是得到“更小、更密”的方块拼贴。
- 它当然会变细,但不会自然变成真正平滑的曲线边缘。

## [2026-03-16 22:20:26] [Session ID: 99992] 笔记: 用户进一步假设“大片点共用同一深度值”

## 现象

- 用户进一步指出:
  - 感觉像一大片点都用了同一深度值

## 口径修正

- 上一条把“方点图元”说得太靠前了,这不足以解释“大块同深度感”。
- 更接近根因的说法是:
  - 每个 LiDAR cell 只能保留一个 first-return 深度
  - cell 内所有更细的表面变化都会被折叠掉
  - 当很多相邻 cell 命中的是同一片平面或近平面时,视觉上就会像一整块都贴在相近深度上

## 对应静态证据

- `ReduceMinRangeSq`:
  - 每个 cell 只保留最小距离(`InterlockedMin`)
- `ResolveMinSplatId`:
  - 只给当前 min-range 对应的那个命中保留颜色来源
- 渲染阶段:
  - 再把这个 cell 的单个 range 放回 cell-center 射线上重建

## 当前结论

- 更准确地说,不是“整片区域真的共享一个完全相同的全局 depth 值”。
- 而是“每个 cell 只有一个深度样本,大量相邻 cell 的样本又很接近,于是看起来像大块深度片层”。

## [2026-03-16 22:21:34] [Session ID: 99992] 笔记: 用户补充“球体表面像台阶”

## 现象

- 在球体上,表面呈现一层层台阶。
- 观感上不像圆滑 mesh,更像被离散层切出来的近似面。

## 结论加强

- 这进一步支持:
  - 当前问题的主因是 cell 级 range image 采样与重建
  - 曲面在这种表示下天然会退化成分层台阶
- 这不需要假设 shader 出现额外 bug,也不需要假设 mesh 数据本身有问题。

## [2026-03-16 22:25:10] [Session ID: 99992] 笔记: “是否用了深度采样,是不是像素不够” 的精确回答

## 来源

### 来源1: `Runtime/Shaders/Gsplat.compute`

- 关键事实:
  - 纯 gsplat LiDAR 路径里,不是先画深度图再采样。
  - 它是直接把 splat 投到 LiDAR 角度格子:
    - `az -> azBin`
    - `elev -> beamIndex`
    - 每个 cell 保留一个 `depthSq`
- 结论:
  - 对 gsplat 本体来说,问题不是“屏幕像素不够”。
  - 更准确是“LiDAR 自己的 range image cell 不够细,且每格只有一个深度样本”。

### 来源2: `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` + `Runtime/Shaders/GsplatLidarExternalCapture.shader`

- 关键事实:
  - `CameraFrustum + external mesh` 路径里,确实会先做离屏 depth capture。
  - 如果 frustum camera 没挂 `targetTexture`,capture RT 默认大小就是:
    - `width = ActiveAzimuthBins`
    - `height = ActiveBeamCount`
  - resolve 时会按 cell center 的 uv,用 `Load(int2(pixel))` 取一个像素的线性 depth
- 结论:
  - 对 external mesh frustum 路径来说,可以把它理解成“LiDAR 专用深度图像素格子不够”。
  - 但这里说的不是最终屏幕分辨率不够,而是 LiDAR capture / range image 分辨率不够。

## 当前结论

- 如果你扫的是 `external mesh` 且走 `CameraFrustum GPU capture`:
  - 是,这里真的用了 depth 采样
  - 也确实可以说存在“像素格子不够”的问题
- 如果你扫的是纯 gsplat:
  - 不是硬件 depth texture 像素不够
  - 但在本质上仍是同类问题:
    - 角度格子(cell)太粗
    - 每格只有一个深度样本

## [2026-03-16 22:27:10] [Session ID: 99992] 笔记: 用户确认使用 camera,锁定 external frustum depth capture 路线

## 现象

- 用户确认:
  - 当前场景应当走 `CameraFrustum + external mesh`
  - 且确实使用了 `camera`

## 已验证结论

- 这意味着当前主链路应是:
  - external mesh -> 离屏 depth/color capture
  - capture RT -> compute resolve
  - 每个 LiDAR cell 读取一个 capture pixel 的线性 depth
  - 再还原成沿 cell-center 射线的 depthSq
- 因此“像素格子不够 + 每格一个深度样本”已经不再只是候选假设,而是与当前配置强相关的主结论。

## 关键含义

- 球体变成台阶,在这条路径下很合理:
  - capture texture 是离散像素网格
  - resolve 又是每个 cell 只取一个像素
  - 所以闭合光滑曲面会先退化成离散深度图,再退化成 cell 级重建点云

## [2026-03-16 22:51:03] [Session ID: 99992] 笔记: external frustum depth capture 精度控制实现

## 来源

### 来源1: `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`

- 新增事实:
  - `TryCaptureExternalHits(...)` 现在会接收:
    - `GsplatLidarExternalCaptureResolutionMode`
    - `captureResolutionScale`
    - `explicitCaptureResolution`
  - capture 尺寸解析改为三态:
    - `Auto`: `pixelRect -> targetTexture -> active LiDAR grid`
    - `Scale`: 在 `Auto` 基准上乘倍率
    - `Explicit`: 直接使用显式宽高
  - 最终宽高会按 `SystemInfo.maxTextureSize` 做 clamp
- 结论:
  - external mesh 的离屏 depth/color capture 精度现在可以显式控制。
  - 用户不再被默认 capture 基准尺寸绑定死。

### 来源2: `Editor/GsplatRendererEditor.cs` + `Editor/GsplatSequenceRendererEditor.cs`

- 新增事实:
  - Inspector 已暴露 `Auto / Scale / Explicit` 三态配置
  - `Scale` 与 `Explicit` 的字段会按 mode 启用/禁用
  - HelpBox 明确说明:
    - 这控制的是 external mesh 的离屏采样精度
    - 不会改变 LiDAR 自身 beam / azimuth 的离散语义
- 结论:
  - 用户现在可以直接在 Inspector 里把 external capture 精度调高,不用改代码。

### 来源3: `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`

- 新增事实:
  - 新增反射测试覆盖:
    - `Auto` 取 `pixelRect`
    - `Auto` 回退 `targetTexture`
    - `Scale` 正常放大
    - `Explicit` 生效并受硬件上限 clamp
- 动态验证:
  - `dotnet build ../../Gsplat.csproj -nologo`: 成功,0 error
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`: 成功,0 error
  - Unity CLI 首轮验证受项目锁影响,直接跑主工程失败
  - 采用临时克隆工程后,`Gsplat.Tests.GsplatLidarExternalGpuCaptureTests` 11/11 通过
- 结论:
  - 本轮功能不只是字段暴露,而是已有自动化测试覆盖的可用实现。

## 综合发现

### 这次修复真正改善了什么

- 它改善的是 `CameraFrustum + external mesh` 路线的 depth capture 精度上限。
- 这会减少 external mesh 轮廓因为离屏深度图太粗而出现的大块台阶感。

### 这次修复没有改变什么

- 它没有改变纯 gsplat LiDAR 的 cell-first 语义。
- 它也没有把 RadarScan 从规则 range image 改造成连续 mesh 重建。
- 所以如果用户未来追求"像真实 mesh 一样圆滑",还需要更激进的语义级方案,例如 multi-sample resolve 或 surface reconstruction。
