namespace Tracker
{
    using GameHelper.Plugin;
    using System.Collections.Generic;
    using System.Numerics;

    public sealed class TrackerSettings : IPSettings
    {
        public bool UniqueLine = false;
        public bool RareLine = false;
        public bool MagicLine = false;

        public Vector4 UniqueLineColor = new(1.0f, 0.580f, 0.0f, 0.564f);
        public Vector4 RareLineColor = new(1.0f, 0.988f, 0.0f, 0.490f);
        public Vector4 MagicLineColor = new(0.0f, 0.117f, 1.0f, 0.294f);

        public List<GroundEffectSettings> GroundEffects;
        public List<StatusEffectSettings> StatusEffects;
        public List<StatusEffectSettings> PlayerStatusEffects;

        public bool AutoSyncGroundEffectsFromFile = false;
        public string GroundEffectsSourceFile = @"D:\PTrade\GH2\Plugins\Tracker\config\settings.txt";
        public string ImportSettingsFile = @"D:\PTrade\GH2\Plugins\Tracker\config\tracker_settings_import.txt";
        public string ExportSettingsFile = @"D:\PTrade\GH2\Plugins\Tracker\config\tracker_settings_export.txt";
        public bool IconCoordinatesMigratedTo8Columns = false;

        // Player effect settings
        public bool ShowPlayerStatusEffects = true;
        public int PlayerStatusYOffset = 35;
        public int PlayerStatusXOffset = 0;
        public int PlayerStatusIconGap = 4;
        public bool AutoApplyStatusLayoutProfile = true;

        // Monster effect settings
        public int MonsterStatusXOffset = 0;
        public int MonsterStatusYOffset = -42;
        public int MonsterStatusIconGap = 4;
        public float TextShadowAlpha = 0.5f;
        public int TextShadowSize = 1;

        // Offsets (pixels) relative to the whole status icon
        public int ChargesXOffset = 0;
        public int ChargesYOffset = 0;
        public int TimerXOffset = 0;
        public int TimerYOffset = 0;

        public Vector4 StatusBarBackgroundColor = new(0, 0, 0, 0.6666667f);
        public int StatusBarMinWidth = 50;

        public TrackerSettings()
        {
            GroundEffects = [
                new GroundEffectSettings(true, "Metadata/Effects/Spells/ground_effects/VisibleServerGroundEffect", new Vector4(1.0f, 0.3019608f, 0.0f, 0.6f), 100, 2, false)
            ];

            StatusEffects = [
                new StatusEffectSettings(true, "shocked_70", "Shocked", new Vector4(0.49411765f, 1.0f, 0.0f, 1.0f), new Vector4(0.0f, 0.17327861f, 0.36683416f, 1.0f))
            ];

            PlayerStatusEffects = [
                new StatusEffectSettings(true, "stolen_mods_buff_70", "StolenBuff", new Vector4(0.0f, 1.0f, 0.47236156f, 1.0f), new Vector4(0.24120605f, 0.71402276f, 1.0f, 0.6627451f))
            ];
        }
    }
}
