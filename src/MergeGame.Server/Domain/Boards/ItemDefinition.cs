namespace MergeGame.Server.Domain.Boards;

/// <summary>
/// 서버가 허용하는 한 단계의 머지 아이템을 정의합니다.
/// 클라이언트가 보내는 이름이나 레벨을 신뢰하지 않고 이 카탈로그를 기준으로 결과를 결정합니다.
/// </summary>
/// <param name="ChainId">같은 머지 계열을 구분하는 안정적인 식별자입니다.</param>
/// <param name="Level">계열 안에서의 단계이며 1부터 시작합니다.</param>
/// <param name="Name">화면과 로그에서 사용할 사람이 읽기 쉬운 이름입니다.</param>
/// <param name="IsMaxLevel">더 이상 머지할 수 없는 마지막 단계인지 나타냅니다.</param>
public sealed record ItemDefinition(
    string ChainId,
    int Level,
    string Name,
    bool IsMaxLevel);
