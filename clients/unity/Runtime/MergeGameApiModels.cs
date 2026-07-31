using System;

namespace MergeGame.Client
{
    // Unity JsonUtility는 public 필드를 직렬화하므로 서버의 camelCase JSON 이름과 필드명을 맞춥니다.
    // 날짜는 DateTime 자동 변환의 플랫폼 차이를 피하려고 ISO 8601 문자열로 보관합니다.

    [Serializable] public sealed class CreateGuestPlayerResponse
    {
        public string playerId = "";
        public string displayName = "";
        public string guestToken = "";
        public string createdAtUtc = "";
    }

    [Serializable] public sealed class GuestLoginRequest
    {
        public string playerId = "";
        public string guestToken = "";
    }

    [Serializable] public sealed class GuestLoginResponse
    {
        public string playerId = "";
        public string accessToken = "";
        public string tokenType = "Bearer";
        public string expiresAtUtc = "";
    }

    [Serializable] public sealed class CurrentPlayerResponse
    {
        public string playerId = "";
        public string displayName = "";
        public string createdAtUtc = "";
    }

    [Serializable] public sealed class BoardItemState
    {
        public string itemId = "";
        public int slotIndex;
        public string chainId = "";
        public int level;
        public string name = "";
        public bool isMaxLevel;
    }

    [Serializable] public sealed class BoardState
    {
        public string playerId = "";
        public int width;
        public int height;
        public long revision;
        public BoardItemState[] items = Array.Empty<BoardItemState>();
    }

    [Serializable] public sealed class MergeBoardItemsRequest
    {
        public int sourceSlot;
        public int targetSlot;
        public long expectedRevision;
    }

    [Serializable] public sealed class EconomySnapshot
    {
        public string playerId = "";
        public int energy;
        public int maxEnergy;
        public long coins;
        public long revision;
        public string nextEnergyAtUtc = "";
        public bool dailyRewardClaimedToday;
    }

    [Serializable] public sealed class GenerateItemRequest
    {
        public int targetSlot;
        public long expectedBoardRevision;
        public long expectedEconomyRevision;
    }

    [Serializable] public sealed class GenerateItemResponse
    {
        public BoardState board = new BoardState();
        public EconomySnapshot economy = new EconomySnapshot();
    }

    [Serializable] public sealed class RevisionRequest
    {
        public long expectedRevision;
    }

    [Serializable] public sealed class QuestSnapshot
    {
        public string questId = "";
        public int currentCount;
        public int targetCount;
        public long rewardCoins;
        public long revision;
        public bool isCompleted;
        public bool isClaimed;
    }

    [Serializable] public sealed class ClaimQuestRewardRequest
    {
        public string idempotencyKey = "";
        public long expectedQuestRevision;
        public long expectedEconomyRevision;
    }

    [Serializable] public sealed class QuestRewardResponse
    {
        public bool replayed;
        public QuestSnapshot quest = new QuestSnapshot();
        public EconomySnapshot economy = new EconomySnapshot();
        public string error = "";
    }

    [Serializable] public sealed class ApiProblem
    {
        public string title = "";
        public int status;
        public string detail = "";
        public string instance = "";
        public string code = "";
        public string traceId = "";
    }

    /// <summary>성공 응답과 HTTP 오류를 예외 없이 한 콜백으로 전달합니다.</summary>
    public sealed class ApiResult<T>
    {
        public bool IsSuccess { get; internal set; }
        public long StatusCode { get; internal set; }
        public T Data { get; internal set; }
        public ApiProblem Problem { get; internal set; }
        public string RawBody { get; internal set; } = "";
    }
}
