using UnityEditor;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Import rules for character FBX files under Art/Characters.
    /// SK_* = rigged mesh, A_* = animation clip (looped, e.g. run/walk cycles).
    /// Meshy rigs use a Generic skeleton.
    /// </summary>
    public class CharacterModelImporter : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Art/Characters/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root) || !assetPath.EndsWith(".fbx")) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = System.IO.Path.GetFileName(assetPath).StartsWith("A_");
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root) || !System.IO.Path.GetFileName(assetPath).StartsWith("A_")) return;

            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
