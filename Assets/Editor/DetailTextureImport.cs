using UnityEditor;

/// <summary>
/// Piece detail maps (Resources/Pieces/*/*_Detail.png) stay readable so PieceModels.TwoTone
/// can colour them for the finishes. Recolour masks (*_Mask.png) are data, so they import linear.
/// </summary>
public class DetailTextureImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/Pieces/")) return;
        var importer = (TextureImporter)assetImporter;
        if (assetPath.EndsWith("_Detail.png")) importer.isReadable = true;
        if (assetPath.EndsWith("_Mask.png")) importer.sRGBTexture = false; // mask and shading, not a colour
    }
}
