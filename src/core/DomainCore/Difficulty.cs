namespace DomainCore
{
    /// <summary>
    /// 難易度（requirements.md 3.2：市場の振れ幅）。値は Config の DifficultyTable にある架空のモデル値。
    /// リターンは万分率（10000 で 100%）の整数で持つ。
    /// </summary>
    public sealed class Difficulty
    {
        public Difficulty(string code, string label, int meanReturnBp, int volatilityBp)
        {
            Code = code;
            Label = label;
            MeanReturnBp = meanReturnBp;
            VolatilityBp = volatilityBp;
        }

        /// <summary>コード（easy・normal・hard）。保存 API と再計算ホストはこの文字列で受け渡す。</summary>
        public string Code { get; }

        /// <summary>画面に出す名前。</summary>
        public string Label { get; }

        /// <summary>市場の年間リターンの平均（万分率）。</summary>
        public int MeanReturnBp { get; }

        /// <summary>市場の年間リターンの振れ幅（万分率）。</summary>
        public int VolatilityBp { get; }
    }
}
