namespace MergeGame.Server.Infrastructure.OpenApi;

/// <summary>
/// 서버와 외부 클라이언트가 공유하는 공개 API 버전 계약입니다.
/// 경로 버전을 한곳에 두면 새 버전을 추가할 때 기존 v1을 실수로 변경하는 일을 줄일 수 있습니다.
/// </summary>
public static class ApiContract
{
    public const string Version = "v1";
    public const string RoutePrefix = "/api/v1";
    public const string DocumentName = "v1";
}
