using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// String identifier of an enemy (allies and dummies included). Declared with a local id, the registry turns it into
    /// "namespace_in_snake_case:local_id" at startup, so mods can't collide with the base game.
    /// A default (empty) id is equal to <see cref="EnemyIds.None"/>.
    /// </summary>
    [Serializable]
    public struct EnemyId : IEquatable<EnemyId>
    {
        [SerializeField] private string _value;

        public string Value => string.IsNullOrEmpty(_value) ? EnemyIds.None._value : _value;

        public EnemyId(string value)
        {
            _value = value;
        }

        public bool IsNone => this == EnemyIds.None;

        public bool Equals(EnemyId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EnemyId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(EnemyId a, EnemyId b) => a.Value == b.Value;
        public static bool operator !=(EnemyId a, EnemyId b) => a.Value != b.Value;
    }

    public static class EnemyIdExtensions
    {
        /// <summary>
        /// "colorless_odyssey:golden_bull" becomes "Golden Bull", "my_mod:slime" becomes "My Mod - Slime".
        /// </summary>
        public static string ToDisplayName(this EnemyId id) => ProjectileIdRegistry.ToDisplayName(id.Value);

        /// <summary>
        /// "colorless_odyssey:the_golden_pile" becomes "THE_GOLDEN_PILE" (same format as the former enum based localization keys).
        /// </summary>
        public static string ToLocalizeKey(this EnemyId id)
        {
            var value = id.Value;
            var separatorIndex = value.IndexOf(ProjectileIdRegistry.Separator);

            return (separatorIndex < 0 ? value : value.Substring(separatorIndex + 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Base game enemy ids. Fields are not constants, so use == instead of switch/case.
    /// Fields must stay non-readonly: the registry rewrites them with their namespaced id.
    /// </summary>
    public static class EnemyIds
    {
        public static EnemyId None = new("none");
        public static EnemyId GoldenBull = new("golden_bull");
        public static EnemyId ThunderMage = new("thunder_mage");
        public static EnemyId ThunderSpirit = new("thunder_spirit");
        public static EnemyId SpiritInvoker = new("spirit_invoker");
        public static EnemyId TinyGoldenSlime = new("tiny_golden_slime");
        public static EnemyId MagicalBlob = new("magical_blob");
        public static EnemyId TheGoldenPile = new("the_golden_pile");
        public static EnemyId AngryCloud = new("angry_cloud");
        public static EnemyId ThunderKnightSpirit = new("thunder_knight_spirit");
        public static EnemyId Kwak = new("kwak");
        public static EnemyId SuperKwak = new("super_kwak");
        public static EnemyId SteroidBull = new("steroid_bull");
        public static EnemyId ThunderrizedMage = new("thunderrized_mage");
        public static EnemyId LittleDummy = new("little_dummy");
        public static EnemyId NormalDummy = new("normal_dummy");
        public static EnemyId BigDummy = new("big_dummy");
        public static EnemyId Monarch = new("monarch");
        public static EnemyId BulletTurret = new("bullet_turret");
        public static EnemyId Sapphire = new("sapphire");
        public static EnemyId Zephyr = new("zephyr");
        public static EnemyId TestDummy = new("test_dummy");
    }

    /// <summary>
    /// Discovers every enemy id declared in <see cref="EnemyIds"/> and in any type named
    /// <c>ModdedEnemyId</c> (whatever its namespace), and rewrites them as "namespace:id".
    /// </summary>
    public static class EnemyIdRegistry
    {
        private const string ModTypeName = "ModdedEnemyId";

        private static List<Entry> _entries;
        private static HashSet<string> _ids;

        public readonly struct Entry
        {
            public readonly EnemyId Id;
            public readonly bool IsModded;

            public Entry(EnemyId id, bool isModded)
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

            Collect(typeof(EnemyIds), false);

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
                .Where(f => f.FieldType == typeof(EnemyId) && !f.IsInitOnly);

            foreach (var field in fields)
            {
                var id = (EnemyId)field.GetValue(null);

                // Already namespaced when initialization ran before (ex: domain reload disabled)
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    id = new EnemyId(prefix + id.Value);
                    field.SetValue(null, id);
                }

                if (!_ids.Add(id.Value))
                {
                    Debug.LogWarning($"Enemy id '{id.Value}' is declared twice, the duplicate '{type.FullName}.{field.Name}' is skipped.");
                    continue;
                }

                _entries.Add(new Entry(id, isModded));
            }
        }
    }
}
