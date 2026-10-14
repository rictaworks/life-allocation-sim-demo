namespace DomainCore
{
    /// <summary>
    /// 新規ゲームの開始情報：検証済みの難易度と、その初期状態（NewGame.Start の結果）。
    /// 再計算（Replayer）の入力になる。
    /// </summary>
    public sealed class GameStart
    {
        public GameStart(Difficulty difficulty, PlayerState initialState)
        {
            Difficulty = difficulty;
            InitialState = initialState;
        }

        /// <summary>難易度（Config の一覧にあるもの）。</summary>
        public Difficulty Difficulty { get; }

        /// <summary>初期状態（22 歳の初年度）。</summary>
        public PlayerState InitialState { get; }
    }
}
