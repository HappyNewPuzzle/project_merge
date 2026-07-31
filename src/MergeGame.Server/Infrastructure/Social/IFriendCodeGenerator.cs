namespace MergeGame.Server.Infrastructure.Social;

/// <summary>충돌 가능성이 낮고 사람이 입력하기 쉬운 공개 친구 코드를 생성합니다.</summary>
public interface IFriendCodeGenerator
{
    string Generate();
}
