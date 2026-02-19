// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    [ScriptedImporter(1, "ply")]
    public class GsplatImporter : ScriptedImporter
    {
        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        static string ReadLine(FileStream fs)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        public static void ReadPlyHeader(FileStream fs, out uint vertexCount, out int propertyCount)
        {
            vertexCount = 0;
            propertyCount = 0;

            string line;
            while ((line = ReadLine(fs)) != null && line != "end_header")
            {
                string[] tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = uint.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                    propertyCount++;
            }
        }

        public class PlyHeaderInfo
        {
            public uint VertexCount = 0;
            public int PropertyCount = 0;
            public int SHPropertyCount = 0;
            public int PositionOffset = -1;
            public int ColorOffset = -1;
            public int SHOffset = -1;
            public int OpacityOffset = -1;
            public int ScaleOffset = -1;
            public int RotationOffset = -1;

            // ----------------------------------------------------------------
            // 4DGS 可选字段 offsets
            // - 只要出现任意一个 4D 字段,导入器就会生成 Velocities/Times/Durations 三个数组,
            //   对缺失字段填默认值,以保证 Runtime 侧逻辑简单且一致.
            // ----------------------------------------------------------------
            public int VelocityXOffset = -1;
            public int VelocityYOffset = -1;
            public int VelocityZOffset = -1;
            public int TimeOffset = -1;
            public int DurationOffset = -1;
        }

        public static PlyHeaderInfo ReadPlyHeader(FileStream fs)
        {
            var info = new PlyHeaderInfo();

            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    info.VertexCount = uint.Parse(tokens[2]);
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        info.PositionOffset = info.PropertyCount;
                        break;
                    case "f_dc_0":
                        info.ColorOffset = info.PropertyCount;
                        break;
                    case "f_rest_0":
                        info.SHOffset = info.PropertyCount;
                        break;
                    case "opacity":
                        info.OpacityOffset = info.PropertyCount;
                        break;
                    case "scale_0":
                        info.ScaleOffset = info.PropertyCount;
                        break;
                    case "rot_0":
                        info.RotationOffset = info.PropertyCount;
                        break;

                    // 4DGS 标准字段
                    case "vx":
                        info.VelocityXOffset = info.PropertyCount;
                        break;
                    case "vy":
                        info.VelocityYOffset = info.PropertyCount;
                        break;
                    case "vz":
                        info.VelocityZOffset = info.PropertyCount;
                        break;
                    case "time":
                        info.TimeOffset = info.PropertyCount;
                        break;
                    case "duration":
                        info.DurationOffset = info.PropertyCount;
                        break;

                    // 4DGS 常见别名字段(用于兼容不同导出器)
                    case "velocity_x":
                        info.VelocityXOffset = info.PropertyCount;
                        break;
                    case "velocity_y":
                        info.VelocityYOffset = info.PropertyCount;
                        break;
                    case "velocity_z":
                        info.VelocityZOffset = info.PropertyCount;
                        break;
                    case "t":
                        info.TimeOffset = info.PropertyCount;
                        break;
                    case "dt":
                        info.DurationOffset = info.PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    info.SHPropertyCount++;
                info.PropertyCount++;
            }

            return info;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            var bounds = new Bounds();

            using (var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
            {
                // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
                if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: currently files larger than 2GB are not supported");
                    return;
                }

                var plyInfo = ReadPlyHeader(fs);
                var shCoeffs = plyInfo.SHPropertyCount / 3;
                gsplatAsset.SplatCount = plyInfo.VertexCount;
                gsplatAsset.SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

                if (gsplatAsset.SHBands > 3 ||
                    GsplatUtils.SHBandsToCoefficientCount(gsplatAsset.SHBands) * 3 != plyInfo.SHPropertyCount)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: unexpected SH property count {plyInfo.SHPropertyCount}");
                    return;
                }

                if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                    plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: missing required properties in PLY header");
                    return;
                }

                gsplatAsset.Positions = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Colors = new Vector4[plyInfo.VertexCount];
                if (shCoeffs > 0)
                    gsplatAsset.SHs = new Vector3[plyInfo.VertexCount * shCoeffs];
                gsplatAsset.Scales = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Rotations = new Vector4[plyInfo.VertexCount];

                // 只要出现任意一个 4D 字段,就启用 4D 数组,缺失字段用默认值填充.
                var hasVelocityX = plyInfo.VelocityXOffset != -1;
                var hasVelocityY = plyInfo.VelocityYOffset != -1;
                var hasVelocityZ = plyInfo.VelocityZOffset != -1;
                var hasAnyVelocity = hasVelocityX || hasVelocityY || hasVelocityZ;
                var hasTime = plyInfo.TimeOffset != -1;
                var hasDuration = plyInfo.DurationOffset != -1;
                var hasAny4D = hasAnyVelocity || hasTime || hasDuration;
                if (hasAny4D)
                {
                    gsplatAsset.Velocities = new Vector3[plyInfo.VertexCount];
                    gsplatAsset.Times = new float[plyInfo.VertexCount];
                    gsplatAsset.Durations = new float[plyInfo.VertexCount];
                }

                var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
                // clamp 统计: 只要发生过 clamp,就输出一次 warning,并包含统计信息.
                var clamped = false;
                var minTime = float.PositiveInfinity;
                var maxTime = float.NegativeInfinity;
                var minDuration = float.PositiveInfinity;
                var maxDuration = float.NegativeInfinity;
                var maxSpeed = 0.0f;
                var maxDurationClamped = 0.0f;
                for (uint i = 0; i < plyInfo.VertexCount; i++)
                {
                    var readBytes = fs.Read(buffer);
                    if (readBytes != buffer.Length)
                    {
                        if (GsplatSettings.Instance.ShowImportErrors)
                            Debug.LogError(
                                $"{ctx.assetPath} import error: unexpected end of file, got {readBytes} bytes at vertex {i}");
                        return;
                    }

                    var properties = MemoryMarshal.Cast<byte, float>(buffer);
                    gsplatAsset.Positions[i] = new Vector3(
                        properties[plyInfo.PositionOffset],
                        properties[plyInfo.PositionOffset + 1],
                        properties[plyInfo.PositionOffset + 2]);
                    gsplatAsset.Colors[i] = new Vector4(
                        properties[plyInfo.ColorOffset],
                        properties[plyInfo.ColorOffset + 1],
                        properties[plyInfo.ColorOffset + 2],
                        GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(
                            properties[j + plyInfo.SHOffset],
                            properties[j + plyInfo.SHOffset + shCoeffs],
                            properties[j + plyInfo.SHOffset + shCoeffs * 2]);
                    gsplatAsset.Scales[i] = new Vector3(
                        Mathf.Exp(properties[plyInfo.ScaleOffset]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                    gsplatAsset.Rotations[i] = new Vector4(
                        properties[plyInfo.RotationOffset],
                        properties[plyInfo.RotationOffset + 1],
                        properties[plyInfo.RotationOffset + 2],
                        properties[plyInfo.RotationOffset + 3]).normalized;

                    if (hasAny4D)
                    {
                        // velocity 默认 0
                        var vel = Vector3.zero;
                        if (hasVelocityX) vel.x = properties[plyInfo.VelocityXOffset];
                        if (hasVelocityY) vel.y = properties[plyInfo.VelocityYOffset];
                        if (hasVelocityZ) vel.z = properties[plyInfo.VelocityZOffset];
                        // velocity 中的 NaN/Inf 会导致后续 maxSpeed 统计失真,这里直接净化为 0.
                        if (float.IsNaN(vel.x) || float.IsInfinity(vel.x))
                        {
                            clamped = true;
                            vel.x = 0.0f;
                        }

                        if (float.IsNaN(vel.y) || float.IsInfinity(vel.y))
                        {
                            clamped = true;
                            vel.y = 0.0f;
                        }

                        if (float.IsNaN(vel.z) || float.IsInfinity(vel.z))
                        {
                            clamped = true;
                            vel.z = 0.0f;
                        }

                        // time 默认 0, duration 默认 1
                        var t0 = hasTime ? properties[plyInfo.TimeOffset] : 0.0f;
                        var dt = hasDuration ? properties[plyInfo.DurationOffset] : 1.0f;

                        // clamp 到 [0,1],并统计 min/max(统计使用 clamp 前的值更利于排查数据源问题)
                        // 注意: 如果数据包含 NaN/Inf,会污染统计与运行时结果,因此这里先做一次净化.
                        if (float.IsNaN(t0) || float.IsInfinity(t0))
                        {
                            clamped = true;
                            t0 = 0.0f;
                        }

                        if (float.IsNaN(dt) || float.IsInfinity(dt))
                        {
                            clamped = true;
                            dt = 0.0f;
                        }

                        minTime = Mathf.Min(minTime, t0);
                        maxTime = Mathf.Max(maxTime, t0);
                        minDuration = Mathf.Min(minDuration, dt);
                        maxDuration = Mathf.Max(maxDuration, dt);

                        var t0Clamped = Mathf.Clamp01(t0);
                        var dtClamped = Mathf.Clamp01(dt);
                        if (t0Clamped != t0 || dtClamped != dt)
                            clamped = true;

                        gsplatAsset.Velocities[i] = vel;
                        gsplatAsset.Times[i] = t0Clamped;
                        gsplatAsset.Durations[i] = dtClamped;

                        // motion 统计(基于 clamp 后的 duration,更贴近运行时可见时间窗)
                        maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                        maxDurationClamped = Mathf.Max(maxDurationClamped, dtClamped);
                    }

                    if (i == 0) bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.Positions[i]);
                    EditorUtility.DisplayProgressBar("Importing Gsplat Asset", "Reading vertices",
                        i / (float)plyInfo.VertexCount);
                }

                if (hasAny4D)
                {
                    gsplatAsset.MaxSpeed = maxSpeed;
                    gsplatAsset.MaxDuration = maxDurationClamped;
                    if (clamped && GsplatSettings.Instance.ShowImportErrors)
                    {
                        Debug.LogWarning(
                            $"{ctx.assetPath} import warning: clamped time/duration to [0,1]. " +
                            $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration})");
                    }
                }
            }

            gsplatAsset.Bounds = bounds;
            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
        }
    }
}
