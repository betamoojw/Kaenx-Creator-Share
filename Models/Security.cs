

namespace Kaenx.Creator.Models;

public class Security
{
    public int MaxUserEntries { get; set; } = 1;
    public int MaxTunnelingUserEntries { get; set; } = 0;
    public int MaxSecurityProxyGroupKeyTableEntries { get; set; } = 0;
    public int MaxSecurityP2PKeyTableEntries { get; set; } = 0;
    public int MaxSecurityIndividualAddressEntries { get; set; } = 0;
    public int MaxSecurityGroupKeyTableEntries { get; set; } = 0;
}