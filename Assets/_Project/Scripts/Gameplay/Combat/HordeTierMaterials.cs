using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shared danger-tier materials (KAN-92). One copy of a zombie type's body material per colour step, with
    /// _BaseColor = type colour x <see cref="HordeScaling.StepColor"/>, made the first time a step is needed and shared by
    /// every zombie of that type. A different material with the same shader keeps the SRP Batcher on (a property block
    /// would turn it off for every tinted zombie). Step 0 = the type's own materials. Lives for the app session, across
    /// Chambers; cleared on a play-mode start.
    /// </summary>
    public static class HordeTierMaterials
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Key: first source material + slot count. Value: step -> renderer material array (null until used).
        private static readonly Dictionary<(Material, int), Material[][]> Cache = new Dictionary<(Material, int), Material[][]>();
        private static readonly List<Material> Made = new List<Material>();

        /// <summary>Materials made so far (debug, tests: stays flat once every step was reached).</summary>
        public static int Count => Made.Count;

        /// <summary>
        /// Renderer materials of <paramref name="step"/> for a body whose own materials are <paramref name="source"/>.
        /// Step 0 or no scaling = <paramref name="source"/> itself. The array is shared: never change it.
        /// </summary>
        public static Material[] Get(Material[] source, HordeScaling scaling, int step)
        {
            if (source == null || source.Length == 0 || source[0] == null || scaling == null || step <= 0) return source;
            var key = (source[0], source.Length);
            if (!Cache.TryGetValue(key, out var steps) || steps.Length != scaling.StepCount)
            {
                steps = new Material[scaling.StepCount][];
                steps[0] = source;
                Cache[key] = steps;
            }
            step = Mathf.Min(step, steps.Length - 1);
            if (steps[step] != null) return steps[step];

            Color tint = scaling.StepColor(step);
            var mats = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src == null) continue;
                int same = System.Array.IndexOf(source, src);
                if (same < i)
                {
                    mats[i] = mats[same]; // all slots share one material: one copy
                    continue;
                }
                var m = new Material(src) { name = $"{src.name} (tier {step})", hideFlags = HideFlags.DontSave };
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, src.GetColor(BaseColorId) * tint);
                mats[i] = m;
                Made.Add(m);
            }
            steps[step] = mats;
            return mats;
        }

        /// <summary>Calm colour (the material's _BaseColor) of a body at <paramref name="step"/>.</summary>
        public static Color CalmTint(Color baseTint, HordeScaling scaling, int step) =>
            scaling == null || step <= 0 ? baseTint : baseTint * scaling.StepColor(step);

        /// <summary>Destroys every tier material (tests, play-mode start).</summary>
        public static void Clear()
        {
            foreach (var m in Made)
                if (m != null)
                {
                    if (Application.isPlaying) Object.Destroy(m);
                    else Object.DestroyImmediate(m);
                }
            Made.Clear();
            Cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Made.Clear();
            Cache.Clear();
        }
    }
}
