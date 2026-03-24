# LATER_PLANS: LiDAR 高密度扫描边缘锯齿成因分析

## [2026-03-16 22:03:10] [Session ID: 99992] 候选改进: 提供更连续的 LiDAR 轮廓模式

- 候选方向:
  - 方案A: 在保持 first return range image 的前提下,增加"sub-cell supersample / jitter accumulate"模式,用时间或多样本减少台阶感
  - 方案B: 增加"surface reconstruction / triangle strip"实验模式,接受语义变化,换取更连续的边缘
  - 方案C: 保持规则点云语义不变,但提供圆点或更高质量 A2C/MSAA 路线,减少方点轮廓感
- 适用场景:
  - 用户更在意轮廓连续性,而不是"像车载 LiDAR 一样的规则扫描线"
- 当前为什么没做:
  - 这已经超出本次"原因分析"范围,属于效果设计取舍与新功能路线
