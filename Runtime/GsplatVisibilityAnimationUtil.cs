// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// 显隐动画进度的驱动方式.
    /// </summary>
    public enum GsplatVisibilityProgressMode
    {
        /// <summary>
        /// 按目标世界空间前沿速度推进.
        /// - 目标: 让不同尺寸的 3DGS 在场景里拥有更接近的 show/hide 扩散速度.
        /// - 默认值按项目里的 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 当前效果做了标定.
        /// </summary>
        WorldSpeed = 0,

        /// <summary>
        /// 兼容旧行为: 继续按 ShowDuration / HideDuration 直接推进 progress01.
        /// </summary>
        LegacyDuration = 1,
    }

    /// <summary>
    /// 显隐动画的共享数学工具.
    /// - 目的: 让 `GsplatRenderer` 与 `GsplatSequenceRenderer` 使用同一套 reveal 进度公式.
    /// - 默认世界速度按当前项目里的 ckpt 参考效果标定,从而做到“基准资产观感不变,其它资产按同一世界速度播放”.
    /// </summary>
    static class GsplatVisibilityAnimationUtil
    {
        // --------------------------------------------------------------------
        // 基准速度(单位: model/world units per second)
        // - 标定来源:
        //   - 参考资产: ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest
        //   - 当前项目场景下估算的 effective maxRadius ≈ 39.65154
        //   - show: totalRange = maxRadius * (1 + 0.05), duration = 4s
        //   - hide: totalRange = maxRadius * (1 + 0.273), duration = 6s
        // - 这样切到 WorldSpeed 后,参考资产仍能保持当前观感.
        // --------------------------------------------------------------------
        internal const float k_DefaultShowWorldSpeed = 10.40853f;
        internal const float k_DefaultHideWorldSpeed = 8.412735f;
        internal const float k_DefaultVisibilityRadiusScale = 1.0f;

        const float k_MinTotalRange = 1.0e-5f;

        internal static GsplatVisibilityProgressMode SanitizeProgressMode(GsplatVisibilityProgressMode mode)
        {
            return mode == GsplatVisibilityProgressMode.LegacyDuration
                ? GsplatVisibilityProgressMode.LegacyDuration
                : GsplatVisibilityProgressMode.WorldSpeed;
        }

        internal static float SanitizeRadiusScale(float radiusScale)
        {
            if (float.IsNaN(radiusScale) || float.IsInfinity(radiusScale) || radiusScale < 0.0f)
                return k_DefaultVisibilityRadiusScale;

            return radiusScale;
        }

        internal static float SanitizeWorldSpeed(float worldSpeed, float fallback)
        {
            if (float.IsNaN(worldSpeed) || float.IsInfinity(worldSpeed) || worldSpeed <= 0.0f)
                return fallback;

            return worldSpeed;
        }

        internal static float SanitizeDuration(float duration, float fallback)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0.0f)
                return fallback;

            return duration;
        }

        internal static float CalcScaledMaxRadius(float rawMaxRadius, float radiusScale)
        {
            if (float.IsNaN(rawMaxRadius) || float.IsInfinity(rawMaxRadius) || rawMaxRadius < 0.0f)
                rawMaxRadius = 0.0f;

            return rawMaxRadius * SanitizeRadiusScale(radiusScale);
        }

        internal static float CalcRadialWidth(float maxRadius, float widthNormalized)
        {
            if (float.IsNaN(maxRadius) || float.IsInfinity(maxRadius) || maxRadius < 0.0f)
                maxRadius = 0.0f;

            return maxRadius * Mathf.Clamp01(widthNormalized);
        }

        internal static float CalcTotalRange(float maxRadius, float trailWidthNormalized)
        {
            return maxRadius + CalcRadialWidth(maxRadius, trailWidthNormalized);
        }

        internal static float EaseInOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f)
                return 2.0f * t * t;

            var u = -2.0f * t + 2.0f;
            return 1.0f - (u * u) * 0.5f;
        }

        internal static float InverseEaseInOutQuad(float eased)
        {
            eased = Mathf.Clamp01(eased);
            if (eased <= 0.0f)
                return 0.0f;
            if (eased >= 1.0f)
                return 1.0f;

            if (eased < 0.5f)
                return Mathf.Sqrt(eased * 0.5f);

            return 1.0f - 0.5f * Mathf.Sqrt(2.0f * (1.0f - eased));
        }

        internal static float CalcProgressStep(
            float dt,
            GsplatVisibilityProgressMode mode,
            float duration,
            float worldSpeed,
            float totalRange)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                return 0.0f;

            if (SanitizeProgressMode(mode) == GsplatVisibilityProgressMode.LegacyDuration)
            {
                if (duration <= 0.0f || float.IsNaN(duration) || float.IsInfinity(duration))
                    return 1.0f;

                return dt / duration;
            }

            if (totalRange <= k_MinTotalRange || float.IsNaN(totalRange) || float.IsInfinity(totalRange))
                return 1.0f;

            var sanitizedWorldSpeed = SanitizeWorldSpeed(worldSpeed, k_DefaultShowWorldSpeed);
            return dt * sanitizedWorldSpeed / totalRange;
        }

        internal static float CalcAdvancedProgress(
            float currentProgress,
            float dt,
            GsplatVisibilityProgressMode mode,
            float duration,
            float worldSpeed,
            float totalRange)
        {
            currentProgress = Mathf.Clamp01(currentProgress);
            var progressStep = CalcProgressStep(dt, mode, duration, worldSpeed, totalRange);
            if (progressStep <= 0.0f)
                return currentProgress;

            if (progressStep >= 1.0f)
                return 1.0f;

            if (SanitizeProgressMode(mode) == GsplatVisibilityProgressMode.LegacyDuration)
                return Mathf.Clamp01(currentProgress + progressStep);

            // ----------------------------------------------------------------
            // WorldSpeed 的目标不是让“原始 progress”线性增长.
            // 真正参与 shader reveal 半径计算的是 easedProgress:
            //   radius = EaseInOutQuad(progress01) * totalRange
            // 因此这里要在线性的世界距离空间里推进 easedProgress,
            // 再反解回 progress01,这样不同 totalRange 才能得到接近的前沿速度.
            // ----------------------------------------------------------------
            var easedProgress = EaseInOutQuad(currentProgress);
            var nextEasedProgress = Mathf.Clamp01(easedProgress + progressStep);
            return InverseEaseInOutQuad(nextEasedProgress);
        }
    }
}
