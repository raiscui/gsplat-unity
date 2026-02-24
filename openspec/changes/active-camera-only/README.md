# active-camera-only

ActiveCameraOnly: 把每帧 GPU sort 的次数收敛为 ~1 次,降低多相机重复排序开销.

- Play 模式/Player: 只保证 1 个 ActiveCamera 的 sort+render(性能优先).
- Editor 非 Play: 以 SceneView 体验为第一优先级,避免 UI 交互导致 SceneView 整体“显示/不显示”闪烁.
