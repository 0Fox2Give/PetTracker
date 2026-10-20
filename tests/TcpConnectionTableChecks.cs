using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using PetTracker;

static class TcpConnectionTableChecks
{
    public static void Run(Action<bool, string> check)
    {
        byte[] Row(int family, int owner)
        {
            bool v4 = family == 2;
            var b = new byte[4 + (v4 ? 24 : 56)];
            BitConverter.GetBytes(1).CopyTo(b, 0);
            int p = 4;
            IPAddress.Parse(v4 ? "192.0.2.2" : "fe80::2").GetAddressBytes().CopyTo(b, p + (v4 ? 4 : 0));
            IPAddress.Parse(v4 ? "192.0.2.1" : "fe80::1").GetAddressBytes().CopyTo(b, p + (v4 ? 12 : 24));
            if (!v4)
            {
                BitConverter.GetBytes(3).CopyTo(b, p + 16);
                BitConverter.GetBytes(4).CopyTo(b, p + 40);
            }
            int lp = p + (v4 ? 8 : 20), rp = p + (v4 ? 16 : 44);
            b[lp] = 0xc3;
            b[lp + 1] = 0x51;
            b[rp] = 0x34;
            b[rp + 1] = 0x10;
            BitConverter.GetBytes(owner).CopyTo(b, p + (v4 ? 20 : 52));
            return b;
        }
        var owners = new HashSet<int> { 101 };
        int reads = 0;
        bool resize = true, failV6 = false, empty = false;
        uint Query(IntPtr buffer, ref int size, int family)
        {
            byte[] data = empty ? new byte[4] : Row(family, 101);
            if (failV6 && family == 23)
                return 5;
            if (buffer == IntPtr.Zero)
            {
                size = resize ? 4 : data.Length;
                return 122;
            }
            reads++;
            if (size < data.Length)
            {
                size = data.Length;
                return 122;
            }
            Marshal.Copy(data, 0, buffer, data.Length);
            size = data.Length;
            return 0;
        }
        var peers = TcpConnectionTables.Read(owners, Query);
        check(peers.Count == 2 && reads == 4, "Growing native tables retry both families instead of publishing missing endpoints");
        check(peers[0] == new GameConnectionPeer("192.0.2.1", 13328, "192.0.2.2", 50001), "Native IPv4 owner row preserves address and network-order port fields");
        check(peers[1] == new GameConnectionPeer("fe80::1%4", 13328, "fe80::2%3", 50001), "Native IPv6 owner row preserves independent address scopes and ports");
        check(TcpConnectionTables.Read(new HashSet<int> { 202 }, Query).Count == 0, "Native connection discovery excludes unrelated process owners");
        resize = false;
        long clock = 0;
        var connections = new GameConnections(_ => new(owners, TcpConnectionTables.Read(owners, Query)), () => clock);
        connections.Refresh();
        failV6 = true;
        bool failed = false;
        try
        {
            connections.Refresh();
        }
        catch (Win32Exception e)
        {
            failed = e.NativeErrorCode == 5;
        }
        check(failed && connections.GameRunning && connections.Generation == 1 && connections.Allows("fe80::1%4", 13328, "fe80::2%3", 50001), "Failure in the second native family cannot publish a partial snapshot or erase its established peer");
        clock += 200;
        check(connections.Allows("fe80::1%4", 13328, "fe80::2%3", 50001), "Native OS scan errors do not discard the established packet that requested discovery");
        failV6 = false;
        empty = true;
        connections.Refresh();
        check(connections.GameRunning && connections.Generation == 1 && !connections.Allows("192.0.2.1", 13328, "192.0.2.2", 50001), "Successful empty native tables revoke closed endpoints without inventing a process exit");
        int attempts = 0;
        uint Churn(IntPtr buffer, ref int size, int family)
        {
            if (buffer != IntPtr.Zero)
                attempts++;
            size += 24;
            return 122;
        }
        failed = false;
        try
        {
            TcpConnectionTables.Read(owners, Churn);
        }
        catch (Win32Exception e)
        {
            failed = e.NativeErrorCode == 122;
        }
        check(failed && attempts == 3, "Repeated table growth is bounded to three native reads and never accepted as empty");
        foreach (int invalidSize in new[] { 0, 3, 4000001 })
        {
            int fills = 0;
            uint BadSize(IntPtr buffer, ref int size, int family)
            {
                if (buffer != IntPtr.Zero)
                    fills++;
                size = invalidSize;
                return 122;
            }
            failed = false;
            try
            {
                TcpConnectionTables.Read(owners, BadSize);
            }
            catch (InvalidDataException)
            {
                failed = true;
            }
            check(failed && fills == 0, "Invalid native allocation size is rejected before any buffer fill: " + invalidSize);
        }
        uint OversizedResult(IntPtr buffer, ref int size, int family)
        {
            if (buffer == IntPtr.Zero)
            {
                size = 4;
                return 122;
            }
            size = 8;
            return 0;
        }
        failed = false;
        try
        {
            TcpConnectionTables.Read(owners, OversizedResult);
        }
        catch (InvalidDataException)
        {
            failed = true;
        }
        check(failed, "A successful native status cannot copy past its allocated buffer");
        foreach (int count in new[] { -1, 1 })
        {
            uint BadCount(IntPtr buffer, ref int size, int family)
            {
                size = 4;
                if (buffer == IntPtr.Zero)
                    return 122;
                Marshal.WriteInt32(buffer, count);
                return 0;
            }
            failed = false;
            try
            {
                TcpConnectionTables.Read(owners, BadCount);
            }
            catch (InvalidDataException)
            {
                failed = true;
            }
            check(failed, "Invalid native row count cannot be published as an empty family: " + count);
        }
    }
}
