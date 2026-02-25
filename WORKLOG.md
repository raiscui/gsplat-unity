# WORKLOG

## 2026-02-25 17:50:00 +0800: 燃烧环扩散曲线改为 easeInOutQuad

### 用户需求
- 将 show/hide 的燃烧环扩散速度曲线改为 `easeInOutQuad`.

### 实现现状与同步
- `Runtime/Shaders/Gsplat.shader` 已使用 `EaseInOutQuad(progress)` 作为扩散半径的 `progressExpand`,并用于与扩散节奏强相关的全局效果(glow/globalWarp/globalShrink).
- 同步修正 OpenSpec change `burn-reveal-visibility` 的 artifacts,避免规格与实现不一致:
  - `openspec/changes/burn-reveal-visibility/specs/gsplat-visibility-animation/spec.md`
  - `openspec/changes/burn-reveal-visibility/tasks.md`

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeInOutQuad_rerun_2026-02-25.xml`

### 备注(测试命令小坑,本机/本版本)
- Unity `-runTests` CLI 在本机 Unity 6000.3.8f1 下实测:
  - 当 `-quit` 放在参数前部时,可能出现“进程正常退出,但没有真正跑测试,也不生成 results.xml”的情况.
  - 更稳态的做法:
    - 不传 `-quit`(tests 完成后仍会自动退出),或
    - 把 `-quit` 放到参数末尾.

## 2026-02-25 18:40:00 +0800: warp 噪声升级为更平滑 noise + 下拉切换(Value/Curl/Hash)

### 用户需求
- 将 show/hide 期间的 warp 噪声进一步升级为更平滑的 noise(更像烟雾/流体的连续流动).
- 提供下拉选项,可以在“当前效果”和“新效果”之间切换对比.

### 实现要点
- Runtime:
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 增加 `VisibilityNoiseMode` 下拉字段.
  - 新增枚举 `GsplatVisibilityNoiseMode`:
    - `ValueSmoke`(默认,保持当前观感)
    - `CurlSmoke`(curl-like 旋涡向量场,更像流动)
    - `HashLegacy`(旧版对照,更碎更抖)
  - `Runtime/GsplatRendererImpl.cs` 新增 `_VisibilityNoiseMode` uniform 下发,每帧写入 MPB.
- Shader:
  - `Runtime/Shaders/Gsplat.hlsl` 增加 value noise 的梯度计算,并实现 `GsplatEvalCurlNoise`(curl(A)=∇×A).
  - `Runtime/Shaders/Gsplat.shader` 根据 `_VisibilityNoiseMode` 切换:
    - `HashLegacy`: 使用 hash noise(无 domain warp)作为旧版对照.
    - `ValueSmoke`: 维持现有 value noise + domain warp 的烟雾场.
    - `CurlSmoke`: 用 curl-like 向量场生成 warpVec,并投影到切向(减少径向拉散),更像旋涡流动.

### 同步
- OpenSpec change `burn-reveal-visibility` 已补充 tasks/spec/design,并更新 `CHANGELOG.md` 记录新增下拉与 CurlSmoke.

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_noise_mode_noquit_2026-02-25.xml`

## 2026-02-25 19:30:00 +0800: glow/size 调优(hide 更早 shrink + show 增加 GlowStartBoost + hide glow 尾巴朝内)

### 用户反馈
1. hide 的 glow 阶段 splat 仍偏大,希望进入 glow 时已经更小.
2. show 也需要一个 GlowStartBoost.
3. hide 的 glow 衰减方向希望“朝内”,避免外围突兀.

### 落地改动
- Runtime:
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 新增 `ShowGlowStartBoost` 字段并下发到 shader.
  - `Runtime/GsplatRendererImpl.cs` 新增 `_VisibilityShowGlowStartBoost` uniform 写入,并在缺省/异常值时回退为 1(避免旧场景因新字段缺失而变暗).
- Shader:
  - `Runtime/Shaders/Gsplat.shader`:
    - show: ring glow 支持 start boost(起始更亮,随后随 eased progress 轻量衰减).
    - hide: ring 使用 `HideGlowStartBoost` 做前沿 boost,并增加内侧 afterglow tail,使 glow 衰减方向朝内.
    - hide: size shrink 使用 `passedForSize` 提前开始,让 glow 阶段 splat 已明显变小.

### 规格同步
- OpenSpec change `burn-reveal-visibility` 已更新 tasks/spec/design,并补充 `CHANGELOG.md`.

## 2026-02-25 20:20:00 +0800: hide size 先快后慢(easeOutCirc-like),避免过早变到极小

### 用户反馈
- hide 燃烧阶段粒子大小仍偏大,希望迅速先变小.
- 现状体感: size 过早变得太小,导致“看起来消失太快”.

### 落地改动
- `Runtime/Shaders/Gsplat.shader`:
  - 新增 `EaseOutCirc` 用于 hide 的 size shrink 节奏.
  - hide size 改为:
    - 在燃烧前沿附近快速 shrink 到非 0 的 `minScaleHide`(较小但仍可见).
    - 后续主要依赖 alpha trail 慢慢消失,避免 size 过早接近 0.

### 规格同步
- OpenSpec change `burn-reveal-visibility` 已追加 tasks/spec/design 说明 hide size 的“先快后慢”节奏.

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_size_easeOutCirc_2026-02-25.xml`

## 2026-02-25 20:10:00 +0800: show/hide 的 ring/tail 语义统一(前沿在外侧,内侧更亮)

### 用户诉求
- 怀疑 show 的 `ShowTrailWidthNormalized` 体感像跑到外侧,内部不够亮.
- 希望保证: 前沿 ring 永远更亮(Boost),内侧 afterglow/tail 朝内衰减.

### 落地改动
- `Runtime/Shaders/Gsplat.shader`:
  - ring 统一为“燃烧前沿在外侧(edgeDist>=0)”,show/hide 共用同一语义,避免“trail 在外”的错觉.
  - 内侧 afterglow/tail 只在内侧(edgeDist<=0),并朝内衰减,同时用 (1-ring) 抑制避免前沿过曝,确保 ring 永远更亮.
  - show: 增加受限的 tail alpha 下限,避免 premul alpha 把内侧余辉吃掉,让“内部更亮”在肉眼上稳定可见.
- 同步更新:
  - `openspec/changes/burn-reveal-visibility/*`(tasks/design/spec)
  - `CHANGELOG.md`

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_trail_glow_semantics_2026-02-25_200342.xml`

## 2026-02-25 20:20:00 +0800: 修复 hide 末尾 lingering(残留 splats 很久才消失)

### 用户反馈
- hide 最最后会残留一些高斯基元很久才消失.

### 根因
- hide 的 passed/visible 若直接跟随 `edgeDistNoisy`,当噪声为正时会把边界往外推,
  导致局部 passed 长时间达不到 1,出现 lingering.

### 落地改动
- `Runtime/Shaders/Gsplat.shader`:
  - ring/glow 仍使用 `edgeDistNoisy`,保留燃烧边界抖动质感.
  - hide 的 alpha fade 与 size shrink 改用 `edgeDistForFade`:
    - 仅允许噪声往内咬(`min(noiseSigned,0)`),不允许往外推.
    - 目的: 保证 hide 末尾一定能烧尽,不会残留.
- 同步更新:
  - `openspec/changes/burn-reveal-visibility/*`(tasks/design/spec)
  - `CHANGELOG.md`

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_lingering_fix_2026-02-25_201817.xml`

## 2026-02-25 20:35:00 +0800: show ring glow 星火闪烁(curl noise),并提供强度可调

### 用户诉求
- show 的 ring glow 亮度希望像火星/星星一样闪闪.
- 希望使用 curl noise,并且强度可调.

### 落地改动
- Runtime:
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 新增 `ShowGlowSparkleStrength`(0=关闭).
  - `Runtime/GsplatRendererImpl.cs`:
    - 新增 `_VisibilityShowGlowSparkleStrength` uniform 下发.
    - `SetVisibilityUniforms(...)` 增加参数并做 NaN/Inf/范围 clamp.
- Shader:
  - `Runtime/Shaders/Gsplat.shader`:
    - show 分支中,对 `ringGlow` 使用 curl-like 噪声场生成“稀疏亮点 + 时间 twinkle”的亮度调制.
    - 仅当 `ShowGlowSparkleStrength>0` 且 `ring>0` 时才计算 curl noise,避免无意义开销.
- 同步更新:
  - `openspec/changes/burn-reveal-visibility/*`(tasks/design/spec)
  - `CHANGELOG.md`

### 回归(证据型)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_sparkle_2026-02-25_202927.xml`

## 2026-02-25 20:45:00 +0800: 默认参数微调(show ring 更厚,trail 更短)

### 用户调参诉求
- `ShowRingWidthNormalized` 大 10%.
- `ShowTrailWidthNormalized` 乘以 40%(缩短 trail).

### 落地改动(默认值)
- `Runtime/GsplatRenderer.cs`:
  - `ShowRingWidthNormalized`: `0.06 -> 0.066`
  - `ShowTrailWidthNormalized`: `0.12 -> 0.048`
- `Runtime/GsplatSequenceRenderer.cs`:
  - `ShowRingWidthNormalized`: `0.06 -> 0.066`
  - `ShowTrailWidthNormalized`: `0.12 -> 0.048`

### 备注
- 该改动只影响“新加组件/Reset”的默认值,不会自动迁移已有 Prefab/场景里已序列化的对象参数.
