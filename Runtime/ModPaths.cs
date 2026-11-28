using System;
using System.IO;

namespace ColorlessOdyssey
{
    /// <summary>Layout of an exported mod, shared by the mod export and the game loading.</summary>
    public static class ModPaths
    {
        public const string BundleFileName = "mod.bundle";
        public const string ScriptsFolderName = "Scripts";

        /// <summary>
        /// Folder of the mod being tested, in the persistent data of the game. Built explicitly because
        /// Application.persistentDataPath differs in a mod project.
        /// </summary>
        public static string TestModFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Tryliom", "Colorless Odyssey", "TestMod");
    }
}
