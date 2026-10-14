using System;
using System.Text.Json;
using CoreHost.Config;
using DomainCore;

namespace CoreHost
{
    /// <summary>
    /// Domain Core の結果を、Protocol.md の形の JSON に書く（HOST_STATE・TURN・HOST_EVALUATION）。
    /// 値は Domain Core の結果をそのまま写す。計算はしない。プロパティ名は ProtocolNames から取る。
    /// </summary>
    internal static class ProtocolWriter
    {
        /// <summary>状態（HOST_STATE）を、name という名前のオブジェクトとして書く。</summary>
        public static void WriteState(Utf8JsonWriter json, string name, PlayerState state)
        {
            json.WriteStartObject(name);
            json.WriteNumber(ProtocolNames.Age, state.Age);
            json.WriteNumber(ProtocolNames.IncomeYen, state.IncomeYen);
            json.WriteNumber(ProtocolNames.SavingsYen, state.SavingsYen);
            json.WriteNumber(ProtocolNames.InvestmentYen, state.InvestmentYen);
            json.WriteNumber(ProtocolNames.Satisfaction, state.Satisfaction);
            json.WriteNumber(ProtocolNames.PriceIndexBp, state.PriceIndexBp);
            json.WriteString(ProtocolNames.Status, StatusName(state.Status));
            json.WriteBoolean(ProtocolNames.Struggling, state.Status == PlayerStatus.Struggling);
            json.WriteNumber(ProtocolNames.RaiseTotalYen, state.RaiseTotalYen);
            json.WriteStartArray(ProtocolNames.OccurredOnceEvents);
            foreach (var code in state.OccurredOnceEventCodes)
            {
                json.WriteStringValue(code);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        /// <summary>1 年の結果（TURN）を書く。turnNo は 1 始まり。</summary>
        public static void WriteTurn(Utf8JsonWriter json, int turnNo, YearResult year)
        {
            json.WriteStartObject();
            json.WriteNumber(ProtocolNames.TurnNo, turnNo);
            json.WriteNumber(ProtocolNames.Age, year.Age);
            json.WriteNumber(ProtocolNames.LivingPct, year.Allocation.LivingPct);
            json.WriteNumber(ProtocolNames.SavingPct, year.Allocation.SavingPct);
            json.WriteNumber(ProtocolNames.InvestPct, year.Allocation.InvestPct);
            if (year.Event.HasEvent)
            {
                json.WriteString(ProtocolNames.EventCode, year.Event.Event.Code);
            }
            else
            {
                json.WriteNull(ProtocolNames.EventCode);
            }

            json.WriteNumber(ProtocolNames.SavingsAfter, year.Next.SavingsYen);
            json.WriteNumber(ProtocolNames.InvestAfter, year.Next.InvestmentYen);
            json.WriteNumber(ProtocolNames.SatisfactionAfter, year.Next.Satisfaction);
            json.WriteEndObject();
        }

        /// <summary>最終評価（HOST_EVALUATION）を、name という名前のオブジェクトとして書く。</summary>
        public static void WriteEvaluation(Utf8JsonWriter json, string name, Evaluation evaluation)
        {
            json.WriteStartObject(name);
            json.WriteNumber(ProtocolNames.TotalAssetsYen, evaluation.TotalAssetsYen);
            json.WriteNumber(ProtocolNames.AverageSatisfaction, evaluation.AverageSatisfaction);
            json.WriteBoolean(ProtocolNames.IsBankrupt, evaluation.IsBankrupt);
            json.WriteNumber(ProtocolNames.StruggleYears, evaluation.StruggleYears);
            json.WriteNumber(ProtocolNames.InvestmentSoldYears, evaluation.InvestmentSoldYears);
            json.WriteString(ProtocolNames.Rank, evaluation.Rank.Code);
            json.WriteStartObject(ProtocolNames.Comments);
            json.WriteString(ProtocolNames.RetirementComment, evaluation.RetirementComment);
            json.WriteString(ProtocolNames.QualityComment, evaluation.QualityComment);
            json.WriteString(ProtocolNames.PreparednessComment, evaluation.PreparednessComment);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        // 保存と応答の状態は 3 つ（playing・completed・bankrupt）。生活困窮は playing と struggling で表す
        private static string StatusName(PlayerStatus status)
        {
            switch (status)
            {
                case PlayerStatus.Playing:
                case PlayerStatus.Struggling:
                    return ProtocolNames.StatusPlaying;
                case PlayerStatus.Completed:
                    return ProtocolNames.StatusCompleted;
                case PlayerStatus.Bankrupt:
                    return ProtocolNames.StatusBankrupt;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
    }
}
