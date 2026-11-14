using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;

namespace Game
{
    // The part of the AI's instructions that describes the data model, read from the live world: each authored
    // type with its brief, then its members with type, range and brief. Nothing here is a hand-kept description.
    public static class WorldTypeText
    {
        public const int Budget = 6000;

        public static readonly string[] Types =
        {
            "Position", "Rotation", "Scale", "Color", "Primitive", "Label", "Look", "WorldStyle", "MotionLoop", "SecondPose",
            "Free", "Pinned", "Ghost", "Gravity", "Float", "Wind", "Drag", "Particles", "Rule", "Count", "Sound", "SoundClip", "Spin"
        };

        // The conduct rules followed by the types, for the instructions while a world is entered.
        public static string Compose(FlecsSession session, string conduct) => conduct.TrimEnd() + "\n\n" + Build(session);

        public static string Build(FlecsSession session)
        {
            var described = new List<JObject>();
            foreach (string type in Types)
            {
                string json = session.Describe(type);
                if (json != null)
                    described.Add(JObject.Parse(json));
            }
            // Full text first; when it is too long, the briefs of nested members go; then the end is cut.
            string text = Render(described, true);
            if (text.Length > Budget)
                text = Render(described, false);
            if (text.Length > Budget)
            {
                text = text.Substring(0, Budget);
                text = text.Substring(0, Math.Max(0, text.LastIndexOf('\n')));
            }
            return text;
        }

        private static string Render(List<JObject> types, bool nestedBriefs)
        {
            var text = new StringBuilder("Types (member: type range - meaning; values of an enum are listed once):\n");
            var enums = new HashSet<string>();
            foreach (var type in types)
            {
                text.Append((string)type["name"]);
                if ((string)type["brief"] is string brief)
                    text.Append(" - ").Append(brief);
                text.Append('\n');
                if (type["members"] is JArray members && members.Count > 0)
                    text.Append("  ").Append(string.Join("; ", members.OfType<JObject>().Select(m => Member(m, nestedBriefs, enums, 0)))).Append('\n');
            }
            return text.ToString();
        }

        private static string Member(JObject member, bool nestedBriefs, HashSet<string> enums, int depth)
        {
            string type = (string)member["type"] ?? "";
            var text = new StringBuilder((string)member["name"]).Append(": ").Append(type.Substring(type.LastIndexOf('.') + 1));
            if (member["count"] != null)
                text.Append('[').Append((int)member["count"]).Append(']');
            if (member["values"] is JArray values && enums.Add(type))
                text.Append('(').Append(string.Join("|", values.Values<string>())).Append(')');
            string range = Range(member);
            if (range.Length > 0)
                text.Append(' ').Append(range);
            if (member["members"] is JArray inner && inner.Count > 0)
                text.Append('{').Append(string.Join(", ", inner.OfType<JObject>().Select(m => Member(m, nestedBriefs, enums, depth + 1)))).Append('}');
            if ((depth == 0 || nestedBriefs) && (string)member["brief"] is string brief)
                text.Append(" - ").Append(brief);
            return text.ToString();
        }

        // A bound that is the limit of the number type is not a range worth saying.
        private static string Range(JObject member)
        {
            if (member["min"] == null || member["max"] == null)
                return "";
            double min = (double)member["min"], max = (double)member["max"];
            bool low = min > -1e30, high = max < 1e30;
            string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            return low && high ? "[" + Number(min) + ".." + Number(max) + "]" : low ? ">=" + Number(min) : high ? "<=" + Number(max) : "";
        }
    }
}
