using Stashr.Core.Model;
using Synthetix;

namespace Stashr.Server;

/// <summary>
/// Synthetix source-generated mappers (ADR-0013). The partial methods have no body — the
/// generator emits the mapping at compile time. SealStatus → SealStatusDto is a same-name
/// scalar mapping; more mappers join as the DTO surface grows.
/// </summary>
[Mapper]
public partial class StatusMapper
{
    public partial SealStatusDto ToDto(SealStatus status);
}
