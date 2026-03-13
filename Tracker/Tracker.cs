namespace Tracker
{
    using GameHelper;
    using GameHelper.Plugin;
    using ImGuiNET;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;

    public sealed class Tracker : PCore<TrackerSettings>
    {
        private sealed class ExternalSettingsSnapshot
        {
            public List<GroundEffectSettings> GroundEffects { get; set; }
        }

        private const float IconTileSize = 32f;
        private const string ImportSuccessPrefix = "Imported:";
        private static readonly string[] ResolutionProfiles = ["1080p", "1440p", "4K"];
        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");
        private string StatusIconsPath => Path.Join(this.DllDirectory, "status_icons.png");
        private MonsterLineLogic MonsterLine { get; set; }
        private GroundEffectLogic GroundEffect { get; set; }
        private StatusEffectLogic StatusEffect { get; set; }
        private IntPtr StatusIconsTexturePtr { get; set; } = IntPtr.Zero;
        private float StatusIconsTextureWidth { get; set; }
        private float StatusIconsTextureHeight { get; set; }
        private readonly Dictionary<string, bool> IconPickerState = new();
        private int SelectedResolutionProfile = 1;
        private string LastExportStatus = string.Empty;

        public override void OnDisable()
        {
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                var serializerSettings = new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace };
                this.Settings = JsonConvert.DeserializeObject<TrackerSettings>(content, serializerSettings);
            }

            var screenHeight = ImGui.GetIO().DisplaySize.Y;
            SelectedResolutionProfile = GetProfileIndexByHeight(screenHeight);
            if (Settings.AutoApplyStatusLayoutProfile)
            {
                ApplyStatusLayoutProfile(SelectedResolutionProfile);
            }

            if (Settings.AutoSyncGroundEffectsFromFile)
            {
                SyncGroundEffectsFromExternalFile();
            }

            MigrateIconCoordinatesTo8ColumnsIfNeeded();

            MonsterLine = new MonsterLineLogic(this.Settings);
            GroundEffect = new GroundEffectLogic(this.Settings);
            StatusEffect = new StatusEffectLogic(this.Settings);
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname));
            var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, settingsData);
        }

        public override void DrawSettings()
        {
            DrawSettingsExport();
            DrawStatusLayoutProfiles();
            DrawTextRenderingSettings();
            DrawMonsterLineSettigns();
            DrawGroundEffectSettings();
            DrawStatusEffectSettings();
            DrawPlayerEffectSettings();
        }

        private void DrawTextRenderingSettings()
        {
            if (!ImGui.CollapsingHeader("Text Rendering"))
            {
                return;
            }

            ImGui.Indent();
            ImGui.Text("Text Shadow Alpha");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.SliderFloat("##GlobalTextShadowAlpha", ref Settings.TextShadowAlpha, 0.0f, 1.0f, "%.2f");

            ImGui.SameLine();
            ImGui.Text("Shadow Size");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.SliderInt("##GlobalTextShadowSize", ref Settings.TextShadowSize, 0, 2);

            ImGui.SameLine();
            ImGui.Text("StatusBar Background");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.ColorEdit4("##StatusBarBackground", ref Settings.StatusBarBackgroundColor);

            ImGui.Separator();
            ImGui.Text("Charges Offset X");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.SliderInt("##ChargesOffsetX", ref Settings.ChargesXOffset, -64, 64);

            ImGui.SameLine();
            ImGui.Text("Charges Offset Y");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.SliderInt("##ChargesOffsetY", ref Settings.ChargesYOffset, -64, 64);

            ImGui.SameLine();
            ImGui.Text("Timer Offset X");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.SliderInt("##TimerOffsetX", ref Settings.TimerXOffset, -64, 64);

            ImGui.SameLine();
            ImGui.Text("Timer Offset Y");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.SliderInt("##TimerOffsetY", ref Settings.TimerYOffset, -64, 64);
            ImGui.Unindent();
        }

        private void DrawSettingsExport()
        {
            if (!ImGui.CollapsingHeader("Settings Import/Export"))
            {
                return;
            }

            ImGui.Indent();
            ImGui.SetNextItemWidth(460);
            ImGui.InputText("Import file path", ref Settings.ImportSettingsFile, 512);

            if (ImGui.Button("Import"))
            {
                LastExportStatus = ImportSettingsFromTextFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Import + Remap 28->8"))
            {
                LastExportStatus = ImportSettingsAndRemapFromTextFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Import + Force remap"))
            {
                LastExportStatus = ImportSettingsAndForceRemapFromTextFile();
            }

            ImGui.Separator();

            ImGui.SetNextItemWidth(460);
            ImGui.InputText("Export file path", ref Settings.ExportSettingsFile, 512);

            if (ImGui.Button("Export"))
            {
                LastExportStatus = ExportSettingsToTextFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Remap 28->8"))
            {
                LastExportStatus = RemapIconCoordinatesTo8ColumnsNow();
            }

            ImGui.SameLine();
            if (ImGui.Button("Force remap"))
            {
                ImGui.OpenPopup("Confirm force remap##Tracker");
            }

            if (ImGui.BeginPopup("Confirm force remap##Tracker"))
            {
                ImGui.TextWrapped("This will recalculate all icon coordinates as if they were in a 28-column layout. Continue?");

                if (ImGui.Button("Yes, remap now"))
                {
                    LastExportStatus = ForceRemapIconCoordinatesTo8ColumnsNow();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!string.IsNullOrWhiteSpace(LastExportStatus))
            {
                ImGui.TextWrapped(LastExportStatus);
            }

            ImGui.Unindent();
        }

        private string ExportSettingsToTextFile()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Settings.ExportSettingsFile))
                {
                    return "Export path is empty.";
                }

                var exportDirectory = Path.GetDirectoryName(Settings.ExportSettingsFile);
                if (string.IsNullOrWhiteSpace(exportDirectory))
                {
                    return "Invalid export path.";
                }

                Directory.CreateDirectory(exportDirectory);
                var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
                File.WriteAllText(Settings.ExportSettingsFile, settingsData);
                return $"Exported: {Settings.ExportSettingsFile}";
            }
            catch (Exception ex)
            {
                return $"Export failed: {ex.Message}";
            }
        }

        private string ImportSettingsFromTextFile()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Settings.ImportSettingsFile))
                {
                    return "Import path is empty.";
                }

                if (!File.Exists(Settings.ImportSettingsFile))
                {
                    return $"Import file not found: {Settings.ImportSettingsFile}";
                }

                var content = File.ReadAllText(Settings.ImportSettingsFile);
                var serializerSettings = new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace };
                var importedSettings = JsonConvert.DeserializeObject<TrackerSettings>(content, serializerSettings);
                if (importedSettings == null)
                {
                    return "Import failed: file content is invalid.";
                }

                Settings = importedSettings;
                SaveSettings();

                ReinitializeLogic();

                return $"Imported: {Settings.ImportSettingsFile}";
            }
            catch (Exception ex)
            {
                return $"Import failed: {ex.Message}";
            }
        }

        private string ImportSettingsAndRemapFromTextFile()
        {
            return ImportSettingsAndThen(RemapIconCoordinatesTo8ColumnsNow);
        }

        private string ImportSettingsAndForceRemapFromTextFile()
        {
            return ImportSettingsAndThen(ForceRemapIconCoordinatesTo8ColumnsNow);
        }

        private string ImportSettingsAndThen(Func<string> afterImportAction)
        {
            var importStatus = ImportSettingsFromTextFile();
            if (!importStatus.StartsWith(ImportSuccessPrefix, StringComparison.Ordinal))
            {
                return importStatus;
            }

            var actionStatus = afterImportAction();
            return $"{importStatus} {actionStatus}";
        }

        private void ReinitializeLogic()
        {
            MonsterLine = new MonsterLineLogic(this.Settings);
            GroundEffect = new GroundEffectLogic(this.Settings);
            StatusEffect = new StatusEffectLogic(this.Settings);
        }

        private string RemapIconCoordinatesTo8ColumnsNow()
        {
            const int oldColumns = 28;
            const int newColumns = 8;

            var hasOldLayoutCoordinates = ListContainsOldLayoutCoordinates(Settings.StatusEffects, newColumns)
                                          || ListContainsOldLayoutCoordinates(Settings.PlayerStatusEffects, newColumns);

            if (!hasOldLayoutCoordinates)
            {
                return "Remap skipped: no icon coordinates from 28-column layout were found.";
            }

            RemapIconCoordinates(Settings.StatusEffects, oldColumns, newColumns);
            RemapIconCoordinates(Settings.PlayerStatusEffects, oldColumns, newColumns);
            Settings.IconCoordinatesMigratedTo8Columns = true;
            SaveSettings();
            return "Icon coordinates remapped from 28 to 8 columns.";
        }

        private string ForceRemapIconCoordinatesTo8ColumnsNow()
        {
            const int oldColumns = 28;
            const int newColumns = 8;

            RemapIconCoordinates(Settings.StatusEffects, oldColumns, newColumns);
            RemapIconCoordinates(Settings.PlayerStatusEffects, oldColumns, newColumns);
            Settings.IconCoordinatesMigratedTo8Columns = true;
            SaveSettings();
            return "Force remap completed: icon coordinates recalculated from 28 to 8 columns.";
        }

        private void MigrateIconCoordinatesTo8ColumnsIfNeeded()
        {
            const int oldColumns = 28;
            const int newColumns = 8;

            if (Settings.IconCoordinatesMigratedTo8Columns)
            {
                return;
            }

            var looksLikeOldLayout = ListContainsOldLayoutCoordinates(Settings.StatusEffects, newColumns)
                                     || ListContainsOldLayoutCoordinates(Settings.PlayerStatusEffects, newColumns);

            if (!looksLikeOldLayout)
            {
                Settings.IconCoordinatesMigratedTo8Columns = true;
                return;
            }

            RemapIconCoordinates(Settings.StatusEffects, oldColumns, newColumns);
            RemapIconCoordinates(Settings.PlayerStatusEffects, oldColumns, newColumns);
            Settings.IconCoordinatesMigratedTo8Columns = true;
            SaveSettings();
        }

        private static bool ListContainsOldLayoutCoordinates(List<StatusEffectSettings> effects, int newColumns)
        {
            if (effects == null || effects.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].IconColumn >= newColumns)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemapIconCoordinates(List<StatusEffectSettings> effects, int oldColumns, int newColumns)
        {
            if (effects == null || effects.Count == 0)
            {
                return;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                var oldIndex = Math.Max(0, effect.IconRow) * oldColumns + Math.Max(0, effect.IconColumn);
                effect.IconColumn = oldIndex % newColumns;
                effect.IconRow = oldIndex / newColumns;
            }
        }

        public override void DrawUI()
        {
            this.MonsterLine.Draw();
            this.GroundEffect.Draw();
            this.StatusEffect.Draw();
        }

        private void DrawMonsterLineSettigns()
        {
            ImGui.Checkbox("##UniqueLine", ref Settings.UniqueLine);
            ImGui.SameLine();
            ColorSwatch("Unique monster line color", ref Settings.UniqueLineColor);

            ImGui.Checkbox("##RareLine", ref Settings.RareLine);
            ImGui.SameLine();
            ColorSwatch("Rare monster line color", ref Settings.RareLineColor);

            ImGui.Checkbox("##MagicLine", ref Settings.MagicLine);
            ImGui.SameLine();
            ColorSwatch("Magic monster line color", ref Settings.MagicLineColor);
        }

        private void DrawGroundEffectSettings()
        {
            if (ImGui.CollapsingHeader("Ground Effects"))
            {
                ImGui.Indent();

                ImGui.Checkbox("Auto sync from file", ref Settings.AutoSyncGroundEffectsFromFile);
                Tooltip("Imports GroundEffects from external settings.txt on startup.");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(360);
                ImGui.InputText("##GroundEffectsSourceFile", ref Settings.GroundEffectsSourceFile, 512);

                ImGui.SameLine();
                if (ImGui.Button("Sync now"))
                {
                    SyncGroundEffectsFromExternalFile();
                }

                ImGui.Separator();

                for (int i = 0; i < Settings.GroundEffects.Count; i++)
                {
                    var groundEffect = Settings.GroundEffects[i];

                    ImGui.Checkbox($"##GroundEffectEnabled{i}", ref groundEffect.IsEnabled);

                    ImGui.SameLine();
                    ColorSwatch($"##GroundEffectColor{i}", ref groundEffect.Color);

                    ImGui.SameLine();
                    ImGui.Text($"Fill");
                    ImGui.SameLine();
                    ImGui.Checkbox($"##Fill{i}", ref groundEffect.Filled);

                    ImGui.SameLine();
                    ImGui.Text($"Weight");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(50);
                    ImGui.SliderInt($"##Weight{i}", ref groundEffect.LineWeight, 1, 5);

                    ImGui.SameLine();
                    ImGui.Text("Radius");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(50);
                    ImGui.SliderInt($"##Radius{i}", ref groundEffect.Radius, 50, 300);

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(GetInputWidth());
                    ImGui.InputText($"##GroundEffectPath{i}", ref groundEffect.Path, 256);

                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##GroundEffectDelete{i}"))
                    {
                        Settings.GroundEffects.RemoveAt(i);
                        break;
                    }
                }

                if (ImGui.Button("Add Ground Effect"))
                    Settings.GroundEffects.Add(new GroundEffectSettings(true, "", new Vector4(1.0f, 0.0f, 0.0f, 0.6f), 100, 1, false));

                ImGui.Unindent();
            }
        }

        private void SyncGroundEffectsFromExternalFile()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Settings.GroundEffectsSourceFile))
                {
                    return;
                }

                if (!File.Exists(Settings.GroundEffectsSourceFile))
                {
                    return;
                }

                var content = File.ReadAllText(Settings.GroundEffectsSourceFile);
                var snapshot = JsonConvert.DeserializeObject<ExternalSettingsSnapshot>(content);
                if (snapshot?.GroundEffects != null && snapshot.GroundEffects.Count > 0)
                {
                    Settings.GroundEffects = snapshot.GroundEffects;
                }
            }
            catch
            {
            }
        }

        private void DrawStatusEffectSettings()
        {
            if (ImGui.CollapsingHeader("Monster Status Effects"))
            {
                ImGui.Indent();

                ImGui.Text("Horizontal Offset");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##MonsterStatusXOffset", ref Settings.MonsterStatusXOffset, -400, 400);

                ImGui.SameLine();
                ImGui.Text("Vertical Offset");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##MonsterStatusYOffset", ref Settings.MonsterStatusYOffset, -400, 400);

                ImGui.SameLine();
                ImGui.Text("Gap");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90);
                ImGui.SliderInt("##MonsterStatusIconGap", ref Settings.MonsterStatusIconGap, 0, 50);

                ImGui.Separator();

                for (int i = 0; i < Settings.StatusEffects.Count; i++)
                {
                    var statusEffect = Settings.StatusEffects[i];

                    ImGui.Checkbox($"##StatusEffectEnabled{i}", ref statusEffect.IsEnabled);
                    Tooltip("Enable status Effect.");

                    ImGui.SameLine();
                    ColorSwatch($"##StatusEffectTextColor{i}", ref statusEffect.TextColor);
                    Tooltip("Status Effect text color.");

                    ImGui.SameLine();
                    ColorSwatch($"##StatusEffectBarColor{i}", ref statusEffect.BarColor);
                    Tooltip("Status Effect bar color.");

                    ImGui.SameLine();
                    ImGui.Text("Icon");
                    ImGui.SameLine();
                    DrawIconPreview(statusEffect, $"MonsterIconPreview{i}");

                    ImGui.SameLine();
                    ImGui.Text("Scale");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(65);
                    ImGui.InputFloat($"##MonsterIconScale{i}", ref statusEffect.IconScale, 0.1f, 0.5f, "%.2f");
                    statusEffect.IconScale = Math.Clamp(statusEffect.IconScale, 0.2f, 10.0f);

                    ImGui.SameLine();
                    if (ImGui.Button($"Pick##MonsterPick{i}"))
                    {
                        IconPickerState[$"MonsterPick{i}"] = true;
                    }

                    DrawIconPickerPopup($"MonsterPick{i}", statusEffect);

                    ImGui.SameLine();
                    ImGui.Text($"Display Name");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(120);
                    ImGui.InputText($"##DisplayName{i}", ref statusEffect.DisplayName, 256);

                    ImGui.SameLine();
                    ImGui.Text($"Name");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(GetInputWidth());
                    ImGui.InputText($"##Name{i}", ref statusEffect.Name, 256);

                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##StatusEffectDelete{i}"))
                    {
                        Settings.StatusEffects.RemoveAt(i);
                        break;
                    }
                }

                if (ImGui.Button("Add Status Effect"))
                    Settings.StatusEffects.Add(new StatusEffectSettings(true, "xxx", "XXX", new Vector4(1.0f, 1.0f, 1.0f, 1.0f), new Vector4(0.4549f, 0.0314f, 0.0314f, 1.0f)));

                ImGui.Unindent();
            }
        }

        private void DrawPlayerEffectSettings()
        {
            if (ImGui.CollapsingHeader("Player Status Effects"))
            {
                ImGui.Indent();

                ImGui.Checkbox("Show Player Status Effects", ref Settings.ShowPlayerStatusEffects);
                Tooltip("Toggle displaying status effects for the player.");

                ImGui.SameLine();
                ImGui.Text("Vertical Offset");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##PlayerStatusYOffset", ref Settings.PlayerStatusYOffset, -200, 400);
                Tooltip("Positive value draws status effects further below the player on screen.");

                ImGui.SameLine();
                ImGui.Text("Horizontal Offset");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##PlayerStatusXOffset", ref Settings.PlayerStatusXOffset, -400, 400);

                ImGui.SameLine();
                ImGui.Text("Gap");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90);
                ImGui.SliderInt("##PlayerStatusIconGap", ref Settings.PlayerStatusIconGap, 0, 50);

                ImGui.Separator();
                ImGui.Text("Player Status Effects Configuration:");

                for (int i = 0; i < Settings.PlayerStatusEffects.Count; i++)
                {
                    var statusEffect = Settings.PlayerStatusEffects[i];

                    ImGui.Checkbox($"##PlayerStatusEffectEnabled{i}", ref statusEffect.IsEnabled);
                    Tooltip("Enable status Effect.");

                    ImGui.SameLine();
                    ColorSwatch($"##PlayerStatusEffectTextColor{i}", ref statusEffect.TextColor);
                    Tooltip("Status Effect text color.");

                    ImGui.SameLine();
                    ColorSwatch($"##PlayerStatusEffectBarColor{i}", ref statusEffect.BarColor);
                    Tooltip("Status Effect bar color.");

                    ImGui.SameLine();
                    ImGui.Text("Icon");
                    ImGui.SameLine();
                    DrawIconPreview(statusEffect, $"PlayerIconPreview{i}");

                    ImGui.SameLine();
                    ImGui.Text("Scale");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(65);
                    ImGui.InputFloat($"##PlayerIconScale{i}", ref statusEffect.IconScale, 0.1f, 0.5f, "%.2f");
                    statusEffect.IconScale = Math.Clamp(statusEffect.IconScale, 0.2f, 10.0f);

                    ImGui.SameLine();
                    if (ImGui.Button($"Pick##PlayerPick{i}"))
                    {
                        IconPickerState[$"PlayerPick{i}"] = true;
                    }

                    DrawIconPickerPopup($"PlayerPick{i}", statusEffect);

                    ImGui.SameLine();
                    ImGui.Text($"Display Name");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(120);
                    ImGui.InputText($"##PlayerDisplayName{i}", ref statusEffect.DisplayName, 256);

                    ImGui.SameLine();
                    ImGui.Text($"Name");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(GetInputWidth());
                    ImGui.InputText($"##PlayerName{i}", ref statusEffect.Name, 256);

                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##PlayerStatusEffectDelete{i}"))
                    {
                        Settings.PlayerStatusEffects.RemoveAt(i);
                        break;
                    }
                }

                if (ImGui.Button("Add Player Status Effect"))
                    Settings.PlayerStatusEffects.Add(new StatusEffectSettings(true, "xxx", "XXX", new Vector4(1.0f, 1.0f, 1.0f, 1.0f), new Vector4(0.4549f, 0.0314f, 0.0314f, 1.0f)));

                ImGui.Unindent();
            }
        }

        private void DrawStatusLayoutProfiles()
        {
            if (!ImGui.CollapsingHeader("Status Layout Profiles"))
            {
                return;
            }

            ImGui.Indent();

            var displaySize = ImGui.GetIO().DisplaySize;
            ImGui.Text($"Detected resolution: {(int)displaySize.X}x{(int)displaySize.Y}");

            ImGui.Checkbox("Auto apply profile on startup", ref Settings.AutoApplyStatusLayoutProfile);
            Tooltip("When enabled, profile is auto-selected by screen height during plugin startup.");

            if (ImGui.Button("Detect"))
            {
                SelectedResolutionProfile = GetProfileIndexByHeight(displaySize.Y);
            }
            Tooltip("Detects profile by current screen height.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.Combo("##StatusLayoutProfile", ref SelectedResolutionProfile, ResolutionProfiles, ResolutionProfiles.Length);

            ImGui.SameLine();
            if (ImGui.Button("Apply"))
            {
                ApplyStatusLayoutProfile(SelectedResolutionProfile);
            }
            Tooltip("Applies default offsets/gap for selected profile.");

            ImGui.SameLine();
            if (ImGui.Button("Reset to current profile defaults"))
            {
                ApplyStatusLayoutProfile(SelectedResolutionProfile);
            }
            Tooltip("Resets offsets/gap back to selected profile defaults.");

            ImGui.Unindent();
        }

        private static void ColorSwatch(string label, ref System.Numerics.Vector4 color)
        {

            if (ImGui.ColorButton(label, color))
                ImGui.OpenPopup(label);

            if (ImGui.BeginPopup(label))
            {
                ImGui.ColorPicker4(label, ref color);
                ImGui.EndPopup();
            }

            if (!label.StartsWith("##"))
            {
                ImGui.SameLine();
                ImGui.Text(label);
            }
        }

        private static void Tooltip(string label)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(label);
                ImGui.EndTooltip();
            }
        }

        private static float GetInputWidth()
        {
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var minWidth = 50.0f;
            var buttonSize = ImGui.CalcTextSize($"Delete");
            buttonSize.X += ImGui.GetStyle().FramePadding.X * 2;
            return Math.Max(availableWidth - buttonSize.X - 10, minWidth);
        }

        private bool TryLoadStatusIconsTexture()
        {
            if (StatusIconsTexturePtr != IntPtr.Zero)
            {
                return true;
            }

            if (!File.Exists(StatusIconsPath))
            {
                return false;
            }

            Core.Overlay.AddOrGetImagePointer(StatusIconsPath, false, out var ptr, out var width, out var height);
            StatusIconsTexturePtr = ptr;
            StatusIconsTextureWidth = width;
            StatusIconsTextureHeight = height;
            return StatusIconsTexturePtr != IntPtr.Zero && StatusIconsTextureWidth > 0 && StatusIconsTextureHeight > 0;
        }

        private static int GetProfileIndexByHeight(float screenHeight)
        {
            if (screenHeight >= 2160)
            {
                return 2;
            }

            if (screenHeight >= 1440)
            {
                return 1;
            }

            return 0;
        }

        private void ApplyStatusLayoutProfile(int profileIndex)
        {
            switch (profileIndex)
            {
                case 0: // 1080p
                    Settings.PlayerStatusXOffset = 0;
                    Settings.PlayerStatusYOffset = 30;
                    Settings.PlayerStatusIconGap = 3;
                    Settings.MonsterStatusXOffset = 0;
                    Settings.MonsterStatusYOffset = -34;
                    Settings.MonsterStatusIconGap = 3;
                    break;

                case 2: // 4K
                    Settings.PlayerStatusXOffset = 0;
                    Settings.PlayerStatusYOffset = 52;
                    Settings.PlayerStatusIconGap = 5;
                    Settings.MonsterStatusXOffset = 0;
                    Settings.MonsterStatusYOffset = -58;
                    Settings.MonsterStatusIconGap = 5;
                    break;

                default: // 1440p
                    Settings.PlayerStatusXOffset = 0;
                    Settings.PlayerStatusYOffset = 38;
                    Settings.PlayerStatusIconGap = 4;
                    Settings.MonsterStatusXOffset = 0;
                    Settings.MonsterStatusYOffset = -42;
                    Settings.MonsterStatusIconGap = 4;
                    break;
            }
        }

        private void DrawIconPreview(StatusEffectSettings statusEffect, string id)
        {
            if (!TryLoadStatusIconsTexture())
            {
                ImGui.Text("N/A");
                return;
            }

            var (uv0, uv1) = GetIconUV(statusEffect.IconColumn, statusEffect.IconRow);
            var previewSize = new Vector2(18, 18);
            ImGui.Image(StatusIconsTexturePtr, previewSize, uv0, uv1);
            Tooltip($"Col: {statusEffect.IconColumn}, Row: {statusEffect.IconRow}");
        }

        private void DrawIconPickerPopup(string key, StatusEffectSettings statusEffect)
        {
            if (!IconPickerState.TryGetValue(key, out var opened) || !opened)
            {
                return;
            }

            if (!TryLoadStatusIconsTexture())
            {
                IconPickerState[key] = false;
                return;
            }

            var shouldStayOpen = true;
            if (ImGui.Begin($"Icon Picker##{key}", ref shouldStayOpen, ImGuiWindowFlags.AlwaysHorizontalScrollbar | ImGuiWindowFlags.NoSavedSettings))
            {
                var imageStart = ImGui.GetCursorScreenPos();
                var imageSize = new Vector2(StatusIconsTextureWidth, StatusIconsTextureHeight);
                ImGui.Image(StatusIconsTexturePtr, imageSize);

                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var clickPos = ImGui.GetIO().MouseClickedPos[0] - imageStart;
                    var maxColumns = Math.Max(1, (int)(StatusIconsTextureWidth / IconTileSize));
                    var maxRows = Math.Max(1, (int)(StatusIconsTextureHeight / IconTileSize));

                    var selectedColumn = (int)(clickPos.X / IconTileSize);
                    var selectedRow = (int)(clickPos.Y / IconTileSize);

                    statusEffect.IconColumn = Math.Clamp(selectedColumn, 0, maxColumns - 1);
                    statusEffect.IconRow = Math.Clamp(selectedRow, 0, maxRows - 1);

                    IconPickerState[key] = false;
                }

                ImGui.Text("Click icon to select");
            }

            ImGui.End();

            if (!shouldStayOpen)
            {
                IconPickerState[key] = false;
            }
        }

        private (Vector2 uv0, Vector2 uv1) GetIconUV(int iconColumn, int iconRow)
        {
            var iconWidthUv = IconTileSize / StatusIconsTextureWidth;
            var iconHeightUv = IconTileSize / StatusIconsTextureHeight;

            var clampedColumn = Math.Max(0, iconColumn);
            var clampedRow = Math.Max(0, iconRow);

            var uv0 = new Vector2(clampedColumn * iconWidthUv, clampedRow * iconHeightUv);
            var uv1 = new Vector2(uv0.X + iconWidthUv, uv0.Y + iconHeightUv);
            return (uv0, uv1);
        }
    }
}
