using MergeGame.Server.Application.Authentication;
using MergeGame.Server.Application.Boards;
using MergeGame.Server.Application.Economy;
using MergeGame.Server.Application.Players;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Application.Social;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Endpoints;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Observability;
using MergeGame.Server.Infrastructure.OpenApi;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using MergeGame.Server.Infrastructure.Social;

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
builder.Services.AddScoped<AuthenticateGuestPlayerService>();
builder.Services.AddScoped<GetPlayerProfileService>();
builder.Services.AddScoped<InitializePlayerBoardService>();
builder.Services.AddScoped<GetPlayerBoardService>();
builder.Services.AddScoped<MergeBoardItemsService>();
builder.Services.AddScoped<InitializeEconomyService>();
builder.Services.AddScoped<GetEconomyService>();
builder.Services.AddScoped<ClaimDailyRewardService>();
builder.Services.AddScoped<GenerateBoardItemService>();
builder.Services.AddScoped<QuestQueryService>();
builder.Services.AddScoped<ClaimQuestRewardService>();
builder.Services.AddScoped<InitializeSocialProfileService>();
builder.Services.AddScoped<GetSocialProfileService>();
builder.Services.AddScoped<AddFriendService>();
builder.Services.AddScoped<SendFriendEnergyGiftService>();
builder.Services.AddSingleton<IGuestCredentialGenerator, GuestCredentialGenerator>();
builder.Services.AddSingleton<IFriendCodeGenerator, FriendCodeGenerator>();
builder.Services.AddSingleton<IItemCatalog, InMemoryItemCatalog>();
builder.Services.AddSingleton(TimeProvider.System);

// JWT 검증, 현재 플레이어 식별, 로그인 요청 속도 제한을 한 번에 등록합니다.
// 서명 키가 누락되거나 안전하지 않으면 등록 시 즉시 실패해 잘못된 인증 서버가 실행되지 않게 합니다.
builder.Services.AddPlayerAuthentication(builder.Configuration);

// 헬스 체크는 서버 프로세스와 MySQL 연결 상태를 외부 모니터링 시스템에 알려 줍니다.
// 데이터베이스 검사는 AddPersistence 내부에서 등록합니다.
builder.Services.AddHealthChecks();

// 모든 처리되지 않은 예외와 프레임워크 오류를 동일한 ProblemDetails 형식으로 반환합니다.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // 클라이언트 오류 문의와 서버 로그를 연결할 수 있도록 모든 오류 응답에 traceId를 포함합니다.
        context.ProblemDetails.Extensions["traceId"] =
            context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 실행 중인 엔드포인트 메타데이터와 XML 주석에서 OpenAPI v1 계약을 생성합니다.
// Unity 클라이언트와 서버가 동일한 요청·응답 구조를 공유하는 기준 문서입니다.
builder.Services.AddMergeGameOpenApi();

var app = builder.Build();

// 요청 추적 ID를 가장 먼저 확정해 이후 미들웨어와 예외 로그가 같은 식별자를 공유합니다.
app.UseMiddleware<RequestTraceMiddleware>();
app.UseExceptionHandler();

// JSON 계약은 도구가 읽는 주소이고 Swagger UI는 개발자가 직접 API를 시험하는 화면입니다.
// 인증 API의 사용법도 문서에 포함되지만 실제 JWT 값은 서버에 저장되지 않습니다.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Merge Game API v1");
    options.RoutePrefix = "docs";
    options.DocumentTitle = "Merge Game API v1";
});

// HTTPS 리디렉션을 적용해 로그인 토큰이나 게임 데이터가 평문 HTTP로 전달되는 것을 방지합니다.
// 로컬 개발에서 HTTP 주소만 사용할 때는 launchSettings.json의 HTTPS 프로필을 사용하면 됩니다.
app.UseHttpsRedirection();

// 엔드포인트별 속도 제한은 라우트 선택 이후 적용되어야 하므로 명시적으로 라우팅을 먼저 실행합니다.
app.UseRouting();
app.UseRateLimiter();

// 인증이 Authorization 헤더를 검증해 User를 만든 뒤, 권한 미들웨어가 보호 API 접근을 결정합니다.
app.UseAuthentication();
// 인증 결과가 만들어진 뒤 쓰기 요청만 감사 로그로 기록하며 토큰과 요청 본문은 기록하지 않습니다.
app.UseMiddleware<AuditLoggingMiddleware>();
app.UseAuthorization();

// 단계별 엔드포인트 매핑을 별도 파일로 분리해 Program.cs를 애플리케이션 조립 역할에 집중시킵니다.
app.MapServerEndpoints();

app.Run();

// 향후 통합 테스트 프로젝트가 실제 서버 진입점을 참조할 수 있도록 Program 형식을 공개합니다.
public partial class Program;
