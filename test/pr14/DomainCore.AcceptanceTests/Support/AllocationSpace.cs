using System.Collections.Generic;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// 配分の値の範囲（requirements.md 3.1・4.1：各項目は 0〜100 の 5 刻み、合計 100）。テストの入力を作る。
    /// </summary>
    internal static class AllocationSpace
    {
        public const int Step = 5;
        public const int Total = 100;

        /// <summary>正しい配分の数。5 刻みの単位で a + b + c = 20（各 0 以上）の解の数で、22 個から 2 個を選ぶ組み合わせの数に等しい。</summary>
        public const int ValidAllocationCount = 231;

        /// <summary>スライダー 1 本が取りうる値の数（0〜100 の 5 刻み）。</summary>
        public const int SliderValueCount = 21;

        public static AllocationItem[] Items() => new[] { AllocationItem.Living, AllocationItem.Saving, AllocationItem.Invest };

        public static IEnumerable<Allocation> AllValid()
        {
            for (var living = 0; living <= Total; living += Step)
            {
                for (var saving = 0; saving <= Total - living; saving += Step)
                {
                    yield return new Allocation(living, saving, Total - living - saving);
                }
            }
        }

        public static IEnumerable<int> SliderValues()
        {
            for (var value = 0; value <= Total; value += Step)
            {
                yield return value;
            }
        }

        public static string Describe(Allocation allocation) =>
            $"{allocation.LivingPct}/{allocation.SavingPct}/{allocation.InvestPct}";
    }
}
