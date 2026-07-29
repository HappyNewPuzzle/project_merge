# 6단계 — 퀘스트·머지 이벤트·멱등 보상

## 단계 목표

서버가 성공으로 확정한 머지를 이벤트로 기록하고 퀘스트 진행도에 반영합니다. 보상
수령은 클라이언트가 생성한 멱등성 키를 영구 원장에 저장해 네트워크 재시도에도
코인이 중복 지급되지 않게 합니다.

## 첫 퀘스트

| 항목 | 값 |
|---|---|
| 퀘스트 ID | `merge_3` |
| 목표 | 아이템 머지 3회 |
| 보상 | 코인 100 |

`POST /api/v1/quests/`는 퀘스트를 최초 한 번 생성합니다. `GET /api/v1/quests/`는
진행 수, 목표, 보상, revision, 완료·수령 여부를 반환합니다.

## 머지 이벤트 연동

머지 성공 시 다음 작업이 같은 `SaveChanges` 트랜잭션으로 저장됩니다.

1. source 아이템 삭제와 target 레벨 상승
2. 보드 revision 증가
3. `item_merged` 이벤트 추가
4. `merge_3` 퀘스트 진행도 및 revision 증가

머지가 검증에 실패하거나 동시성 충돌로 롤백되면 이벤트와 진행도도 남지 않습니다.
이벤트에는 플레이어, 보드 revision, 결과 아이템 레벨, 서버 UTC 시각을 기록합니다.

## 보상 수령

```http
POST /api/v1/quests/merge_3/claim
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "idempotencyKey": "quest-merge-3-device-request-001",
  "expectedQuestRevision": 4,
  "expectedEconomyRevision": 2
}
```

최초 성공은 퀘스트를 수령 완료로 바꾸고 코인 100을 더하며 두 revision을 올립니다.
같은 키를 다시 보내면 HTTP 200과 `replayed: true`를 반환하지만 코인은 추가하지
않습니다. 재시도에서는 최초 요청의 오래된 revision을 그대로 보내도 됩니다.

멱등성 키는 한 논리적 보상 요청에 하나만 만들고 응답을 잃었을 때 같은 키를
재사용해야 합니다. 새 키를 만들면 이미 수령된 퀘스트 규칙에 의해 거부됩니다.

## MySQL 스키마

- `gameplay_events`: 서버 확정 행동 원장과 플레이어·시간 인덱스
- `player_quests`: `(player_id, quest_id)` 복합 키, 진행도, 보상, 동시성 revision
- `reward_claims`: `(player_id, idempotency_key)` 복합 키로 중복 지급 차단

세 테이블 모두 플레이어 삭제 시 FK cascade로 제거됩니다.

## 자동 테스트

- 머지 3회 후 완료 및 한 번만 수령 가능한 상태 전이
- 미완료 퀘스트 보상 거부
- 같은 멱등성 키 2회 요청 시 코인 100과 원장 1건만 저장
- 성공한 기존 머지 서비스가 게임 이벤트 1건을 함께 저장

## 단계 완료 검증

2026-07-29에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 21개 통과, 실패 0개
- EF 모델·마이그레이션 일치 및 MySQL 복합 키·인덱스 SQL 확인
- 알려진 NuGet 취약 패키지: 0개
- 실제 MySQL API 통합 실행: 로컬 MySQL/Docker 인스턴스가 없어 미수행

EF 자동 생성 파일을 제외한 코드에는 트랜잭션과 멱등성 설계 이유를 주석으로
기록했습니다.

## 다음 단계 제안

7단계에서는 운영에 필요한 구조화 오류 처리, 요청 추적 ID, 감사 로그 정리,
Docker Compose 기반 MySQL 통합 테스트 환경을 구축하는 것이 적합합니다.
