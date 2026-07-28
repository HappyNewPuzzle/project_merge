using MergeGame.Server.Application.Players;
using MergeGame.Server.Endpoints;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;

// WebApplicationBuilder는 설정 파일, 환경 변수, 로깅, DI 컨테이너를 한 번에 준비합니다.
// 명령줄 인수를 전달해야 --urls 같은 ASP.NET Core 기본 옵션도 정상 동작합니다.
var builder = WebApplication.CreateBuilder(args);

// Windows 서비스용 Event Log 공급자는 제한된 실행 계정에서 로그 기록 권한 오류를 낼 수 있습니다.
// 콘솔 로그만 명시적으로 사용하면 로컬 개발, Docker, 클라우드 로그 수집기가 동일하게 표준 출력을 읽습니다.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    // 한 줄 형식은 컨테이너 로그 검색과 외부 수집기의 파싱을 단순하게 만듭니다.
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// 데이터 접근에 필요한 서비스를 한곳에서 등록합니다.
// Program.cs가 데이터베이스 구현 세부사항으로 복잡해지는 것을 막기 위해 확장 메서드로 분리했습니다.
builder.Services.AddPersistence(builder.Configuration);

// 게스트 계정 생성 유스케이스와 보안 토큰 생성기를 등록합니다.
// 인터페이스를 기준으로 등록해 테스트에서는 실제 난수 생성기 대신 예측 가능한 구현으로 교체할 수 있습니다.
builder.Services.AddScoped<CreateGuestPlayerService>();
builder.Services.AddSingleton<IGuestCredentialGenerator, GuestCredentialGenerator>();
builder.Services.AddSingleton(TimeProvider.System);

// 헬스 체크는 서버 프로세스와 MySQL 연결 상태를 외부 모니터링 시스템에 알려 줍니다.
// 데이터베이스 검사는 AddPersistence 내부에서 등록합니다.
builder.Services.AddHealthChecks();

var app = builder.Build();

// HTTPS 리디렉션을 적용해 로그인 토큰이나 게임 데이터가 평문 HTTP로 전달되는 것을 방지합니다.
// 로컬 개발에서 HTTP 주소만 사용할 때는 launchSettings.json의 HTTPS 프로필을 사용하면 됩니다.
app.UseHttpsRedirection();

// 단계별 엔드포인트 매핑을 별도 파일로 분리해 Program.cs를 애플리케이션 조립 역할에 집중시킵니다.
app.MapServerEndpoints();

app.Run();

// 향후 통합 테스트 프로젝트가 실제 서버 진입점을 참조할 수 있도록 Program 형식을 공개합니다.
public partial class Program;
