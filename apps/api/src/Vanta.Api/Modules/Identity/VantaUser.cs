using Microsoft.AspNetCore.Identity;

namespace Vanta.Api.Modules.Identity;

// A user always belongs to exactly one tenant (no cross-tenant accounts in the MVP).
public sealed class VantaUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}
