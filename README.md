# UWP Tests
Basic apps exploring Microsoft's [Universal Windows Platform (UWP)](https://docs.microsoft.com/en-us/windows/uwp/)
1. __Hello3DP__ : A "Hello World" for using a
[Windows.Devices.SerialCommunications.SerialDevice](https://docs.microsoft.com/en-us/uwp/api/windows.devices.serialcommunication.serialdevice)
to connect to a 3D printer. Written against MatterHackers Pulse D-224 printer.
These printers give over a kilobyte of status text upon connection, so the test
app reads that information upon connection and uses it to identify the printer
in the case multiple serial devices are available on the computer. Not all 3D
printers send such status text immediately upon connection, an alternate
approach for those printers is to send
[M115](https://marlinfw.org/docs/gcode/M115.html).
2. __PollingComms__ : Unlike some serial communication devices, a 3D printer
does not guarantee a 1:1 ratio between commands sent by PC and the responses
sent by printer. Sometimes the printer sends out unsolicited information,
which breaks the "send command/wait for response" cycle because there might
be extra unrelated data. This test app builds on top of Hello3DP by setting
up a continuous read loop to retrieve data and matches them up to commands
when applicable and discards them when not. Responses are returned to original
sender of command via use of
[System.Threading.Tasks.TaskCompletionSource](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1)
Includes: rudimentary code to handle situations like USB unplugging. If not
entirely gracefully, at least not crash & burn.
Includes: rudimentary parsing of XYZ coordinates.
3. __CameraTest__ : Test app to show live webcam footage by following instructions in
[Display the camera preview](https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/simple-camera-preview-access).
4. __CameraUserControl__ : Creating custom user controls so they could receive
keyboard and mouse events for interactivity. Combines the 3D printer control
logic of __PollingComms__ with the camera preview of __CameraTest__ and
put them in an interactive application so we could navigate X/Y/Z space while
watching camera feedback.
