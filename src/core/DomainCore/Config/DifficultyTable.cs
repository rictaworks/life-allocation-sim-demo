// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
using System;
using System.Collections.ObjectModel;

namespace DomainCore.Config
{
    /// <summary>
    /// 難易度 3 段階（requirements.md 3.2：市場の振れ幅）。リターンは万分率（10000 で 100%）の整数。
    /// 市場の平均はどれも同じにし、振れ幅だけを変える。コードは保存 API と再計算ホストが受け渡す文字列。
    /// </summary>
    public static class DifficultyTable
    {
        public static readonly ReadOnlyCollection<Difficulty> All = Array.AsReadOnly(new[]
        {
            new Difficulty(code: "easy", label: "やさしい", meanReturnBp: 400, volatilityBp: 500),
            new Difficulty(code: "normal", label: "ふつう", meanReturnBp: 400, volatilityBp: 1200),
            new Difficulty(code: "hard", label: "むずかしい", meanReturnBp: 400, volatilityBp: 2000),
        });
    }
}
