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

    /// <summary>Material used by a group of sprites of a weapon, a ray or an AoE.</summary>
    public enum EffectMaterialType
    {
        Default,
        Custom
    }

    /// <summary>
    /// Gives access to the weapon, projectile, ray and AoE materials shared by the game and the mods. Resolving them at runtime
    /// ensures a mod uses the materials and shaders of the running game, not the copies packed in its bundle.
    /// </summary>
    public static class CombatMaterials
    {
        private const string ResourcesFolder = "CombatMaterials/";

        private static readonly int Saturation = Shader.PropertyToID("_Saturation");
        private static readonly int GlowColor = Shader.PropertyToID("_GlowColor");

        private static Material _projectile;
        private static Material _fullSaturationProjectile;
        private static Material _enemyProjectile;
        private static Material _rayBody;
        private static Material _rayStartEnd;
        private static Material _aoe;
        private static Material _weapon;

        private static Material Weapon => Load(ref _weapon, "WeaponMat");
        private static Material Projectile => Load(ref _projectile, "ProjectileMat");
        private static Material RayBody => Load(ref _rayBody, "RayMaterial");
        private static Material RayStartEnd => Load(ref _rayStartEnd, "StartEndRay");
        private static Material Aoe => Load(ref _aoe, "DefaulltAoeMat");

        /// <summary>
        /// Returns the material to assign to a projectile. An invalid custom material falls back to the default one.
        /// </summary>
        /// <param name="type">Material type selected on the projectile.</param>
        /// <param name="customMaterial">Only used when <paramref name="type"/> is <see cref="ProjectileMaterialType.Custom"/>.</param>
        /// <param name="context">Object highlighted by the warning if the custom material is invalid.</param>
        public static Material ResolveProjectile(ProjectileMaterialType type, Material customMaterial, Object context)
        {
            switch (type)
            {
                case ProjectileMaterialType.FullSaturation: return Load(ref _fullSaturationProjectile, "FullSatuProjectileMat");
                case ProjectileMaterialType.Enemy: return Load(ref _enemyProjectile, "EnemyProjectileMat");
                case ProjectileMaterialType.Custom:
                    if (!IsUsable(customMaterial, context))
                    {
                        return Projectile;
                    }

                    // Projectiles drive these properties at runtime
                    if (!customMaterial.HasProperty(Saturation) || !customMaterial.HasProperty(GlowColor))
                    {
                        Debug.LogWarning($"[CombatMaterials] Material {customMaterial.name} on {context.name} misses " +
                                         "_Saturation / _GlowColor, using the default one.", context);

                        return Projectile;
                    }

                    return customMaterial;
                default: return Projectile;
            }
        }

        /// <summary>Returns the material of a weapon.</summary>
        public static Material ResolveWeapon(EffectMaterialType type, Material customMaterial, Object context)
        {
            return ResolveEffect(Weapon, type, customMaterial, context);
        }

        /// <summary>Returns the material of the body sprites of a ray.</summary>
        public static Material ResolveRayBody(EffectMaterialType type, Material customMaterial, Object context)
        {
            return ResolveEffect(RayBody, type, customMaterial, context);
        }

        /// <summary>Returns the material of the start and end sprites of a ray.</summary>
        public static Material ResolveRayStartEnd(EffectMaterialType type, Material customMaterial, Object context)
        {
            return ResolveEffect(RayStartEnd, type, customMaterial, context);
        }

        /// <summary>Returns the material of an AoE.</summary>
        public static Material ResolveAoe(EffectMaterialType type, Material customMaterial, Object context)
        {
            return ResolveEffect(Aoe, type, customMaterial, context);
        }

        private static Material ResolveEffect(Material defaultMaterial, EffectMaterialType type, Material customMaterial, Object context)
        {
            if (type is EffectMaterialType.Custom && IsUsable(customMaterial, context))
            {
                return customMaterial;
            }

            return defaultMaterial;
        }

        private static bool IsUsable(Material customMaterial, Object context)
        {
            if (!customMaterial)
            {
                Debug.LogWarning($"[CombatMaterials] No custom material set on {context.name}, using the default one.", context);

                return false;
            }

            RebindCoreShader(customMaterial, Projectile.shader);
            RebindCoreShader(customMaterial, RayBody.shader);
            RebindCoreShader(customMaterial, RayStartEnd.shader);
            RebindCoreShader(customMaterial, Aoe.shader);

            if (!customMaterial.shader.isSupported)
            {
                Debug.LogWarning($"[CombatMaterials] The shader of {customMaterial.name} on {context.name} is not supported, " +
                                 "using the default material.", context);

                return false;
            }

            return true;
        }

        /// <summary>A bundle embeds its own copy of the core shaders, which may be outdated: use the one of the game.</summary>
        private static void RebindCoreShader(Material material, Shader coreShader)
        {
            if (material.shader != coreShader && material.shader.name == coreShader.name)
            {
                material.shader = coreShader;
            }
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
