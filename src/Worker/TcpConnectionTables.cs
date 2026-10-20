using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace PetTracker;

public delegate uint TcpTableQuery(IntPtr buffer, ref int size, int family);

public static class TcpConnectionTables
{
    const uint InsufficientBuffer = 122;
    const int MaxTableBytes = 4000000, MaxReads = 3;

    // Windows can grow the table between sizing and reading it. Never publish
    // an empty/partial connection list when either family could not be read.
    public static IReadOnlyList<GameConnectionPeer> Read(IReadOnlySet<int> owners, TcpTableQuery query)
    {
        var peers = new List<GameConnectionPeer>();
        foreach (int family in new[] { 2, 23 })
        {
            byte[] table = ReadTable(family, query);
            int stride = family == 2 ? 24 : 56;
            int count = BitConverter.ToInt32(table, 0);
            if (count < 0 || count > (table.Length - 4) / stride)
                throw new InvalidDataException("Invalid TCP table row count.");
            for (int i = 0; i < count; i++)
            {
                int p = 4 + i * stride, pid = BitConverter.ToInt32(table, p + (family == 2 ? 20 : 52));
                if (!owners.Contains(pid))
                    continue;
                int lp = p + (family == 2 ? 8 : 20), rp = p + (family == 2 ? 16 : 44);
                string local = family == 2 ? new IPAddress(table.AsSpan(p + 4, 4)).ToString() : new IPAddress(table.AsSpan(p, 16), BitConverter.ToUInt32(table, p + 16)).ToString();
                string remote = family == 2 ? new IPAddress(table.AsSpan(p + 12, 4)).ToString() : new IPAddress(table.AsSpan(p + 24, 16), BitConverter.ToUInt32(table, p + 40)).ToString();
                peers.Add(new(remote, (table[rp] << 8) | table[rp + 1], local, (table[lp] << 8) | table[lp + 1]));
            }
        }
        return peers;
    }

    static byte[] ReadTable(int family, TcpTableQuery query)
    {
        int size = 0;
        uint result = query(IntPtr.Zero, ref size, family);
        if (result != 0 && result != InsufficientBuffer)
            throw new Win32Exception((int)result);
        for (int attempt = 0; attempt < MaxReads; attempt++)
        {
            if (size < 4 || size > MaxTableBytes)
                throw new InvalidDataException("Invalid TCP table size.");
            int capacity = size;
            IntPtr buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                result = query(buffer, ref size, family);
                if (result == InsufficientBuffer)
                    continue;
                if (result != 0)
                    throw new Win32Exception((int)result);
                if (size < 4 || size > capacity)
                    throw new InvalidDataException("Invalid TCP table result size.");
                byte[] table = new byte[size];
                Marshal.Copy(buffer, table, 0, size);
                return table;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        throw new Win32Exception((int)InsufficientBuffer, "TCP connection table kept changing during discovery.");
    }
}
