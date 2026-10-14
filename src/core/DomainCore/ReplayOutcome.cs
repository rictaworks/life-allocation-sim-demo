using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 再計算が成功したときの結果：各年の結果、最終状態、（終了していれば）最終評価。
    /// 評価は、ゲームが完走か破綻で終わったときだけある。終わっていないときに Evaluation を読むと InvalidOperationException。
    /// </summary>
    public sealed class ReplayOutcome
    {
        private readonly Evaluation evaluation;

        internal ReplayOutcome(IEnumerable<YearResult> years, PlayerState finalState, Evaluation evaluation)
        {
            Years = new ReadOnlyCollection<YearResult>(new List<YearResult>(years));
            FinalState = finalState;
            this.evaluation = evaluation;
        }

        /// <summary>各年の精算の結果（1 年目から順に）。</summary>
        public IReadOnlyList<YearResult> Years { get; }

        /// <summary>最後の状態。</summary>
        public PlayerState FinalState { get; }

        /// <summary>完走か破綻で、ゲームが終了していれば true。</summary>
        public bool IsFinished => FinalState.IsFinished;

        /// <summary>最終評価。終了していなければ InvalidOperationException（IsFinished を確かめてから読む）。</summary>
        public Evaluation Evaluation => evaluation ?? throw new InvalidOperationException(ErrorMessages.OutcomeNotFinished);
    }
}
