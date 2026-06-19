# 3ds-to-virtual-ps4-controler
This project was created purely out of curiosity and challenge. I can't imagine anyone in 2026 needing to use their 3DS as a controller on a PC, but that's not important. Everything works fine, all the buttons are present, including the gyroscope and C-stick. Enjoy.

How to use:
Install the Nintendo 3DS CI or 3DSX on your console and connect the console to WiFi (important: your PC and console must be connected to the same network).

Then download the 3DSYaPiDoor 1.0.exe and run it. If you encounter any problems, you'll likely need to download the 

ViGEmBus driver: https://github.com/nefarius/ViGEmBus/releases

and perhaps Microsoft's SDK .NET 8+ version.
I'm not sure about this because I don't have the opportunity to test on different platforms. Everything was tested on Windows 11 64-bit and the new 3DS XL. This is my first project of this kind, and I'm not entirely sure I'm doing everything correctly.

Well, if you were able to install everything and get it running, you should see something like this:

On 3ds:

<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/1e436e37-f754-411b-94da-300060c853e9" />

On PC:

<img width="738" height="202" alt="{7D068DFB-F120-4E91-A826-9D7E26046D6F}" src="https://github.com/user-attachments/assets/5633593b-c19a-4314-b7de-b493e083bd2d" />

Next, you need to enter the port on your PC. It is recommended to leave it at default and just press enter. Then you'll see your PC's local IP address. It'll most likely be this one. Now return to your console, press Y, and enter the IP and port one by one. Then select the connection you just created, and that's it.

If you did everything correctly, you'll see a green "connected" sign. I'm too lazy to explain what you can do with this. But I'll still explain the process a bit: you've created a virtual PS4 controller on your PC, and all controls from your 3DS are sent to this controller via WiFi. The programs have some useful features, like turning off the console's display or calibrating the PC's gyroscope. It's not much, but it's honest work.

For those who'd like to try their hand at modifying it, I've left the entire source code. In the 3ds folder, you'll find everything you need to create this program, but you'll need to download devcitpro for ARM, etc., and the same for PC. The same goes for PC. In the win64 folder, you'll find all the files you need for development. I think you'll figure it out. (though I doubt anyone will need it XDD).
