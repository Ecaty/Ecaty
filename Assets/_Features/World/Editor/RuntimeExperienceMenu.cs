using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class RuntimeExperienceMenu
    {
        private static RuntimeExperience Experience => Object.FindAnyObjectByType<RuntimeExperience>();
        [MenuItem("Runtime/Runtime engine/Start runtime playground", true)]
        private static bool Available() => EditorApplication.isPlaying && Experience && !RuntimeExperienceVerification.Running;
        [MenuItem("Runtime/Runtime engine/Start runtime playground")]
        private static void Playground() => Experience.Dock.StartCreaturePlayground();
        [MenuItem("Runtime/Runtime engine/Save current experience", true)]
        private static bool SaveAvailable() => Available();
        [MenuItem("Runtime/Runtime engine/Save current experience")]
        private static void Save() => Experience.Save();
        [MenuItem("Runtime/Runtime engine/Resume saved experience", true)]
        private static bool LoadAvailable() => Available() && File.Exists(RuntimeExperience.SavePath);
        [MenuItem("Runtime/Runtime engine/Resume saved experience")]
        private static void Load() => Experience.Dock.StartSavedWorld();
    }
}
