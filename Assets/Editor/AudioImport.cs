using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the game's audio: short effects are kept small and ready in memory (ADPCM,
/// decompressed on load); music streams from disk.
/// </summary>
public class AudioImport : AssetPostprocessor
{
    private void OnPreprocessAudio()
    {
        var importer = (AudioImporter)assetImporter;
        bool sfx = assetPath.Contains("Resources/Audio/Sfx/");
        bool music = assetPath.Contains("Resources/Audio/Music/");
        if (!sfx && !music) return;

        importer.forceToMono = true;
        importer.loadInBackground = music;
        var settings = importer.defaultSampleSettings;
        if (sfx)
        {
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
        }
        else
        {
            importer.forceToMono = false;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
        }
        settings.preloadAudioData = sfx;
        importer.defaultSampleSettings = settings;
    }
}
