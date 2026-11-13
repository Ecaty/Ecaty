using System;
using System.IO;
using System.Threading.Tasks;
using Runtime.Flecs;
using ReleaseDesk.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Editor
{
    /// <summary>
    /// Store screenshots through Flecs: each shot's setup is Flecs Script run on the live world (camera via
    /// GalleryView, entering a world via GalleryEnterIntent, environment via capability components). The rendered
    /// camera is the gallery's view camera, so UI is never in the image. The player's saves are backed up before the
    /// first shot and restored after Play mode ends, because the app saves its state when Play stops.
    /// </summary>
    [InitializeOnLoad]
    public sealed class FlecsShotStage : IShotStage
    {
        private const string BackupKey = "Runtime.FlecsShotStage.Backup";
        private static readonly string[] Saves =
        {
            Path.Combine("GalleryPrototype", "tree-arrangement.json"),
            Path.Combine("Experiences", "current.json"),
            Path.Combine("Clay", "creation.json")
        };
        private bool backedUp;

        static FlecsShotStage()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode)
                    RestoreSaves();
            };
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                RestoreSaves();
        }

        public async Task<Camera> Prepare(ShotRequest shot)
        {
            if (!backedUp)
            {
                BackUpSaves();
                backedUp = true;
            }
            var world = await WaitFor(() => Object.FindAnyObjectByType<FlecsWorld>(), w => w && w.Session != null && !w.Session.IsDisposed, 30);
            var gallery = await WaitFor(() => Object.FindAnyObjectByType<CrystalWorldGallery>(), g => g && g.ViewCamera, 30);
            if (!string.IsNullOrWhiteSpace(shot.setup))
                world.Session.RunScript(shot.setup);
            world.SyncViews();
            return gallery.ViewCamera;
        }

        public void Finish()
        {
        }

        private static string BackupRoot => Path.Combine(Path.GetTempPath(), "shot-stage-saves");

        private static void BackUpSaves()
        {
            if (Directory.Exists(BackupRoot))
                Directory.Delete(BackupRoot, true);
            foreach (string save in Saves)
            {
                string source = Path.Combine(Application.persistentDataPath, save);
                string copy = Path.Combine(BackupRoot, save);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                if (File.Exists(source))
                    File.Copy(source, copy, true);
                else
                    File.WriteAllText(copy + ".absent", "");
            }
            SessionState.SetString(BackupKey, BackupRoot);
        }

        private static void RestoreSaves()
        {
            string root = SessionState.GetString(BackupKey, "");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            foreach (string save in Saves)
            {
                string target = Path.Combine(Application.persistentDataPath, save);
                string copy = Path.Combine(root, save);
                if (File.Exists(copy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(copy, target, true);
                }
                else if (File.Exists(copy + ".absent") && File.Exists(target))
                    File.Delete(target);
            }
            SessionState.EraseString(BackupKey);
            Directory.Delete(root, true);
            Debug.Log("Screenshots: restored the player's saves.");
        }

        private static async Task<T> WaitFor<T>(Func<T> find, Func<T, bool> ready, float seconds) where T : class
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (true)
            {
                var value = find();
                if (value != null && ready(value))
                    return value;
                if (Time.realtimeSinceStartup > until)
                    throw new TimeoutException($"{typeof(T).Name} was not ready after {seconds} s.");
                await Task.Delay(200);
            }
        }
    }
}
