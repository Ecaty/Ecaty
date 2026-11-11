#if UNITY_EDITOR
using System.IO;
using Ecaty.Agents;
using UnityEditor;
using UnityEngine;

namespace Ecaty
{
    internal static class ReferencePhotoPicker
    {
        internal static byte[] Pick()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Choose a reference image", "",
                new[] { "Images", "jpg,jpeg,png" });
            return string.IsNullOrEmpty(path) ? null : ReadPhoto(path);
        }

        private static byte[] ReadPhoto(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
                throw new AgentServiceException("Choose an image under 16 MB.");
            var source = new Texture2D(2, 2);
            Texture2D scaled = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path)))
                    throw new AgentServiceException("Choose a valid JPG or PNG.");
                float ratio = Mathf.Min(1, 768f / Mathf.Max(source.width, source.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * ratio));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * ratio));
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                return ImageConversion.EncodeToJPG(scaled, 82);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target)
                    RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(source);
                if (scaled)
                    UnityEngine.Object.Destroy(scaled);
            }
        }
    }
}
#endif
