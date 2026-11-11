using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace Ecaty.Tests
{
    [Category("Native")]
    public sealed class NativeGalleryTests
    {
        FlecsSession session;
        JObject Read(string id, string type) => (JObject)JObject.Parse(session.EntityJson(id))["components"][type];
        void Set(string id, string type, JObject value) => session.SetComponentJson(id, type, value.ToString());
        [SetUp]
        public void Setup()
        {
            session = new FlecsSession(0);
            foreach (string file in new[] { "Gallery/Examples/GalleryCatalog", "Gallery/Runtime/WorldGalleryTree", "Gallery/Runtime/WorldGalleryTreeMotion", "Gallery/Runtime/WorldGalleryNavigation" })
                session.SetManagedScript(System.IO.Path.GetFileName(file), Resources.Load<TextAsset>(file).text);
        }
        [TearDown] public void Teardown() => session?.Dispose();
        [Test]
        public void NativeClockDrivesTurns()
        {
            Set("GalleryTreeLayer1", "GalleryLayerIntent", new JObject { ["angle"] = 0, ["speed"] = .017, ["started"] = 0 });
            session.Tick(.1f);
            float first = (float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"];
            session.Tick(.1f);
            Assert.That((float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"], Is.GreaterThan(first));
        }
        [Test]
        public void NativePauseRetainsPhase()
        {
            Set("GalleryTreeSettings", "GalleryTreeActivity", new JObject { ["rate"] = 0, ["draft"] = false, ["sequence"] = 0, ["elapsed"] = 12.5, ["started"] = 0 });
            session.Tick(.1f);
            session.Tick(.2f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryTreeMotion")["time"], Is.EqualTo(12.5f).Within(.0001f));
        }
        [Test]
        public void NativeEntryGateRequiresBothArmingAndDistance()
        {
            foreach (var input in new[] { (false, .2f, false), (true, 2f, false), (true, .2f, true) })
            {
                Set("GallerySettings", "GalleryApproach", new JObject { ["threshold"] = .4, ["measured"] = input.Item2, ["armed"] = input.Item1 });
                session.Tick(.01f);
                Assert.That((bool)Read("GalleryEntryGate", "GalleryEntryRequest")["ready"], Is.EqualTo(input.Item3));
            }
        }
        [Test]
        public void ReturnClockAndDesignerInputsRemainReactiveWhilePreviewsPause()
        {
            Set("GalleryTreeSettings", "GalleryTreeActivity", new JObject { ["rate"] = 0, ["draft"] = false, ["sequence"] = 0, ["elapsed"] = 12.5, ["started"] = 0 });
            Set("GalleryTreeSettings", "GalleryReturnIntent", new JObject { ["active"] = true, ["started"] = 0, ["world"] = "GalleryAquarium", ["x"] = 0, ["y"] = 0, ["z"] = 0, ["radius"] = 10 });
            session.Tick(.1f);
            float first = (float)Read("GalleryTreeSettings", "GalleryReturnState")["progress"];
            session.Tick(.1f);
            float second = (float)Read("GalleryTreeSettings", "GalleryReturnState")["progress"];
            Assert.That(second, Is.GreaterThan(first));
            Assert.That((float)Read("GalleryTreeSettings", "GalleryTreeMotion")["time"], Is.EqualTo(12.5f).Within(.0001f));
            var style = Read("GalleryTreeSettings", "GalleryReturnStyle");
            style["seconds"] = 3.8;
            Set("GalleryTreeSettings", "GalleryReturnStyle", style);
            session.Tick(.01f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryReturnState")["progress"], Is.LessThan(second));
        }
        [Test]
        public void NewReturnEpochReconstructsItsOwnProgress()
        {
            session.Tick(.2f);
            var intent = Read("GalleryTreeSettings", "GalleryReturnIntent");
            intent["started"] = .2;
            intent["active"] = true;
            intent["world"] = "GallerySpace";
            Set("GalleryTreeSettings", "GalleryReturnIntent", intent);
            session.Tick(.01f);
            var state = Read("GalleryTreeSettings", "GalleryReturnState");
            Assert.That((float)state["epoch"], Is.EqualTo(.2f).Within(.0001f));
            Assert.That((float)state["progress"], Is.LessThan(.02f));
        }
        [Test]
        public void RecenterFollowsMeasuredZoomInsteadOfScrollEvents()
        {
            Set("GalleryTreeSettings", "GalleryCameraAnchor", new JObject { ["active"] = true, ["x"] = 5, ["y"] = 0, ["z"] = 2, ["zoom"] = 10, ["minimum"] = 1, ["centerY"] = 0 });
            Set("GalleryTreeSettings", "GalleryCameraMeasure", new JObject { ["zoom"] = 10 });
            session.Tick(.01f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryRecenterState")["weight"], Is.EqualTo(0).Within(.0001f));
            Set("GalleryTreeSettings", "GalleryCameraMeasure", new JObject { ["zoom"] = 5 });
            session.Tick(.01f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryRecenterState")["weight"], Is.InRange(.10f, .12f));
            Set("GalleryTreeSettings", "GalleryCameraMeasure", new JObject { ["zoom"] = 1 });
            session.Tick(.01f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryRecenterState")["weight"], Is.EqualTo(1).Within(.0001f));
        }
        [Test]
        public void GlassCrossingUsesNativeTime()
        {
            Set("GalleryTreeSettings", "GalleryGlassIntent", new JObject { ["started"] = 0, ["entering"] = true });
            session.Tick(.2f);
            Assert.That((float)Read("GalleryTreeSettings", "GalleryGlassState")["progress"], Is.InRange(.25f, .35f));
        }
        [Test]
        public void NavigationRoundTripsThroughNativeReflection()
        {
            var intent = Read("GalleryTreeSettings", "GalleryTreeNavigation");
            intent["target"] = "GalleryAquarium";
            intent["approach"] = true;
            intent["exitArmed"] = true;
            intent["boundary"] = 45;
            Set("GalleryTreeSettings", "GalleryTreeNavigation", intent);
            var actual = Read("GalleryTreeSettings", "GalleryTreeNavigation");
            Assert.That((string)actual["target"], Is.EqualTo("GalleryAquarium"));
            Assert.That((bool)actual["approach"], Is.True);
            Assert.That((float)actual["boundary"], Is.EqualTo(45));
        }
    }
}
