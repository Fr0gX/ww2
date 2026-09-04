using UnityEditor;

namespace WW2.Editor
{
    /// <summary>
    /// ArtV6 overlays are deliberately large source images that are reduced at
    /// runtime. Keep their alpha clean and avoid block-compression fringes.
    /// </summary>
    public sealed class ArtV6TextureImporter : AssetPostprocessor
    {
        private const string Root = "Assets/WW2/Resources/ArtV6/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.borderMipmap = true;
            importer.mipMapsPreserveCoverage = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
