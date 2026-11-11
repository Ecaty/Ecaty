using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ecaty
{
    internal static class PhotoRecipeInput
    {
        private static JObject Vector() => new JObject
        {
            ["type"] = "array",
            ["items"] = new JObject { ["type"] = "number" },
            ["minItems"] = 3,
            ["maxItems"] = 3
        };

        public static JObject Schema()
        {
            var properties = new JObject
            {
                ["id"] = new JObject { ["type"] = "string", ["pattern"] = "^[a-z][a-z0-9_]{0,31}$" },
                ["parent"] = new JObject { ["type"] = new JArray("string", "null") },
                ["shape"] = new JObject { ["type"] = "string", ["enum"] = new JArray("pivot", "ellipsoid", "box") },
                ["localPosition"] = Vector(),
                ["localRotation"] = Vector(),
                ["size"] = Vector(),
                ["color"] = Vector(),
                ["finish"] = Vector(),
                ["operation"] = new JObject { ["type"] = "string", ["enum"] = new JArray("solid", "dye") },
                ["blend"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = .25 }
            };
            var part = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false,
                ["required"] = new JArray("id", "parent", "shape", "localPosition")
            };
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("version", "parts"),
                ["properties"] = new JObject
                {
                    ["version"] = new JObject { ["type"] = "integer", ["enum"] = new JArray(4) },
                    ["parts"] = new JObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 64, ["items"] = part }
                }
            };
        }

        public static string Read(JToken recipe)
        {
            if (recipe is JObject structured)
                return structured.ToString(Formatting.None);
            if (recipe?.Type == JTokenType.String)
                return (string)recipe;
            throw new ArgumentException("recipe must be an object with version and parts.");
        }
    }
}
