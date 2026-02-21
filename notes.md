# 笔记: 4DGS(OpenSpec change 设计输入)

## 当前仓库 3DGS 渲染管线(摘要)
- 数据导入: `Editor/GsplatImporter.cs` 读取 PLY,写入 `Runtime/GsplatAsset.cs`.
- GPU 资源: `Runtime/GsplatRendererImpl.cs` 创建 `GraphicsBuffer`(position/scale/rotation/color/SH/order).
- 每帧两步:
  1) GPU 排序: `Runtime/Shaders/Gsplat.compute` 计算深度 key 并 radix sort,输出 `OrderBuffer`.
  2) 绘制: `Runtime/Shaders/Gsplat.shader` + `Runtime/Shaders/Gsplat.hlsl` 取排序后的 id,计算协方差投影,做透明混合.
- HDRP 注入: `Runtime/SRP/GsplatHDRPPass.cs` 在 CustomPass 里调用 `GsplatSorter.Instance.DispatchSort`.

## 4DGS 目标(来自对话的默认假设)
- 渲染管线: HDRP.
- 输入数据: canonical 4D PLY(同一份数据同时包含 3DGS 字段 + 4D 字段).
- 4D 字段: position, velocity, time, duration.
- 时间单位: time/duration 归一化到 [0, 1].
- 运动模型: pos(t) = pos0 + vel * (t - time0).
- 可见性: t 在 [time0, time0 + duration] 内可见,窗外直接不可见.
- 同时希望有两套后端:
  - Gsplat 后端: 保持当前 shader 数学与 GPU 排序,扩展到 4D.
  - VFX Graph 后端: 尽量接近 Gsplat 的视觉,并用于更方便的 time/velocity 驱动.
- 规模预期: > 10M splats.
- 目标显存: 16-24GB.
- SH: 保留(0-3),优先保证与训练结果接近.

## 已知风险(需要在 tasks 里显式化)
- >10M + SH(尤其 SH3) 的显存与带宽压力极大.
- PLY 导入会产生巨大托管数组,容易触发内存峰值与导入耗时.
- VFX Graph 对超大粒子数不友好.
  - 需要明确它的规模上限,以及降级策略.

## 参考实现: keijiro/SplatVFX(探索记录)

### 2026-02-16 18:43:24 +0800
- SplatVFX 是基于 Unity VFX Graph 的 3DGS 实验实现.
- 它的数据侧核心形态与我们在 `4dgs-vfx-backend` 里设想的路线一致:
  - 用 `GraphicsBuffer` 把 splat 属性喂给 VFX.
  - 用 VFX Property Binder 在运行期把 buffer 绑定到 `VisualEffect` exposed properties.
- 关键代码位置(上游仓库):
  - `jp.keijiro.splat-vfx/Runtime/SplatData.cs`:
    - `SplatData` 里缓存并创建 `PositionBuffer/AxisBuffer/ColorBuffer`,并在首次访问时 `SetData(...)`.
  - `jp.keijiro.splat-vfx/Runtime/SplatDataBinder.cs`:
    - `VFXSplatDataBinder : VFXBinderBase` 使用 `component.SetGraphicsBuffer(...)` 和 `component.SetUInt(...)` 做绑定.
- 备注:
  - SplatVFX 主要面向 `.splat` 格式与 3DGS,并非 4DGS.
  - 我们若要做 4D,仍需要额外的 `Velocity/Time/Duration` buffers,以及 `TimeNormalized` 参数在 VFX 侧的传播与使用.

### 2026-02-16 21:12:01 +0800
- 补充调研: SplatVFX 的 `.splat` 文件格式与导入器实现(用于评估我们是否要做 `.splat4d`)
  - 上游导入器位置: `jp.keijiro.splat-vfx/Editor/SplatImporter.cs`.
  - `.splat` 是无 header 的 raw binary.
  - 每个 splat 固定 32 bytes,通过 `count = bytes.Length / 32` 计算数量.
  - 单条记录布局(概念级):
    - position: `px/py/pz`(float32 * 3)
    - scale: `sx/sy/sz`(float32 * 3)
    - color: `r/g/b/a`(uint8 * 4)
    - rotation: `rw/rx/ry/rz`(uint8 * 4, 用 `(byte-128)/128` 还原到 [-1,1] 再组四元数)
  - 额外坐标约定:
    - position 的 z 会被取反(`-pz`),并且四元数分量在还原时也有符号调整(这是它与数据来源坐标系的约定).

- 对我们做 4D 扩展的启示(与当前 change 的关系):
  - 如果希望复用 `.splat` 思路承载 4D 字段,就必须新增 velocity/time/duration 的存储方式.
  - 因为 `.splat` 没有 header,最直接的扩展方式是定义一个新的 record size,例如:
    - `.splat4d`: 以 64 bytes/record 对齐(更利于 GPU structured 读取与未来扩展).
    - 字段建议: base32 + velocity(float3=12) + time(float=4) + duration(float=4) + padding(12).
  - 当前 `add-4dgs-support` 的第一阶段决策仍是 "以 PLY 扩展为主".
    - `.splat4d` 更像是二期优化(降低导入峰值内存,以及更贴近 VFX Graph 的工作流).

## 文件格式: KSPLAT(探索记录,来自 playcanvas/splat-transform 的 read-ksplat 解析逻辑)

### 2026-02-16 18:43:24 +0800
- `.ksplat` 是一种高压缩的 3D Gaussian Splat 数据格式.
  - 使用分段(section) + 空间分桶(bucket) + 量化(quantization)来压缩.
  - 支持多种压缩模式:
    - 模式0: float32(中心/尺度/旋转/SH)
    - 模式1: 中心 uint16 量化 + scale/rot/SH 使用 float16
    - 模式2: 中心 uint16 量化 + scale/rot 使用 float16 + SH 使用 uint8(min/max 线性映射)
- 关键结构(概念级,非完整 spec):
  - MainHeader 大小 4096 bytes,包含 version、sections 数量、splat 总数、compressionMode、harmonics 量化范围等.
  - 每个 SectionHeader 大小 1024 bytes,包含本 section splat 数、bucket 参数、spatialBlockSize、harmonicsDegree 等.
  - 数据区包含 partial bucket sizes、bucket centers、以及逐 splat 的压缩数据.
- read-ksplat 解出的字段集合(与 3DGS PLY 对齐):
  - `x/y/z`
  - `scale_0/1/2`
  - `rot_0/1/2/3`
  - `f_dc_0/1/2`
  - `opacity`
  - `f_rest_*`(按 harmonicsDegree 决定数量)
- 重要结论:
  - KSPLAT 本身不包含 FreeTimeGS 需要的 4D 字段(velocity/time/duration).
  - 如果我们要在 Unity 实现 4DGS,必须为 velocity/time/duration 设计额外承载方式(例如 sidecar 文件或扩展块).

## 决策补充: `.splat4d` 纳入一期 + VFX 工作流按 SplatVFX 风格

### 2026-02-16 21:38:27 +0800
- 用户选择:
  - VFX 后端定位不走 "仅对照" 也不走 "等同主后端冲 >10M".
  - 先按 `SplatVFX` 的方案改进 VFX 工作流.
  - `.splat4d` 必须纳入一期 tasks.

- 已同步到 OpenSpec artifacts 的关键点:
  - `.splat4d` 定义为无 header 的固定 record 数组,recordSize=64 bytes,little-endian.
  - `.splat4d` 一期只承载 SH0(DC) 与 opacity,不承载高阶 SH.
  - VFX backend 增加 "接近 SplatVFX 的导入体验" 要求(导入后快速得到可播放对象,并自动完成 buffer 绑定).

### 2026-02-17 00:02:11 +0800
- 补充核对: `SplatVFX` 的 VFX Graph 工作流核心是 "ScriptedImporter 自动生成 prefab + binder" + "GraphicsBuffer 绑定".
  - binder: 通过 `VisualEffect.SetUInt("SplatCount", ...)` 与 `SetGraphicsBuffer(...)` 绑定至少 Position/Axis/Color 三类 buffer.
  - importer: `.splat` 导入时会创建 prefab,并挂 `VisualEffect` 与 binder(用户基本不需要手工连线).
- 补充核对: `SplatVFX` 的 `.splat` 有隐式坐标系约定(解析时会对 `pz` 取反,并对四元数分量做符号调整).
  - 因此我们在 `.splat4d` spec 里明确: Unity 导入期不做隐式翻轴,坐标系差异应由导出器处理.

### 2026-02-17 00:18:02 +0800
- VFX Graph 后端的落地策略(对齐 SplatVFX,但兼容 4DGS):
  - 复用 SplatVFX 风格的 VFX Graph sample(它主要依赖 Position/Axis/Color 三个 buffers).
  - 在我们这边用 compute shader 生成:
    - `AxisBuffer`: 由 RotationBuffer(wxyz) + ScaleBuffer 计算出 3 个轴向向量(带尺度),布局与 SplatVFX 一致.
    - `VfxPositionBuffer/VfxColorBuffer`: 按 `pos(t)` 与时间窗语义生成动态 position 与 alpha=0 的 color,让 VFX 图本身更简单.
  - 这样 VFX 后端可以做到:
    - 绑定逻辑简单(组件侧做,图内少写复杂数学).
    - 4D 语义更不容易写错(时间窗裁剪由 compute 统一保证).

---

## 2026-02-17: 从 4DGaussians/动态 3DGS 生成 `.splat4d` 的数据映射

### 来源1: 4DGaussians(仓库内代码定位)
- 关键脚本:
  - `export_perframe_3DGS.py`:
    - README 里给出的用法:
      - `python export_perframe_3DGS.py --iteration 14000 --configs arguments/dnerf/lego.py --model_path output/dnerf/lego`
    - 输出目录: `.../gaussian_pertimestamp/time_*.ply`.
- 关键事实:
  - 4DGaussians 通过 time-conditioned deformation network 输出某个时间 t 下的 gaussians 状态.
  - 它并不直接提供 per-gaussian 的 `velocity/time/duration` 三元组.
  - 因此想落到本仓库的 `.splat4d`(线性速度 + 时间窗)时,必须做“采样 + 差分/分段拟合”的近似.

### 来源2: FreeTimeGsVanilla(语义对齐参考)
- 关键事实(README 明确写了 4D primitive):
  - 每个 Gaussian 都有 position/velocity/time/duration.
  - 并且明确做了 velocity 单位转换: meters/frame -> meters/normalized_time(乘 total_frames).
- 价值:
  - 它提供了把离散帧序列压成线性速度的工程范式.
  - 但其代码为 AGPL,不适合直接拷贝进 MIT 项目,只能做思路参考.

### 字段映射到 `.splat4d`(本仓库 importer 约定)
- position: 取采样时间点的 `x/y/z`.
- scale: 需要线性尺度.
  - 对 gaussian-splatting 系(含 4DGaussians 导出的 PLY),通常 `scale_0/1/2` 是 log(scale),需要 exp.
- rotation: 取 `rot_0..3`(wxyz)并 normalize 后量化到 4 个 uint8.
- color:
  - `.splat4d` 的 `r/g/b` 存 baseRgb.
  - baseRgb = f_dc * SH_C0 + 0.5.
  - `.splat4d` 的 `a` 存 opacity(0..1).
  - 如果 PLY 的 opacity 是 logit,需要 sigmoid.
- velocity/time/duration:
  - 4DGaussians 没有直接输出,需要从多时间点的 position 做差分推导.

### 生成策略(两条路线)
- 快速版(单条记录/gaussian):
  - 取首帧 pos0,取末帧 pos1.
  - vel = (pos1 - pos0) / (t1 - t0).
  - time=0,duration=1.
  - 优点: 文件小,最容易跑通 Unity 播放链路.
  - 缺点: 只能表达平均速度,非线性运动会偏差.
- 分段版(每段一条记录/gaussian):
  - 选 keyframes,每段 [ti, ti+dt].
  - vel = (pos(ti+dt) - pos(ti)) / dt.
  - time=ti,duration=dt.
  - 优点: 更接近任意运动.
  - 缺点: splat 数会乘以段数,文件会变大.

### 落地工具
- 本仓库新增:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`(PLY 序列 -> `.splat4d`,支持 average/keyframe 两种模式)
  - `Tools~/Splat4D/README.md`(从 4DGaussians 导出并转换的流程说明)

---

## 2026-02-17: FreeTimeGsVanilla(MIT) checkpoint -> `.splat4d` 直出

### 关键事实: 为什么 FreeTimeGsVanilla 更容易直出 `.splat4d`
- 它的 checkpoint 里已经包含 per-gaussian 的 4D 参数:
  - `means`: canonical position(在 `mu_t` 时刻的位置)
  - `velocities`: meters/normalized_time
  - `times`: `mu_t`(最可见的中心时刻)
  - `durations`: `log(sigma)`(时间高斯核的 sigma)
- 因此不需要像 4DGaussians 那样导出多帧 PLY 再差分/拟合 velocity.

### 时间窗映射: 从高斯核到硬窗口
- FreeTimeGS temporal opacity:
  - `temporal_opacity(t) = exp(-0.5 * ((t - mu_t) / s)^2)`
  - `s = exp(durations)`
- `.splat4d` 语义是硬窗口:
  - `visible iff time0 <= t <= time0 + duration`
- 默认映射(对齐 viewer 的阈值概念):
  - 给定 `temporal_threshold`(默认 0.01),求出 `sigma_factor = sqrt(-2 * ln(threshold))`.
  - `half_width = s * sigma_factor`
  - `window = [mu_t - half_width, mu_t + half_width]`(clamp 到 [0,1])
  - 写入 `.splat4d.time = window_start`, `.splat4d.duration = window_len`
  - 并把 position 平移到 `window_start` 时刻,保证运动模型一致:
    - `pos0 = mu_x + v * (time0 - mu_t)`

### 落地脚本
- 已在内部 MIT 版 FreeTimeGsVanilla 落地导出器:
  - `../FreeTimeGsVanilla/src/export_splat4d.py`
- 典型用法:
  - `python src/export_splat4d.py --ckpt ckpt_30000.pt --output ckpt_30000.splat4d`
- 重要参数:
  - `--temporal-threshold`: 控制 window 宽度(越小 window 越宽)
  - `--min-sigma`: 防止极小 sigma 导致 window 塌缩
  - `--base-opacity-threshold`: 可选,用于减小输出文件(只看 base opacity)
  - `--chunk-size`: 分块写入,控制峰值内存

---

### 2026-02-17 12:08:41 +0800
- 对 `hustvl/4DGaussians` 导出的本机目录做一致性核对:
  - 目录: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp`
  - 文件: `time_*.ply`(20 帧)
- 结论:
  - 每帧 `element vertex N` 一致,均为 `27673`.
  - header 的 `format` 一致,均为 `binary_little_endian`.
  - vertex property layout 一致(62 个字段).
  - 必需字段 `x/y/z,f_dc_0..2,opacity,scale_0..2,rot_0..3` 均存在,可以直接喂本仓库的 PLY 序列转换器.
  - 不包含 `vx/vy/vz,time,duration`,因此需要离线差分/分段拟合 velocity 才能生成 `.splat4d`.

### 2026-02-17 12:11:13 +0800
- 对该序列做了 1 次额外 sanity check(抽样 `time_00000.ply`):
  - `opacity` 范围约为 `[-8.06, 9.01]`,明显是 logit,因此转换器默认的 `--opacity-mode logit` 是匹配的.
  - `scale_*` 存在大量负值(例如 `scale_0` 最小约 `-55`),明显是 log-scale,因此转换器默认的 `--scale-mode log` 是匹配的.
  - `rot_*` 未归一化(例如 `rot_0` 最大值 > 1),因此转换时必须 normalize(转换器已做).

### 2026-02-17 21:08:36 +0800
- 兼容性问题: `UnityEngine.VFX.Utility.VFXPropertyBinding` 在部分 Unity/VFX Graph 版本下不可用或不可被 Runtime asmdef 引用.
  - 症状: `Runtime/VFX/GsplatVfxBinder.cs` 触发 CS0246(`VFXPropertyBinding` / `VFXPropertyBindingAttribute` 找不到).
  - 处理策略: binder 的运行时绑定不依赖该 attribute,可以移除它来保证跨版本编译.
  - 当前落地: 已在 `Runtime/VFX/GsplatVfxBinder.cs` 移除所有 `[VFXPropertyBinding(...)]`,仅保留 `[SerializeField]`.

---

### 2026-02-18 13:15:30 +0800
- 对比 PlayCanvas `SOG(.sog)` 与 `mkkellogg KSPLAT(.ksplat)` 的定位与编码差异(用于评估 `.splat4d` 下一代格式方向).
  - `.sog`(Spatially Ordered Gaussians):
    - 形态: 一组 WebP 图(`*.webp`) + `meta.json`,也可以被打包成 zip 的 `.sog`.
    - 编码风格: 更接近“纹理流”与“GPU 直接可读”的思路.
    - 关键点: position 用 16-bit 量化并拆成两张图;scale 用 codebook + 索引;rotation 用更紧凑的编码;SH 用 palette + labels.
    - 参考:
      - SplatTransform 文档: https://developer.playcanvas.com/user-manual/gaussian-splatting/editing/splat-transform/
      - SOG spec(v2): https://developer.playcanvas.com/user-manual/gaussian-splatting/sog/
  - `.ksplat`:
    - 形态: 单文件二进制,强调加载速度(因为其布局匹配某个 viewer 的内部格式).
    - 编码风格: 提供压缩等级(例如 0/1/2),把部分字段从 32-bit 压到 16-bit,并可进一步压缩 SH 精度.
    - 参考:
      - GaussianSplats3D README: https://github.com/mkkellogg/GaussianSplats3D

- 对 `.splat4d` 改版的启示(偏“工程落地”视角):
  - 如果目标是 Unity 侧最小改动:
    - 容器形态更偏向 `.ksplat` 的“单文件 + header + level”会更容易落地(对 ScriptedImporter 也更友好).
    - 但量化/压缩细节更值得参考 SOG 的公开 spec(尤其是 position u16 range + codebook/palette 的方法).
  - 如果目标是“运行时也保持压缩”:
    - 需要像 SOG 那样把数据以 quantized buffer/texture 的形式直接喂 GPU.
    - 让 compute/shader 在运行时解码,避免导入期展开为 float 数组(否则压缩优势只停留在磁盘).

---

### 2026-02-18 14:05:09 +0800
- [Gsplat][VFX] `GsplatVfxBinder` 缺少 `VfxComputeShader` 的报错修复思路
  - 现象:
    - Console 输出一次性 error:
      - `GsplatVfxBinder 缺少 VfxComputeShader. 请在 Inspector 中指定为 Packages/wu.yize.gsplat/Runtime/Shaders/GsplatVfx.compute`
    - 常见触发: 用户手工搭建 VFX Graph 工作流时忘记在 binder 上拖 compute shader.
  - 本质(根因):
    - binder 的 `UpdateBinding` 必须依赖辅助 compute shader 来构建 `AxisBuffer` 与 4D 动态 buffers.
    - 在“手工添加组件”场景下,该字段缺省值为空,导致进入 Play 后才暴露错误.
  - 修复策略:
    - Editor-only 自愈: 在 `Reset/OnValidate/OnEnable` 中,
      当 `VfxComputeShader==null` 时通过 `AssetDatabase.LoadAssetAtPath` 自动回填默认 compute shader.
    - 运行时(Player)不做路径加载,避免引入 `UnityEditor` 依赖,并保持可手动覆盖的自由度.
  - 回归锁定:
    - 增加 EditMode 单测,验证 binder 在“漏配”时会自动回填默认 compute shader.

---

### 2026-02-18 14:21:29 +0800
- [Gsplat][Tests] `GsplatVfxBinderTests` 的编译错误与修复策略
  - 现象(用户反馈):
    - `GsplatVfxBinderTests.cs`: CS1061(`enabled` 找不到)
    - `GsplatVfxBinderTests.cs`: CS0012(`VFXBinderBase` 所在程序集未被引用,提示 `Unity.VisualEffectGraph.Runtime`)
  - 本质(根因):
    - 测试程序集强类型引用 `Gsplat.VFX.GsplatVfxBinder` 时,
      编译器需要解析其基类 `VFXBinderBase`.
    - Unity 的 asmdef 不会自动带上传递依赖,
      因此 tests 没显式引用 `Unity.VisualEffectGraph.Runtime` 就会触发 CS0012,
      进一步让 `Behaviour.enabled` 无法解析,出现 CS1061.
  - 修复策略(兼顾“VFX Graph 可选依赖”):
    - 测试改用反射获取类型 + `Behaviour` 基类操作:
      - 当 binder 类型不存在(未安装 VFX Graph)时直接 `Assert.Ignore`.
      - 当类型存在时,反射清空 `VfxComputeShader`,
        再通过 `Behaviour.enabled` 触发 `OnEnable` 兜底逻辑完成自动回填.
    - 这样 tests 不需要在编译期硬依赖 `Unity.VisualEffectGraph.Runtime`.

---

### 2026-02-18 15:15:11 +0800
- [Gsplat][VFX] VFX 后端“只能显示某一帧,无法随 TimeNormalized 播放”的根因与修复
  - 现象:
    - VFX 后端能显示某个时刻的 4DGS.
    - 但 `GsplatRenderer.TimeNormalized` 连续变化时,粒子位置/颜色不跟着更新.
    - 只有手动触发 `VisualEffect.Play()` 之类的“重启/重刷”动作时,才看到画面更新.
  - 本质(根因):
    - `VFXPropertyBinder` 在 `LateUpdate` 每帧都会调用 `binding.UpdateBinding(m_VisualEffect)`.
      - 这意味着 binder 的“每帧绑定/每帧 dispatch compute”并不缺.
    - 真正的问题在 VFX Graph 资产结构:
      - `Samples~/VFXGraphSample/VFX/Splat.vfx` 之前缺失 Update Context.
      - `PositionBuffer/AxisBuffer/ColorBuffer` 只在 Initialize 阶段被采样并写入粒子属性.
      - 粒子初始化完成后,属性就被冻结,后续 buffer 更新不会影响已经存在的粒子.
  - 修复策略:
    - 给 `Splat.vfx` 增加 `VFXBasicUpdate` Context,把 flow 改为 Spawn -> Init -> Update -> Output.
    - 在 Update Context 中复用 `InitializeSplat` subgraph block,每帧把 position/color/alpha/axis 从 buffer 写回粒子属性.
    - 同时把 `InitializeSplat.vfxblock` 的 `m_SuitableContexts` 从 Init-only 调整为 Init|Update,允许在 Update 中复用.

---

### 2026-02-18 15:20:27 +0800
- [Research][PlayCanvas][SOG] `.sog` 的核心形态与 SH 承载方式(用于对齐我们 `.sog4d` 的设计直觉)
  - 来源: The SOG Format - PlayCanvas Developer Site
    - 网址: https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/sog/
  - 关键摘录(原文短引):
    - "A bundled SOG is a ZIP of the files above."
    - "`sh0.webp` holds the DC (l=0) SH coefficient per color channel and alpha"
  - 结论:
    - PlayCanvas SOG 的"bundle"就是 zip 容器,这与我们把 `.sog4d` 定义成 zip 单文件是一致的.
    - SOG 把 base color + opacity 放在 `sh0.webp`(数据图)里,并用 palette+labels 承载高阶 SH.

- [Research][antimatter15][.splat] `.splat` 为什么常被认为是 SH0-only
  - 来源: antimatter15/splat README
    - 网址: https://github.com/antimatter15/splat/blob/main/README.md
  - 关键摘录(原文短引):
    - "does not currently support view dependent shading effects with spherical harmonics"
  - 结论:
    - 这类 `.splat`(antimatter15 viewer)为了减小文件,明确不做 SH(view-dependent)效果.
    - 因此它本质上是"只存 base color/opacity 的 SH0-only 视效近似",不等价于完整 PLY 的 SH 表达.

- [Research][PlayCanvas][SplatTransform] splat-transform 对格式的官方命名(避免我们讨论时概念漂移)
  - 来源: playcanvas/splat-transform README
    - 网址: https://github.com/playcanvas/splat-transform
  - 关键摘录(原文短引):
    - "`.sog` ... Bundled super-compressed format (recommended)"
    - "`.splat` ... (antimatter15 format)"
    - "`.ksplat` ... (mkkellogg format)"
  - 结论:
    - 我们后续讨论的 `.splat/.ksplat/.sog` 名词,可以直接对齐 splat-transform 的定义,避免互相指代不同格式实现.

---

### 2026-02-18 16:19:00 +0800
- [Research][PlayCanvas][SOG v2] SH0 与高阶 SH 的 codebook/centroids/labels 细节(用于修正我们 `.sog4d` 的 SH encoding)
  - 来源: PlayCanvas SOG v2 spec 节选(通过 `playcanvas/splat-transform` 文档引用整理)
  - 关键摘录(原文短引):
    - "`sh0.webp`(RGBA) ... A is the opacity in `[0,1]` ... R,G,B are 0..255 indices into `sh0.codebook`."
    - "`shN_labels.webp` stores a 16-bit index ... `const index = shN_labels.r + (shN_labels.g << 8);`"
  - 结论(对我们 `.sog4d` 的直接启发):
    - `sh0.webp` 的 RGB 更像是 "DC 系数(f_dc)的 codebook 索引",而不是直接存 baseRgb.
      - 这样更贴近我们 Unity shader 管线: `ColorBuffer.rgb` 期望的是 f_dc,而不是 baseRgb.
    - opacity 在 SOG 里直接放在 `sh0.webp` 的 alpha,因此不需要单独的 opacity stream.
    - 高阶 SH(`shN`) 采用两级压缩:
      - per-gaussian/per-frame `labels.webp`: u16 label(建议按 little-endian: 低 8-bit 在 R,高 8-bit 在 G).
      - global `centroids.webp`: palette,每个 entry 存一组 AC 系数向量(数量由 `bands` 决定).

---

### 2026-02-18 17:20:11 +0800
- [Decision][sog4d] 性能优先的 SH0-3 + opacity 编码选择
  - `sh0.webp`(RGBA8,per-frame):
    - RGB: DC 系数(`f_dc`)的 codebook 索引(`sh0Codebook[256]`)
    - A: opacity(u8)
  - `shN_labels.webp`(per-frame): u16 labels,采用 little-endian(`index = r + (g << 8)`).
  - `shN_centroids.bin`(global): palette,默认 `float16` little-endian(允许 `float32`).
  - 移除独立 `opacity.webp` stream,opacity 统一来自 `sh0.webp` alpha.
  - `{frame}` 路径模板统一为十进制 frameIndex,左侧补零到至少 5 位(例如 `00003`).

---

### 2026-02-18 17:47:50 +0800
- [Research][DualGS] DualGS 论文 §4 COMPRESSION 的 SH 压缩与我们 `.sog4d` 的关系
  - 资料:
    - DualGS 论文 HTML: `https://ar5iv.labs.arxiv.org/html/2502.00931`
    - DynGsplat-unity 参考实现: `https://raw.githubusercontent.com/HiFi-Human/DynGsplat-unity/main/CompressScripts/compress.py`
  - DualGS 在 §4 的核心做法(概念级摘要):
    - 分段(segment)处理(论文示例为 50 帧一个 segment).
    - SH 使用 "persistent codebook":
      - 对 segment 内所有帧的 SH 系数做 K-means 得到 codebook(论文提到 4 个 codebooks,长度 K=8192).
      - 每个 gaussian 每帧存一个 codebook index.
      - 观察到相邻帧只有约 1% 的 indices 会变化,因此只存:
        - 第 1 帧的完整 indices
        - 后续帧相对前一帧的 "indices changed" 位置与新值(并再做排序/长度编码)
    - opacity/scale 以 LUT(gaussianId x time) 形式写成 8-bit 图,再用 WebP/JPEG 压缩.
  - 与我们 `.sog4d` 的对应关系:
    - 我们当前的 `shN_centroids.bin + shN_labels.webp` 本质上就是 DualGS 的 "codebook + per-frame indices".
    - DualGS 额外加的一层 "只存 indices 的变化" 属于时间域的熵编码/差分优化:
      - 这会显著降低文件大小,并允许更大的 `shNCount`(例如 8192)仍然保持较低码率.
      - 但它牺牲了随机访问: 若要任意跳到某帧,需要从该 segment 的 base indices 开始应用 delta 更新.
      - 更适合作为 exporter 的存储层优化,由 Unity importer 解码还原成可随机访问的 per-frame labels 纹理.

---

### 2026-02-18 18:17:23 +0800
- [Correction][DualGS] DualGS 论文链接与关键原文核对(避免引用错误)
  - 之前的 ar5iv 链接可能对应了错误的抓取版本.
  - 我重新核对的 DualGS(arXiv HTML)链接:
    - https://arxiv.org/html/2409.08353v1
  - 关键摘录(原文短引,用于确认 "delta labels" 不是我们臆测):
    - "on average, only one percent of the skin Gaussians SH indices change between adjacent frames"
    - "we only save the first frame indices and the positions where the indices change in adjacent frames"
  - 另一个与实现对齐的事实:
    - DualGS 在该段明确写到 "four codebooks" 且 `L=8192`(他们的 setting).

- [Research][DynGsplat-unity] `compress.py` 的落盘结构与 DualGS 的对应关系(实现侧证据)
  - 来源:
    - https://raw.githubusercontent.com/HiFi-Human/DynGsplat-unity/main/CompressScripts/compress.py
  - 观察到的关键点:
    - 按 block/segment 处理帧区间(由 `len_block` 控制).
    - 分别对 `rgb`/`sh_1`/`sh_2`/`sh_3` 训练 codebook(这与 DualGS 的 "four codebooks" 叙述吻合).
    - `canonical_index.bytes`: 保存首帧的完整 indices(可视为 base labels).
    - `index.bytes`: 保存后续帧相对前一帧的 changed `(index,value)` 列表,并带有 offsets header.
  - 结论:
    - DynGsplat-unity 的实现,等价于 "persistent codebook + delta indices" 的工程化落地.

- [Decision][sog4d][delta-v1] 我们的 `shNLabelsEncoding="delta-v1"` 与 DualGS 的关系
  - 相同点:
    - 都是在 time 维度对 labels 做差分: base + per-frame changed positions.
  - 不同点:
    - DualGS 是 per-codebook(4 份 indices)做 delta.
    - 我们当前 spec 先收敛为 "单份 shN labels"(一个 label 指向整套 rest 系数块),以更贴近 SOG 的互操作语义.
  - 这意味着:
    - 若未来想更像 DualGS 那样拆成 3 份(sh1/sh2/sh3) labels,需要新增 streams 与 delta 扩展,而不是直接复用当前字段.

---

### 2026-02-18 18:44:20 +0800
- [Decision][sog4d] Exporter 的默认策略(你的偏好落盘)
  - 你的偏好:
    - 默认性能优先.
    - 如果性能没区别,则质量优先.
    - 默认启用 delta.
  - 我将其具体化为 exporter 的默认输出:
    - `streams.sh.shNLabelsEncoding="delta-v1"`(显式写入字段).
    - `streams.sh.shNCount=8192`.
    - `segmentLength=50`(每段 50 帧,与 DualGS 叙述对齐).
  - 关键理由:
    - `bands=3` 时,`shN_centroids.bin` 即使 `shNCount=8192` 且为 float16,体积也约 0.7MB.
    - 相比 per-frame 属性图,centroids 通常不是体积与 I/O 的主矛盾.
    - 真实的运行时瓶颈更可能出现在 "每帧解码+插值写入 float buffers" 这一步,而不是 palette entry 数本身.

---

## 2026-02-18 22:13:00 +0800: continuous-learning 四文件摘要(收尾 `sog4d-sequence-format`)

- 任务目标(task_plan):
  - 落地 `.sog4d`(zip 单文件)的序列 4DGS 格式: 逐帧 keyframe + position/scale/rotation/opacity/SH0-3 全属性可插值.
  - 提供 Unity 导入器 + runtime 播放 + exporter + 回归测试,形成闭环.
- 关键决定(task_plan/notes):
  - opacity 并入 `sh0.webp` alpha(对齐 SOG),不再单独存 `opacity.webp`.
  - labels(u16) 统一 little-endian(`label = r + (g << 8)`).
  - SH rest 采用 `shN_centroids.bin`(默认 f16 little-endian) + `shN_labels.webp`,并默认启用 `delta-v1`.
  - exporter 默认 `shNCount=8192`,`segmentLength=50`,性能优先(无性能差异则质量优先).
- 关键发现(notes/ERRORFIX):
  - DualGS 的 time-delta labels 思路与我们的 `delta-v1` 同构,更适合做存储层优化,导入期展开保持运行时随机访问.
  - Unity Package Samples 会复制到 `Assets/Samples/...`,且不会随 `Samples~` 自动更新,容易造成"我修了包但工程还在跑旧 sample"的错觉.
- 实际变更(WORKLOG):
  - Unity: 新增 `.sog4d` importer + `GsplatSequenceAsset/Renderer` + decode compute + runtime ZIP/WebP 加载与 chunk streaming.
  - Tooling: `Tools~/Sog4D/ply_sequence_to_sog4d.py`(pack/validate),并提供最小测试 bundle + EditMode tests.
  - 文档: README 与 `Documentation~/Implementation Details.md` 补齐 `.sog4d` 用法与排查路径.
- 错误与根因(ERRORFIX):
  - VFX 后端无法随 `TimeNormalized` 更新: 根因是 sample 的 VFX Graph 缺失 Update Context,导致属性只在 Initialize 阶段写入.
- 可复用点候选(1-3 条):
  1) `.sog/.sog4d` 的 u16 labels 端序与读取方式: 用 RG 表示 little-endian u16,并在导入期做越界 fail-fast.
  2) delta labels 的工程化落点: 存储用 delta,导入期展开为 per-frame labels 纹理,换取运行时 O(1) 访问与双帧插值简单性.
  3) Unity Sample copy 的排查法: 同时检查 `Packages/.../Samples~` 与 `Assets/Samples/...` 是否分叉.
- 是否需要固化到 docs/specs:
  - 是: 归档前需要把 change 里的 delta specs 同步到 `openspec/specs/**`.
- 是否提取/更新 skill:
  - 否(本轮先不新建 skill).
  - 但建议把 "Samples copy 不自动更新" 追加到项目 `AGENTS.md`,作为长期协作约定.

---

## 2026-02-19 11:17:50 +0800: `.sog4d` WebP 解码补齐(让回归测试跑满)

- 现象:
  - Unity `ImageConversion.LoadImage(Texture2D, webpBytes)` 在当前 Unity 版本对 WebP 返回 false.
  - 导致 `.sog4d` importer 在“需要解码 WebP 数据图”的阶段 fail-fast.
  - tests 为了避免假失败,此前选择 `Assert.Ignore`,因此出现 6 个 skipped.
- 本质:
  - `.sog4d` 的 WebP 是“数据图”,必须 lossless 且 byte 精确.
  - 依赖 Unity 内置解码器会产生“同一份数据在不同 Unity 版本下不可导入”的不稳定性.
- 结论/方案:
  - 选择 `libwebp` 的 decoder(我们只需要 `WebPGetInfo/WebPDecodeRGBAInto`).
  - 在 importer 中保持“优先 LoadImage,失败后 fallback 到 libwebp”的策略,兼容未来 Unity 原生支持时的零成本路径.
- 工程化细节(踩坑与解决):
  - 直接用 CMake 产出 universal dylib 时会遇到 `_Float16 is not supported`:
    - 根因: multi-arch 配置下 SIMD 探测失败,会把 `-mno-sse2` 注入 `CMAKE_C_FLAGS`,
      导致 x86_64 下 `_Float16` 不可用,编译炸掉.
    - 解法: 分别构建 arm64 与 x86_64 两份 `libwebpdecoder.dylib`,再用 `lipo` 合并成 universal.
  - 构建环境里若带着 Homebrew 的 `LDFLAGS/CPPFLAGS/LIBRARY_PATH`,可能会把依赖带进产物(例如 `libunwind`):
    - 解法: build 时显式 `env -u LDFLAGS -u CPPFLAGS -u LIBRARY_PATH ...` 清理环境变量.
  - 最终产物:
    - `libwebpdecoder.dylib` -> 改 `install_name` 为 `@rpath/libGsplatWebpDecoder.dylib`,并重命名为 `libGsplatWebpDecoder.dylib`.
    - 放入包内 `Editor/Plugins/macOS/`,通过 P/Invoke 调用.

## 2026-02-19 11:55:25 +0800: `.sog4d` importer 的 WebP 解码与 tests skipped

- 现象:
  - `Gsplat.Tests.GsplatSog4DImporterTests` 里有 6 个用例被 skipped.
  - skipped 原因是 tests 内部 `Assert.Ignore("当前 Unity 版本不支持 WebP 解码...")`.

- 根因:
  - 在 Unity 6000.3.8f1 这类环境里,`ImageConversion.LoadImage(Texture2D, webpBytes, ...)` 对 WebP 常返回 false.
  - 但 `.sog4d` 里的 WebP 是“数据图”,我们必须能 lossless 解码成 RGBA8,才能继续 importer/runtime decode.

- 解决方案(稳定路径):
  - 包内提供 macOS Editor 的 `libwebp` decoder:
    - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib`(universal: arm64+x86_64)
    - `Editor/GsplatWebpNative.cs`(P/Invoke)
  - importer 策略变为:
    - 先尝试 `ImageConversion.LoadImage`
    - 若返回 false,自动 fallback 到 `GsplatWebpNative.TryDecodeRgba32` + `Texture2D.LoadRawTextureData`
  - tests 的能力探测策略与 importer 对齐:
    - 先尝试 `ImageConversion.LoadImage`
    - 失败后再反射调用 `Gsplat.Editor.GsplatWebpNative.SupportsWebpDecoding`

- 稳态补强:
  - 补齐 `Editor/Plugins/**` 的 `.meta`,避免 Unity 导入期自动生成 meta 导致的“不确定性/漂移”.

- CI/命令行额外坑:
  - `-batchmode -nographics` 下图形设备为 Null 时,ComputeShader 可能无法反射 kernels.
  - `GsplatSettings` 初始化排序器会触发 Unity error log("Kernel 'InitPayload' not found"),进而让 EditMode tests 误失败.
- 处理: `Runtime/GsplatSettings.cs` 在 `SystemInfo.graphicsDeviceType==Null` 时跳过 sorter 初始化.

- 证据:
  - `/private/tmp/gsplat_webp_test_project_02_results.xml`:
    - `Gsplat.Tests.GsplatSog4DImporterTests`: passed=10, failed=0, skipped=0

---

## 2026-02-19 16:02:27 +0800: `GsplatSequenceDecode.compute` kernel invalid(运行时)

- 现象:
  - Unity Console 报错: `GsplatSequenceDecode.compute: Kernel at index (1) is invalid`.
  - 调用路径: `GsplatSequenceRenderer.TryDecodeThisFrame -> ComputeShader.Dispatch`.

- 根因推断(与代码实现一致):
  - `.sog4d` importer/runtime bundle 生成的数据图是 `TextureFormat.RGBA32`(UNorm) 的 `Texture2DArray`(linear).
  - decode compute shader 之前使用 `Texture2DArray<uint4>` 读取这些纹理.
  - 在部分 Graphics API(尤其 Metal)上,"用整数视图读取 UNorm 纹理" 可能导致 kernel 编译失败.
  - 结果表现为:
    - kernel 名字存在,`FindKernel` 能拿到 index.
    - 但 `Dispatch` 时 kernel 仍被 Unity 判定为 invalid,并持续输出 error log.

- 修复策略:
  - shader 侧:
    - 改用 `Texture2DArray<float4>` 读取数据图.
    - 读取后把 float(0..1)还原为 byte(0..255),再继续按 u8/u16 规则解码.
  - C# 侧:
    - 增加无图形设备 guard(`GraphicsDeviceType.Null`)避免 `-nographics` 刷屏.
    - 用 `GetKernelThreadGroupSizes` 做 fail-fast 反射,在 kernel 编译失败/不支持时尽早给出可操作报错,
      避免 `Dispatch` 只刷 error log 的黑盒.

## 2026-02-19 17:54:42 +0800: Metal 不支持 `StructuredBuffer.GetDimensions`(需要 C# 传 count)

- 现象(用户日志):
  - `HLSLcc: Metal shading language does not support buffer size query from shader. Pass the size to shader as const instead.`

- 根因:
  - decode compute shader 内调用了 `StructuredBuffer.GetDimensions`.
  - HLSLcc -> MSL 不支持该能力,因此 kernel 编译失败,进而导致 kernel invalid.

- 结论:
  - Metal 下不要在 shader 内做 buffer size query.
  - 改为由 C# 显式传入 buffer count,并在 shader 内做 clamp/越界防御.

---

## 2026-02-19 18:53:37 +0800: continuous-learning 四文件摘要(提取 Unity/Metal compute 排障经验)

### 四文件摘要(用于决定是否提取 skill)
- 任务目标(task_plan.md):
  - 修复 Unity/macOS/Metal 下 `.sog4d` 序列播放的 compute kernel invalid.
  - 把 PLY -> `.sog4d` 的 pack 命令整理成可复制粘贴的手册,用于实际工作流.
- 关键决定(task_plan.md):
  - 数据图纹理(importer 生成的 `TextureFormat.RGBA32` UNorm)在 compute shader 中用 `float4` 读取,再还原 byte 解码,避免 Metal 的整数视图兼容性问题.
  - Metal 下彻底移除 `StructuredBuffer.GetDimensions`(buffer size query),改为由 C# 传入各 buffer count 常量.
  - C# 增加 `GetKernelThreadGroupSizes` 的 fail-fast 检测,把 "FindKernel 能找到但 Dispatch invalid" 从黑盒变成可操作报错.
- 关键发现(notes.md):
  - Metal 的 MSL 不支持 shader 内查询 buffer size,会导致 HLSLcc 编译失败,最终表现为 kernel invalid.
- 实际变更(WORKLOG.md):
  - 修改 `Runtime/Shaders/GsplatSequenceDecode.compute` 与 `Runtime/GsplatSequenceRenderer.cs` 以满足 Metal 兼容性.
  - 扩写 `Tools~/Sog4D/README.md` 输出 pack 命令菜谱手册.
- 错误与根因(ERRORFIX.md):
  - `Kernel at index (...) is invalid` 既可能是“找不到 kernel”,也可能是“kernel 编译失败/不支持当前 Graphics API”.
  - `HLSLcc: Metal shading language does not support buffer size query from shader...` 的根因是 `StructuredBuffer.GetDimensions`.
- 可复用点候选(1-3 条):
  1. Metal 下不要用 `StructuredBuffer.GetDimensions`,需要 C# 传 count 常量.
  2. RGBA8 UNorm 数据图优先用 `float4` 读取并还原 byte,而不是用整数视图读.
  3. 用 `GetKernelThreadGroupSizes` 做 compute kernel 的 fail-fast 验证,避免运行期刷屏黑盒.
- 是否需要固化到 docs/specs: 否(本次属于排障经验,更适合固化为 skill + 项目级协作约定).
- 是否提取/更新 skill: 是(新增 `self-learning.unity-metal-compute-kernel-invalid`).

### 固化动作
- 用户级 skill:
  - `~/.codex/skills/self-learning.unity-metal-compute-kernel-invalid/SKILL.md`
- 项目级协作约定:
  - `AGENTS.md` 追加 Metal compute 的 `GetDimensions` 限制与 UNorm 数据纹理读取建议.

---

## 2026-02-19 19:12:31 +0800: `GetKernelThreadGroupSizes` 在 Metal 下可能误报(IndexOutOfRange),需要降级处理

- 新现象:
  - 在少数 Unity/Metal 组合下,`ComputeShader.GetKernelThreadGroupSizes` 可能抛 `IndexOutOfRangeException`,
    即便 compute shader 已无编译 error,且 kernel 仍可能可正常 Dispatch.
- 启示:
  - 不要把 `GetKernelThreadGroupSizes` 当作“唯一真理”.
  - 更稳的 kernel 验证顺序:
    1. `ComputeShader.IsSupported(kernel)` 作为基础能力探测.
    2. `GetKernelThreadGroupSizes` 作为“加分项检查”(成功就校验 numthreads,失败就降级为 warning).
  - 另外,只验证“当前会实际使用”的 kernel,避免未使用 kernel 的反射问题阻塞播放.

---

## 2026-02-19 19:14:49 +0800: Metal shader warning(`isnan/isinf`)清理

- 背景:
  - `DecodeQuatAtFrame` 与 `NlerpQuat` 里对 `len` 做了 `isnan/isinf` 检查.
  - 但当前实现里 `q` 来自 RGBA8 数据图的 byte 还原,理论上不会产生 NaN/Inf.
  - Metal 编译器会给出 "value cannot be NaN/infinity" warning,污染 Console.
- 处理:
  - 移除 `isnan/isinf`,只保留 `len < epsilon` 的防御分支.

---

## 2026-02-19 20:13:39 +0800: `gaussian_pertimestamp` PLY 序列 -> 高质量 `.sog4d` 输出记录

- 输入目录:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp`
- 关键推断(来自 PLY 字段/范围扫描):
  - `opacity` 为 logit,需要 `--opacity-mode sigmoid`
  - `scale_0/1/2` 为 log(scale),需要 `--scale-mode exp`
  - `f_rest_0..44`(45 标量)存在,因此 `--sh-bands 3`
- 输出文件:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
- 自检结果:
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input ...`
  - 输出: `[sog4d] validate ok (delta-v1).`
- 工具链踩坑与修复点:
  - 高采样量场景下,`numpy.random.Generator.choice(replace=False, p=...)` 会要求 `p` 的非零项数量 >= size.
  - 当 `importance=opacity*volume` 在 float32 下下溢为 0 时,会触发 `ValueError: Fewer non-zero entries in p than size`.
  - 已通过 float64 计算权重 + 稳态采样函数 `_weighted_choice_no_replace` 修复.

---

## 2026-02-19 20:18:45 +0800: continuous-learning 增量提取(numpy 加权不放回采样)

- 新增用户级 skill:
  - `~/.codex/skills/self-learning.numpy-choice-nonzero-p-replace-false/SKILL.md`
- 触发原因:
  - `ValueError: Fewer non-zero entries in p than size` 这条报错很常见,但根因与修复点不够直观.
  - 本次我们已经在真实代码路径中修复并验证(生成并 validate `.sog4d` 成功),适合固化为可复用经验.

---

## 2026-02-19 21:13:30 +0800: 多配置输出与落盘目录约定

- 经验:
  - `--input-dir` 的目录经常会被上游导出脚本重新生成/清理(例如重新导出 `time_*.ply` 时).
  - 如果把 `.sog4d` 输出也写在同一个目录里,容易在下次导出时被误删.
- 约定(本次输出采用):
  - 把 `.sog4d` 输出集中放到一个独立目录:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_out/`

---

## 2026-02-21 15:34:59 +0800: 输出 `.sog4d` 规格(给 FreeTimeGsVanilla 改版 exporter)

### 权威来源(以实现为准)

- 规格入口:
  - `openspec/specs/sog4d-container/spec.md`
  - `openspec/specs/sog4d-sequence-encoding/spec.md`
  - `openspec/specs/sog4d-unity-importer/spec.md`
- 读者实现(Unity importer):
  - `Editor/GsplatSog4DImporter.cs`
- 写者实现(离线 pack/validate 工具):
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
- 运行时解码(用于对齐字节语义):
  - `Runtime/Shaders/GsplatSequenceDecode.compute`

### 这次补齐/对齐的关键细节

- delta-v1 的 update entry 真实布局:
  - `u32 splatId` + `u16 newLabel` + `u16 reserved`.
  - `reserved` 必须为 0.
  - 同一个 block 内,`splatId` 必须严格递增.
- `meta.json.version=2` 的 SH rest 编码:
  - SH rest 按 band 拆分为 `sh1`(3 coeff),`sh2`(5 coeff),`sh3`(7 coeff).
  - 每个 band 都是独立的 palette(centroids.bin) + labels(WebP 或 delta-v1).
- WebP 是数据图:
  - 必须 lossless.
  - 读取时必须禁用 sRGB/压缩/mipmap/重采样等会改变 byte 的处理.

### exporter 最小 checklist(避免踩坑)

- `splatCount` 必须 frame-to-frame 稳定,并且 splatId 的顺序在所有帧一致.
- `layout.type` 当前只支持 `"row-major"`,并且 `pixelIndex == splatId`.
- 所有 per-frame 路径模板:
  - 必须包含 `{frame}`.
  - `{frame}` 必须替换为 5 位零填充十进制(`00000`).
- ZIP bundle 内的所有路径必须是相对路径:
  - 只能用 "/" 分隔符.
  - 不能包含 ":".
  - 不能包含 "." 或 ".." 片段(避免 path traversal).

---

## 2026-02-21 16:20:30 +0800: FreeTimeGsVanilla checkpoint 的 4D 字段仍然存在(用于 `.sog4d` exporter 映射)

### 公开仓库事实(可直接对照代码)

- `OpsiClear/FreeTimeGsVanilla` 的 checkpoint(`ckpt_*.pt`)里,`ckpt["splats"]` 仍然包含:
  - `means/scales/quats/opacities/sh0/shN/times/durations/velocities`
  - 并且 `opacities` 是 logit,`scales` 是 log scale,`durations` 是 log(sigma).
- temporal opacity 的权威实现:
  - `sigma = exp(durations)`
  - `temporal_opacity(t) = exp(-0.5 * ((t - mu_t)/sigma)^2)`
  - `opacity(t) = sigmoid(opacities_logit) * temporal_opacity(t)`

### 易踩坑

- 公开仓库里存在一个容易误抄的片段:
  - `_export_ply_compact()` 内部曾出现过“直接用 durations 做除法”的写法.
  - 但同仓库的 `compute_temporal_opacity()` 明确使用 `exp(durations)`.
  - 因此 exporter 侧应以 `compute_temporal_opacity()` 的公式为准,避免把 log(sigma) 当 sigma 用.

### 本仓库给 exporter 的落盘手册

- 已新增手册:
  - `Tools~/Sog4D/FreeTimeGsCheckpointToSog4D.md`
- 并已在 `Tools~/Sog4D/README.md` 增加入口说明.
