using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using UnityEngine;

public sealed class LanRoomDiscovery : IDisposable
{
    public const int DiscoveryPort = 47777;
    private const string Query = "WEBFIGHT-LAN-3";
    [Serializable]
    public sealed class Room
    {
        public string protocol = Query;
        public string name;
        public int port;
        public int players;
        public bool locked;
        [NonSerialized] public string address;
        [NonSerialized] public float seen;
        public string Id => address + ":" + port;
    }
    private UdpClient socket;
    private bool hosting;
    private float nextSearch;
    private float nextPoll;
    private readonly List<IPEndPoint> searchTargets = new List<IPEndPoint>();
    private static readonly byte[] QueryBytes = Encoding.ASCII.GetBytes(Query);
    private Room advertisement;
    public readonly List<Room> Rooms = new List<Room>();
    public int Revision { get; private set; }
    public string Error { get; private set; } = "";

    public void StartHost(string name, int port, bool locked)
    {
        Dispose(); hosting = true;
        Error = "";
        try
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            socket.Client.Blocking = false;
            advertisement = new Room { name = name, port = port, players = 1, locked = locked };
        }
        catch (SocketException error) { Dispose(); Error = error.Message; }
    }

    public void StartSearch()
    {
        Dispose(); hosting = false;
        Error = "";
        try
        {
            socket = new UdpClient(0);
            socket.EnableBroadcast = true;
            socket.Client.Blocking = false;
            RefreshSearchTargets();
            SearchNow();
        }
        catch (SocketException error) { Dispose(); Error = error.Message; }
    }

    public void SearchNow() { nextSearch = 0; }

    private void RefreshSearchTargets()
    {
        searchTargets.Clear();
        searchTargets.Add(new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        searchTargets.Add(new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
        // Limited broadcasts are not routed to every active Wi-Fi/Ethernet interface.
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (entry.Address.AddressFamily != AddressFamily.InterNetwork || entry.IPv4Mask == null) continue;
                    byte[] ip = entry.Address.GetAddressBytes(), mask = entry.IPv4Mask.GetAddressBytes();
                    if (mask[0] == 0) continue;
                    for (int i = 0; i < 4; i++) ip[i] |= (byte)~mask[i];
                    var endpoint = new IPEndPoint(new IPAddress(ip), DiscoveryPort);
                    if (!searchTargets.Contains(endpoint)) searchTargets.Add(endpoint);
                }
            }
        }
        catch (NetworkInformationException error) { Error = error.Message; }
    }

    public void Pump(int players = 1, bool playing = false)
    {
        if (socket == null) return;
        float now = Time.realtimeSinceStartup;
        if (now < nextPoll && (hosting || now < nextSearch)) return;
        nextPoll = now + 1f / 30f;
        try
        {
            if (!hosting && now >= nextSearch)
            {
                nextSearch = now + 1;
                foreach (var endpoint in searchTargets)
                {
                    try { socket.Send(QueryBytes, QueryBytes.Length, endpoint); }
                    catch (SocketException error) { Error = error.Message; }
                }
                if (Rooms.RemoveAll(room => now - room.seen > 4) > 0) Revision++;
            }
            for (int i = 0; i < 32 && socket.Available > 0; i++)
            {
                var source = new IPEndPoint(IPAddress.Any, 0);
                var bytes = socket.Receive(ref source);
                if (bytes.Length > 1024) continue;
                var text = Encoding.UTF8.GetString(bytes);
                if (hosting)
                {
                    if (text != Query || playing || players >= 2) continue;
                    advertisement.players = players;
                    var response = Encoding.UTF8.GetBytes(JsonUtility.ToJson(advertisement));
                    socket.Send(response, response.Length, source);
                }
                else if (text.StartsWith("{"))
                {
                    Room room;
                    try { room = JsonUtility.FromJson<Room>(text); } catch (ArgumentException) { continue; }
                    if (room == null || room.protocol != Query || string.IsNullOrEmpty(room.name) || room.name.Length > 32 || room.port < 1 || room.port > 65535) continue;
                    room.address = source.Address.ToString(); room.seen = now;
                    var existing = Rooms.Find(item => item.Id == room.Id);
                    if (existing == null) { if (Rooms.Count < 64) { Rooms.Add(room); Revision++; } }
                    else { existing.seen = now; if (existing.name != room.name || existing.players != room.players || existing.locked != room.locked) { Rooms.Remove(existing); Rooms.Add(room); Revision++; } }
                }
            }
        }
        catch (SocketException error) { Error = error.Message; }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() { socket?.Dispose(); socket = null; nextPoll = nextSearch = 0; Rooms.Clear(); Revision++; }
}
