using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColorlessOdyssey
{
    /// <summary>
    /// Attaches the scripts of a mod to a prefab. A bundle cannot reference a script compiled at runtime,
    /// so the prefab only stores type names and the components are added when it is instantiated.
    /// Their serialized values are not saved yet: set them from code.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ModScriptHost : MonoBehaviour
    {
        [SerializeField, Tooltip("Full names (namespace included) of MonoBehaviour classes of the mod")]
        private List<string> _scriptTypeNames = new();

        /// <summary>
        /// Set by the game. Must only return types of the loaded mod, otherwise a prefab could add any component.
        /// </summary>
        public static Func<string, Type> TypeResolver;

        private void Awake()
        {
            foreach (var typeName in _scriptTypeNames)
            {
                var type = TypeResolver?.Invoke(typeName);

                if (type == null || type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    Debug.LogWarning($"[ModScriptHost] No mod MonoBehaviour named '{typeName}' for {name}.", this);

                    continue;
                }

                gameObject.AddComponent(type);
            }
        }
    }
}
