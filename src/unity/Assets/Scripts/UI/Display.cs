using System.Globalization;
using LifeAllocationSim.Config;

namespace LifeAllocationSim.Ui
{
    /// <summary>表示のための書式だけを扱う（金額の桁区切り・万円・%）。計算の規則は持たない。</summary>
    public static class Display
    {
        public static string Yen(long yen) => yen.ToString(AppConfig.ThousandsFormat, CultureInfo.InvariantCulture);

        public static string ManYen(long yen) => Yen(yen / AppConfig.ManYenUnit);

        public static string Percent(int pct) => pct.ToString(CultureInfo.InvariantCulture);

        public static string PercentFromBasisPoints(int basisPoints) =>
            (basisPoints / (double)AppConfig.BasisPointsPerPercent).ToString(AppConfig.DecimalFormat, CultureInfo.InvariantCulture);

        public static string Format(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
