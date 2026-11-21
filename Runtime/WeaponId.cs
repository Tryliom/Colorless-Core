using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// String identifier of a weapon. Declared with a local id, the registry turns it into
    /// "namespace_in_snake_case:local_id" at startup, so mods can't collide with the base game.
    /// A default (empty) id is equal to <see cref="WeaponIds.None"/>.
    /// </summary>
    [Serializable]
    public struct WeaponId : IEquatable<WeaponId>
    {
        [SerializeField] private string _value;

        public string Value => string.IsNullOrEmpty(_value) ? WeaponIds.None._value : _value;

        public WeaponId(string value)
        {
            _value = value;
        }

        public bool IsNone => this == WeaponIds.None;

        public bool Equals(WeaponId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is WeaponId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(WeaponId a, WeaponId b) => a.Value == b.Value;
        public static bool operator !=(WeaponId a, WeaponId b) => a.Value != b.Value;
    }

    public static class WeaponIdExtensions
    {
        /// <summary>
        /// "colorless_odyssey:ice_arrow" becomes "Ice Arrow", "my_mod:ball" becomes "My Mod - Ball".
        /// </summary>
        public static string ToDisplayName(this WeaponId id) => ProjectileIdRegistry.ToDisplayName(id.Value);

        /// <summary>
        /// "colorless_odyssey:ice_pop" becomes "ICE_POP" (same format as the former enum based localization keys).
        /// </summary>
        public static string ToLocalizeKey(this WeaponId id)
        {
            var value = id.Value;
            var separatorIndex = value.IndexOf(ProjectileIdRegistry.Separator);

            return (separatorIndex < 0 ? value : value.Substring(separatorIndex + 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Base game Weapon ids. Fields are not constants, so use == instead of switch/case.
    /// Fields must stay non-readonly: the registry rewrites them with their namespaced id.
    /// </summary>
    public static class WeaponIds
    {
        public static WeaponId None = new("none");
        public static WeaponId Axe = new("axe");
        public static WeaponId BouncingBall = new("bouncing_ball");
        public static WeaponId CosmicRay = new("cosmic_ray");
        public static WeaponId DragonicStaff = new("dragonic_staff");
        public static WeaponId PrismaticLightTome = new("prismatic_light_tome");
        public static WeaponId FireStaff = new("fire_staff");
        public static WeaponId GravityPearlStaff = new("gravity_pearl_staff");
        public static WeaponId Gun = new("gun");
        public static WeaponId Katana = new("katana");
        public static WeaponId Knife = new("knife");
        public static WeaponId MagicWand = new("magic_wand");
        public static WeaponId MegaShark = new("mega_shark");
        public static WeaponId MiniShark = new("mini_shark");
        public static WeaponId PlasmaRay = new("plasma_ray");
        public static WeaponId Shotgun = new("shotgun");
        public static WeaponId SnowBall = new("snow_ball");
        public static WeaponId SpikyStaff = new("spiky_staff");
        public static WeaponId Sword = new("sword");
        public static WeaponId ThermalRay = new("thermal_ray");
        public static WeaponId ThunderCannon = new("thunder_cannon");
        public static WeaponId ZeusThunder = new("zeus_thunder");
        public static WeaponId LaserStaff = new("laser_staff");
        public static WeaponId CrossBow = new("cross_bow");
        public static WeaponId HolyCross = new("holy_cross");
        public static WeaponId Bow = new("bow");
        public static WeaponId Rapier = new("rapier");
        public static WeaponId Dagger = new("dagger");
        public static WeaponId ElfBow = new("elf_bow");
        public static WeaponId QiSword = new("qi_sword");
        public static WeaponId Shuriken = new("shuriken");
        public static WeaponId ShadowScythe = new("shadow_scythe");
        public static WeaponId PinkBlossomKatana = new("pink_blossom_katana");
        public static WeaponId ElementalBow = new("elemental_bow");
        public static WeaponId VolcanoStaff = new("volcano_staff");
        public static WeaponId MagicalBow = new("magical_bow");
        public static WeaponId KwakLauncher = new("kwak_launcher");
        public static WeaponId StarStick = new("star_stick");
        public static WeaponId SagittariusArc = new("sagittarius_arc");
        public static WeaponId SmallScythe = new("small_scythe");
        public static WeaponId SoulExploiter = new("soul_exploiter");
        public static WeaponId StarGun = new("star_gun");
        public static WeaponId FusionStaff = new("fusion_staff");
        public static WeaponId GoldenShotgun = new("golden_shotgun");
        public static WeaponId UnstableSpear = new("unstable_spear");
        public static WeaponId MagicPortal = new("magic_portal");
        public static WeaponId Izanagi = new("izanagi");
        public static WeaponId TurretDeployer = new("turret_deployer");
        public static WeaponId FrostTome = new("frost_tome");
        public static WeaponId PaperPlane = new("paper_plane");
        public static WeaponId FrostChakram = new("frost_chakram");
        public static WeaponId GiantArrow = new("giant_arrow");
        public static WeaponId Bonk = new("bonk");
        public static WeaponId Fist = new("fist");
        public static WeaponId LightScythe = new("light_scythe");
        public static WeaponId TalismanBag = new("talisman_bag");
        public static WeaponId CrystalArtefact = new("crystal_artefact");
        public static WeaponId IcePop = new("ice_pop");
        public static WeaponId DeadlyWeapon = new("deadly_weapon");
        public static WeaponId CombatPickaxe = new("combat_pickaxe");
        public static WeaponId CompoundBow = new("compound_bow");
    }

    /// <summary>
    /// Discovers every Weapon id declared in <see cref="WeaponIds"/> and in any type named
    /// <c>ModdedWeaponId</c> (whatever its namespace), and rewrites them as "namespace:id".
    /// </summary>
    public static class WeaponIdRegistry
    {
        private const string ModTypeName = "ModdedWeaponId";

        private static List<Entry> _entries;
        private static HashSet<string> _ids;

        public readonly struct Entry
        {
            public readonly WeaponId Id;
            public readonly bool IsModded;

            public Entry(WeaponId id, bool isModded)
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

            Collect(typeof(WeaponIds), false);

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
                .Where(f => f.FieldType == typeof(WeaponId) && !f.IsInitOnly);

            foreach (var field in fields)
            {
                var id = (WeaponId)field.GetValue(null);

                // Already namespaced when initialization ran before (ex: domain reload disabled)
                if (!id.Value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    id = new WeaponId(prefix + id.Value);
                    field.SetValue(null, id);
                }

                if (!_ids.Add(id.Value))
                {
                    Debug.LogWarning($"Weapon id '{id.Value}' is declared twice, the duplicate '{type.FullName}.{field.Name}' is skipped.");
                    continue;
                }

                _entries.Add(new Entry(id, isModded));
            }
        }
    }
}
