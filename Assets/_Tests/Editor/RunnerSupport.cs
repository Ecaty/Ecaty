using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ecaty.Editor
{
    /// <summary>
    /// Named Editor entry points called by Tests/Run.ps1 through the Unity CLI.
    /// They replace inline C# strings so the runner stays readable and compiler-checked.
    /// </summary>
    public static class RunnerSupport
    {
        private const string AutoTickEnabled = "Unity.Pipeline.AutoTick.Enabled";
        private const string AutoTickInterval = "Unity.Pipeline.AutoTick.IntervalMs";
        private const string ArrangementKey = "WorldTests.OriginalArrangement";

        public static object AutoTickState() => new
        {
            enabled = SessionState.GetBool(AutoTickEnabled, false),
            interval = SessionState.GetInt(AutoTickInterval, 16)
        };

        public static string GallerySavePath() =>
            Path.Combine(Application.persistentDataPath, "GalleryPrototype", "tree-arrangement.json");

        /// <summary>Stores and saves the running gallery arrangement so the runner can restore it afterwards.</summary>
        public static bool CaptureGalleryArrangement()
        {
            var gallery = Object.FindAnyObjectByType<CrystalWorldGallery>();
            if (gallery)
            {
                if (gallery.IsDragging)
                    throw new System.InvalidOperationException("Finish or cancel the held world before running tests.");
                SessionState.SetString(ArrangementKey, gallery.CaptureArrangement());
                gallery.SaveArrangement();
            }
            return true;
        }

        public static bool RestoreGalleryArrangement()
        {
            string snapshot = SessionState.GetString(ArrangementKey, "");
            var gallery = Object.FindAnyObjectByType<CrystalWorldGallery>();
            if (gallery && !string.IsNullOrEmpty(snapshot))
                gallery.RestoreArrangement(snapshot);
            SessionState.EraseString(ArrangementKey);
            return true;
        }
    }
}
