using System;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

// Unity Transport owns UDP connection establishment, ordering, retries and timeouts.
public sealed class LanFightTransport : IDisposable
{
    private NetworkDriver driver;
    private NetworkConnection connection;
    private NetworkPipeline statePipeline;
    private NetworkPipeline controlPipeline;
    private bool hosting;
    public bool Connected { get; private set; }
    public string Status { get; private set; } = "Offline";
    public Action<byte[]> Received;
    public Action Joined;
    public Action Left;

    public void Host(ushort port)
    {
        Create();
        hosting = true;
        if (driver.Bind(NetworkEndpoint.AnyIpv4.WithPort(port)) != 0 || driver.Listen() != 0)
        {
            Dispose();
            throw new InvalidOperationException("UDP port is already in use: " + port);
        }
        Status = "Waiting for opponent";
    }

    public void Join(string address, ushort port)
    {
        if (!NetworkEndpoint.TryParse(address, port, out var endpoint))
            throw new ArgumentException("Enter a valid IPv4 address.");
        Create();
        connection = driver.Connect(endpoint);
        Status = "Connecting";
    }

    private void Create()
    {
        Dispose();
        driver = NetworkDriver.Create();
        statePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
        controlPipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
    }

    public void Pump()
    {
        if (!driver.IsCreated) return;
        driver.ScheduleUpdate().Complete();
        if (hosting)
        {
            NetworkConnection incoming;
            while ((incoming = driver.Accept()) != default)
            {
                if (connection.IsCreated) driver.Disconnect(incoming);
                else
                {
                    connection = incoming;
                    Connected = true;
                    Status = "Connected";
                    Joined?.Invoke();
                }
            }
        }
        if (!connection.IsCreated) return;
        NetworkEvent.Type kind;
        while ((kind = driver.PopEventForConnection(connection, out var reader)) != NetworkEvent.Type.Empty)
        {
            if (kind == NetworkEvent.Type.Connect)
            {
                Connected = true;
                Status = "Connected";
                Joined?.Invoke();
            }
            else if (kind == NetworkEvent.Type.Disconnect)
            {
                connection = default;
                Connected = false;
                Status = "Disconnected";
                Left?.Invoke();
                break;
            }
            else if (kind == NetworkEvent.Type.Data && reader.Length <= 1200)
            {
                var bytes = new byte[reader.Length];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = reader.ReadByte();
                Received?.Invoke(bytes);
            }
        }
    }

    public bool Send(byte[] bytes, bool reliable = false)
    {
        if (!Connected || bytes.Length > 1200) return false;
        if (driver.BeginSend(reliable ? controlPipeline : statePipeline, connection, out var writer) != 0) return false;
        foreach (var value in bytes) writer.WriteByte(value);
        return driver.EndSend(writer) >= 0;
    }

    public void Dispose()
    {
        if (driver.IsCreated)
        {
            if (connection.IsCreated) { driver.Disconnect(connection); driver.ScheduleUpdate().Complete(); }
            driver.Dispose();
        }
        connection = default;
        Connected = false;
        hosting = false;
        Status = "Offline";
    }
}
