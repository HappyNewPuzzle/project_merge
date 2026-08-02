# 13단계 — 계정 정지 및 영구 관리자 감사 원장

## 단계 목표

운영자가 비정상 이용 계정을 정지하거나 정상화할 수 있게 하되, 모든 변경에 이유,
낙관적 동시성 revision, 멱등성 키와 변경 전후 상태를 남깁니다. 정지는 새 로그인뿐
아니라 refresh token과 이미 발급된 JWT 요청에도 적용됩니다.

## 정지·해제 API

관리자 전용 엔드포인트입니다.

```http
POST /api/v1/admin/players/{playerId}/suspension
X-Admin-Key: ...
Content-Type: application/json
```

```json
{
  "suspended": true,
  "reason": "비정상 재화 사용 조사",
  "idempotencyKey": "ticket-20260803-001",
  "expectedRevision": 0
}
```

처음 제재 상태를 만드는 플레이어는 `expectedRevision: 0`을 사용합니다. 이후에는 관리자
플레이어 조회 응답의 `moderationRevision`을 보내야 합니다. 다른 운영자가 먼저 상태를
변경하면 HTTP 409를 반환하므로 최신 상태를 다시 조회하고 판단해야 합니다.

`reason`은 3~256자, `idempotencyKey`는 8~64자입니다. 사유에는 토큰, 결제 정보 등
민감정보를 입력하지 않고 내부 사건 번호와 필요한 최소 설명만 기록합니다.

## 멱등성과 동시성

`(operator_id, idempotency_key)` 조합에 MySQL 고유 인덱스를 적용합니다. 같은 네트워크
요청을 재시도하면 최초 결과를 `replayed: true`로 반환하고 revision을 다시 올리지
않습니다. 같은 키를 다른 대상에 재사용하면 HTTP 409입니다.

`player_moderations.revision`은 EF Core concurrency token입니다. 상태 변경, 활성 refresh
session 폐기, 감사 행 추가는 한 번의 `SaveChanges` 트랜잭션으로 저장됩니다. 일부만
저장되는 상태는 허용하지 않습니다.

## 정지 적용 범위

계정을 정지하면 다음 경로가 차단됩니다.

1. 게스트 로그인: 새 JWT와 refresh token을 발급하지 않음
2. 활성 refresh session: 같은 트랜잭션에서 `account_suspended`로 전부 폐기
3. refresh API: 폐기된 토큰이므로 HTTP 401
4. 기존 JWT 보호 API: `SuspendedPlayerMiddleware`가 DB 상태를 확인해 HTTP 403

403 응답에는 `account_suspended` 코드와 trace ID만 포함하고 내부 정지 사유는
플레이어에게 노출하지 않습니다. 관리자 API 키 요청에는 플레이어 정지 미들웨어를
적용하지 않으므로 운영자가 정지 상태를 조회하고 해제할 수 있습니다.

정지 해제 후에는 기존 refresh token이 복구되지 않습니다. 플레이어는 원본 게스트
자격 증명으로 다시 로그인해 새 토큰 계열을 발급받아야 합니다.

## 영구 감사 원장

`admin_action_audits`에는 다음 값이 저장됩니다.

- 감사 ID
- OperatorId와 멱등성 키
- 대상 플레이어 ID
- `player.suspension.changed` 작업명
- 변경 전후 `active` 또는 `suspended`
- 운영자가 입력한 사유
- 결과 moderation revision
- 서버 UTC 생성 시각

관리자 API 키 원문, JWT, refresh token, 게스트 토큰 해시는 저장하지 않습니다. 대상과
시각 인덱스로 플레이어별 변경 이력을 조사할 수 있습니다. 감사 행은 일반 애플리케이션
API에서 수정·삭제하는 기능을 제공하지 않습니다.

## MySQL 마이그레이션

`AddPlayerModerationAndAdminAudit`가 다음 테이블을 추가합니다.

- `player_moderations`: 현재 정지 상태, 사유, revision, 갱신 시각
- `admin_action_audits`: 변경 전후 영구 감사 행

플레이어 삭제 시 두 테이블도 CASCADE 삭제됩니다. 법적·보안 요건상 탈퇴 후에도 감사
기록 보존이 필요하다면 익명화된 별도 감사 저장소로 내보내는 정책이 추가로 필요합니다.

## 운영 절차

1. 관리자 플레이어 조회로 현재 상태와 `moderationRevision`을 확인합니다.
2. 사건 번호 기반의 새 멱등성 키와 최소한의 사유를 준비합니다.
3. 정지 또는 해제 요청을 한 번 전송합니다.
4. 타임아웃이면 같은 키로 재시도합니다.
5. HTTP 409면 요청을 자동 반복하지 말고 상태를 다시 확인합니다.
6. 구조화 요청 로그의 OperatorId·TargetPlayerId·trace ID와 감사 행을 연결합니다.

## 단계 완료 검증

2026-08-03에 다음 항목을 확인했습니다.

- Release 빌드: 경고 0개, 오류 0개
- 자동 테스트: 40개 통과, 실패 0개, 건너뜀 0개
- 정지 상태·활성 session 폐기·감사 원장의 단일 저장 및 멱등 재시도 테스트 통과
- 정지 플레이어의 게스트 로그인과 JWT 발급 차단 테스트 통과
- OpenAPI v1 관리자 정지 경로 계약 검사 통과
- EF Core 모델 변경 미반영 여부: 없음
- MySQL 테이블 2개, 관리자 멱등성 고유 인덱스, 대상·시각 감사 인덱스 SQL 확인
- 신규 NuGet 패키지: 0개
- 외부 NuGet 최신 취약성 조회: 이전 단계와 같은 보안 정책으로 미실행. 패키지 집합은
  취약 패키지 0개로 확인한 10단계와 동일함

## 다음 단계 제안

14단계에서는 동일한 감사·멱등성 기반 위에 제한된 코인 조정 기능을 추가하고, 2인 승인
또는 조정 한도 정책으로 운영 실수를 방지하는 것이 적합합니다.
