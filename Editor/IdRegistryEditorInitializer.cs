using UnityEditor;

namespace ColorlessOdyssey.Editor
{
    /// <summary>
    /// Runs the id registries outside play mode so that ids (AoeIds.X, ...) are namespaced in the editor too.
    /// </summary>
    public static class IdRegistryEditorInitializer
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            WeaponIdRegistry.Initialize();
            RayIdRegistry.Initialize();
            ProjectileIdRegistry.Initialize();
            AoeIdRegistry.Initialize();
            EnemyIdRegistry.Initialize();
        }
    }
}
