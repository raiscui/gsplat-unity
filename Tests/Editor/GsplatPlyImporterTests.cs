// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatPlyImporterTests
    {
        const string k_TestRootAssetPath = "Assets/__GsplatPlyImporterTests";
        const string k_TestAssetPath = k_TestRootAssetPath + "/single_frame_valid_3dgs.ply";

        [SetUp]
        public void SetUp()
        {
            // 目的: 让 importer 失败时直接把错误抛到 Console,便于定位.
            GsplatSettings.Instance.ShowImportErrors = true;

            if (!AssetDatabase.IsValidFolder(k_TestRootAssetPath))
                AssetDatabase.CreateFolder("Assets", "__GsplatPlyImporterTests");
        }

        [TearDown]
        public void TearDown()
        {
            // 清理测试生成的临时资产,避免污染其它测试.
            AssetDatabase.DeleteAsset(k_TestRootAssetPath);
        }

        static string GetProjectRootPath()
        {
            // Application.dataPath => <project>/Assets
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static string GetFixtureFullPath()
        {
            return Path.Combine(GetProjectRootPath(), "Packages", "wu.yize.gsplat", "Tools~", "Splat4D", "tests",
                "data", "single_frame_valid_3dgs.ply");
        }

        static void CopyFixtureToAssetPath(string assetPath)
        {
            var fixtureFullPath = GetFixtureFullPath();
            Assert.IsTrue(File.Exists(fixtureFullPath), $"缺少 PLY importer 测试夹具: {fixtureFullPath}");

            var targetFullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath));
            File.Copy(fixtureFullPath, targetFullPath, overwrite: true);
        }

        [Test]
        public void Import_FixturePly_CreatesPlayablePrefabMainObject()
        {
            // 目标:
            // - `.ply` 导入后 main object 必须是可实例化的 GameObject.
            // - 该 GameObject 需要自动挂载 `GsplatRenderer`,并绑定导入出的 `GsplatAsset`.
            CopyFixtureToAssetPath(k_TestAssetPath);

            AssetDatabase.ImportAsset(k_TestAssetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_TestAssetPath);
            Assert.IsNotNull(prefab, "导入 `.ply` 后 main prefab 应该存在,否则无法直接拖入场景");
            Assert.AreEqual("single_frame_valid_3dgs", prefab.name, "main prefab 名称应与文件名保持一致");

            var renderer = prefab.GetComponent<GsplatRenderer>();
            Assert.IsNotNull(renderer, "导入 `.ply` 后 prefab 应自动挂载 GsplatRenderer");
            Assert.IsNotNull(renderer.GsplatAsset, "导入 `.ply` 后 renderer.GsplatAsset 应自动绑定");
            Assert.Greater(renderer.GsplatAsset.SplatCount, 0u, "导入 `.ply` 后 splat 数量应大于 0");

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(k_TestAssetPath);
            var foundSubAsset = false;
            foreach (var asset in allAssets)
            {
                if (asset == renderer.GsplatAsset)
                {
                    foundSubAsset = true;
                    break;
                }
            }

            Assert.IsTrue(foundSubAsset, "导入 `.ply` 后 GsplatAsset 应作为 sub-asset 保留");
        }
    }
}
