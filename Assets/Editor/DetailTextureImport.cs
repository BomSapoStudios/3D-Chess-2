using UnityEditor;

/// <summary>
/// Piece detail maps (Resources/Pieces/*/*_Detail.png) stay readable so PieceModels.TwoTone
/// can colour them for the finishes.
/// </summary>
public class DetailTextureImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/Pieces/") || !assetPath.EndsWith("_Detail.png")) return;
        var importer = (TextureImporter)assetImporter;
        importer.isReadable = true;
    }
}
