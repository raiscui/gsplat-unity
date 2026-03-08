## 1. Runtime API 与 Inspector

- [x] 1.1 在 `Runtime/GsplatUtils.cs` 新增 LiDAR 粒子 AA 模式枚举,明确包含 `LegacySoftEdge`、`AnalyticCoverage`、`AlphaToCoverage`、`AnalyticCoveragePlusAlphaToCoverage`.
- [x] 1.2 为 `GsplatRenderer` 增加 LiDAR 粒子 AA 序列化字段、默认值与 validate 防御,并保持旧场景兼容.
- [x] 1.3 为 `GsplatSequenceRenderer` 增加同款 LiDAR 粒子 AA 字段与校验,确保两条 runtime API 一致.
- [x] 1.4 更新 `Editor/GsplatRendererEditor.cs`,在 LiDAR Visual 区域暴露 AA 模式,并写清推荐模式与 MSAA 前提.
- [x] 1.5 更新 `Editor/GsplatSequenceRendererEditor.cs`,同步暴露同款 AA 模式与说明文案.

## 2. Shader 与材质基础设施

- [x] 2.1 在当前 LiDAR shader 主链中实现 `AnalyticCoverage` 路线,让边缘过渡由屏幕导数驱动,不再只依赖固定 feather.
- [x] 2.2 保留 `LegacySoftEdge` 路线,确保旧项目可以稳定回退到当前边缘语义.
- [x] 2.3 为 `AlphaToCoverage` 引入 LiDAR 专用 A2C shader shell 或等价材质资源,避免试图在单一 pass 内动态切 `AlphaToMask`.
- [x] 2.4 更新 `Runtime/GsplatSettings.cs`,补齐 LiDAR A2C 所需 shader / material 引用或创建逻辑.
- [x] 2.5 在 `Runtime/Lidar/GsplatLidarScan.cs` 接入 LiDAR AA 模式到 draw 提交链路,根据有效模式选择对应材质和 shader 分支.

## 3. 有效模式判定与 fallback

- [x] 3.1 实现 LiDAR A2C 模式的有效性判断,至少覆盖“当前相机或目标是否具备有效 MSAA”这一前提.
- [x] 3.2 让 `AlphaToCoverage` 在无 MSAA 时稳定回退到 `AnalyticCoverage`,而不是静默失效或进入不可预测状态.
- [x] 3.3 让 `AnalyticCoveragePlusAlphaToCoverage` 在无 MSAA 时同样回退到 `AnalyticCoverage`.
- [x] 3.4 补充必要的 Inspector helpbox 或运行时诊断,明确说明 A2C 当前是否真的生效.

## 4. 回归测试与文档

- [x] 4.1 扩展 `Tests/Editor/GsplatLidarScanTests.cs`,覆盖 LiDAR 粒子 AA 模式默认值、非法值归一化与 fallback 语义.
- [x] 4.2 扩展或新增 `Tests/Editor/GsplatLidarShaderPropertyTests.cs`,锁定 LiDAR shader 具备 analytic coverage 路线,以及 A2C shell 的 `AlphaToMask On` 契约.
- [x] 4.3 更新 `README.md`,说明各 AA 模式的差异、推荐值和 MSAA 依赖关系.
- [x] 4.4 更新 `CHANGELOG.md`,记录 RadarScan 粒子新增可选抗锯齿模式.
- [x] 4.5 运行 OpenSpec 状态检查,确认 `proposal/design/spec/tasks` 完整可 apply.
