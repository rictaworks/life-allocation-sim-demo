using System;
using System.Collections.Generic;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 1 年の精算（requirements.md 4.3 advanceYear）。処理順は次のとおりで、変えない：
    /// 1 配分・2 生活費判定・3 積立・4 イベント・5 運用・6 取り崩し・7 物価・8 状態判定・9 結果。
    /// - 乱数は 1 年に必ず 2 回使う。先にイベントの抽選（EventDrawer）、次に市場のリターン。
    /// - 配分が不正なら、乱数を使わず、状態も変えずに不正の理由を返す（不正な配分でターンは進まない）。
    /// - 金額は円の整数（円未満は切り捨て、端数は貯蓄枠へ）。投資評価額は 0 未満にしない。
    /// </summary>
    public static class YearSettlement
    {
        /// <summary>1 年を精算する。配分が不正なら、その理由を返す（乱数は使わない）。終了したゲームは InvalidOperationException。</summary>
        public static ValidationResult<YearResult, AllocationViolation> Advance(PlayerState state, Allocation allocation, Difficulty difficulty, SeededRandom random)
        {
            if (state.IsFinished)
            {
                throw new InvalidOperationException(ErrorMessages.FinishedStateCannotAdvance);
            }

            var validation = Simulator.ValidateAllocation(allocation);
            if (!validation.IsValid)
            {
                return ValidationResult<YearResult, AllocationViolation>.Invalid(validation.Reason);
            }

            var draw = EventDrawer.Draw(state, random);
            var drawnReturnBp = DrawReturnBp(difficulty, random);
            return ValidationResult<YearResult, AllocationViolation>.Valid(Settle(state, allocation, difficulty, draw, drawnReturnBp));
        }

        /// <summary>難易度の平均と振れ幅から、年間のリターン（万分率）を一様に決める。振れ幅の両端を含む。乱数は 1 回。</summary>
        internal static int DrawReturnBp(Difficulty difficulty, SeededRandom random)
        {
            var spread = random.NextInt((GameRules.VolatilityEndpointCount * difficulty.VolatilityBp) + 1);
            return difficulty.MeanReturnBp + spread - difficulty.VolatilityBp;
        }

        /// <summary>
        /// 1 年分の処理（乱数を使わない）。イベントと市場のリターンを引数で受け取るので、各ステップを単体で確かめられる。
        /// 配分は検証済みであること（Advance が確かめる）。
        /// </summary>
        internal static YearResult Settle(PlayerState state, Allocation allocation, Difficulty difficulty, EventDraw draw, int drawnReturnBp)
        {
            var bp = GameRules.BasisPointsPerUnit;
            var pctTotal = GameRules.AllocationTotalPct;

            // 1 配分：手取り年収を 3 枠に分ける。円未満は切り捨て、端数は貯蓄枠へ
            var livingBudget = state.IncomeYen * allocation.LivingPct / pctTotal;
            var investFrame = state.IncomeYen * allocation.InvestPct / pctTotal;
            var savingFrame = state.IncomeYen - livingBudget - investFrame;

            // 2 生活費判定：余剰は貯蓄へ、不足は取り崩しへ。満足度は充足の比率で増減する
            var required = LifeStage.ForAge(state.Age).BaseLivingCostYen * state.PriceIndexBp / bp;
            var surplus = Math.Max(0L, livingBudget - required);
            var shortfall = Math.Max(0L, required - livingBudget);
            var satisfaction = ClampSatisfaction(state.Satisfaction + SatisfactionChange(livingBudget, required));

            // 3 積立：貯蓄枠と余剰を貯蓄へ、投資枠を投資へ
            var savings = state.SavingsYen + savingFrame + surplus;
            var investment = state.InvestmentYen + investFrame;

            // 4 イベント：出費は貯蓄から払い、足りない分は取り崩しへ。昇給は次の年の手取り年収に加わる
            var raiseTotal = state.RaiseTotalYen;
            var eventExpense = 0L;
            var eventIncome = 0L;
            var eventShortfall = 0L;
            var hasMarketOverride = false;
            var marketOverrideBp = 0;
            var occurred = new List<string>(state.OccurredOnceEventCodes);
            if (draw.HasEvent)
            {
                var lifeEvent = draw.Event;
                switch (lifeEvent.Effect)
                {
                    case LifeEventEffect.Expense:
                        var paid = Math.Min(savings, lifeEvent.AmountYen);
                        savings -= paid;
                        eventShortfall = lifeEvent.AmountYen - paid;
                        eventExpense = lifeEvent.AmountYen;
                        break;
                    case LifeEventEffect.OneTimeIncome:
                        savings += lifeEvent.AmountYen;
                        eventIncome = lifeEvent.AmountYen;
                        break;
                    case LifeEventEffect.IncomeRaise:
                        raiseTotal += lifeEvent.AmountYen;
                        eventIncome = lifeEvent.AmountYen;
                        break;
                    case LifeEventEffect.MarketReturnOverride:
                        hasMarketOverride = true;
                        marketOverrideBp = lifeEvent.MarketReturnBp;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(draw), lifeEvent.Effect, ErrorMessages.UndefinedLifeEventEffect);
                }

                if (lifeEvent.OccursOnce)
                {
                    occurred.Add(lifeEvent.Code);
                }
            }

            // 5 運用：貯蓄に固定金利。投資は年間リターンで評価し直す（暴落・好況の年はイベントの値）。0 未満にしない
            var interest = savings * GameRules.SavingsInterestBp / bp;
            savings += interest;
            var returnBp = hasMarketOverride ? marketOverrideBp : drawnReturnBp;
            var valuation = Math.Max(0L, investment * (bp + returnBp) / bp);
            var investmentGain = valuation - investment;
            investment = valuation;

            // 6 取り崩し：不足額を ①貯蓄 → ②投資（その年の評価額で売却）の順に充てる
            var withdrawal = shortfall + eventShortfall;
            var fromSavings = Math.Min(savings, withdrawal);
            savings -= fromSavings;
            var remaining = withdrawal - fromSavings;
            var fromInvestment = Math.Min(investment, remaining);
            investment -= fromInvestment;
            var unmet = remaining - fromInvestment;

            // 7 物価：物価係数を毎年わずかに上げる（整数演算）
            var nextPriceIndex = (int)((long)state.PriceIndexBp * (bp + GameRules.InflationBp) / bp);

            // 8 状態判定：取り崩し後も不足が残れば破綻。65 歳に達すれば完走。満足度が閾値未満なら生活困窮
            var nextAge = state.Age + 1;
            var status = StatusAfter(unmet, nextAge, satisfaction);

            // 9 結果
            var nextIncome = LifeStage.ForAge(nextAge).TakeHomeIncomeYen + raiseTotal;
            var next = new PlayerState(nextAge, nextIncome, savings, investment, satisfaction, nextPriceIndex, status, raiseTotal, occurred);

            return new YearResult
            {
                Age = state.Age,
                Allocation = allocation,
                IncomeYen = state.IncomeYen,
                LivingBudgetYen = livingBudget,
                RequiredLivingYen = required,
                SurplusYen = surplus,
                LivingShortfallYen = shortfall,
                SavingDepositYen = savingFrame + surplus,
                InvestDepositYen = investFrame,
                Event = draw,
                EventExpenseYen = eventExpense,
                EventIncomeYen = eventIncome,
                MarketReturnBp = returnBp,
                SavingsInterestYen = interest,
                InvestmentGainYen = investmentGain,
                ShortfallYen = withdrawal,
                WithdrawnFromSavingsYen = fromSavings,
                WithdrawnFromInvestmentYen = fromInvestment,
                UnmetYen = unmet,
                Next = next,
            };
        }

        /// <summary>
        /// 生活費の充足による満足度の増減（点）。充足の比率（生活費枠 ÷ 必要生活費）を、上限（1.5 倍）までで見て、
        /// 100% からの差 10% あたり SatisfactionPointsPerTenPercent 点。差は整数の割り算で、0 に近い方へ切り捨てる。
        /// ただし差が 0 でないのに切り捨てで 0 点になるときは、切り捨て前の差（生活費枠 − 必要生活費）の符号に合わせて 1 点にする（不足は下げ、余剰は上げる。4.3-2）。
        /// 比率を先に切り捨てると、必要額の 0.01% 未満の余剰は比率が 100% のままになるため、符号は切り捨て前の差で決める。
        /// </summary>
        internal static int SatisfactionChange(long livingBudgetYen, long requiredYen)
        {
            var ratioBp = livingBudgetYen * GameRules.BasisPointsPerUnit / requiredYen;
            var cappedBp = Math.Min(ratioBp, (long)GameRules.SatisfactionLivingCapBp);
            var gapBp = cappedBp - GameRules.BasisPointsPerUnit;
            var points = (int)(gapBp * GameRules.SatisfactionPointsPerTenPercent / GameRules.BasisPointsPerTenPercent);
            var difference = livingBudgetYen - requiredYen;
            if (points == 0 && difference != 0)
            {
                return difference > 0 ? 1 : -1;
            }

            return points;
        }

        private static int ClampSatisfaction(int value) =>
            Math.Max(GameRules.SatisfactionMin, Math.Min(GameRules.SatisfactionMax, value));

        private static PlayerStatus StatusAfter(long unmet, int nextAge, int satisfaction)
        {
            if (unmet > 0)
            {
                return PlayerStatus.Bankrupt;
            }

            if (nextAge >= GameRules.FinalAge)
            {
                return PlayerStatus.Completed;
            }

            if (satisfaction < GameRules.StruggleSatisfactionThreshold)
            {
                return PlayerStatus.Struggling;
            }

            return PlayerStatus.Playing;
        }
    }
}
