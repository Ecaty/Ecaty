using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditorInternal;
using UnityEngine;
namespace Ecaty.Editor
{
    public static class GalleryPerformanceProbe
    {
        public static object Summarize(string reportName = "gallery-profile")
        {
            var totals = new Dictionary<string, double>();
            int count = 0, last = ProfilerDriver.lastFrameIndex;
            for (int frame = Math.Max(ProfilerDriver.firstFrameIndex, last - 59); frame <= last; frame++)
            {
                using var view = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (!view.valid)
                    continue;
                count++;
                for (int sample = 0; sample < view.sampleCount; sample++)
                {
                    string name = view.GetSampleName(sample);
                    double ms = view.GetSampleTimeMs(sample);
                    totals[name] = totals.TryGetValue(name, out var old) ? old + ms : ms;
                }
            }
            var report = new JObject { ["frames"] = count, ["note"] = "Editor capture, inclusive CPU sample means; includes profiling and unfocused Editor overhead. Not a player/device FPS benchmark.", ["top"] = new JArray(totals.OrderByDescending(p => p.Value).Take(35).Select(p => new JObject { ["name"] = p.Key, ["milliseconds"] = p.Value / Math.Max(1, count) })) };
            if (!System.Text.RegularExpressions.Regex.IsMatch(reportName, "^[a-z0-9-]+$"))
                throw new ArgumentException("Report name");
            File.WriteAllText(Path.Combine(Application.dataPath, "../Verification/" + reportName + ".json"), report.ToString());
            return report;
        }
    }
}
