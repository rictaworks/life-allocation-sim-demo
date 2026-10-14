using System;
using System.Collections;
using DomainCore;
using LifeAllocationSim.Config;
using LifeAllocationSim.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LifeAllocationSim.Api
{
    /// <summary>
    /// 保存 API（同一オリジン）への通信。UnityWebRequest を使い、応答はコールバックで返す。
    /// Cookie はブラウザが送受信する。このクラスはセッション ID を扱わない。
    /// </summary>
    public sealed class ApiClient
    {
        private readonly MonoBehaviour runner;

        public ApiClient(MonoBehaviour runner)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>セッションを確認・発行する（POST /api/session）。</summary>
        public void PostSession(Action<ApiResponse> done) =>
            Send(UnityWebRequest.kHttpVerbPOST, ApiConfig.SessionPath, ApiConfig.SessionBody, done);

        /// <summary>今のゲームを取得する（GET /api/game）。ゲームが無ければ game は null。</summary>
        public void GetGame(Action<ApiResponse> done) =>
            Send(UnityWebRequest.kHttpVerbGET, ApiConfig.GamePath, null, done);

        /// <summary>新しいゲームを始める（POST /api/games）。</summary>
        public void PostGame(string difficultyCode, Action<ApiResponse> done)
        {
            var body = new JObject { [ApiConfig.KeyDifficulty] = difficultyCode };
            Send(UnityWebRequest.kHttpVerbPOST, ApiConfig.GamesPath, body.ToString(Formatting.None), done);
        }

        /// <summary>1 年分の配分を保存する（POST /api/games/turns）。同じ番号の再送は、保存済みの結果を返す。</summary>
        public void PostTurn(int turnNo, Allocation allocation, Action<ApiResponse> done)
        {
            var body = new JObject
            {
                [ApiConfig.KeyTurnNo] = turnNo,
                [ApiConfig.KeyLivingPct] = allocation.LivingPct,
                [ApiConfig.KeySavingPct] = allocation.SavingPct,
                [ApiConfig.KeyInvestPct] = allocation.InvestPct,
                [ApiConfig.KeyWebsite] = ApiConfig.EmptyWebsite,
            };
            Send(UnityWebRequest.kHttpVerbPOST, ApiConfig.TurnsPath, body.ToString(Formatting.None), done);
        }

        private void Send(string method, string path, string body, Action<ApiResponse> done)
        {
            runner.StartCoroutine(SendRoutine(method, path, body, done));
        }

        private static IEnumerator SendRoutine(string method, string path, string body, Action<ApiResponse> done)
        {
            using (var request = new UnityWebRequest(path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = ApiConfig.TimeoutSeconds;
                request.SetRequestHeader(ApiConfig.HeaderAccept, ApiConfig.ContentTypeJson);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader(ApiConfig.HeaderContentType, ApiConfig.ContentTypeJson);
                }

                yield return request.SendWebRequest();

                var networkError = request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.DataProcessingError;
                // 本文が読めなかったときは null のまま渡す（空の文字列で代えない。読む側が null を失敗として扱う：ApiResponse）
                var text = request.downloadHandler.text;
                var response = new ApiResponse(
                    networkError,
                    request.responseCode,
                    text,
                    request.GetResponseHeader(ApiConfig.HeaderRetryAfter));
                done(response);
            }
        }
    }
}
