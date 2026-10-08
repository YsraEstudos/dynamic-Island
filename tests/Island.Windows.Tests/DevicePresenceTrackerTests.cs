using Island.Windows.Devices;

namespace Island.Windows.Tests;

public sealed class DevicePresenceTrackerTests
{
    [Fact]
    public void InitialEnumerationIsSilentAndConnectedStateIsEmittedAfterBaseline()
    {
        var tracker = new DevicePresenceTracker();

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "container-1", true, "USB Drive"));
        tracker.CompleteBaseline();

        var transitions = tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-2", "container-2", true, "Camera");

        var transition = Assert.Single(transitions);
        Assert.True(transition.Connected);
        Assert.Equal("Camera", transition.Name);
    }

    [Fact]
    public void MultipleUsbInterfacesProduceOneConnectionAndRemoveOnlyLastInterfaceDisconnects()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "container-1", true, "USB Drive");
        tracker.CompleteBaseline();

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-2", "container-1", true, "USB Drive"));
        Assert.Empty(tracker.Remove("if-1"));

        var transition = Assert.Single(tracker.Remove("if-2"));
        Assert.False(transition.Connected);
        Assert.Equal("USB Drive", transition.Name);
    }

    [Fact]
    public void SecondConnectedEndpointInAnAlreadyConnectedContainerDoesNotDuplicateConnection()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "container-1", true, "USB Drive");
        tracker.CompleteBaseline(DevicePresenceKind.Usb);

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-2", "container-1", true, "USB Drive"));
    }

    [Fact]
    public void BluetoothDisconnectWaitsUntilTheLastConnectedEndpointIsGone()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", true, "Headset");
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-2", "container-1", true, "Headset");
        tracker.CompleteBaseline(DevicePresenceKind.Bluetooth);

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", false, null));
        var disconnected = Assert.Single(
            tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-2", "container-1", false, null));
        Assert.False(disconnected.Connected);
    }

    [Fact]
    public void UsbAndBluetoothBaselinesCompleteIndependently()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "usb-1", true, "Drive");
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "bt-1", true, "Headset");
        tracker.CompleteBaseline(DevicePresenceKind.Usb);

        Assert.Single(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-2", "usb-2", true, "Camera"));
        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-2", "bt-2", true, "Mouse"));

        tracker.CompleteBaseline(DevicePresenceKind.Bluetooth);
        Assert.Single(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-3", "bt-3", true, "Keyboard"));
    }

    [Fact]
    public void UsbAndBluetoothWithSameContainerTextRemainIndependent()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "shared-id", true, "Drive");
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "shared-id", true, "Headset");
        tracker.CompleteBaseline();

        var usbRemoved = Assert.Single(tracker.Remove("if-1"));
        Assert.Equal(DevicePresenceKind.Usb, usbRemoved.Kind);
        Assert.False(usbRemoved.Connected);
        Assert.Equal("Drive", usbRemoved.Name);

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "shared-id", true, null));
    }

    [Fact]
    public void EnrichingAnEndpointGroupDoesNotDuplicateItsExistingConnection()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "if-1", true, null);
        tracker.CompleteBaseline(DevicePresenceKind.Usb);

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-1", "container-1", true, "Drive"));
        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Usb, "if-2", "container-1", true, "Drive"));
    }

    [Fact]
    public void PartialUpdateRetainsNameAndBluetoothPairingWithoutConnectionIsSilent()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", false, "Headset");
        tracker.CompleteBaseline();

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", false, null));
        var connected = Assert.Single(
            tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", true, null));
        Assert.True(connected.Connected);
        Assert.Equal("Headset", connected.Name);

        var disconnected = Assert.Single(
            tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", false, null));
        Assert.False(disconnected.Connected);
        Assert.Equal("Headset", disconnected.Name);
    }

    [Fact]
    public void DuplicateStateAndDuplicateRemoveDoNotEmitTransitions()
    {
        var tracker = new DevicePresenceTracker();
        tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", true, "Mouse");
        tracker.CompleteBaseline();

        Assert.Empty(tracker.AddOrUpdate(DevicePresenceKind.Bluetooth, "aep-1", "container-1", true, "Mouse"));
        Assert.Single(tracker.Remove("aep-1"));
        Assert.Empty(tracker.Remove("aep-1"));
    }
}
