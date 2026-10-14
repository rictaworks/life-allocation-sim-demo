using System.Linq;
using DomainCore.Config;

namespace DomainCore.Tests.Support
{
    /// <summary>
    /// テストで使う状態・イベント・難易度の見本。値は Config の架空のモデル値を、コードや名前で引く。
    /// 既定値は 22 歳の初年度（手取り年収 2,800,000 円・満足度 60・物価係数 1.0）。
    /// </summary>
    internal static class GameFixtures
    {
        public static PlayerState State(
            int age = 22,
            long incomeYen = 2_800_000,
            long savingsYen = 0,
            long investmentYen = 0,
            int satisfaction = 60,
            int priceIndexBp = 10000,
            PlayerStatus status = PlayerStatus.Playing,
            long raiseTotalYen = 0,
            string[]? occurredOnce = null)
        {
            return new PlayerState(
                age,
                incomeYen,
                savingsYen,
                investmentYen,
                satisfaction,
                priceIndexBp,
                status,
                raiseTotalYen,
                occurredOnce ?? new string[0]);
        }

        public static LifeEvent EventNamed(string code) => LifeEventTable.All.Single(lifeEvent => lifeEvent.Code == code);

        public static Difficulty DifficultyNamed(string code) => DifficultyTable.All.Single(difficulty => difficulty.Code == code);
    }
}
