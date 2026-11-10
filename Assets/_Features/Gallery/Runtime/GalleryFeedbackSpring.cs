#if ECATY_CAP_FLEXALON
using UnityEngine;
namespace Ecaty
{
    /// <summary>Reusable presentation spring. Flecs supplies its goal; it owns no application state.</summary>
    public sealed class GalleryFeedbackSpring
    {
#if ECATY_CAP_FEEL
        readonly MoreMountains.Feedbacks.MMSpringFloat spring=new();
#else
        float value,velocity;
#endif
        public float Value{get;private set;}
        public GalleryFeedbackSpring(float initial=0)=>Reset(initial);
        public void Reset(float initial){Value=initial;
#if ECATY_CAP_FEEL
            spring.CurrentValue=initial;spring.Velocity=0;spring.TargetValue=initial;
#else
            value=initial;velocity=0;
#endif
        }
        public float Step(float goal,float seconds,float deltaTime,float damping=.9f)
        {
#if ECATY_CAP_FEEL
            spring.Damping=damping;spring.Frequency=1/(Mathf.PI*Mathf.Max(.03f,seconds));spring.TargetValue=goal;spring.UpdateSpringValue(Mathf.Min(.1f,deltaTime));Value=Mathf.Clamp01(spring.CurrentValue);
#else
            value=Mathf.SmoothDamp(value,goal,ref velocity,Mathf.Max(.03f,seconds),100,deltaTime);Value=Mathf.Clamp01(value);
#endif
            return Value;
        }
    }
}
#endif
