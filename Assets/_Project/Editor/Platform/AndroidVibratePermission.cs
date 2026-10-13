#if UNITY_ANDROID // UnityEditor.Android exists only with the Android module; the build switches target first.
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace SurvivorSouls.Editor.Platform
{
    /// <summary>
    /// Adds <c>android.permission.VIBRATE</c> to the generated unityLibrary manifest (KAN-12).
    /// Unity adds it by itself only when scripts call <c>Handheld.Vibrate</c>; <see cref="Core.Haptics"/> uses the
    /// Vibrator API through JNI, so the permission must be declared here. Normal permission: no runtime prompt.
    /// </summary>
    public sealed class AndroidVibratePermission : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string Permission = "android.permission.VIBRATE";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[AndroidVibratePermission] Manifest not found: {manifestPath}");
                return;
            }
            if (AddPermission(manifestPath)) Debug.Log($"[AndroidVibratePermission] Added {Permission}.");
        }

        /// <summary>Adds the permission when missing. Returns true when the file changed.</summary>
        public static bool AddPermission(string manifestPath)
        {
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            XmlElement manifest = doc.DocumentElement;
            if (manifest == null) return false;

            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
                if (node is XmlElement e && e.GetAttribute("name", AndroidNs) == Permission) return false;

            XmlElement permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNs, Permission);
            manifest.PrependChild(permission);
            doc.Save(manifestPath);
            return true;
        }
    }
}
#endif
