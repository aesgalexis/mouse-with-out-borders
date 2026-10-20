# Mouse, with out borders

A small Windows application to use **one mouse across two Windows PCs** on the same local network.

The mouse remains physically connected to the controller PC. Moving past the right edge of the controller's primary screen sends the pointer to the left edge of the other PC. Moving back past the other PC's left edge returns it. Mouse buttons and wheel follow the pointer. **No keyboard sharing, clipboard sharing, or other remote-control features.**

## Quick start

1. Download the Windows build from GitHub Actions or build with `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.
2. Run the app on **both Windows PCs**, connected to the same trusted LAN.
3. On the second PC select **Receive mouse**, click **Start**, and note its LAN IP and pairing key.
4. On the PC with the mouse select **Control mouse**, enter the other PC's IP and pairing key, and click **Connect**.
5. Move the mouse through the **right edge** of the controller's primary screen. Return through the **left edge** of the other PC.

Both PCs must run the program in an interactive Windows session. Allow the application through Windows Firewall on **private networks** if prompted. The default TCP port is **42187**. Keep the port closed to the internet.

## Scope and current limitations

- First version supports two PCs, with the receiver positioned to the **right** of the controller. Each PC uses its **primary monitor**.
- Communication is authenticated with a randomly generated pairing key and message authentication codes; mouse activity is **not encrypted**. Use a trusted private network, never port-forward the listener.
- Windows may prevent mouse injection into elevated apps, secure desktops, or UAC prompts; those contexts are not supported.
- A connection loss releases the controller's cursor lock automatically.
- This is an initial implementation. It still needs compilation/real-hardware testing on two Windows PCs before being called production-ready.
