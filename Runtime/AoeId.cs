using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// String identifier of a aoe. Declared with a local id, the registry turns it into
    /// "namespace_in_snake_case:local_id" at startup, so mods can't collide with the base game.
    /// A default (empty) id is equal to <see cref="AoeIds.None"/>.
    /// </summary>
    [Serializable]
    public struct AoeId : IEquatable<AoeId>
    {
        [SerializeField] private string _value;

        public string Value => string.IsNullOrEmpty(_value) ? AoeIds.None._value : _value;

        public AoeId(string value)
        {
            _value = value;
        }

        public bool IsNone => this == AoeIds.None;

        public bool Equals(AoeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AoeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(AoeId a, AoeId b) => a.Value == b.Value;
        public static bool operator !=(AoeId a, AoeId b) => a.Value != b.Value;
    }

    public static class AoeIdExtensions
    {
        /// <summary>
        /// "colorless_odyssey:ice_arrow" becomes "Ice Arrow", "my_mod:ball" becomes "My Mod - Ball".
        /// </summary>
        public static string ToDisplayName(this AoeId id) => ProjectileIdRegistry.ToDisplayName(id.Value);

        /// <summary>
        /// "colorless_odyssey:ice_pop" becomes "ICE_POP" (same format as the former enum based localization keys).
        /// </summary>
        public static string ToLocalizeKey(this AoeId id)
        {
            var value = id.Value;
            var separatorIndex = value.IndexOf(ProjectileIdRegistry.Separator);

            return (separatorIndex < 0 ? value : value.Substring(separatorIndex + 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Base game Aoe ids. Fields are not constants, so use == instead of switch/case.
    /// Fields must stay non-readonly: the registry rewrites them with their namespaced id.
    /// </summary>
    public static class AoeIds
    {
        public static AoeId None = new("none");
        public static AoeId Thunder = new("thunder");
        public static AoeId FireStorm = new("fire_storm");
        public static AoeId HolyAura = new("holy_aura");
        public static AoeId MegaMagic = new("mega_magic");
        public static AoeId Explosion = new("explosion");
        public static AoeId ThermalExplosion = new("thermal_explosion");
        public static AoeId FrostExplosion = new("frost_explosion");
        public static AoeId GlacialWave = new("glacial_wave");
        public static AoeId Wave = new("wave");
        public static AoeId PoisonSpike = new("poison_spike");
        public static AoeId PoisonPool = new("poison_pool");
        public static AoeId GoldenPileCry = new("golden_pile_cry");
        public static AoeId SteroidBullCry = new("steroid_bull_cry");
        public static AoeId MagicalBuff = new("magical_buff");
        public static AoeId IceSpikes = new("ice_spikes");
        public static AoeId StarExplosion = new("star_explosion");
        public static AoeId Emotional = new("emotional");
        public static AoeId ElectricAura = new("electric_aura");
        public static AoeId Shockwave = new("shockwave");
        public static AoeId PrismaticBuff = new("prismatic_buff");
        public static AoeId IceTrailSpike = new("ice_trail_spike");
        public static AoeId AcidCloud = new("acid_cloud");
        public static AoeId Maelstrom = new("maelstrom");
        public static AoeId SunAura = new("sun_aura");
        public static AoeId PoisonCloud = new("poison_cloud");
    }

    /// <summary>
    /// Discovers every Aoe id declared in <see cref="AoeIds"/> and in any type named
    /// <c>ModdedAoeId</c> (whatever its namespace), and rewrites them as "namespace:id".
    /// </summary>
    public static class AoeIdRegistry
    {
        private const string ModTypeName = "ModdedAoeId";

        private static List<Entry> _entries;
        private static HashSet<string> _ids;

        public readonly struct Entry
        {
            public readonly AoeId Id;
            public readonly bool IsModded;

            public Entry(AoeId id, bool isModded)
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

            Collect(typeof(AoeIds), false);

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
            var prefix = ProjectileIdRegistry.NamespaceToSnakeCase(type.Namespace) + ProjectileIdRegistry.Separator;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(AoeId) && !f.IsInitOnly);

            foreach (var field in fields)
            {
                var id = (AoeId)field.GetValue(null);

                // Already namespaced when initialization ran before (ex: domain reload disabled)
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    id = new AoeId(prefix + id.Value);
                    field.SetValue(null, id);
                }

                if (!_ids.Add(id.Value))
                {
                    Debug.LogWarning($"Aoe id '{id.Value}' is declared twice, the duplicate '{type.FullName}.{field.Name}' is skipped.");
                    continue;
                }

                _entries.Add(new Entry(id, isModded));
            }
        }
    }
}
