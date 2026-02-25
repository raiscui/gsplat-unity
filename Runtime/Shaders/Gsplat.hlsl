// Gaussian Splatting helper functions & structs
// most of these are from https://github.com/playcanvas/engine/tree/main/src/scene/shader-lib/glsl/chunks/gsplat
// Copyright (c) 2011-2024 PlayCanvas Ltd
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

struct SplatSource
{
    uint order;
    uint id;
    float2 cornerUV;
};

struct SplatCenter
{
    float3 view;
    float4 proj;
    float4x4 modelView;
    float projMat00;
};

struct SplatCovariance
{
    float3 covA;
    float3 covB;
};

// stores the offset from center for the current gaussian
struct SplatCorner
{
    float2 offset; // corner offset from center in clip space
    float2 uv; // corner uv
    #if GSPLAT_AA
    float aaFactor; // for scenes generated with antialiasing
    #endif
};

const float4 discardVec = float4(0.0, 0.0, 2.0, 1.0);

float3x3 QuatToMat3(float4 R)
{
    float4 R2 = R + R;
    float X = R2.x * R.w;
    float4 Y = R2.y * R;
    float4 Z = R2.z * R;
    float W = R2.w * R.w;

    return float3x3(
        1.0 - Z.z - W,
        Y.z + X,
        Y.w - Z.x,
        Y.z - X,
        1.0 - Y.y - W,
        Z.w + Y.x,
        Y.w + Z.x,
        Z.w - Y.x,
        1.0 - Y.y - Z.z
    );
}

// quat format: w, x, y, z 
SplatCovariance CalcCovariance(float4 quat, float3 scale)
{
    float3x3 rot = QuatToMat3(quat);

    // M = S * R
    float3x3 M = transpose(float3x3(
        scale.x * rot[0],
        scale.y * rot[1],
        scale.z * rot[2]
    ));

    SplatCovariance cov;
    cov.covA = float3(dot(M[0], M[0]), dot(M[0], M[1]), dot(M[0], M[2]));
    cov.covB = float3(dot(M[1], M[1]), dot(M[1], M[2]), dot(M[2], M[2]));
    return cov;
}

// calculate the clip-space offset from the center for this gaussian
bool InitCorner(SplatSource source, SplatCovariance covariance, SplatCenter center, out SplatCorner corner)
{
    float3 covA = covariance.covA;
    float3 covB = covariance.covB;
    float3x3 Vrk = float3x3(
        covA.x, covA.y, covA.z,
        covA.y, covB.x, covB.y,
        covA.z, covB.y, covB.z
    );

    float focal = _ScreenParams.x * center.projMat00;

    float3 v = unity_OrthoParams.w == 1.0 ? float3(0.0, 0.0, 1.0) : center.view.xyz;

    float J1 = focal / v.z;
    float2 J2 = -J1 / v.z * v.xy;
    float3x3 J = float3x3(
        J1, 0.0, J2.x,
        0.0, J1, J2.y,
        0.0, 0.0, 0.0
    );

    float3x3 W = center.modelView;
    float3x3 T = mul(J, W);
    float3x3 cov = mul(mul(T, Vrk), transpose(T));

    #if GSPLAT_AA
        // calculate AA factor
        float detOrig = cov[0][0] * cov[1][1] - cov[0][1] * cov[0][1];
        float detBlur = (cov[0][0] + 0.3) * (cov[1][1] + 0.3) - cov[0][1] * cov[0][1];
        corner.aaFactor = sqrt(max(detOrig / detBlur, 0.0));
    #endif

    float diagonal1 = cov[0][0] + 0.3;
    float offDiagonal = cov[0][1];
    float diagonal2 = cov[1][1] + 0.3;

    float mid = 0.5 * (diagonal1 + diagonal2);
    float radius = length(float2((diagonal1 - diagonal2) / 2.0, offDiagonal));
    float lambda1 = mid + radius;
    float lambda2 = max(mid - radius, 0.1);

    // Use the smaller viewport dimension to limit the kernel size relative to the screen resolution.
    float vmin = min(1024.0, min(_ScreenParams.x, _ScreenParams.y));

    float l1 = 2.0 * min(sqrt(2.0 * lambda1), vmin);
    float l2 = 2.0 * min(sqrt(2.0 * lambda2), vmin);

    // early-out gaussians smaller than 2 pixels
    if (l1 < 2.0 && l2 < 2.0)
    {
        return false;
    }

    float2 c = center.proj.ww / _ScreenParams.xy;

    // cull against frustum x/y axes
    float maxL = max(l1, l2);
    if (any(abs(center.proj.xy) - float2(maxL, maxL) * c > center.proj.ww))
    {
        return false;
    }

    float2 diagonalVector = normalize(float2(offDiagonal, lambda1 - diagonal1));
    float2 v1 = l1 * diagonalVector;
    float2 v2 = l2 * float2(diagonalVector.y, -diagonalVector.x);

    corner.offset = (source.cornerUV.x * v1 + source.cornerUV.y * v2) * c;
    corner.uv = source.cornerUV;

    return true;
}

void ClipCorner(inout SplatCorner corner, float alpha)
{
    float clip = min(1.0, sqrt(-log(1.0 / 255.0 / alpha)) / 2.0);
    corner.offset *= clip;
    corner.uv *= clip;
}

// spherical Harmonics
#ifdef SH_BANDS_1
#define SH_COEFFS 3
#elif defined(SH_BANDS_2)
#define SH_COEFFS 8
#elif defined(SH_BANDS_3)
#define SH_COEFFS 15
#else
#define SH_COEFFS 0
#endif

#define SH_C0 0.28209479177387814f

#ifndef SH_BANDS_0
#define SH_C1 0.4886025119029199f
#define SH_C2_0 1.0925484305920792f
#define SH_C2_1 -1.0925484305920792f
#define SH_C2_2 0.31539156525252005f
#define SH_C2_3 -1.0925484305920792f
#define SH_C2_4 0.5462742152960396f
#define SH_C3_0 -0.5900435899266435f
#define SH_C3_1 2.890611442640554f
#define SH_C3_2 -0.4570457994644658f
#define SH_C3_3 0.3731763325901154f
#define SH_C3_4 -0.4570457994644658f
#define SH_C3_5 1.445305721320277f
#define SH_C3_6 -0.5900435899266435f

// see https://github.com/graphdeco-inria/gaussian-splatting/blob/main/utils/sh_utils.py
float3 EvalSH(const inout float3 sh[SH_COEFFS], float3 dir, int degree = 3)
{
    if (degree == 0)
        return float3(0, 0, 0);
    
    float x = dir.x;
    float y = dir.y;
    float z = dir.z;

    // 1st degree
    float3 result = SH_C1 * (-sh[0] * y + sh[1] * z - sh[2] * x);
    if (degree == 1)
        return result;

#if defined(SH_BANDS_2) || defined(SH_BANDS_3)
    // 2nd degree
    float xx = x * x;
    float yy = y * y;
    float zz = z * z;
    float xy = x * y;
    float yz = y * z;
    float xz = x * z;

    result = result + (
        sh[3] * (SH_C2_0 * xy) +
        sh[4] * (SH_C2_1 * yz) +
        sh[5] * (SH_C2_2 * (2.0 * zz - xx - yy)) +
        sh[6] * (SH_C2_3 * xz) +
        sh[7] * (SH_C2_4 * (xx - yy))
    );

    if (degree == 2)
        return result;
#endif

#ifdef SH_BANDS_3
    // 3rd degree
    result = result + (
        sh[8] * (SH_C3_0 * y * (3.0 * xx - yy)) +
        sh[9] * (SH_C3_1 * xy * z) +
        sh[10] * (SH_C3_2 * y * (4.0 * zz - xx - yy)) +
        sh[11] * (SH_C3_3 * z * (2.0 * zz - 3.0 * xx - 3.0 * yy)) +
        sh[12] * (SH_C3_4 * x * (4.0 * zz - xx - yy)) +
        sh[13] * (SH_C3_5 * z * (xx - yy)) +
        sh[14] * (SH_C3_6 * x * (xx - 3.0 * yy))
    );
#endif

    return result;
}
#endif

// --------------------------------------------------------------------
// 可选: 显隐燃烧环动画用的轻量 noise(无贴图,无 sin)
// - 输出 noise01: [0,1]
// - 输出 noiseSigned: [-1,1]
// - 设计目标: 开销小,且在 model space 下可稳定复现,用于边界抖动与灰烬颗粒感.
// --------------------------------------------------------------------
float GsplatHash13(float3 p3)
{
    // 参考: "hash without sine" 的常见写法(只用 frac/dot/mul/add).
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

void GsplatEvalHashNoise01(float3 p, out float noise01, out float noiseSigned)
{
    noise01 = GsplatHash13(p);
    noiseSigned = noise01 * 2.0 - 1.0;
}

// 3D value noise:
// - 使用 8 个格点 hash + trilinear 插值,空间上更平滑,更接近“烟雾/流体”的连续变化.
// - 返回范围: [0,1]
float GsplatValueNoise01(float3 p)
{
    float3 ip = floor(p);
    float3 fp = frac(p);

    // 平滑插值权重(Perlin 的 fade 简化版).
    float3 u = fp * fp * (3.0 - 2.0 * fp);

    float n000 = GsplatHash13(ip + float3(0.0, 0.0, 0.0));
    float n100 = GsplatHash13(ip + float3(1.0, 0.0, 0.0));
    float n010 = GsplatHash13(ip + float3(0.0, 1.0, 0.0));
    float n110 = GsplatHash13(ip + float3(1.0, 1.0, 0.0));
    float n001 = GsplatHash13(ip + float3(0.0, 0.0, 1.0));
    float n101 = GsplatHash13(ip + float3(1.0, 0.0, 1.0));
    float n011 = GsplatHash13(ip + float3(0.0, 1.0, 1.0));
    float n111 = GsplatHash13(ip + float3(1.0, 1.0, 1.0));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z);
}

void GsplatEvalValueNoise01(float3 p, out float noise01, out float noiseSigned)
{
    noise01 = GsplatValueNoise01(p);
    noiseSigned = noise01 * 2.0 - 1.0;
}

// --------------------------------------------------------------------
// 3D value noise + gradient:
// - 目标: 给 curl-like 噪声场提供“可导”的连续噪声.
// - 实现方式: 仍然只做 8 个 corner hash,但同时计算 trilinear+fade 的偏导数.
//
// 注意:
// - 这里的梯度是对输入 p 的偏导(∂/∂x,∂/∂y,∂/∂z).
// - 在 cell 边界处 fade 的导数为 0,因此梯度可保持连续,比白噪声式抖动更像烟雾流动.
// --------------------------------------------------------------------
void GsplatValueNoise01Grad(float3 p, out float noise01, out float3 grad01)
{
    float3 ip = floor(p);
    float3 fp = frac(p);

    // fade: f(t)=t^2*(3-2t), f'(t)=6t(1-t)
    float3 u = fp * fp * (3.0 - 2.0 * fp);
    float3 du = 6.0 * fp * (1.0 - fp);

    float n000 = GsplatHash13(ip + float3(0.0, 0.0, 0.0));
    float n100 = GsplatHash13(ip + float3(1.0, 0.0, 0.0));
    float n010 = GsplatHash13(ip + float3(0.0, 1.0, 0.0));
    float n110 = GsplatHash13(ip + float3(1.0, 1.0, 0.0));
    float n001 = GsplatHash13(ip + float3(0.0, 0.0, 1.0));
    float n101 = GsplatHash13(ip + float3(1.0, 0.0, 1.0));
    float n011 = GsplatHash13(ip + float3(0.0, 1.0, 1.0));
    float n111 = GsplatHash13(ip + float3(1.0, 1.0, 1.0));

    // x 方向插值
    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);

    // y 方向插值
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);

    // z 方向插值(最终 noise)
    noise01 = lerp(nxy0, nxy1, u.z);

    // ∂/∂x:
    // - 只有 u.x 依赖 x,因此只需要对 x 方向的 lerp 求导,再把结果继续按 y/z 插值.
    float dnx00_dx = (n100 - n000) * du.x;
    float dnx10_dx = (n110 - n010) * du.x;
    float dnx01_dx = (n101 - n001) * du.x;
    float dnx11_dx = (n111 - n011) * du.x;
    float dnxy0_dx = lerp(dnx00_dx, dnx10_dx, u.y);
    float dnxy1_dx = lerp(dnx01_dx, dnx11_dx, u.y);
    float dn_dx = lerp(dnxy0_dx, dnxy1_dx, u.z);

    // ∂/∂y:
    // - nxy0 = nx00 + (nx10-nx00)*u.y
    // - nxy1 = nx01 + (nx11-nx01)*u.y
    float dnxy0_dy = (nx10 - nx00) * du.y;
    float dnxy1_dy = (nx11 - nx01) * du.y;
    float dn_dy = lerp(dnxy0_dy, dnxy1_dy, u.z);

    // ∂/∂z:
    // - noise = nxy0 + (nxy1-nxy0)*u.z
    float dn_dz = (nxy1 - nxy0) * du.z;

    grad01 = float3(dn_dx, dn_dy, dn_dz);
}

void GsplatEvalValueNoise01Grad(float3 p, out float noise01, out float noiseSigned, out float3 gradSigned)
{
    float3 grad01;
    GsplatValueNoise01Grad(p, noise01, grad01);
    noiseSigned = noise01 * 2.0 - 1.0;
    gradSigned = grad01 * 2.0;
}

// --------------------------------------------------------------------
// Curl-like 噪声场:
// - 用 3 份独立的 value noise 作为 vector potential A(p)=(Ax,Ay,Az),
//   然后取 curl(A)=∇×A 得到“旋涡/流动”更明显的向量场.
//
// 设计目标:
// - 相比直接用两个标量噪声拼 tangent/bitangent,它更像连续烟雾的旋涡,更少“随机抖动”感.
// - 只在 show/hide 动画期间启用,因此即使稍贵也可接受.
// --------------------------------------------------------------------
float3 GsplatEvalCurlNoise(float3 p)
{
    // 3 个不同 offset 的噪声,作为 vector potential 的三个分量.
    // 说明: offset 只要足够大且不共线即可,这里选固定常数用于可复现.
    float n1, s1;
    float n2, s2;
    float n3, s3;
    float3 g1, g2, g3;
    GsplatEvalValueNoise01Grad(p + float3(17.13, 31.77, 47.11), n1, s1, g1);
    GsplatEvalValueNoise01Grad(p + float3(53.11, 12.77, 9.71), n2, s2, g2);
    GsplatEvalValueNoise01Grad(p + float3(29.21, 83.11, 11.73), n3, s3, g3);

    // curl(A) = (∂Az/∂y - ∂Ay/∂z, ∂Ax/∂z - ∂Az/∂x, ∂Ay/∂x - ∂Ax/∂y)
    return float3(
        g3.y - g2.z,
        g1.z - g3.x,
        g2.x - g1.y
    );
}
