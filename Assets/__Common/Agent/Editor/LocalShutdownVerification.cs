using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ecaty.Agents.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ecaty.Editor
{
    public static class LocalShutdownVerification
    {
        private const string Key = "Ecaty.LocalShutdownCheck";
        private static string Mode => SessionState.GetString(Key + ".mode", "vision");

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += Changed;

        public static void Start(string mode = "vision")
        {
            if (mode != "loading" && mode != "vision" && mode != "text")
                throw new ArgumentException("Unknown test mode.");
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Run from Edit mode.");
            if (Processes().Length != 0)
                throw new InvalidOperationException("Stop the existing local test server first.");
            SessionState.SetString(Key + ".mode", mode);
            SessionState.SetString(Key, "starting");
            EditorApplication.isPlaying = true;
        }

        private static void Changed(PlayModeStateChange state)
        {
            string stage = SessionState.GetString(Key, "");
            if (state == PlayModeStateChange.EnteredPlayMode && stage == "starting")
                Run();
            if (state != PlayModeStateChange.EnteredEditMode || stage != "stopping")
                return;
            long started = long.Parse(SessionState.GetString(Key + ".stop", "0"), CultureInfo.InvariantCulture);
            double seconds = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - started) / 1000.0;
            int count = SessionState.GetInt(Key + ".count", 0);
            bool exited = count > 0 && Processes().Length == 0;
            bool pending = SessionState.GetBool(Key + ".pending", false);
            Write(new JObject
            {
                ["passed"] = exited && pending && seconds < 15,
                ["mode"] = Mode,
                ["requestWasPending"] = pending,
                ["ownedProcessesAtStop"] = count,
                ["ownedProcessesExited"] = exited,
                ["seconds"] = seconds,
                ["checkedAt"] = DateTimeOffset.UtcNow.ToString("O")
            });
            SessionState.EraseString(Key);
        }

        private static int[] Processes()
        {
            return Process.GetProcessesByName("llama-server").Select(process =>
            {
                using (process)
                    return process.Id;
            }).ToArray();
        }

        private static void Write(JObject report) =>
            File.WriteAllText("Verification/local-ai-shutdown-" + Mode + "-checks.json", report.ToString());

        private static async void Run()
        {
            try
            {
                Application.runInBackground = true;
                await Task.Delay(600);
                var chat = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
                chat.Controller.SelectProvider("local");
                Task request = chat.Controller.ConnectAsync();
                if (Mode != "loading")
                {
                    await request;
                    if (!chat.Controller.IsConnected)
                        throw new InvalidOperationException("Local model did not connect.");
                    if (Mode == "vision")
                    {
                        var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
                        var pixels = new Color[512 * 512];
                        for (int i = 0; i < pixels.Length; i++)
                            pixels[i] = i % 512 < 256 ? Color.red : Color.blue;
                        texture.SetPixels(pixels);
                        texture.Apply();
                        byte[] jpeg = texture.EncodeToJPG();
                        UnityEngine.Object.Destroy(texture);
                        chat.Controller.AttachJpeg(jpeg, "Create a model from this reference.", "apply_photo_object");
                        request = chat.Controller.SendAsync("Create a model from this reference.");
                    }
                    else
                        request = chat.Controller.SendAsync("Describe 100 imaginary workshop tools in detail. Do not call tools.");
                }
                if (Mode == "loading")
                {
                    var deadline = DateTime.UtcNow.AddSeconds(6);
                    while (Processes().Length == 0 && !request.IsCompleted && DateTime.UtcNow < deadline)
                        await Task.Delay(100);
                }
                else
                    await Task.Delay(750);
                SessionState.SetInt(Key + ".count", Processes().Length);
                SessionState.SetBool(Key + ".pending", !request.IsCompleted);
                SessionState.SetString(Key + ".stop", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
                SessionState.SetString(Key, "stopping");
                EditorApplication.isPlaying = false;
            }
            catch (Exception error)
            {
                Write(new JObject { ["passed"] = false, ["error"] = error.Message });
                SessionState.EraseString(Key);
                EditorApplication.isPlaying = false;
            }
        }
    }
}
