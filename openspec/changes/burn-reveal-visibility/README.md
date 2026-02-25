# burn-reveal-visibility

为 `GsplatRenderer`/`GsplatSequenceRenderer` 增加一个可选的“显隐动画”:

- show: 初始完全不可见,从中心出现燃烧发光环,环形向外扩散,逐步显示完整点云.
- hide: 从中心起燃更强的高亮环,环向外扩散,噪波越来越大像碎屑,逐渐透明消失并最终停止排序/渲染开销.

