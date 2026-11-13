using UnityEngine;

namespace Game
{
    internal static class WorkshopVisuals
    {
        public static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader);
            var theme = GameTheme.Default;
            var tinted = Color.Lerp(color, theme.ToolAccent, theme.ToolTintStrength);
            tinted.a = color.a;
            material.color = tinted;
            return material;
        }

        public static GameObject Miniature(Transform parent, PrimitiveType shape, Vector3 size, Material material)
        {
            var preview = GameObject.CreatePrimitive(shape);
            preview.layer = 2;
            preview.transform.SetParent(parent, false);
            preview.transform.localScale = size;
            Object.Destroy(preview.GetComponent<Collider>());
            preview.GetComponent<Renderer>().sharedMaterial = material;
            return preview;
        }
    }
}

