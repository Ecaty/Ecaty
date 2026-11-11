#if ODIN_VALIDATOR
using Sirenix.OdinInspector.Editor.Validation;
[assembly:RegisterValidator(typeof(Ecaty.Editor.GalleryConfigValidator))]
namespace Ecaty.Editor {
    public sealed class GalleryConfigValidator:RootObjectValidator<GalleryLayoutProfile>
    {
        protected override void Validate(ValidationResult result)
        {
            var p=Value;
            if(p.LayerSpacing<p.WorldDiameter)result.AddError("Layer spacing is smaller than the world sphere diameter; layers can collide.");
            if(p.MinimumPickOpacity<.98f)result.AddError("Faded-world picking must require at least 0.98 opacity.");
            if(p.RingRotationSmoothTime<.02f)result.AddError("Ring rotation smoothing must be positive.");
            if(UnityEditor.AssetDatabase.GetAssetPath(p)==GalleryConfigScript.AssetPath&&!GalleryConfigScript.IsCurrent())result.AddError("GalleryConfig.flecs is out of date with Gallery.asset; edit any value or run Runtime > Gallery > Export config.");
        }
    }

}
#endif
