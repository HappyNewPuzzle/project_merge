namespace MergeGame.Server.Domain.Generators;

/// <summary>
/// 플레이어별 생성기의 충전량과 회복 기준 시각을 관리합니다.
/// 에너지와 별도로 충전량을 두어 짧은 시간에 무제한 생성하는 요청을 서버에서 차단합니다.
/// </summary>
public sealed class PlayerGenerator
{
    private PlayerGenerator()
    {
    }

    public Guid PlayerId { get; private set; }
    public string GeneratorId { get; private set; } = string.Empty;
    public int Charges { get; private set; }
    public long Revision { get; private set; }
    public DateTime ChargeUpdatedAtUtc { get; private set; }

    /// <summary>처음 사용하는 생성기는 최대 충전 상태로 시작합니다.</summary>
    public static PlayerGenerator CreateInitial(
        Guid playerId,
        GeneratorDefinition definition,
        DateTime nowUtc) => new()
        {
            PlayerId = playerId,
            GeneratorId = definition.Id,
            Charges = definition.MaxCharges,
            Revision = 1,
            ChargeUpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };

    /// <summary>
    /// 현재 서버 시각까지 회복된 충전량 하나를 소비합니다.
    /// 실제 DB 동시 요청은 Revision 동시성 토큰이 한 번 더 보호합니다.
    /// </summary>
    public bool TryConsumeCharge(DateTime nowUtc, GeneratorDefinition definition)
    {
        var projected = CreateSnapshot(nowUtc, definition);
        if (projected.Charges <= 0)
        {
            return false;
        }

        Charges = projected.Charges - 1;
        // 최대 충전 상태에서 처음 소비한 순간부터 다음 충전 시간이 흐르기 시작합니다.
        ChargeUpdatedAtUtc = projected.Charges >= definition.MaxCharges
            ? DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
            : projected.ChargeUpdatedAtUtc;
        Revision++;
        return true;
    }

    /// <summary>DB를 바꾸지 않고 회복분을 반영한 클라이언트용 상태를 계산합니다.</summary>
    public GeneratorState CreateSnapshot(DateTime nowUtc, GeneratorDefinition definition)
    {
        var utcNow = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var projectedCharges = Charges;
        var projectedAnchor = ChargeUpdatedAtUtc;

        if (projectedCharges >= definition.MaxCharges)
        {
            projectedCharges = definition.MaxCharges;
            projectedAnchor = utcNow;
        }
        else
        {
            var elapsed = utcNow - projectedAnchor;
            if (elapsed > TimeSpan.Zero)
            {
                var recovered = (int)(elapsed.Ticks / definition.ChargeRecoveryInterval.Ticks);
                if (recovered > 0)
                {
                    projectedCharges = Math.Min(definition.MaxCharges, projectedCharges + recovered);
                    projectedAnchor = projectedCharges >= definition.MaxCharges
                        ? utcNow
                        : projectedAnchor.AddTicks(definition.ChargeRecoveryInterval.Ticks * recovered);
                }
            }
        }

        var nextChargeAtUtc = projectedCharges >= definition.MaxCharges
            ? (DateTime?)null
            : projectedAnchor.Add(definition.ChargeRecoveryInterval);
        var remainingSeconds = nextChargeAtUtc is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((nextChargeAtUtc.Value - utcNow).TotalSeconds));

        return new GeneratorState(
            GeneratorId,
            projectedCharges,
            definition.MaxCharges,
            projectedCharges == 0,
            nextChargeAtUtc,
            remainingSeconds,
            Revision,
            projectedAnchor);
    }
}

/// <summary>생성기 UI가 충전 및 쿨다운을 표시하는 데 필요한 서버 계산 결과입니다.</summary>
public sealed record GeneratorState(
    string GeneratorId,
    int Charges,
    int MaxCharges,
    bool IsCoolingDown,
    DateTime? NextChargeAtUtc,
    int CooldownRemainingSeconds,
    long Revision,
    DateTime ChargeUpdatedAtUtc);
