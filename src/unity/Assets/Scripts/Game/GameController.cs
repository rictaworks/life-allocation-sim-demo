using System;
using System.Collections.Generic;
using DomainCore;
using LifeAllocationSim.Api;
using LifeAllocationSim.Config;
using LifeAllocationSim.Ui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LifeAllocationSim.Game
{
    /// <summary>
    /// 画面の流れと保存 API の呼び出しを組み立てる。計算は Domain Core に任せ、ここでは規則を持たない。
    /// 通信の間は覆いを出し、同時に 2 つの要求は出さない（覆いが操作を止める）。
    /// </summary>
    public sealed class GameController
    {
        private readonly ApiClient api;
        private readonly ScreenHost host;
        private GameSession session;
        private bool hasSavedGame;

        public GameController(ApiClient api, ScreenHost host)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>起動：セッションを確認し、保存されたゲームの有無を見てタイトルを出す。</summary>
        public void Begin() => Boot();

        private void Boot() => Exchange(api.PostSession, _ => LoadStatus(), Boot);

        private void LoadStatus()
        {
            Exchange(api.GetGame, response =>
            {
                var game = JsonFields.Token(response.ParseBody(), ApiConfig.KeyGame);
                hasSavedGame = game.Type == JTokenType.Object;
                ShowTitle(null);
            }, LoadStatus);
        }

        /// <summary>タイトルの「新規開始」。保存されたゲームがあれば、上書きの確認を出す。</summary>
        public void RequestNewGame(string difficultyCode)
        {
            if (hasSavedGame)
            {
                ShowOverwriteConfirm(difficultyCode);
                return;
            }

            StartNewGame(difficultyCode);
        }

        /// <summary>タイトルの「続きから」。最新の保存を読み直して再計算し、続きの画面を出す。</summary>
        public void ResumeGame()
        {
            Exchange(api.GetGame, response =>
            {
                var game = JsonFields.Token(response.ParseBody(), ApiConfig.KeyGame);
                if (game.Type == JTokenType.Null)
                {
                    hasSavedGame = false;
                    session = null;
                    ShowTitle(UiTexts.ResumeMissingNotice);
                    return;
                }

                if (!Restore(GameDto.FromJson(JsonFields.AsObject(game, ApiConfig.KeyGame))))
                {
                    return;
                }

                hasSavedGame = true;
                ShowCurrent();
            }, ResumeGame);
        }

        private void ShowOverwriteConfirm(string difficultyCode)
        {
            new MessageScreen(host).Show(
                UiTexts.OverwriteHeading,
                UiTexts.OverwriteBody,
                new[]
                {
                    new MessageAction(UiTexts.OverwriteYes, () => StartNewGame(difficultyCode)),
                    new MessageAction(UiTexts.OverwriteNo, () => ShowTitle(null)),
                });
        }

        private void StartNewGame(string difficultyCode)
        {
            Exchange(done => api.PostGame(difficultyCode, done), response =>
            {
                var game = JsonFields.Object(response.ParseBody(), ApiConfig.KeyGame);
                if (!Restore(GameDto.FromJson(game)))
                {
                    return;
                }

                hasSavedGame = true;
                ShowCurrent();
            }, () => StartNewGame(difficultyCode));
        }

        /// <summary>メインの「次の年へ」。まず Domain Core で結果を求め、保存が同じ結果を返したときだけ進める。</summary>
        private void AdvanceYear(Allocation allocation)
        {
            if (session == null)
            {
                throw new InvalidOperationException(LogTexts.SessionMissing);
            }

            if (!GameEngine.ValidateAllocation(allocation).IsValid)
            {
                ShowError(UiTexts.ErrorAllocation, null, null);
                return;
            }

            var preview = session.PreviewNext(allocation);
            if (!preview.IsValid)
            {
                Debug.LogWarning(LogTexts.ReplayRejected);
                ShowError(
                    preview.Failure.Violation == ReplayViolation.GameFinished ? UiTexts.ErrorFinished : UiTexts.ErrorAllocation,
                    null,
                    null);
                return;
            }

            var turnNo = session.NextTurnNo;
            Exchange(done => api.PostTurn(turnNo, allocation, done), response =>
            {
                var turn = TurnResultDto.FromJson(JsonFields.Object(response.ParseBody(), ApiConfig.KeyTurn));
                var next = preview.Outcome.FinalState;
                if (!Matches(turn, turnNo, next))
                {
                    Debug.LogWarning(string.Format(LogTexts.ResultMismatch, turnNo));
                    ShowError(UiTexts.ErrorMismatch, ResumeGame, UiTexts.Reload);
                    return;
                }

                var years = preview.Outcome.Years;
                session.Commit(allocation, preview.Outcome);
                ShowSettlement(years[years.Count - 1]);
            }, () => AdvanceYear(allocation), inGame: true);
        }

        private static bool Matches(TurnResultDto turn, int turnNo, PlayerState next) =>
            turn.TurnNo == turnNo
            && turn.SavingsAfter == next.SavingsYen
            && turn.InvestAfter == next.InvestmentYen
            && turn.SatisfactionAfter == next.Satisfaction;

        private void ShowSettlement(YearResult year)
        {
            if (year.Event.HasEvent)
            {
                new EventScreen(host).Show(year, () => ShowYearResult(year));
                return;
            }

            ShowYearResult(year);
        }

        private void ShowYearResult(YearResult year) => new YearResultScreen(host).Show(year, ShowCurrent);

        private bool Restore(GameDto dto)
        {
            var history = new List<Allocation>(dto.Turns.Count);
            foreach (var turn in dto.Turns)
            {
                history.Add(new Allocation(turn.LivingPct, turn.SavingPct, turn.InvestPct));
            }

            if (GameSession.TryRestore(dto.Difficulty, dto.Seed, history, out var restored))
            {
                session = restored;
                return true;
            }

            Debug.LogWarning(LogTexts.ReplayRejected);
            ShowError(UiTexts.ErrorDataUnreadable, null, null);
            return false;
        }

        private void ShowCurrent()
        {
            if (session == null)
            {
                throw new InvalidOperationException(LogTexts.SessionMissing);
            }

            if (session.IsFinished)
            {
                new FinalScreen(host).Show(session, BackToTitle);
                return;
            }

            new MainScreen(host).Show(session, AdvanceYear, BackToTitle);
        }

        private void ShowTitle(string notice) => new TitleScreen(host).Show(hasSavedGame, notice, RequestNewGame, ResumeGame);

        private void BackToTitle() => ShowTitle(null);

        private void ResetToTitle(string notice)
        {
            hasSavedGame = false;
            session = null;
            ShowTitle(notice);
        }

        /// <summary>保存 API の失敗を、code と状態コードに応じた文言にする。内部の詳細は出さない。</summary>
        private void HandleFailure(ApiResponse response, Action retry, bool inGame)
        {
            if (response.NetworkError)
            {
                Debug.LogWarning(LogTexts.ApiNetworkFailure);
                ShowError(UiTexts.ErrorNetwork, retry, UiTexts.Retry);
                return;
            }

            var code = response.ErrorCode ?? string.Empty;
            Debug.LogWarning(string.Format(LogTexts.ApiErrorResponse, response.StatusCode, code));
            // 404 と、ゲームの途中で session が無くなった 401 は、データがリセットされたものとして新規開始へ誘導する
            if (code == ApiConfig.ErrorGameNotFound || (inGame && code == ApiConfig.ErrorSessionRequired))
            {
                ResetToTitle(UiTexts.ResetNotice);
                return;
            }

            if (response.StatusCode == ApiConfig.StatusTooManyRequests || code == ApiConfig.ErrorRateLimited)
            {
                ShowError(UiTexts.ErrorRateLimited, retry, UiTexts.Retry);
                return;
            }

            if (response.StatusCode == ApiConfig.StatusServiceUnavailable || code == ApiConfig.ErrorServiceUnavailable)
            {
                ShowError(UiTexts.ErrorUnavailable, retry, UiTexts.Retry);
                return;
            }

            switch (code)
            {
                case ApiConfig.ErrorTurnConflict:
                case ApiConfig.ErrorTurnAllocationMismatch:
                    ShowError(UiTexts.ErrorConflict, ResumeGame, UiTexts.Reload);
                    return;
                case ApiConfig.ErrorGameFinished:
                    ShowError(UiTexts.ErrorFinished, ResumeGame, UiTexts.Reload);
                    return;
                case ApiConfig.ErrorSessionRequired:
                    ShowError(UiTexts.ErrorSession, Boot, UiTexts.Retry);
                    return;
                case ApiConfig.ErrorAllocationNegative:
                case ApiConfig.ErrorAllocationStepMismatch:
                case ApiConfig.ErrorAllocationSumMismatch:
                    ShowError(UiTexts.ErrorAllocation, null, null);
                    return;
                case ApiConfig.ErrorDifficultyMissing:
                case ApiConfig.ErrorDifficultyUnknown:
                    ShowError(UiTexts.ErrorDifficulty, null, null);
                    return;
                default:
                    ShowError(UiTexts.ErrorGeneric, retry, UiTexts.Retry);
                    return;
            }
        }

        private void ShowError(string message, Action retry, string retryLabel)
        {
            var actions = new List<MessageAction>();
            if (retry != null)
            {
                actions.Add(new MessageAction(retryLabel, retry));
            }

            actions.Add(new MessageAction(UiTexts.BackToTitle, BackToTitle));
            new MessageScreen(host).Show(UiTexts.ErrorHeading, message, actions);
        }

        /// <summary>
        /// 通信を 1 回行い、成功なら onSuccess、失敗なら HandleFailure に渡す。
        /// 応答の形が違うとき（FormatException・JsonException）は、汎用の文言を出す。
        /// inGame は、ゲームの途中の通信（次の年への保存）だけ true にする。
        /// </summary>
        private void Exchange(Action<Action<ApiResponse>> send, Action<ApiResponse> onSuccess, Action retry, bool inGame = false)
        {
            host.SetBusy(true);
            send(response =>
            {
                host.SetBusy(false);
                if (!response.Succeeded)
                {
                    HandleFailure(response, retry, inGame);
                    return;
                }

                try
                {
                    onSuccess(response);
                }
                catch (FormatException)
                {
                    Debug.LogWarning(string.Format(LogTexts.ApiUnexpectedBody, response.StatusCode));
                    ShowError(UiTexts.ErrorGeneric, retry, UiTexts.Retry);
                }
                catch (JsonException)
                {
                    Debug.LogWarning(string.Format(LogTexts.ApiUnexpectedBody, response.StatusCode));
                    ShowError(UiTexts.ErrorGeneric, retry, UiTexts.Retry);
                }
            });
        }
    }
}
