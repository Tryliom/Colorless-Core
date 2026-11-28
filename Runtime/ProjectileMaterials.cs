using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>Material used by the sprites of a projectile.</summary>
    public enum ProjectileMaterialType
    {
        Default,
        FullSaturation,
        Enemy,
        Custom
    }

    /// <summary>
    /// Gives access to the projectile materials shared by the game and the mods. Resolving them at runtime ensures
    /// a mod uses the materials and shader of the running game, not the copies packed in its bundle.
    /// </summary>
    public static class ProjectileMaterials
    {
        private const string ResourcesFolder = "ProjectileMaterials/";

        private static readonly int Saturation = Shader.PropertyToID("_Saturation");
        private static readonly int GlowColor = Shader.PropertyToID("_GlowColor");

        private static Material _default;
        private static Material _fullSaturation;
        private static Material _enemy;

        /// <summary>
        /// Returns the material to assign to a projectile. An invalid custom material falls back to the default one.
        /// </summary>
        /// <param name="type">Material type selected on the projectile.</param>
        /// <param name="customMaterial">Only used when <paramref name="type"/> is <see cref="ProjectileMaterialType.Custom"/>.</param>
        /// <param name="context">Object highlighted by the warning if the custom material is invalid.</param>
        public static Material Resolve(ProjectileMaterialType type, Material customMaterial, Object context)
        {
            switch (type)
            {
                case ProjectileMaterialType.FullSaturation: return Load(ref _fullSaturation, "FullSatuProjectileMat");
                case ProjectileMaterialType.Enemy: return Load(ref _enemy, "EnemyProjectileMat");
                case ProjectileMaterialType.Custom: return ResolveCustom(customMaterial, context);
                default: return Load(ref _default, "ProjectileMat");
            }
        }

        private static Material ResolveCustom(Material customMaterial, Object context)
        {
            var defaultMaterial = Load(ref _default, "ProjectileMat");

            if (!customMaterial)
            {
                Debug.LogWarning($"[ProjectileMaterials] No custom material set on {context.name}, using the default one.", context);

                return defaultMaterial;
            }

            var baseShader = defaultMaterial.shader;

            // A bundle embeds its own copy of the base shader, which may be outdated: use the one of the game
            if (customMaterial.shader != baseShader && customMaterial.shader.name == baseShader.name)
            {
                customMaterial.shader = baseShader;
            }

            if (!customMaterial.shader.isSupported || !customMaterial.HasProperty(Saturation) || !customMaterial.HasProperty(GlowColor))
            {
                Debug.LogWarning($"[ProjectileMaterials] Material {customMaterial.name} on {context.name} is not supported or misses " +
                                 "_Saturation / _GlowColor, using the default one.", context);

                return defaultMaterial;
            }

            return customMaterial;
        }

        private static Material Load(ref Material cache, string materialName)
        {
            if (!cache)
            {
                cache = Resources.Load<Material>(ResourcesFolder + materialName);
            }

            return cache;
        }
    }
}
