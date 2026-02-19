// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Gsplat.Editor
{
    internal static class GsplatWebpNative
    {
        // ------------------------------------------------------------------
        // 说明:
        // - `.sog4d` 的 WebP 是“数据图”,必须 lossless 解码成 RGBA8.
        // - Unity 的 `ImageConversion.LoadImage` 在很多版本里不支持 WebP.
        // - 因此我们提供一个原生 `libwebp` decoder(目前: macOS Editor),
        //   用于 importer 与 tests 的一致性解码.
        //
        // 目标:
        // - 解码输出的 byte 顺序与 libwebp 一致: RGBA,行优先(row-major).
        // - 不引入 System.Drawing 这类 Unity 不友好的依赖.
        // ------------------------------------------------------------------

        const string k_NativeLib = "GsplatWebpDecoder";

        // int WebPGetInfo(const uint8_t* data, size_t data_size, int* width, int* height);
        [DllImport(k_NativeLib, EntryPoint = "WebPGetInfo", CallingConvention = CallingConvention.Cdecl)]
        static extern int WebPGetInfo(byte[] data, UIntPtr dataSize, out int width, out int height);

        // uint8_t* WebPDecodeRGBAInto(const uint8_t* data, size_t data_size,
        //                             uint8_t* output_buffer, size_t output_buffer_size,
        //                             int output_stride);
        [DllImport(k_NativeLib, EntryPoint = "WebPDecodeRGBAInto", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr WebPDecodeRGBAInto(
            byte[] data,
            UIntPtr dataSize,
            byte[] outputBuffer,
            UIntPtr outputBufferSize,
            int outputStride);

        internal static bool TryDecodeRgba32(byte[] webpBytes, out int width, out int height, out byte[] rgba,
            out string error)
        {
            width = 0;
            height = 0;
            rgba = null;
            error = null;

            if (webpBytes == null || webpBytes.Length == 0)
            {
                error = "empty WebP bytes";
                return false;
            }

            try
            {
                if (WebPGetInfo(webpBytes, (UIntPtr)webpBytes.Length, out width, out height) == 0)
                {
                    error = "WebPGetInfo failed (invalid header?)";
                    return false;
                }

                // RGBA8
                var stride = checked(width * 4);
                var byteCount = checked(stride * height);
                rgba = new byte[byteCount];

                var p = WebPDecodeRGBAInto(webpBytes, (UIntPtr)webpBytes.Length, rgba, (UIntPtr)rgba.Length, stride);
                if (p == IntPtr.Zero)
                {
                    rgba = null;
                    error = "WebPDecodeRGBAInto returned null (decode failed)";
                    return false;
                }

                return true;
            }
            catch (DllNotFoundException)
            {
                error =
                    "native WebP decoder not found. On macOS Editor, expect `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib` to be present.";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                error = "native WebP decoder entrypoints not found (WebPGetInfo/WebPDecodeRGBAInto)";
                return false;
            }
            catch (Exception e)
            {
                rgba = null;
                error = e.Message;
                return false;
            }
        }

        internal static bool SupportsWebpDecoding(byte[] webpBytes)
        {
            // 用“真实解码一次”的方式判定,避免只过 header 但 decode 不可用的假阳性.
            return TryDecodeRgba32(webpBytes, out _, out _, out _, out _);
        }
    }
}

