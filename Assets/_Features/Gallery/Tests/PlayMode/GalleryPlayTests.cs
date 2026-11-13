using System.Collections;
using System.Linq;
using Runtime.Flecs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests
{
    public sealed class GalleryPlayTests
    {
        [UnityTest]
        public IEnumerator ProductionGalleryAdvancesAndRenders()
        {
            yield return null;
            yield return null;
            var world = Object.FindAnyObjectByType<FlecsWorld>();
            Assert.That(world, Is.Not.Null, "Run from the sample scene");
            int frame = Time.frameCount;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Time.frameCount, Is.GreaterThan(frame));
            Assert.That(world.Session.Lookup("GalleryTreeSettings"), Is.Not.EqualTo(0));
            Assert.That(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Any(r => r.sharedMaterial && r.sharedMaterial.shader.name == "Runtime/Gallery Orbit"), Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
