// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
using System;
using System.Collections.ObjectModel;

namespace DomainCore.Config
{
    /// <summary>
    /// ゲームの規則の数値（requirements.md 3.1・3.3・4.1・5章）。
    /// Unity と再計算ホストは、これらの数値を自分で持たずにここを参照する。保存 API（Rails）は規則を持たず、再計算ホストの結果を使う。
    /// </summary>
    public static class GameRules
    {
        /// <summary>開始の年齢。</summary>
        public const int StartAge = 22;

        /// <summary>終了の年齢。この年齢に達したら完走。</summary>
        public const int FinalAge = 65;

        /// <summary>配分の合計（%）。</summary>
        public const int AllocationTotalPct = 100;

        /// <summary>配分の刻み（%）。</summary>
        public const int AllocationStepPct = 5;

        /// <summary>万分率の分母。リターンと確率は、この値で 100% を表す整数で持つ。</summary>
        public const int BasisPointsPerUnit = 10000;

        /// <summary>生活満足度の下限。</summary>
        public const int SatisfactionMin = 0;

        /// <summary>生活満足度の上限。</summary>
        public const int SatisfactionMax = 100;

        /// <summary>生活満足度の初期値。</summary>
        public const int InitialSatisfaction = 60;

        /// <summary>生活困窮の閾値。満足度がこれ未満の年は「生活困窮」（requirements.md 4.3-8）。</summary>
        public const int StruggleSatisfactionThreshold = 20;

        /// <summary>満足度に効かせる生活費の充足の上限（万分率。15000 は必要生活費の 1.5 倍）。これを超える余剰は満足度に効かない（4.3-2）。</summary>
        public const int SatisfactionLivingCapBp = 15000;

        /// <summary>充足の差 10% あたりの満足度の増減（点）。</summary>
        public const int SatisfactionPointsPerTenPercent = 2;

        /// <summary>10% を表す万分率。</summary>
        public const int BasisPointsPerTenPercent = 1000;

        /// <summary>貯蓄の固定金利（万分率。年率）。元本保証・低金利（requirements.md 3.3）。</summary>
        public const int SavingsInterestBp = 10;

        /// <summary>物価係数の 1 年あたりの上げ幅（万分率）。わずかに上げる（requirements.md 4.3-7）。</summary>
        public const int InflationBp = 100;

        /// <summary>物価係数の初期値（万分率。10000 は 1.0 倍）。</summary>
        public const int InitialPriceIndexBp = BasisPointsPerUnit;

        /// <summary>老後資金の目安額（円）。評価の老後資金の観点で総資産と比べる（requirements.md 4.4-2）。</summary>
        public const long RetirementTargetYen = 30_000_000;

        /// <summary>評価の生活の質で「高い」とみなす平均満足度の下限。</summary>
        public const int QualityHighMinSatisfaction = 60;

        /// <summary>評価の生活の質で「ふつう」とみなす平均満足度の下限。これ未満は「低め」。</summary>
        public const int QualityMiddleMinSatisfaction = 40;

        /// <summary>
        /// 自動調整（AllocationAdjuster）で、まだ動かしていない項目の順序（動かした時期が古い順）。
        /// 端数は、残り 2 本のうち先に並ぶ方が吸収する。初めは貯蓄が吸収する（requirements.md 4.3-1 の「端数は貯蓄枠へ」と揃える）。
        /// </summary>
        public static readonly ReadOnlyCollection<AllocationItem> AdjusterInitialAbsorbOrder =
            Array.AsReadOnly(new[] { AllocationItem.Saving, AllocationItem.Living, AllocationItem.Invest });
    }
}
