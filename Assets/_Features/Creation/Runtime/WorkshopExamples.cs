using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty
{
    internal static class WorkshopExamples
    {
        public static void BellAndGate(RuntimeExperience experience)
        {
            var tools = experience.Workshop;
            if (!tools.Place("ChainBell", new Vector3(-2.9f, 0, -.75f)) ||
                !tools.Place("ChainBunny", new Vector3(-1.5f, 0, .15f)) ||
                !tools.Place("ChainPlate", new Vector3(-1.5f, 0, .15f)) ||
                !tools.Place("ChainGate", new Vector3(1.1f, 0, .65f)) ||
                !tools.Place("ChainDuck", new Vector3(1.1f, 0, -.6f)) ||
                !tools.Connect("ChainBell", "ChainBunny") ||
                !tools.Connect("ChainBunny", "ChainPlate") ||
                !tools.Connect("ChainPlate", "ChainGate") ||
                !tools.Connect("ChainGate", "ChainDuck"))
                throw new InvalidOperationException("Could not load the Bell & Gate example.");
        }

        public static void CreaturePlayground(RuntimeExperience experience)
        {
            var recipe = Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text;
            var settings = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
            foreach (string id in new[] { "CreatureA", "CreatureB" })
            {
                experience.CreateModel(id, recipe);
                settings["driver"] = id == "CreatureA" ? "player" : "follow";
                settings["spawn"] = new JArray(id == "CreatureA" ? -2 : 2, 0, 0);
                experience.Configure(id, settings.ToString(Formatting.None));
                var model = experience.Models[id];
                string ear = model.Creation.RuntimeNodes.Single(p => p.Value == model.Creation.FindPart("ear_base")).Key;
                experience.ApplyBehavior("tap_" + id, "TogglePose tap_" + id +
                    "(listener:" + id + ",target:" + ear + ",degrees:35) { Persistent }");
            }
            experience.ApplyBehavior("follow", "const p = CreatureA[ActorMotion]\nCreatureBGoal { MoveGoal: {p.x,p.z,1.8} }");
            experience.SelectControl("CreatureA");
        }
    }
}

