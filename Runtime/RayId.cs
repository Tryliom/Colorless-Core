using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// String identifier of a ray. Declared with a local id, the registry turns it into
    /// "namespace_in_snake_case:local_id" at startup, so mods can't collide with the base game.
    /// A default (empty) id is equal to <see cref="RayIds.None"/>.
    /// </summary>
    [Serializable]
    public struct RayId : IEquatable<RayId>
    {
        [SerializeField] private string _value;

        public string Value => string.IsNullOrEmpty(_value) ? RayIds.None._value : _value;

        public RayId(string value)
        {
            _value = value;
        }

        public bool IsNone => this == RayIds.None;

        public bool Equals(RayId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RayId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(RayId a, RayId b) => a.Value == b.Value;
        public static bool operator !=(RayId a, RayId b) => a.Value != b.Value;
    }

    public static class RayIdExtensions
    {
        /// <summary>
        /// "colorless_odyssey:ice_arrow" becomes "Ice Arrow", "my_mod:ball" becomes "My Mod - Ball".
        /// </summary>
        public static string ToDisplayName(this RayId id) => ProjectileIdRegistry.ToDisplayName(id.Value);

        /// <summary>
        /// "colorless_odyssey:ice_pop" becomes "ICE_POP" (same format as the former enum based localization keys).
        /// </summary>
        public static string ToLocalizeKey(this RayId id)
        {
            var value = id.Value;
            var separatorIndex = value.IndexOf(ProjectileIdRegistry.Separator);

            return (separatorIndex < 0 ? value : value.Substring(separatorIndex + 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Base game Ray ids. Fields are not constants, so use == instead of switch/case.
    /// Fields must stay non-readonly: the registry rewrites them with their namespaced id.
    /// </summary>
    public static class RayIds
    {
        public static RayId None = new("none");
        public static RayId Thermal = new("thermal");
        public static RayId Plasma = new("plasma");
        public static RayId Energy = new("energy");
        public static RayId Cosmic = new("cosmic");
        public static RayId ChainLightning = new("chain_lightning");
    }

    /// <summary>
    /// Discovers every Ray id declared in <see cref="RayIds"/> and in any type named
    /// <c>ModdedRayId</c> (whatever its namespace), and rewrites them as "namespace:id".
    /// </summary>
    public static class RayIdRegistry
    {
        private const string ModTypeName = "ModdedRayId";

        private static List<Entry> _entries;
        private static HashSet<string> _ids;

        public readonly struct Entry
        {
            public readonly RayId Id;
            public readonly bool IsModded;

            public Entry(RayId id, bool isModded)
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

            Collect(typeof(RayIds), false);

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
                .Where(f => f.FieldType == typeof(RayId) && !f.IsInitOnly);

            foreach (var field in fields)
            {
                var id = (RayId)field.GetValue(null);

                // Already namespaced when initialization ran before (ex: domain reload disabled)
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    id = new RayId(prefix + id.Value);
                    field.SetValue(null, id);
                }

                if (!_ids.Add(id.Value))
                {
                    Debug.LogWarning($"Ray id '{id.Value}' is declared twice, the duplicate '{type.FullName}.{field.Name}' is skipped.");
                    continue;
                }

                _entries.Add(new Entry(id, isModded));
            }
        }
    }
}
