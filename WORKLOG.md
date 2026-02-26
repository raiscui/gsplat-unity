# WORKLOG

## 2026-02-26 11:48:36 +0800
- (续档) 原 `WORKLOG.md` 已重命名为 `WORKLOG_2026-02-26_114836.md`,因为超过 1000 行.
- 本文件从此时间点起继续记录后续工作.

## 2026-02-26 12:26:00 +0800
- 新增一种新的显示效果: `ParticleDots`(粒子圆片/圆点).
- 支持通过 API 在 `Gaussian` 常规效果与 `ParticleDots` 之间切换.
- 切换默认带动画.
  - 曲线: `easeInOutQuart`.
  - 时长: 1.5 秒.
- 点大小可调.
  - 语义: 屏幕像素半径(px radius).

### 变更内容
- Runtime:
  - `Runtime/GsplatUtils.cs`: 新增 `GsplatRenderStyle` 与 `GsplatUtils.EaseInOutQuart`.
  - `Runtime/GsplatRenderer.cs`: 增加 RenderStyle 字段与 API(`SetRenderStyle`,`SetParticleDotRadiusPixels`),并实现切换动画状态机(含 Editor ticker 兜底).
  - `Runtime/GsplatSequenceRenderer.cs`: 同步支持 RenderStyle 字段/API/动画,并在 Update 与 SRP 相机回调链路中推进与下发 uniforms.
  - `Runtime/GsplatRendererImpl.cs`: 新增 uniforms 下发:
    - `_RenderStyleBlend`
    - `_ParticleDotRadiusPixels`
- Shader:
  - `Runtime/Shaders/Gsplat.shader`:
    - 新增 ParticleDots 的 vertex/frag 核(实心 + 柔边).
    - 通过 `_RenderStyleBlend` 做单次 draw 的形态渐变(morph).
    - `blend=0` 时保持旧行为(保留 `<2px` early-out 与 frustum cull).
- Tests:
  - `Tests/Editor/GsplatUtilsTests.cs`: 增加 `EaseInOutQuart` 关键采样点单测.
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`: 增加 RenderStyle 动画收敛单测(反射推进,不依赖 Editor PlayerLoop).
- Docs:
  - `README.md`: 增加 Render style 说明与 API 示例.
  - `CHANGELOG.md`: Unreleased 记录新增 RenderStyle 功能.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_2026-02-26_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_renderstyle_2026-02-26_noquit.log`

## 2026-02-26 12:52:00 +0800
- 解决 Inspector 下 RenderStyle 下拉切换“不看到动画”的体验问题.
  - 原因: 下拉只是改序列化字段,不会走 `SetRenderStyle(..., animated:true)` 的动画状态机.
- Editor Inspector 增加两个按钮用于播放动画切换:
  - `Editor/GsplatRendererEditor.cs`: `Gaussian(动画)` / `ParticleDots(动画)`.
  - `Editor/GsplatSequenceRendererEditor.cs`: 同步增加.
- 同时改良了 Editor 按钮触发的参数一致性:
  - 在触发按钮型 API 前先 `serializedObject.ApplyModifiedProperties()`,
    避免按钮读取到旧的 duration/参数导致“改了但没生效”的错觉.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_inspector_buttons_2026-02-26.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_renderstyle_inspector_buttons_2026-02-26.log`
