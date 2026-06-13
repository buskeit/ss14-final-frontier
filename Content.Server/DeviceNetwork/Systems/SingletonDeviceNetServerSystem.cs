using Content.Server.DeviceNetwork.Components;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Power;
using System.Diagnostics.CodeAnalysis;

namespace Content.Server.DeviceNetwork.Systems;

/// <summary>
/// Keeps one active server entity per station. Activates another available one if the currently active server becomes unavailable
/// Server in this context means an entity that manages the devicenet packets like the <see cref="Content.Server.Medical.CrewMonitoring.CrewMonitoringServerSystem"/>
/// </summary>
public sealed class SingletonDeviceNetServerSystem : EntitySystem
{
    [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    private float _reconnectDelay = 1.0f;
    private float _reconnectTimer = 0f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _reconnectTimer += frameTime;
        if (_reconnectTimer < _reconnectDelay)
            return;
        _reconnectTimer = 0f;

        var query = EntityQueryEnumerator<SingletonDeviceNetServerComponent, DeviceNetworkComponent>();
        while (query.MoveNext(out var uid, out var server, out var device))
        {
            if (!server.Available || !server.Active)
                continue;

            // Avoid relying only on PowerChangedEvent
            var isPowered = this.IsPowered(uid, EntityManager);
            if (server.Available != isPowered)
            {
                server.Available = isPowered;
                if (!isPowered)
                {
                    DisconnectServer(uid, server, device);
                    continue;
                }
            }

            if (!_deviceNetworkSystem.IsDeviceConnected(uid, device))
            {
                _deviceNetworkSystem.ConnectDevice(uid, device);
            }
        }
    }

    private void OnMapInit(EntityUid uid, SingletonDeviceNetServerComponent component, MapInitEvent args)
    {
        EnsureConnected(uid, component);
    }

    private void OnStartup(EntityUid uid, SingletonDeviceNetServerComponent component, ComponentStartup args)
    {
        EnsureConnected(uid, component);
    }

    private void EnsureConnected(EntityUid uid, SingletonDeviceNetServerComponent component)
    {
        if (!component.Available || !component.Active)
            return;

        if (!TryComp<DeviceNetworkComponent>(uid, out var device))
            return;

        if (!_deviceNetworkSystem.IsDeviceConnected(uid, device))
        {
            _deviceNetworkSystem.ConnectDevice(uid, device);
        }
    }

    /// <summary>
    /// Returns whether the given entity is an active server or not
    /// </summary>
    public bool IsActiveServer(EntityUid serverId, SingletonDeviceNetServerComponent? serverComponent = default)
    {
        if (!Resolve(serverId, ref serverComponent))
            return false;

        if (!serverComponent.Active)
            return false;

        // Ensure it is connected to the device network if active and available
        if (serverComponent.Available && TryComp<DeviceNetworkComponent>(serverId, out var device))
        {
            // Avoid relying only on PowerChangedEvent
            var isPowered = this.IsPowered(serverId, EntityManager);
            if (serverComponent.Available != isPowered)
            {
                serverComponent.Available = isPowered;
                if (!isPowered)
                {
                    DisconnectServer(serverId, serverComponent, device);
                    return false;
                }
            }

            if (!_deviceNetworkSystem.IsDeviceConnected(serverId, device))
            {
                _deviceNetworkSystem.ConnectDevice(serverId, device);
            }
        }

        return serverComponent.Active;
    }

    /// <summary>
    /// Returns the address of the currently active server for the given station id if there is one.<br/>
    /// What kind of server you're trying to get the active instance of is determined by the component type parameter TComp.<br/>
    /// <br/>
    /// Setting TComp to <see cref="CrewMonitoringServerComponent"/>, for example, gives you the address of an entity containing the crew monitoring server component.<br/>
    /// </summary>
    /// <param name="stationId">The entityUid of the station</param>
    /// <param name="address">The address of the active server if it exists</param>
    /// <typeparam name="TComp">The component type that determines what type of server you're getting the address of</typeparam>
    /// <returns>True if there is an active serve. False otherwise</returns>
    public bool TryGetActiveServerAddress<TComp>(EntityUid stationId, [NotNullWhen(true)] out string? address) where TComp : IComponent
    {
        var servers = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TComp
        >();

        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? last = default;

        while (servers.MoveNext(out var uid, out var server, out var device, out _))
        {
            if (!_stationSystem.GetOwningStation(uid)?.Equals(stationId) ?? true)
                continue;

            // Avoid relying only on PowerChangedEvent
            var isPowered = this.IsPowered(uid, EntityManager);
            if (server.Available != isPowered)
            {
                server.Available = isPowered;
                if (!isPowered && server.Active)
                {
                    DisconnectServer(uid, server, device);
                }
            }

            if (!server.Available)
            {
                continue;
            }

            last = (uid, server, device);

            if (!server.Active || string.IsNullOrEmpty(device.Address))
                continue;

            // Verify the active server is actually connected in DeviceNetworkSystem
            if (!_deviceNetworkSystem.IsDeviceConnected(uid, device))
            {
                _deviceNetworkSystem.ConnectDevice(uid, device);
            }

            address = device.Address;
            return true;
        }

        //If there was no active server for the station make the last available inactive one active
        if (last.HasValue)
        {
            ConnectServer(last.Value.id, last.Value.server, last.Value.device);
            address = last.Value.device.Address;
            return true;
        }

        address = null;
        return address != null;
    }

    /// <summary>
    /// Disconnects the server losing power
    /// </summary>
    private void OnPowerChanged(EntityUid uid, SingletonDeviceNetServerComponent component, ref PowerChangedEvent args)
    {
        component.Available = args.Powered;

        if (!args.Powered && component.Active)
            DisconnectServer(uid, component);
    }

    private void ConnectServer(EntityUid uid, SingletonDeviceNetServerComponent? server = null, DeviceNetworkComponent? device = null)
    {
        if (!Resolve(uid, ref server, ref device))
            return;

        server.Active = true;

        var connectedEvent = new DeviceNetServerConnectedEvent();
        RaiseLocalEvent(uid, ref connectedEvent);

        if (_deviceNetworkSystem.IsDeviceConnected(uid, device))
            return;

        _deviceNetworkSystem.ConnectDevice(uid, device);
    }

    /// <summary>
    /// Disconnects a server from the device network and clears the currently active server
    /// </summary>
    private void DisconnectServer(EntityUid uid, SingletonDeviceNetServerComponent? server = null, DeviceNetworkComponent? device = null)
    {
        if (!Resolve(uid, ref server, ref device))
            return;

        server.Active = false;

        var disconnectedEvent = new DeviceNetServerDisconnectedEvent();
        RaiseLocalEvent(uid, ref disconnectedEvent);

        _deviceNetworkSystem.DisconnectDevice(uid, device, false);
    }
}

/// <summary>
/// Raised when a server gets activated and connected to the device net
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerConnectedEvent;

/// <summary>
/// Raised when a server gets disconnected
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerDisconnectedEvent;
