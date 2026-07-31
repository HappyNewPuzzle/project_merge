# 9단계 — 친구 관계 및 일일 에너지 선물

## 단계 목표

플레이어가 인증 식별자를 직접 공유하지 않고 짧은 친구 코드로 서로 연결되고, 친구에게
UTC 날짜 기준 하루 한 번 에너지를 선물할 수 있게 합니다. 중복 요청과 동시 요청은
애플리케이션 검사뿐 아니라 MySQL 고유 인덱스와 경제 revision으로 최종 차단합니다.

## 친구 코드

`POST /api/v1/social/profile`은 플레이어별 소셜 프로필을 지연 생성합니다. 9단계 전에
가입한 기존 플레이어도 별도 데이터 변환 없이 호출 시 프로필을 만들 수 있습니다.

- 길이: 영문 대문자·숫자 8자
- 제외 문자: 육안으로 혼동하기 쉬운 `0`, `O`, `1`, `I`
- 생성 방식: `RandomNumberGenerator` 기반 암호학적 난수
- DB 규칙: 대소문자를 구분하지 않는 고유 인덱스
- 충돌 처리: 새 코드로 최대 5회 재시도

친구 코드는 공개 검색 코드일 뿐 인증 수단이 아닙니다. 로그인에는 기존 guestToken과
JWT만 사용하며 친구 코드로 계정에 접근할 수 없습니다.

## 친구 관계

`POST /api/v1/social/friends` 요청 본문입니다.

```json
{ "friendCode": "ABCD2345" }
```

친구 요청/수락 화면을 두지 않는 현재 게임 규칙에서는 유효한 코드를 입력하면 즉시
양방향 친구가 됩니다. 두 플레이어 GUID를 정렬해 작은 값을 `player_low_id`, 큰 값을
`player_high_id`에 저장합니다. 따라서 A가 B를 추가한 행과 B가 A를 추가한 행이 따로
생기지 않습니다. 같은 요청을 반복하면 HTTP 200과 `alreadyFriends: true`를 반환합니다.

자기 코드 입력은 `cannot_add_self`, 존재하지 않는 코드는 `friend_code_not_found`로
거부합니다.

## 소셜 상태 조회

`GET /api/v1/social/profile`은 다음을 반환합니다.

- 내 친구 코드
- 친구 플레이어 ID와 표시 이름
- 친구가 된 UTC 시각
- 오늘 해당 친구에게 에너지를 보냈는지 여부

친구 목록은 표시 이름 순으로 정렬됩니다. 목록에 인증 토큰, 게스트 토큰 해시, 친구의
재화 전체 이력 등 비공개 정보는 포함하지 않습니다.

## 일일 에너지 선물

`POST /api/v1/social/friends/{friendPlayerId}/energy-gift`는 친구 경제 상태에 에너지 5를
즉시 지급합니다.

1. 서버가 실제 친구 관계를 확인합니다.
2. 발신자·수신자·현재 UTC 날짜의 기존 선물을 확인합니다.
3. 친구의 시간 기반 자연 충전을 먼저 적용합니다.
4. 최대 100을 넘지 않도록 에너지 5를 더하고 economy revision을 증가시킵니다.
5. 경제 변경과 선물 증적을 한 번의 `SaveChanges` 트랜잭션으로 저장합니다.

이미 에너지가 100이면 `recipient_energy_full`로 지급하지 않으며 선물 횟수도 소비하지
않습니다. 같은 UTC 날짜에 성공한 요청을 반복하면 `replayed: true`로 응답하고 에너지를
다시 지급하지 않습니다. 날짜가 바뀌면 다시 보낼 수 있습니다.

## MySQL 무결성 및 동시성

마이그레이션 `AddSocialFriendsAndEnergyGifts`가 다음 테이블을 추가합니다.

- `player_social_profiles`: 플레이어별 공개 친구 코드
- `friendships`: 정규화된 양방향 친구 관계
- `energy_gifts`: 날짜별 선물 지급 증적

핵심 제약은 다음과 같습니다.

- 친구 코드 고유 인덱스
- `(player_low_id, player_high_id)` 친구 조합 고유 인덱스
- `(sender_player_id, recipient_player_id, gift_date_utc)` 일일 선물 고유 인덱스
- 자기 친구/자기 선물을 막는 CHECK 제약
- 플레이어 삭제 시 관련 소셜 데이터 CASCADE 삭제
- 친구 경제 상태의 EF Core revision 낙관적 동시성 검사

두 서버 인스턴스가 같은 선물을 동시에 처리해도 한 트랜잭션만 성공합니다. 고유 인덱스
또는 revision 충돌이 최종 안전망으로 동작합니다.

## API와 Unity 연동

모든 소셜 API는 Bearer JWT가 필요하며 OpenAPI `/docs`의 `Social` 태그에서 확인할 수
있습니다. `clients/unity/Runtime`에는 다음 메서드와 DTO가 추가됐습니다.

- `InitializeSocialProfile`
- `GetSocialProfile`
- `AddFriend`
- `SendFriendEnergyGift`

클라이언트는 선물 버튼 활성화 여부를 `energyGiftSentToday`로 표시할 수 있지만, 최종
지급 가능 여부는 항상 서버 응답을 기준으로 판단해야 합니다.

## 단계 완료 검증

2026-07-31에 다음 항목을 확인했습니다.

- Release 빌드: 경고 0개, 오류 0개
- 자동 테스트: 32개 통과, 실패 0개, 건너뜀 0개
- 친구 GUID 정규화, 코드 형식, 에너지 지급 도메인 테스트 통과
- 친구 중복 추가, 일일 선물 중복 지급, 프로필 멱등 초기화 서비스 테스트 통과
- OpenAPI v1 핵심 경로 13개와 소셜 Bearer 인증 계약 검사 통과
- EF Core 모델 변경 미반영 여부: 없음
- MySQL 마이그레이션 SQL의 테이블 3개, 고유 인덱스 3개, CHECK·FK 제약 확인
- 알려진 NuGet 취약 패키지: 직접 및 전이 패키지 모두 0개

## 다음 단계 제안

10단계에서는 친구 요청과 수락/거절 상태, 받은 선물 우편함과 수령 만료 정책을 추가해
비동기 소셜 흐름을 확장하거나, 관리자 운영 API와 플레이어 제재 시스템을 구축할 수
있습니다.
