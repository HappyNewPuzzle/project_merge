namespace MergeGame.Server.Domain.Content;

/// <summary>
/// Unity 캐시와 서버 규칙의 호환성을 판정하는 콘텐츠 계약 버전입니다.
/// 아이템 단계, 생성 확률 또는 경제 규칙이 바뀌면 반드시 새 값으로 올립니다.
/// </summary>
public static class GameContentVersion
{
    public const string Current = "2026.08.15.2";
}
