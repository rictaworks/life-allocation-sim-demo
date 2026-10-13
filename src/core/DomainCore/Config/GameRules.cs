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

        /// <summary>
        /// 自動調整（AllocationAdjuster）で、まだ動かしていない項目の順序（動かした時期が古い順）。
        /// 端数は、残り 2 本のうち先に並ぶ方が吸収する。初めは貯蓄が吸収する（requirements.md 4.3-1 の「端数は貯蓄枠へ」と揃える）。
        /// </summary>
        public static readonly ReadOnlyCollection<AllocationItem> AdjusterInitialAbsorbOrder =
            Array.AsReadOnly(new[] { AllocationItem.Saving, AllocationItem.Living, AllocationItem.Invest });
    }
}
