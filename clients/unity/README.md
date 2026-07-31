# Unity API 클라이언트

`Runtime` 폴더를 Unity 프로젝트의 `Assets/MergeGame/Runtime`으로 복사합니다. 이 구현은
Unity 기본 `JsonUtility`, `UnityWebRequest`, 코루틴만 사용하므로 별도 JSON 패키지가
필요하지 않습니다.

```csharp
private IEnumerator Login(string playerId, string guestToken)
{
    var api = new MergeGameApiClient("https://localhost:7001");
    yield return api.LoginGuest(new GuestLoginRequest
    {
        playerId = playerId,
        guestToken = guestToken
    }, result =>
    {
        if (result.IsSuccess)
            api.AccessToken = result.Data.accessToken;
        else
            Debug.LogError($"{result.Problem?.code} trace={result.Problem?.traceId}");
    });
}
```

`revision` 값은 서버의 동시성 계약입니다. 보드·경제·퀘스트 변경 성공 후 받은 최신
revision을 다음 요청의 `expected...Revision`에 사용하고, HTTP 409이면 서버 상태를
다시 조회한 뒤 사용자 동작을 재적용합니다. 게스트 토큰과 JWT는 `PlayerPrefs` 평문이
아닌 플랫폼 보안 저장소에 보관해야 합니다.
