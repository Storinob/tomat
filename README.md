<img width="250" height="250" alt="logo-gh" src="https://github.com/user-attachments/assets/779776db-8fe4-4e12-8f6d-e97d9e5fc7f9" />

# tomat

A lightweight C# (.NET 8.0) utility that replaces the standard media flyout for Windows 10. It displays the current music source at the top center of the screen for 3.4 seconds, and if there are others, a list will be displayed. This flyout itself is not a window, so it does not change focus or keyboard input.
The program also runs in the background without an icon on the taskbar.

First of all, I built this utility for myself, but I also prepared it for public use. Ready to download and run the `.exe` file is in the repository.

![Platform](https://img.shields.io/badge/platform-Windows_10-0078D6?logo=windows&logoColor=white)
[![Latest Release](https://img.shields.io/github/v/release/Storinob/tomat?color=007acc&label=release)](https://github.com/Storinob/tomat/releases/)


## Screenshots
(1920x1080)
<img width="1920" height="1080" alt="426505c5" src="https://github.com/user-attachments/assets/0f81eb0f-f175-4ad7-8a5d-1b36e3846ce4" />
<img width="640" height="360" alt="tomat1" src="https://github.com/user-attachments/assets/520c0633-2dc4-4085-b769-a73c3c6aae2e" />
<img width="640" height="360" alt="tomat2" src="https://github.com/user-attachments/assets/e6f954e9-9906-422c-9358-429eae4d82fb" />

## Build

[![License](https://img.shields.io/github/license/Storinob/tomat?color=green)](https://github.com/Storinob/tomat/blob/main/LICENSE)
![.NET Version](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
[![Last Commit](https://img.shields.io/github/last-commit/Storinob/tomat?color=brightgreen&label=updated)](https://github.com/Storinob/tomat/commits/main)
![Repo Size](https://img.shields.io/github/repo-size/Storinob/tomat?color=orange)

Command must be executed inside the project folder.

If you just want to compile a program (~24,4MB):
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
If you want to build a program completely independent (packs some libraries into the program itself, increasing its weight to ~234MB. **Basically, not needed for u and in most cases the first option is suitable.**):
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
```
To compile the project, you will need the **.NET 8.0 SDK**.
