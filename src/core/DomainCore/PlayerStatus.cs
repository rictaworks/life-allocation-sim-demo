namespace DomainCore
{
    /// <summary>
    /// プレイヤーの状態（requirements.md 3.3・11 章）。プレイ中と生活困窮は続く状態、破綻と完走は終了の状態。
    /// </summary>
    public enum PlayerStatus
    {
        /// <summary>プレイ中。</summary>
        Playing = 1,

        /// <summary>生活困窮（満足度が閾値未満。警告表示。翌年以降に回復できる）。</summary>
        Struggling = 2,

        /// <summary>破綻（取り崩し後も不足が残った。即終了）。</summary>
        Bankrupt = 3,

        /// <summary>完走（65 歳に達した）。</summary>
        Completed = 4,
    }
}
