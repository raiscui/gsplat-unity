## 1. External capture size policy

- [ ] 1.1 梳理并收敛 `GsplatLidarExternalGpuCapture` 的 `Auto / Scale / Explicit` capture-size 决策,明确 `Scale` 以 Auto 基准为起点做 supersampling / downsampling。
- [ ] 1.2 统一 static / dynamic 两组 external capture 的尺寸推导、稳定舍入和硬件上限 clamp,避免两组 capture layout 漂移。
- [ ] 1.3 确认 supersampling 同时作用于 external depth 与 surfaceColor capture,不出现 depth / color 分辨率失配。

## 2. Preserve resolve semantics

- [ ] 2.1 保持 `Gsplat.compute` 的 external resolve 为 point-based nearest-surface 路径,不要在方案1里引入 blur、naive bilinear 或其他跨边界深度混合。
- [ ] 2.2 在实现与注释中明确 supersampling 只是提高 capture fidelity,不改变 external hit 与 gsplat hit 的 nearest-hit 竞争语义。
- [ ] 2.3 检查 fallback / 默认路径,确保 `Auto + scale=1` 继续等价于当前行为,不会给旧场景引入隐式开销变化。

## 3. Inspector and documentation

- [ ] 3.1 更新 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的 Inspector 文案,明确 `Scale > 1` 是 external capture depth stair-stepping 的首选缓解手段。
- [ ] 3.2 更新 runtime 字段 tooltip / 注释,说明 `Scale`、`Explicit` 与性能开销的关系,避免用户误解这些参数的用途。
- [ ] 3.3 更新 `README.md` 与 `CHANGELOG.md`,补充 supersampling 的适用场景、局限和推荐调参方式。

## 4. Tests and verification

- [ ] 4.1 扩展 `GsplatLidarExternalGpuCaptureTests`,覆盖 `Auto / Scale / Explicit` 的尺寸推导、非法 scale 回退与硬件上限 clamp。
- [ ] 4.2 增加针对“supersampling 不改变 nearest-surface / nearest-hit 语义”的回归测试,防止后续实现偷偷引入跨边界深度混合。
- [ ] 4.3 增加针对 depth / surfaceColor capture layout 一致性的回归测试,锁定 supersampling 对两类 capture 同步生效。
- [ ] 4.4 运行 OpenSpec 状态检查与相关 EditMode 测试,确认 change 进入 apply-ready 且质量契约可验证。
