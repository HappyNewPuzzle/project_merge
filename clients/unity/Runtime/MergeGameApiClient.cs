using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MergeGame.Client
{
    /// <summary>
    /// OpenAPI v1 계약을 사용하는 Unity 코루틴 클라이언트입니다.
    /// MonoBehaviour가 아니므로 화면 전환 후에도 원하는 수명 관리 객체에서 보관할 수 있습니다.
    /// </summary>
    public sealed class MergeGameApiClient
    {
        private const string ApiPrefix = "/api/v1";
        private readonly string _baseUrl;

        /// <summary>로그인 성공 후 설정하면 보호 API에 Bearer 헤더가 자동으로 붙습니다.</summary>
        public string AccessToken { get; set; } = "";

        public MergeGameApiClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("서버 baseUrl이 필요합니다.", nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
        }

        public IEnumerator CreateGuest(Action<ApiResult<CreateGuestPlayerResponse>> completed) =>
            Send<CreateGuestPlayerResponse>(UnityWebRequest.kHttpVerbPOST, "/players/guest", null, false, completed);

        public IEnumerator LoginGuest(GuestLoginRequest body, Action<ApiResult<GuestLoginResponse>> completed) =>
            Send<GuestLoginResponse>(UnityWebRequest.kHttpVerbPOST, "/auth/guest", body, false, completed);

        public IEnumerator RefreshAccessToken(RefreshTokenRequest body, Action<ApiResult<GuestLoginResponse>> completed) =>
            Send<GuestLoginResponse>(UnityWebRequest.kHttpVerbPOST, "/auth/refresh", body, false, completed);

        public IEnumerator Logout(RefreshTokenRequest body, Action<ApiResult<EmptyResponse>> completed) =>
            Send<EmptyResponse>(UnityWebRequest.kHttpVerbPOST, "/auth/logout", body, true, completed);

        public IEnumerator GetCurrentPlayer(Action<ApiResult<CurrentPlayerResponse>> completed) =>
            Send<CurrentPlayerResponse>(UnityWebRequest.kHttpVerbGET, "/players/me", null, true, completed);

        public IEnumerator InitializeBoard(Action<ApiResult<BoardState>> completed) =>
            Send<BoardState>(UnityWebRequest.kHttpVerbPOST, "/board/", null, true, completed);

        public IEnumerator GetBoard(Action<ApiResult<BoardState>> completed) =>
            Send<BoardState>(UnityWebRequest.kHttpVerbGET, "/board/", null, true, completed);

        public IEnumerator MergeItems(MergeBoardItemsRequest body, Action<ApiResult<BoardState>> completed) =>
            Send<BoardState>(UnityWebRequest.kHttpVerbPOST, "/board/merge", body, true, completed);

        public IEnumerator InitializeEconomy(Action<ApiResult<EconomySnapshot>> completed) =>
            Send<EconomySnapshot>(UnityWebRequest.kHttpVerbPOST, "/economy/", null, true, completed);

        public IEnumerator GetEconomy(Action<ApiResult<EconomySnapshot>> completed) =>
            Send<EconomySnapshot>(UnityWebRequest.kHttpVerbGET, "/economy/", null, true, completed);

        public IEnumerator GenerateItem(GenerateItemRequest body, Action<ApiResult<GenerateItemResponse>> completed) =>
            Send<GenerateItemResponse>(UnityWebRequest.kHttpVerbPOST, "/economy/generate", body, true, completed);

        public IEnumerator ClaimDailyReward(RevisionRequest body, Action<ApiResult<EconomySnapshot>> completed) =>
            Send<EconomySnapshot>(UnityWebRequest.kHttpVerbPOST, "/economy/daily-reward", body, true, completed);

        public IEnumerator InitializeQuests(Action<ApiResult<QuestSnapshot>> completed) =>
            Send<QuestSnapshot>(UnityWebRequest.kHttpVerbPOST, "/quests/", null, true, completed);

        public IEnumerator GetQuests(Action<ApiResult<QuestSnapshot>> completed) =>
            Send<QuestSnapshot>(UnityWebRequest.kHttpVerbGET, "/quests/", null, true, completed);

        public IEnumerator ClaimQuestReward(string questId, ClaimQuestRewardRequest body,
            Action<ApiResult<QuestRewardResponse>> completed) =>
            Send<QuestRewardResponse>(UnityWebRequest.kHttpVerbPOST,
                "/quests/" + UnityWebRequest.EscapeURL(questId) + "/claim", body, true, completed);

        public IEnumerator InitializeSocialProfile(Action<ApiResult<SocialProfileSnapshot>> completed) =>
            Send<SocialProfileSnapshot>(UnityWebRequest.kHttpVerbPOST, "/social/profile", null, true, completed);

        public IEnumerator GetSocialProfile(Action<ApiResult<SocialState>> completed) =>
            Send<SocialState>(UnityWebRequest.kHttpVerbGET, "/social/profile", null, true, completed);

        public IEnumerator AddFriend(AddFriendRequest body, Action<ApiResult<AddFriendResponse>> completed) =>
            Send<AddFriendResponse>(UnityWebRequest.kHttpVerbPOST, "/social/friends", body, true, completed);

        public IEnumerator SendFriendEnergyGift(string friendPlayerId, Action<ApiResult<EnergyGiftResponse>> completed) =>
            Send<EnergyGiftResponse>(UnityWebRequest.kHttpVerbPOST,
                "/social/friends/" + UnityWebRequest.EscapeURL(friendPlayerId) + "/energy-gift", null, true, completed);

        private IEnumerator Send<T>(string method, string path, object body, bool requiresAuthentication,
            Action<ApiResult<T>> completed)
        {
            // 본문 없는 POST도 서버가 JSON 요청으로 일관되게 처리하도록 빈 객체를 전송합니다.
            var json = body == null ? "{}" : JsonUtility.ToJson(body);
            using var request = new UnityWebRequest(_baseUrl + ApiPrefix + path, method);
            if (method != UnityWebRequest.kHttpVerbGET)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("X-Trace-Id", Guid.NewGuid().ToString("N"));

            if (requiresAuthentication && !string.IsNullOrWhiteSpace(AccessToken))
                request.SetRequestHeader("Authorization", "Bearer " + AccessToken);

            yield return request.SendWebRequest();

            var raw = request.downloadHandler == null ? "" : request.downloadHandler.text;
            var success = request.responseCode >= 200 && request.responseCode < 300;
            var result = new ApiResult<T>
            {
                IsSuccess = success,
                StatusCode = request.responseCode,
                RawBody = raw
            };

            // JsonUtility는 빈 문자열을 역직렬화할 수 없으므로 본문 존재 여부를 먼저 확인합니다.
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (success)
                    result.Data = JsonUtility.FromJson<T>(raw);
                else
                    result.Problem = JsonUtility.FromJson<ApiProblem>(raw);
            }

            completed?.Invoke(result);
        }
    }
}
