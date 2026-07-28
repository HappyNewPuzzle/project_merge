# 4단계 — 플레이어 보드 및 서버 검증 머지

## 단계 목표

머지 게임의 핵심 상태를 서버와 MySQL이 소유하도록 구성합니다. 클라이언트는 어떤
두 슬롯을 합치고 싶은지만 요청하며, 아이템 일치 여부, 다음 레벨, 최대 레벨,
보드 동시성은 모두 서버가 검증합니다.

## 보드 구조

- 크기: 5열 × 7행, 총 35슬롯
- 슬롯 인덱스: `0`부터 `34`
- 플레이어당 보드 1개
- 최초 revision: `1`
- 시작 아이템: `garden` 계열 1레벨 2개, 슬롯 `0`과 `1`

보드를 처음 만든 직후 두 시작 아이템을 머지해 기본 동작을 확인할 수 있습니다.

## 서버 아이템 카탈로그

현재 아이템 정의는 `InMemoryItemCatalog`에 서버 코드로 관리합니다.

| chainId | level | 이름 | 최대 레벨 |
|---|---:|---|---|
| `garden` | 1 | Seed Bag | 아니요 |
| `garden` | 2 | Green Sprout | 아니요 |
| `garden` | 3 | Flower Pot | 아니요 |
| `garden` | 4 | Flower Basket | 아니요 |
| `garden` | 5 | Garden Arch | 예 |

클라이언트가 다음 레벨이나 결과 아이템을 보내지 않습니다. 서버가 현재 아이템의
`chainId`와 `level`을 조회해 카탈로그에 등록된 다음 단계만 생성합니다.

## 보드 초기화

```http
POST /api/v1/board/
Authorization: Bearer {accessToken}
Content-Length: 0
```

최초 요청은 HTTP 201, 이미 보드가 있으면 HTTP 200을 반환합니다. 반복 호출해도
시작 아이템이 다시 지급되지 않습니다.

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "width": 5,
  "height": 7,
  "revision": 1,
  "items": [
    {
      "itemId": "22c19784-5343-4a56-a107-c19d1fd06388",
      "slotIndex": 0,
      "chainId": "garden",
      "level": 1,
      "name": "Seed Bag",
      "isMaxLevel": false
    },
    {
      "itemId": "4f319bdd-31e6-47a0-8b6f-86469c175294",
      "slotIndex": 1,
      "chainId": "garden",
      "level": 1,
      "name": "Seed Bag",
      "isMaxLevel": false
    }
  ]
}
```

## 보드 조회

```http
GET /api/v1/board/
Authorization: Bearer {accessToken}
```

현재 전체 보드를 반환합니다. 보드가 아직 없으면 HTTP 404와
`board_not_initialized` 오류를 반환합니다.

## 아이템 머지

```http
POST /api/v1/board/merge
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "sourceSlot": 0,
  "targetSlot": 1,
  "expectedRevision": 1
}
```

성공하면 source 슬롯의 아이템은 소비되고 target 슬롯의 아이템이 다음 레벨로
변경됩니다. 응답은 revision이 `2`로 증가한 전체 보드입니다.

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "width": 5,
  "height": 7,
  "revision": 2,
  "items": [
    {
      "itemId": "4f319bdd-31e6-47a0-8b6f-86469c175294",
      "slotIndex": 1,
      "chainId": "garden",
      "level": 2,
      "name": "Green Sprout",
      "isMaxLevel": false
    }
  ]
}
```

## 서버 머지 규칙

서버는 상태를 변경하기 전에 다음 순서로 모두 검증합니다.

1. `expectedRevision`이 현재 revision과 같은지
2. 두 슬롯이 `0~34` 범위인지
3. source와 target이 서로 다른지
4. 두 슬롯 모두 아이템이 있는지
5. 두 아이템의 `chainId`와 `level`이 같은지
6. 현재 아이템이 서버 카탈로그에 존재하는지
7. 최대 레벨이 아니며 다음 단계 정의가 존재하는지

하나라도 실패하면 아이템, revision, 수정 시각은 전혀 바뀌지 않습니다.

## 오류 응답

| HTTP | code | 의미 |
|---:|---|---|
| 401 | Bearer challenge | JWT 없음 또는 유효하지 않음 |
| 404 | `board_not_initialized` | 보드가 생성되지 않음 |
| 409 | `stale_revision` | 다른 요청이 먼저 보드를 변경함 |
| 422 | `invalid_slot` | 슬롯 범위 오류 |
| 422 | `same_slot` | 같은 슬롯을 두 번 선택 |
| 422 | `empty_slot` | 선택 슬롯 중 하나가 비어 있음 |
| 422 | `items_do_not_match` | 계열 또는 레벨이 다름 |
| 422 | `max_level_reached` | 최종 단계 아이템 |

409 응답에는 서버의 `currentRevision`과 최신 전체 보드가 포함됩니다. 클라이언트는
로컬 보드를 이 값으로 교체한 뒤 사용자 입력을 다시 받아야 합니다. 실패한 요청을
자동 재실행하면 사용자가 의도하지 않은 머지가 발생할 수 있습니다.

## 낙관적 동시성

클라이언트 검증만으로는 같은 보드의 두 요청이 동시에 성공하는 문제를 막을 수
없습니다. `player_boards.revision`은 EF Core 동시성 토큰입니다.

머지 저장 시 개념적으로 다음 조건이 포함됩니다.

```sql
UPDATE player_boards
SET revision = 2
WHERE player_id = @playerId AND revision = 1;
```

다른 요청이 먼저 revision을 올리면 영향을 받는 행이 0개가 되고 EF Core가
`DbUpdateConcurrencyException`을 발생시킵니다. 아이템 삭제, 대상 레벨 변경,
revision 갱신은 하나의 `SaveChanges` 트랜잭션에서 실행되므로 전부 성공하거나
전부 롤백됩니다.

## MySQL 스키마

### `player_boards`

- `player_id`: `players.id`를 참조하는 기본 키
- `revision`: 증가 전용 보드 버전 및 EF Core 동시성 토큰
- `created_at_utc`, `updated_at_utc`: UTC 생성·수정 시각

### `board_items`

- `id`: 아이템 인스턴스 기본 키
- `player_id`: 소유 보드 외래 키
- `slot_index`: 0~34 CHECK 제약
- `chain_id`, `level`: 서버 아이템 정의 키
- `(player_id, slot_index)`: 슬롯 중복 방지 고유 인덱스

플레이어를 삭제하면 보드와 아이템이 FK cascade로 함께 제거됩니다.

## 마이그레이션 적용

```powershell
dotnet tool restore
$env:ConnectionStrings__MergeGameDatabase='Server=localhost;Port=3306;Database=merge_game;User=merge_game_app;Password=실제비밀번호'
$env:Jwt__SigningKey='최소-32바이트의-실제-비밀-키'
dotnet tool run dotnet-ef database update --project src/MergeGame.Server --startup-project src/MergeGame.Server
```

## 자동 테스트

`dotnet test ProjectMerge.sln`은 기존 인증 테스트를 포함해 다음 보드 규칙을 검증합니다.

- 정상 머지의 source 소비, target 레벨 상승, revision 증가
- 오래된 revision과 같은 슬롯 요청이 보드를 변경하지 않는지
- 최대 단계 아이템 머지 거부
- 초기화 반복 시 보드와 시작 아이템 중복 방지
- 머지 결과가 EF Core 저장소에 원자적으로 반영되는지
- 같은 revision을 읽은 두 DB 작성자 중 두 번째가 동시성 예외로 차단되는지

## 단계 완료 검증

2026-07-28에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 14개 통과, 실패 0개
- MySQL 마이그레이션 SQL: 두 테이블, FK cascade, CHECK, 고유 인덱스 확인
- 직접 및 전이 NuGet 패키지 취약성 검사: 알려진 취약 패키지 0개
- 실제 MySQL API 통합 실행: 로컬 MySQL/Docker 인스턴스가 없어 미수행

EF Core가 자동 생성한 Designer와 모델 스냅샷을 제외한 도메인, 서비스, 매핑,
엔드포인트, 테스트 코드에는 규칙과 설계 이유를 설명하는 주석을 포함했습니다.

## 다음 단계 제안

5단계에서는 에너지와 재화, 아이템 생성기, 보상 수령, 서버 시간 기반 충전 규칙을
구현하는 것이 적합합니다.
