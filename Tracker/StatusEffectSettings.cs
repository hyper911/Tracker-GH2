using System.Numerics;

namespace Tracker
{
    public class StatusEffectSettings(bool isEnabled, string name, string displayName, Vector4 textcolor, Vector4 barColor, int iconColumn = 0, int iconRow = 0, float iconScale = 1.0f)
    {
        public string Name = name;
        public string DisplayName = displayName;
        public Vector4 TextColor = textcolor;
        public Vector4 BarColor = barColor;
        public bool IsEnabled = isEnabled;
        public int IconColumn = iconColumn;
        public int IconRow = iconRow;
        public float IconScale = iconScale;
    }
}
