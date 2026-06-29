using UnityEditor;

namespace Editor
{
    public static class EnterPlayModeMenu
    {
        private const string ROOT = "Tools/Enter Play Mode Settings/";

        private const string OPT_DEFAULT = ROOT + "Default (Reload Domain & Scene)";
        private const string OPT_DISABLE_DOMAIN = ROOT + "Disable Domain Reload";
        private const string OPT_DISABLE_SCENE = ROOT + "Disable Scene Reload";
        private const string OPT_FAST_NO_RELOADS = ROOT + "Fast: Disable Domain & Scene Reload";

        // This is now public so the toolbar can use it
        public enum Preset
        {
            DefaultReloads,
            DisableDomainReload,
            DisableSceneReload,
            FastNoReloads
        }

        // ----- Menu items -----

        [MenuItem(OPT_DEFAULT, priority = 0)]
        private static void Set_Default() => Apply(Preset.DefaultReloads);

        [MenuItem(OPT_DEFAULT, true)]
        private static bool Validate_Default()
        {
            SetChecks();
            return true;
        }

        [MenuItem(OPT_DISABLE_DOMAIN, priority = 1)]
        private static void Set_DisableDomain() => Apply(Preset.DisableDomainReload);

        [MenuItem(OPT_DISABLE_DOMAIN, true)]
        private static bool Validate_DisableDomain()
        {
            SetChecks();
            return true;
        }

        [MenuItem(OPT_DISABLE_SCENE, priority = 2)]
        private static void Set_DisableScene() => Apply(Preset.DisableSceneReload);

        [MenuItem(OPT_DISABLE_SCENE, true)]
        private static bool Validate_DisableScene()
        {
            SetChecks();
            return true;
        }

        [MenuItem(OPT_FAST_NO_RELOADS, priority = 3)]
        private static void Set_FastNoReloads() => Apply(Preset.FastNoReloads);

        [MenuItem(OPT_FAST_NO_RELOADS, true)]
        private static bool Validate_FastNoReloads()
        {
            SetChecks();
            return true;
        }

        // ----- Public helpers for other code (toolbar, shortcuts, etc) -----

        public static void ApplyPreset(Preset preset) => Apply(preset);

        public static Preset GetCurrentPreset() => GetCurrent();

        // ----- Core impl -----

        private static void Apply(Preset p)
        {
            switch (p)
            {
                case Preset.DefaultReloads:
                    EditorSettings.enterPlayModeOptionsEnabled = false;
                    EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
                    break;

                case Preset.DisableDomainReload:
                    EditorSettings.enterPlayModeOptionsEnabled = true;
                    EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                    break;

                case Preset.DisableSceneReload:
                    EditorSettings.enterPlayModeOptionsEnabled = true;
                    EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableSceneReload;
                    break;

                case Preset.FastNoReloads:
                    EditorSettings.enterPlayModeOptionsEnabled = true;
                    EditorSettings.enterPlayModeOptions =
                        EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                    break;
            }

            SetChecks(); // refresh ticks immediately
        }

        private static Preset GetCurrent()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
                return Preset.DefaultReloads;

            var o = EditorSettings.enterPlayModeOptions;
            bool domain = (o & EnterPlayModeOptions.DisableDomainReload) != 0;
            bool scene = (o & EnterPlayModeOptions.DisableSceneReload) != 0;

            if (domain && scene) return Preset.FastNoReloads;
            if (domain) return Preset.DisableDomainReload;
            if (scene) return Preset.DisableSceneReload;
            return Preset.DefaultReloads; // unlikely, but safe mapping
        }

        private static void SetChecks()
        {
            var current = GetCurrent();
            Menu.SetChecked(OPT_DEFAULT, current == Preset.DefaultReloads);
            Menu.SetChecked(OPT_DISABLE_DOMAIN, current == Preset.DisableDomainReload);
            Menu.SetChecked(OPT_DISABLE_SCENE, current == Preset.DisableSceneReload);
            Menu.SetChecked(OPT_FAST_NO_RELOADS, current == Preset.FastNoReloads);
        }
    }
}