using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EnclosureHub.Core;

public static class LocalNetwork
{
    /// <summary>
    /// Best guess of the address other devices on the LAN can reach this host on: the first IPv4
    /// address of an operational, non-loopback interface, preferring interfaces with a gateway.
    /// </summary>
    public static IPAddress? FindLocalIPv4()
    {
        IPAddress? fallback = null;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var properties = nic.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Count > 0;

                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                        continue;

                    if (hasGateway)
                        return address;

                    fallback ??= address;
                }
            }
        }
        catch (NetworkInformationException)
        {
            // no permission / unsupported platform – caller falls back to the host name
        }

        return fallback;
    }
}
