using System;
using System.Drawing;
using System.IO;
using UnityEditor;
using UnityEngine;

public class PaintedColorImporter 
{
    public static Texture2D Import(
        byte[] imageBytes,
        int expectedWidth,
        int expectedHeight)
    {
        if (imageBytes == null|| imageBytes.Length == 0)
        throw new ArgumentException("empty photo");

        if (expectedWidth <=0 || expectedHeight <=0 ||
        expectedWidth > 16384 || expectedHeight > 16384)
        {
            throw new ArgumentException("your photo wasnt in range");
        }//以上安全检查

        var temporary = new Texture2D(
            2, 2, TextureFormat.RGBA32, false);

        try
        {
            if (!ImageConversion.LoadImage(temporary, imageBytes))
            throw new Exception("fail to read returned photo");

             if (temporary.width != expectedWidth ||
                temporary.height != expectedHeight)
            {
                throw new Exception(
                    $"wrong range, need" +
                    $"{expectedWidth}×{expectedHeight}，actual " +
                    $"{temporary.width}×{temporary.height}。");
            }

            const string folder = "Asset/PaintedDepth/Generated";

            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/PaintedColor.png");

            File.WriteAllBytes(assetPath, temporary.EncodeToPNG());
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
            
            var importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;

                // PaintedColor 是颜色图，不是法线或深度数据。
                importer.sRGBTexture = true;

                // 先用于逐像素预览，运行时再配置 mipmap。
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;

                importer.textureCompression =
                    TextureImporterCompression.Uncompressed;

                importer.maxTextureSize = Mathf.Clamp(
                    Mathf.NextPowerOfTwo(
                        Mathf.Max(expectedWidth, expectedHeight)),
                    32,
                    16384);

                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                assetPath);
        }



        finally
        {
            UnityEngine.Object.DestroyImmediate(temporary);
        }

        

    }
}
