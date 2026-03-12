## 1. Exporter And CLI

- [x] 1.1 扩展 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 的输入解析,支持“单个普通 3DGS `.ply` 文件”与“`.ply` 目录”两种正式入口,并为 `--input-ply` / `--input-dir` 做互斥校验
- [x] 1.2 将单文件入口归一到现有 `.splat4d` 编码主流程,确保单帧导出继续复用标准 64B record 契约,并把静态默认值锁定为 `vx/vy/vz = 0`, `time = 0`, `duration = 1`
- [x] 1.3 补强导出错误语义,确保缺失 Gaussian Splatting 必需字段时明确失败,且单帧输入误用 `keyframe` 模式时返回清晰错误

## 2. Unity Importer And Runtime Semantics

- [x] 2.1 审计 `Editor/GsplatSplat4DImporter.cs`,确保由普通单帧 `.ply` 生成的静态 `.splat4d` 继续走正常 `.splat4d` 导入路径,并保留 canonical 4D arrays 语义
- [x] 2.2 审计 `Runtime/GsplatRenderer.cs` 的播放控制与 4D 语义路径,确保静态单帧 `.splat4d` 在 `TimeNormalized / AutoPlay / Loop` 下保持固定画面,不出现伪动态行为
- [x] 2.3 在需要的位置补最小 guard、断言和中文注释,把“静态单帧 `.splat4d` 仍是合法 4D 资产”的实现口径固定下来

## 3. Tests And Fixtures

- [x] 3.1 增加工具侧回归测试,覆盖单文件输入、输入互斥、静态默认 4D 字段写出、缺失必需 Gaussian 字段时失败
- [x] 3.2 增加 Unity EditMode 导入测试,覆盖静态单帧 `.splat4d` 仍生成正常 `GsplatAsset`,且 `Velocities / Times / Durations` 与 `splatCount` 等长
- [x] 3.3 增加运行时回归测试,覆盖静态单帧 `.splat4d` 在不同 `TimeNormalized` 以及 `AutoPlay / Loop` 下的稳定固定画面语义
- [x] 3.4 锁定一份仓库内真实普通 3DGS `.ply` 作为正式验收夹具,并用它完成一次 `.ply -> .splat4d -> Unity` 的真实闭环验证

## 4. Docs And Final Verification

- [x] 4.1 更新 `README.md` 与相关工具说明,让 `--input-ply`、单帧静态默认值、以及 `.splat4d -> GsplatRenderer` 的单帧保证和真实实现保持一致
- [x] 4.2 运行相关工具测试、Unity EditMode 回归与真实样例验证,处理新增 error / warning,并保留关键验证证据
- [x] 4.3 刷新 OpenSpec 状态,确认 `splat4d-single-frame-ply-support` 达到 `All artifacts complete` 的 apply-ready 状态
