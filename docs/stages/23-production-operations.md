# 23단계: 운영 안전장치와 배포 자동화

## 목표

개발 기능을 실제 서비스 환경에 배포할 때 필요한 최소 운영 안전장치를 추가했습니다. 운영자별 권한 분리, 고액 경제 조정의 이중 승인, Unity 클라이언트 버전 차단, 재현 가능한 패키지 복원, 실제 MySQL 기반 CI, 배포 및 백업 절차를 한 단계로 묶었습니다.

## 구현 내용

### 1. 운영자별 인증 정보와 역할 기반 권한

`AdminApi:Credentials`에 여러 운영자의 API 키와 역할을 등록할 수 있습니다.

- `AdminReader`: 운영 현황과 플레이어 요약 조회
- `AdminModerator`: 계정 정지 및 해제
- `AdminEconomy`: 코인 조정과 고액 조정 승인

기존 `AdminApi:ApiKey`, `OperatorId` 설정도 이전 배포와의 호환을 위해 유지합니다. 기존 단일 키에는 세 역할이 모두 부여되지만, 운영 환경에서는 반드시 운영자별 키와 최소 역할을 사용해야 합니다.

```json
{
  "AdminApi": {
    "Enabled": true,
    "RequireTwoPersonApprovalAtOrAbove": 5000,
    "Credentials": [
      { "OperatorId": "support-reader", "ApiKey": "SECRET_FROM_VAULT", "Roles": ["AdminReader"] },
      { "OperatorId": "economy-a", "ApiKey": "ANOTHER_SECRET_FROM_VAULT", "Roles": ["AdminEconomy"] }
    ]
  }
}
```

비밀 키는 설정 파일에 커밋하지 않고 환경 변수 또는 운영 비밀 저장소에서 주입합니다.

### 2. 고액 코인 조정 이중 승인

절댓값이 `RequireTwoPersonApprovalAtOrAbove` 이상인 직접 코인 조정은 `422 two_person_approval_required`로 거부됩니다. 다음 순서로 처리해야 합니다.

1. 요청자가 `POST /api/v1/admin/approvals/coin-adjustments`로 승인 요청을 생성합니다.
2. 다른 `AdminEconomy` 운영자가 `POST /api/v1/admin/approvals/{approvalId}/approve`를 호출합니다.
3. 서버는 승인 상태, 코인 변경, 관리자 감사 기록, 경제 원장을 함께 저장합니다.

승인 요청의 유효 시간은 24시간입니다. 요청자 본인의 승인은 `409`, 만료·revision 불일치·잔액 부족 등의 실행 실패는 `422`입니다. 승인 API를 재호출해도 코인과 감사 원장은 추가 기록되지 않습니다.

### 3. Unity 클라이언트 버전 호환성

공개 `GET /api/v1/version` 응답으로 서버 버전, API 계약 버전, 콘텐츠 버전, 최소 Unity 클라이언트 버전과 버전 헤더 강제 여부를 확인할 수 있습니다.

보호된 게임 API 요청에는 `X-Client-Version`을 사용할 수 있습니다. 지원 최소 버전보다 낮거나 형식이 잘못되면 서버는 HTTP `426`과 `client_upgrade_required`를 반환합니다. 모든 응답에는 `X-Server-Version`, `X-Content-Version`이 포함됩니다.

초기 배포에서는 `RequireVersionHeader=false`로 구형 클라이언트를 수용하고, Unity 배포가 완료된 뒤 `true`로 전환하는 순서를 권장합니다. 로그인·게스트 생성·콘텐츠·버전·관리자 경로는 복구 가능성을 위해 강제 검사 대상에서 제외됩니다.

### 4. CI와 공급망 재현성

`.github/workflows/server-ci.yml`은 pull request와 `main` push에서 다음 검사를 실행합니다.

1. lock file 고정 모드로 NuGet 복원
2. Release 빌드와 전체 테스트
3. EF 모델과 마이그레이션 불일치 검사
4. MySQL 8 서비스에 전체 마이그레이션 적용
5. 서버 publish와 런타임 Docker 이미지 빌드
6. 테스트 결과 업로드

`packages.lock.json`을 저장소에 포함했고 Dependabot이 NuGet, Docker, GitHub Actions 업데이트를 매주 제안합니다.

### 5. 배포와 데이터 복구 문서

- [배포 런북](../operations/deployment-runbook.md): 사전 검증, 마이그레이션, 순차 배포, 롤백 기준
- [MySQL 백업·복구 런북](../operations/mysql-backup-restore.md): 백업, 암호화 보관, 복구 리허설, 검증
- `scripts/verify-release.ps1`: 운영 비밀을 환경 변수로 받은 뒤 Release 빌드·테스트·EF 동기화·publish를 일괄 검증

## 데이터베이스 변경

`AdminApprovalRequests` 테이블을 추가했습니다. 요청 운영자와 멱등 키의 복합 고유 인덱스로 중복 요청을 차단하고, 대상 플레이어·금액·예상 revision·사유·승인자·만료 및 승인 시간을 보존합니다.

## 검증 항목

- 역할이 부족한 운영자 키의 `403` 응답
- 최소 클라이언트 버전 미달의 `426` 응답과 공개 버전 조회
- 요청자 자기 승인 차단
- 다른 운영자 승인 성공 및 재시도 시 단 한 번만 지급
- OpenAPI에 신규 버전·승인 경로 노출
- Release 빌드, 전체 테스트, EF pending model 검사

로컬 Docker 데몬이 실행되지 않는 환경에서도 단위·통합 테스트와 EF 모델 검사는 수행할 수 있습니다. 실제 MySQL 마이그레이션 및 컨테이너 빌드는 GitHub Actions CI가 매 push마다 검증합니다.
