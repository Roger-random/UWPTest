# UWP Tests
Basic apps exploring Microsoft's [Universal Windows Platform (UWP)](https://docs.microsoft.com/en-us/windows/uwp/)
* __Hello3DP__ : A "Hello World" for using a
[Windows.Devices.SerialCommunications.SerialDevice](https://docs.microsoft.com/en-us/uwp/api/windows.devices.serialcommunication.serialdevice)
to connect to a 3D printer. Written against MatterHackers Pulse D-224 printer.
These printers give over a kilobyte of status text upon connection, so the test
app reads that information upon connection and display in a TextBox. Some of
the status text is sent immediately, but the SD card status takes another second
or two to complete. Not all 3D printers send such status text immediately upon
connection.