## 1. Public API and defaults

- [x] 1.1 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 新增 `LidarExternalEdgeAwareResolveMode` 与 `LidarExternalSubpixelResolveMode`,并把默认值设为 `Off + Off`。
- [x] 1.2 在 runtime validate / sanitize 路径中锁定合法枚举值与默认回退,避免旧序列化数据落入未定义状态。
- [x] 1.3 更新两个 Inspector 的 external capture UI,明确四种组合状态与成本差异,并避免把高级质量路径默认打开。

## 2. Hybrid resolve pipeline

- [x] 2.1 在 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` 与相关数据下发链路中补齐 hybrid resolve mode 参数传递。
- [x] 2.2 在 `Runtime/Shaders/Gsplat.compute` 中实现 `Quad4` subpixel candidate 生成,保证候选位置 deterministic 且在 `Off` 时保持中心 uv。
- [x] 2.3 在 `Runtime/Shaders/Gsplat.compute` 中实现 `Kernel2x2 / Kernel3x3` edge-aware nearest resolve,包括一致性过滤与中心回退。
- [x] 2.4 实现统一的 final winner 选择阶段,确保两条路径同时开启时按“subpixel -> edge-aware -> final nearest winner”顺序执行。
- [x] 2.5 确保 final surfaceColor 跟随 final depth winner,不出现颜色与距离来自不同候选的情况。

## 3. Semantics, docs, and guidance

- [x] 3.1 在实现注释与 Inspector/README 文案中明确 hybrid resolve 不是 blur,也不是 naive bilinear depth mixing。
- [x] 3.2 在 `README.md` 与 `CHANGELOG.md` 中补充 `Kernel2x2`、`Kernel3x3`、`Quad4` 的用途、组合方式和性能权衡。
- [x] 3.3 把 `Kernel3x3 + Quad4` 标注为更高质量也更高成本的档位,默认推荐仍保持保守。

## 4. Tests and verification

- [x] 4.1 为 `Off + Off`、edge-only、subpixel-only、combined 四种组合补齐回归测试,锁定公开 API 与默认行为。
- [x] 4.2 增加 edge-aware 过滤与中心回退测试,确认过滤过紧时最多退化回中心 point sample,不会制造未定义结果。
- [x] 4.3 增加 deterministic `Quad4` 与“color follows winner”测试,防止后续实现引入随机 jitter 或 depth/color 脱钩。
- [x] 4.4 运行 `openspec status --change "lidar-external-hybrid-resolve"` 与相关 EditMode 验证,确认 change 进入 apply-ready 且实现阶段有可执行护栏。
