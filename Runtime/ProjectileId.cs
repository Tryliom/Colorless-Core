using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// String identifier of a projectile. Declared with a local id ("axe"), the registry turns it into
    /// "namespace_in_snake_case:local_id" (ex: "colorless_odyssey:axe") at startup, so mods can't collide with the base game.
    /// </summary>
    [Serializable]
    public struct ProjectileId : IEquatable<ProjectileId>
    {
        [SerializeField] private string _value;

        public string Value => _value ?? string.Empty;

        public ProjectileId(string value)
        {
            _value = value;
        }

        public bool Equals(ProjectileId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ProjectileId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(ProjectileId a, ProjectileId b) => a.Value == b.Value;
        public static bool operator !=(ProjectileId a, ProjectileId b) => a.Value != b.Value;
    }

    [Flags]
    public enum ProjectileFlags
    {
        None = 0,
        Enemy = 1,
        Melee = 2
    }

    /// <summary>
    /// Declares metadata of a projectile id field. Read by <see cref="ProjectileIdRegistry"/>, so it is not stored in the serialized id.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ProjectileFlagsAttribute : Attribute
    {
        public readonly ProjectileFlags Flags;

        public ProjectileFlagsAttribute(ProjectileFlags flags)
        {
            Flags = flags;
        }
    }

    public static class ProjectileIdExtensions
    {
        public static ProjectileFlags GetFlags(this ProjectileId id) => ProjectileIdRegistry.GetFlags(id);
        public static bool HasFlag(this ProjectileId id, ProjectileFlags flag) => (ProjectileIdRegistry.GetFlags(id) & flag) == flag;
        public static bool IsEnemyProjectile(this ProjectileId id) => id.HasFlag(ProjectileFlags.Enemy);
        public static bool IsMeleeProjectile(this ProjectileId id) => id.HasFlag(ProjectileFlags.Melee);

        /// <summary>
        /// "colorless_odyssey:ice_arrow" becomes "Ice Arrow", "my_mod:ball" becomes "My Mod - Ball".
        /// </summary>
        public static string ToDisplayName(this ProjectileId id) => ProjectileIdRegistry.ToDisplayName(id.Value);
    }

    /// <summary>
    /// Base game projectile ids. Fields are not constants, so use == instead of switch/case.
    /// Fields must stay non-readonly: the registry rewrites them with their namespaced id.
    /// </summary>
    public static class ProjectileIds
    {
        public static ProjectileId None = new("none");
        public static ProjectileId Axe = new("axe");
        public static ProjectileId BouncingBall = new("bouncing_ball");
        public static ProjectileId Bullet = new("bullet");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyProjectile = new("enemy_projectile");
        public static ProjectileId GravityShell = new("gravity_shell");
        public static ProjectileId MagicDragonBreathing = new("magic_dragon_breathing");
        public static ProjectileId Laser = new("laser");
        public static ProjectileId MagicArrow = new("magic_arrow");
        public static ProjectileId PlayerMagicBall = new("player_magic_ball");
        public static ProjectileId MagicDragon = new("magic_dragon");
        public static ProjectileId SpikyBall = new("spiky_ball");
        public static ProjectileId BloodMissile = new("blood_missile");
        public static ProjectileId ThunderBall = new("thunder_ball");
        public static ProjectileId GreenSpike = new("green_spike");
        public static ProjectileId FireBall = new("fire_ball");
        public static ProjectileId SnowBall = new("snow_ball");
		[ProjectileFlags(ProjectileFlags.Melee)]
		public static ProjectileId Slash = new("slash");
        public static ProjectileId Wave = new("wave");
        public static ProjectileId Bubble = new("bubble");
        public static ProjectileId EnergyBall = new("energy_ball");
        public static ProjectileId Arrow = new("arrow");
        public static ProjectileId HolyCross = new("holy_cross");
		[ProjectileFlags(ProjectileFlags.Melee)]
		public static ProjectileId RapierMeleeProjectile = new("rapier_melee_projectile");
        public static ProjectileId FlyingDagger = new("flying_dagger");
        public static ProjectileId FlyingSword = new("flying_sword");
        public static ProjectileId FireArrow = new("fire_arrow");
        public static ProjectileId IceArrow = new("ice_arrow");
        public static ProjectileId DecayArrow = new("decay_arrow");
        public static ProjectileId PoisonArrow = new("poison_arrow");
        public static ProjectileId HolyArrow = new("holy_arrow");
        public static ProjectileId Shuriken = new("shuriken");
        public static ProjectileId ShadowScythe = new("shadow_scythe");
        public static ProjectileId BlossomPetal = new("blossom_petal");
        public static ProjectileId MagicalArrow = new("magical_arrow");
        public static ProjectileId SlowThunderBall = new("slow_thunder_ball");
        public static ProjectileId CrossThunderBall = new("cross_thunder_ball");
        public static ProjectileId KwakBall = new("kwak_ball");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyMagicArrow = new("enemy_magic_arrow");
        public static ProjectileId PlayerMagicArrow = new("player_magic_arrow");
        public static ProjectileId FallingStar = new("falling_star");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyThunderBall = new("enemy_thunder_ball");
        public static ProjectileId SagittariusArrow = new("sagittarius_arrow");
        public static ProjectileId SoulSword = new("soul_sword");
        public static ProjectileId Soul = new("soul");
        public static ProjectileId SmallScythe = new("small_scythe");
        public static ProjectileId FrostShard = new("frost_shard");
        public static ProjectileId StarFragment = new("star_fragment");
        public static ProjectileId VengefulSword = new("vengeful_sword");
        public static ProjectileId MiniStar = new("mini_star");
        public static ProjectileId GoldenBullet = new("golden_bullet");
        public static ProjectileId UnstableSpear = new("unstable_spear");
        public static ProjectileId ExplosiveSkull = new("explosive_skull");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemySoulSword = new("enemy_soul_sword");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyShell = new("enemy_shell");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyMeteor = new("enemy_meteor");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyShuriken = new("enemy_shuriken");
		[ProjectileFlags(ProjectileFlags.Enemy)]
		public static ProjectileId EnemyPulsatingArrow = new("enemy_pulsating_arrow");
        public static ProjectileId ChainLightning = new("chain_lightning");
		[ProjectileFlags(ProjectileFlags.Melee)]
		public static ProjectileId IzanagiSpinAttack = new("izanagi_spin_attack");
        public static ProjectileId ControllableSnowBall = new("controllable_snow_ball");
        public static ProjectileId FairyFire = new("fairy_fire");
        public static ProjectileId FairyFrost = new("fairy_frost");
        public static ProjectileId FairyPoison = new("fairy_poison");
        public static ProjectileId InvisibleProjectile = new("invisible_projectile");
        public static ProjectileId FlyingPaperPlane = new("flying_paper_plane");
        public static ProjectileId GiantArrow = new("giant_arrow");
        public static ProjectileId FrostChakram = new("frost_chakram");
        public static ProjectileId Fist = new("fist");
        public static ProjectileId HandPalmStrike = new("hand_palm_strike");
        public static ProjectileId LightScythe = new("light_scythe");
        public static ProjectileId Talisman = new("talisman");
        public static ProjectileId DeadlyWeapon = new("deadly_weapon");
        public static ProjectileId Mercury = new("mercury");
        public static ProjectileId Venus = new("venus");
        public static ProjectileId Mars = new("mars");
        public static ProjectileId Jupiter = new("jupiter");
        public static ProjectileId Saturn = new("saturn");
        public static ProjectileId Uranus = new("uranus");
        public static ProjectileId Neptune = new("neptune");
        public static ProjectileId Earth = new("earth");
        public static ProjectileId Missile = new("missile");
        public static ProjectileId Asteroid = new("asteroid");
        public static ProjectileId MaelstromBullet = new("maelstrom_bullet");
        public static ProjectileId TrailProjectile = new("trail_projectile");
        public static ProjectileId HelibladeBlue = new("heliblade_blue");
        public static ProjectileId HelibladeRed = new("heliblade_red");
        public static ProjectileId FireOrb = new("fire_orb");
    }

    /// <summary>
    /// Discovers every projectile id declared in <see cref="ProjectileIds"/> and in any type named
    /// <c>ModdedProjectileId</c> (whatever its namespace), and rewrites them as "namespace:id".
    /// Always use the ids after <see cref="Initialize"/> (done automatically before the first scene loads).
    /// </summary>
    public static class ProjectileIdRegistry
    {
        public const char Separator = ':';
	    private const string ModTypeName = "ModdedProjectileId";
        private const string BaseGameGroup = "colorless_odyssey";

        private static List<Entry> _entries;
        private static HashSet<string> _ids;
        private static Dictionary<string, ProjectileFlags> _flags;

        public readonly struct Entry
        {
            public readonly ProjectileId Id;
            public readonly bool IsModded;

            public Entry(ProjectileId id, bool isModded)
            {
                Id = id;
                IsModded = isModded;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            _entries = new List<Entry>();
            _ids = new HashSet<string>();
            _flags = new Dictionary<string, ProjectileFlags>();

            Collect(typeof(ProjectileIds), false);

            foreach (var type in FindModTypes())
            {
                Collect(type, true);
            }
        }

        public static IReadOnlyList<Entry> GetAll()
        {
            if (_entries == null)
            {
                Initialize();
            }

            return _entries;
        }

        public static ProjectileFlags GetFlags(ProjectileId id)
        {
            if (_flags == null)
            {
                Initialize();
            }

            return _flags.GetValueOrDefault(id.Value, ProjectileFlags.None);
        }

        /// <summary>
        /// "ColorlessOdyssey" becomes "colorless_odyssey". Dots are kept as segment separators.
        /// </summary>
        public static string NamespaceToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "global";
            }

            var builder = new StringBuilder();

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];

                if (char.IsUpper(c))
                {
                    if (i > 0 && value[i - 1] != '.' && value[i - 1] != '_')
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        public static bool IsBaseGameId(string id) => id != null && id.StartsWith(BaseGameGroup + Separator, StringComparison.Ordinal);

        public static string ToDisplayName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            var separatorIndex = id.IndexOf(Separator);

            if (separatorIndex < 0)
            {
                return Capitalize(id);
            }

            var group = id.Substring(0, separatorIndex);
            var name = Capitalize(id.Substring(separatorIndex + 1));

            return group == BaseGameGroup ? name : $"{Capitalize(group)} - {name}";
        }

        private static string Capitalize(string value)
        {
            var words = value.Split(new[] { '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < words.Length; i++)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }

            return string.Join(" ", words);
        }

        private static IEnumerable<Type> FindModTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type.IsClass && type.Name == ModTypeName)
                    {
                        yield return type;
                    }
                }
            }
        }

        private static void Collect(Type type, bool isModded)
        {
            var prefix = NamespaceToSnakeCase(type.Namespace) + Separator;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ProjectileId) && !f.IsInitOnly);

            foreach (var field in fields)
            {
                var id = (ProjectileId)field.GetValue(null);

                // Already namespaced when initialization ran before (ex: domain reload disabled)
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    id = new ProjectileId(prefix + id.Value);
                    field.SetValue(null, id);
                }

                if (!_ids.Add(id.Value))
                {
                    Debug.LogWarning($"Projectile id '{id.Value}' is declared twice, the duplicate '{type.FullName}.{field.Name}' is skipped.");
                    continue;
                }

                var flagsAttribute = (ProjectileFlagsAttribute) Attribute.GetCustomAttribute(field, typeof(ProjectileFlagsAttribute));

                if (flagsAttribute != null)
                {
                    _flags[id.Value] = flagsAttribute.Flags;
                }

                _entries.Add(new Entry(id, isModded));
            }
        }
    }
}
