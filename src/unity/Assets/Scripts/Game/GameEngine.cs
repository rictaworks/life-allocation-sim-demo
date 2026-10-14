using System.Collections.Generic;
using DomainCore;

namespace LifeAllocationSim.Game
{
    /// <summary>
    /// Domain Core（DLL）の呼び出しだけを行う。規則の式はここにも Unity の他の場所にも書かない。
    /// 年の精算・評価・配分の検証・自動調整はすべて DLL が行う。
    /// </summary>
    public static class GameEngine
    {
        /// <summary>難易度のコードから新しいゲームの始まりを得る（NewGame.Start）。</summary>
        public static ValidationResult<GameStart, DifficultyCodeViolation> StartNew(string difficultyCode) =>
            NewGame.Start(difficultyCode);

        /// <summary>配分を検証する（Simulator.ValidateAllocation）。</summary>
        public static ValidationResult<Allocation, AllocationViolation> ValidateAllocation(Allocation allocation) =>
            Simulator.ValidateAllocation(allocation);

        /// <summary>始まり・seed・配分の履歴から状態を再構築する（Replayer.Replay）。年の精算もここで行われる。</summary>
        public static ReplayResult Replay(GameStart start, long seed, IReadOnlyList<Allocation> history) =>
            Replayer.Replay(start, seed, history);

        /// <summary>スライダーの自動調整の器を作る（AllocationAdjuster）。</summary>
        public static AllocationAdjuster CreateAdjuster(Allocation initial) => new AllocationAdjuster(initial);
    }
}
