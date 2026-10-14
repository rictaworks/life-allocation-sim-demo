using System;
using System.Collections.Generic;
using LifeAllocationSim.Config;
using Newtonsoft.Json.Linq;

namespace LifeAllocationSim.Api
{
    /// <summary>
    /// GAME のうち、再計算に使う部分（seed・難易度・配分の履歴）。
    /// サーバーが返す状態の値（state や evaluation）は使わない。状態は Domain Core の再計算だけから求める。
    /// </summary>
    public sealed class GameDto
    {
        public GameDto(long seed, string difficulty, IReadOnlyList<TurnDto> turns)
        {
            Seed = seed;
            Difficulty = difficulty;
            Turns = turns;
        }

        public long Seed { get; }

        public string Difficulty { get; }

        /// <summary>配分の履歴。turn_no の昇順で、1 から連番であること。</summary>
        public IReadOnlyList<TurnDto> Turns { get; }

        public static GameDto FromJson(JObject game)
        {
            var seed = JsonFields.Integer(game, ApiConfig.KeySeed);
            if (seed < 0)
            {
                throw new FormatException(string.Format(LogTexts.FieldInvalid, ApiConfig.KeySeed));
            }

            var difficulty = JsonFields.String(game, ApiConfig.KeyDifficulty);
            var array = JsonFields.Array(game, ApiConfig.KeyTurns);
            var turns = new List<TurnDto>(array.Count);
            for (var index = 0; index < array.Count; index++)
            {
                var turn = TurnDto.FromJson(JsonFields.AsObject(array[index], ApiConfig.KeyTurns));
                if (turn.TurnNo != index + 1)
                {
                    throw new FormatException(string.Format(LogTexts.FieldInvalid, ApiConfig.KeyTurnNo));
                }

                turns.Add(turn);
            }

            return new GameDto(seed, difficulty, turns);
        }
    }

    /// <summary>履歴の 1 ターン分（配分だけを使う）。</summary>
    public sealed class TurnDto
    {
        public TurnDto(int turnNo, int livingPct, int savingPct, int investPct)
        {
            TurnNo = turnNo;
            LivingPct = livingPct;
            SavingPct = savingPct;
            InvestPct = investPct;
        }

        public int TurnNo { get; }

        public int LivingPct { get; }

        public int SavingPct { get; }

        public int InvestPct { get; }

        public static TurnDto FromJson(JObject turn) =>
            new TurnDto(
                JsonFields.Int32(turn, ApiConfig.KeyTurnNo),
                JsonFields.Int32(turn, ApiConfig.KeyLivingPct),
                JsonFields.Int32(turn, ApiConfig.KeySavingPct),
                JsonFields.Int32(turn, ApiConfig.KeyInvestPct));
    }

    /// <summary>保存の応答（TURN）のうち、再計算の結果と照らし合わせる項目。</summary>
    public sealed class TurnResultDto
    {
        private TurnResultDto(int turnNo, long savingsAfter, long investAfter, int satisfactionAfter)
        {
            TurnNo = turnNo;
            SavingsAfter = savingsAfter;
            InvestAfter = investAfter;
            SatisfactionAfter = satisfactionAfter;
        }

        public int TurnNo { get; }

        public long SavingsAfter { get; }

        public long InvestAfter { get; }

        public int SatisfactionAfter { get; }

        public static TurnResultDto FromJson(JObject turn) =>
            new TurnResultDto(
                JsonFields.Int32(turn, ApiConfig.KeyTurnNo),
                JsonFields.Integer(turn, ApiConfig.KeySavingsAfter),
                JsonFields.Integer(turn, ApiConfig.KeyInvestAfter),
                JsonFields.Int32(turn, ApiConfig.KeySatisfactionAfter));
    }
}
