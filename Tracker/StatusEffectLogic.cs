using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameHelper.Utils;
using GameOffsets.Objects.States.InGameState;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Tracker
{
    public class StatusEffectLogic(TrackerSettings settings)
    {
        private const float IconTileSize = 32f;
        private TrackerSettings Settings { get; } = settings;
        private IntPtr StatusIconsTexturePtr { get; set; } = IntPtr.Zero;
        private float StatusIconsTextureWidth { get; set; }
        private float StatusIconsTextureHeight { get; set; }
        private string StatusIconsPath => Path.Join(AppContext.BaseDirectory, "Plugins", "Tracker", "status_icons.png");

        public void Draw()
        {
            if (!TryLoadIconsTexture())
            {
                return;
            }

            void DrawForEntity(Render entityRender, Buffs entityBuffs, List<StatusEffectSettings> effectsList, float horizontalOffset, float verticalOffset, float iconGap)
            {
                var effects = effectsList.Where(effect => effect.IsEnabled && entityBuffs.StatusEffects.ContainsKey(effect.Name)).ToList();
                if (!effects.Any())
                {
                    return;
                }

                var drawList = ImGui.GetBackgroundDrawList();
                var entitylocation = Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(entityRender.WorldPosition);
                entitylocation.X += horizontalOffset;
                entitylocation.Y += verticalOffset;

                var totalWidth = 0f;
                var scaledSizes = new List<float>(effects.Count);
                for (var i = 0; i < effects.Count; i++)
                {
                    var scaledIconSize = Math.Max(8f, IconTileSize * Math.Max(0.2f, effects[i].IconScale));
                    scaledSizes.Add(scaledIconSize);
                    totalWidth += scaledIconSize;
                    if (i < effects.Count - 1)
                    {
                        totalWidth += iconGap;
                    }
                }

                var startX = entitylocation.X - (totalWidth * 0.5f);
                var startY = entitylocation.Y;

                var shadowAlpha = Math.Clamp(Settings.TextShadowAlpha, 0.0f, 1.0f);
                var shadowSize = Math.Clamp(Settings.TextShadowSize, 0, 2);
                var shadowColor = ImGuiHelper.Color(new Vector4(0, 0, 0, shadowAlpha));
                var currentPos = new Vector2(startX, startY);

                for (var i = 0; i < effects.Count; i++)
                {
                    var effect = effects[i];
                    var statusEffect = entityBuffs.StatusEffects[effect.Name];

                    var scaledIconSize = scaledSizes[i];
                    var iconSize = new Vector2(scaledIconSize, scaledIconSize);
                    var iconEnd = currentPos + iconSize;

                    // Draw circular background (structure 1)
                    var center = currentPos + (iconSize * 0.5f);
                    var radius = Math.Min(iconSize.X, iconSize.Y) * 0.5f;
                    drawList.AddCircleFilled(center, radius, ImGuiHelper.Color(effect.BarColor));

                    // Draw icon slightly smaller (10% smaller) so background is visible (structure 2)
                    var pad = iconSize * 0.05f; // 5% padding each side -> icon becomes ~90% size
                    var imgPos0 = currentPos + pad;
                    var imgPos1 = iconEnd - pad;
                    var (uv0, uv1) = GetIconUV(effect.IconColumn, effect.IconRow);
                    drawList.AddImage(StatusIconsTexturePtr, imgPos0, imgPos1, uv0, uv1);

                    // Charges (structure 3): draw above the icon with background taken from structure 1 (effect.BarColor)
                    var charges = GetCharges(statusEffect);
                    if (charges > 0)
                    {
                        var chargesText = charges.ToString();
                        var chargesSize = ImGui.CalcTextSize(chargesText);
                        var chargesPadding = new Vector2(4, 2);
                        // Shift charges down by ~12% of icon height and allow user offsets
                        var baseChargesDown = iconSize.Y * 0.12f;
                        var chargesOffsetX = Settings.ChargesXOffset;
                        var chargesOffsetY = Settings.ChargesYOffset;
                        // Make background width adapt to the charges text width + padding
                        var bgWidth = chargesSize.X + chargesPadding.X * 2f;
                        var chargesBgMin = new Vector2(center.X - (bgWidth * 0.5f) + chargesOffsetX, currentPos.Y - chargesSize.Y - chargesPadding.Y - 2 + baseChargesDown + chargesOffsetY);
                        var chargesBgMax = new Vector2(center.X + (bgWidth * 0.5f) + chargesOffsetX, currentPos.Y - 2 + baseChargesDown + chargesOffsetY);
                        drawList.AddRectFilled(chargesBgMin, chargesBgMax, ImGuiHelper.Color(effect.BarColor));
                        var chargesPos = new Vector2(center.X - (chargesSize.X * 0.5f) + chargesOffsetX, chargesBgMin.Y + 1);
                        DrawTextWithShadow(drawList, chargesPos, shadowColor, shadowSize, chargesText);
                        drawList.AddText(chargesPos, ImGuiHelper.Color(effect.TextColor), chargesText);
                    }

                    // Timer (structure 4): draw under the icon
                    if (statusEffect.TimeLeft > 0)
                    {
                        var timerText = ToTimerText(statusEffect.TimeLeft);
                        var timerSize = ImGui.CalcTextSize(timerText);

                        // Bar width is 10% less than full icon width and height is 10% larger than timer text
                        var barWidth = iconSize.X * 0.9f;
                        var barHeight = timerSize.Y * 1.1f;
                        var barStartX = currentPos.X + (iconSize.X - barWidth) * 0.5f + Settings.TimerXOffset;
                        var barY = iconEnd.Y + 2 + Settings.TimerYOffset;
                        var barMin = new Vector2(barStartX, barY);
                        var barMax = new Vector2(barStartX + barWidth, barY + barHeight);

                        // background for the timer (same size as bar)
                        drawList.AddRectFilled(barMin, barMax, ImGuiHelper.Color(Settings.StatusBarBackgroundColor));

                        // compute proportion using total duration if available
                        var total = GetTotalDuration(statusEffect);
                        var proportion = 1.0f;
                        if (total > 0.0001f)
                        {
                            proportion = Math.Clamp(statusEffect.TimeLeft / total, 0.0f, 1.0f);
                        }

                        var filledMaxX = barStartX + (barWidth * proportion);
                        var filledMin = barMin;
                        var filledMax = new Vector2(filledMaxX, barMax.Y);
                        // draw filled portion first so timer text will be on top
                        drawList.AddRectFilled(filledMin, filledMax, ImGuiHelper.Color(effect.BarColor));

                        // center timer text inside the bar (draw after filled part so it's on top)
                        var timerPos = new Vector2(barStartX + (barWidth - timerSize.X) * 0.5f, barY + (barHeight - timerSize.Y) * 0.5f);
                        DrawTextWithShadow(drawList, timerPos, shadowColor, shadowSize, timerText);
                        drawList.AddText(timerPos, ImGuiHelper.Color(effect.TextColor), timerText);
                    }

                    currentPos.X += iconSize.X + iconGap;
                }
            }

            if (Settings.ShowPlayerStatusEffects)
            {
                var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
                if (player != null && player.IsValid)
                {
                    if (player.TryGetComponent<Render>(out var playerRender) && player.TryGetComponent<Buffs>(out var playerBuffs))
                    {
                        DrawForEntity(
                            playerRender,
                            playerBuffs,
                            Settings.PlayerStatusEffects,
                            Settings.PlayerStatusXOffset,
                            Settings.PlayerStatusYOffset,
                            Settings.PlayerStatusIconGap);
                    }
                }
            }

            foreach (var entity in GetMonsters())
            {
                if (!entity.TryGetComponent<Render>(out var entityRender)) continue;
                if (!entity.TryGetComponent<Buffs>(out var entityBuffs)) continue;

                DrawForEntity(
                    entityRender,
                    entityBuffs,
                    Settings.StatusEffects,
                    Settings.MonsterStatusXOffset,
                    Settings.MonsterStatusYOffset,
                    Settings.MonsterStatusIconGap);
            }
        }

        private bool TryLoadIconsTexture()
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

        private static string ToTimerText(float timeLeft)
        {
            var seconds = Math.Max(0, (int)Math.Floor(timeLeft));
            var minutes = seconds / 60;
            var restSeconds = seconds % 60;
            return $"{minutes}:{restSeconds:00}";
        }

        private static void DrawTextWithShadow(ImDrawListPtr drawList, Vector2 textPos, uint shadowColor, int shadowSize, string text)
        {
            if (shadowSize <= 0)
            {
                return;
            }

            drawList.AddText(new Vector2(textPos.X + shadowSize, textPos.Y), shadowColor, text);
            drawList.AddText(new Vector2(textPos.X - shadowSize, textPos.Y), shadowColor, text);
            drawList.AddText(new Vector2(textPos.X, textPos.Y + shadowSize), shadowColor, text);
            drawList.AddText(new Vector2(textPos.X, textPos.Y - shadowSize), shadowColor, text);
        }

        private static int GetCharges(object statusEffect)
        {
            var type = statusEffect.GetType();
            var memberNames = new[] { "Charges", "Charge", "Stacks", "StackCount" };

            foreach (var memberName in memberNames)
            {
                var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(statusEffect);
                    if (value == null) return 0;

                    try { return Convert.ToInt32(value); }
                    catch { return 0; }
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(statusEffect);
                    if (value == null) return 0;

                    try { return Convert.ToInt32(value); }
                    catch { return 0; }
                }
            }

            return 0;
        }

        private static float GetTotalDuration(object statusEffect)
        {
            var type = statusEffect.GetType();
            var memberNames = new[] { "Duration", "Time", "MaxTime", "TotalTime", "TimeTotal", "Length" };

            foreach (var memberName in memberNames)
            {
                var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(statusEffect);
                    if (value == null) continue;
                    try { return Convert.ToSingle(value); }
                    catch { continue; }
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(statusEffect);
                    if (value == null) continue;
                    try { return Convert.ToSingle(value); }
                    catch { continue; }
                }
            }

            return 0f;
        }

        private IEnumerable<Entity> GetMonsters()
        {
            var areaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            var monsters = areaInstance.AwakeEntities.Where(IsValidMonster).Select(entity => entity.Value);
            return monsters.Where(entity => entity.TryGetComponent<ObjectMagicProperties>(out var comp) && (comp.Rarity != Rarity.Normal && comp.Rarity != Rarity.Magic));
        }

        private bool IsValidMonster(KeyValuePair<EntityNodeKey, Entity> entity)
        {
            return
                entity.Value.IsValid &&
                entity.Value.EntityState == EntityStates.None &&
                entity.Value.EntityType == EntityTypes.Monster;
        }
    }
}
