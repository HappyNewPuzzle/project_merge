# 25. 서버 권위 아이템 도감 (클라이언트 Step 54)

## 계약

인증된 `GET /api/v1/collection`은 다음을 반환한다.

```json
{"discoveredItems":[{"chainId":"toy","level":1}]}
```

Bootstrap 응답에 같은 구조의 `collection` 필드를 추가했다. 기존 필드는 유지한다.
정의/이름/가격/최대 단계는 Content Catalog를 재사용하며 클라이언트 쓰기 API는 없다.
계정은 JWT에서만 결정하고 정지 계정은 기존 미들웨어에서 차단한다.

## 저장 및 호환성

`item_discoveries` 기본 키는 player_id + chain_id + level이다.
`ItemCollection.RecordTrackedItemsAsync`를 성공한 초기화·생성·머지·이동·판매·보관
유스케이스의 SaveChanges 직전에 명시적으로 호출한다. 자동 전역 interceptor는 없다.
추적 중인 아이템의 현재 값과 원래 값을 수집하므로 소비된 입력 단계도 남는다.
발견 추가는 기존 보드/경제/영수증과 같은 SaveChanges 트랜잭션으로 저장된다.
실패·replay 경로는 신규 발견을 저장하지 않는다. 동일 정의 경쟁은 기존 충돌 복구를 사용한다.

Migration은 기존 보드와 보관함의 실제 소유 아이템을 UNION으로 보충한다.
Bootstrap에서도 남은 아이템을 보충한다. 과거 판매/소비되어 사라진 아이템은 복원할 수 없다.
기존 테이블·데이터는 삭제하지 않으며 운영 DB에는 이 작업에서 적용하지 않는다.
서버 업데이트와 Migration을 먼저 배포한 뒤 새 클라이언트를 배포한다.
Down은 발견 기록 테이블을 삭제하므로 운영 롤백 시 데이터 보존 검토가 필요하다.

## 검증

- .NET 테스트 88/88 통과 (2026-09-10).
- 실제 ASP.NET 테스트 호스트: 인증, 계정 격리, 생성/replay, 실패 미등록,
  머지 후 입력/결과 단계 유지, Bootstrap 재조회, 기존 보드 통합 액션 검증.
- EF Migration 생성 성공. 실제 MySQL 적용/재접속 검증은 클라이언트 Step 57에서 별도 수행한다.
- DB/HTTP 실패에서 UI가 발견을 추정하지 않도록 클라이언트는 서버 도감만 표시해야 한다.
