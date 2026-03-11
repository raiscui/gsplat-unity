## 1. Exporter And CLI

- [ ] 1.1 扩展 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 的输入解析,支持“单个 `.ply` 文件”与“`.ply` 目录”两种正式入口,并做互斥校验
- [ ] 1.2 将单帧入口归一到现有 `ply_files` 主流程,确保 `frameCount = 1` 时继续生成标准 `.sog4d` bundle,而不是伪造第二帧
- [ ] 1.3 补强 validate / self-check 的单帧口径,确认合法单帧 bundle 不会被误判为无效

## 2. Unity Importer And Runtime

- [ ] 2.1 审计 `Editor/GsplatSog4DImporter.cs` 的单帧导入路径,补显式 guard/断言,确保 `frameCount = 1` 时继续生成 `GsplatSequenceAsset`
- [ ] 2.2 审计 `Runtime/GsplatSog4DRuntimeBundle.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 的单帧退化路径,确保 decode / interpolate / chunk 逻辑不依赖独立第二帧
- [ ] 2.3 在需要的位置补最小修复与注释,把单帧固定帧语义与现有 `EvaluateFromTimeNormalized(...)` 保持一致

## 3. Tests And Fixtures

- [ ] 3.1 增加单帧 `.ply -> .sog4d` 的最小工具侧回归验证,覆盖输入互斥、`frameCount = 1` 输出与 validate 通过
- [ ] 3.2 增加单帧 `.sog4d` 的 Unity EditMode 导入测试,覆盖“仍生成序列资产”与“可被 `GsplatSequenceRenderer` 引用”
- [ ] 3.3 增加单帧运行时退化测试,覆盖 `TimeNormalized` / `InterpolationMode` 在单帧资产上的固定帧语义

## 4. Docs And Verification

- [ ] 4.1 更新 `Tools~/Sog4D/README.md`,补单帧输入命令示例、输入规则与常见错误说明
- [ ] 4.2 更新 `README.md` 的 `.sog4d` 说明,明确单帧 `.ply -> .sog4d -> Unity` 是正式支持路径,且导入后仍是 `GsplatSequenceAsset`
- [ ] 4.3 运行相关自动化验证,至少覆盖工具自检与 Unity EditMode 回归,并处理新增 error / warning
- [ ] 4.4 刷新 OpenSpec 状态,确认 `sog4d-single-frame-ply-support` 已具备 apply-ready 所需 artifact 与实现证据
